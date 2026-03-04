// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Sorcha.Validator.Service.Configuration;
using Sorcha.Validator.Service.Models;
using Sorcha.Validator.Service.Services.Interfaces;
using Sorcha.ServiceClients.Register;
using Sorcha.Register.Models;

namespace Sorcha.Validator.Service.Services;

/// <summary>
/// Background service that monitors memory pools and triggers docket building
/// based on hybrid conditions (time threshold OR size threshold)
/// </summary>
public class DocketBuildTriggerService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IRegisterMonitoringRegistry _registry;
    private readonly DocketBuildConfiguration _config;
    private readonly ILogger<DocketBuildTriggerService> _logger;

    // Track last build time per register
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastBuildTimes = new();

    // Track registers that have had genesis dockets written (prevent re-creation)
    private readonly ConcurrentDictionary<string, bool> _genesisWritten = new();

    // Track genesis docket write retry attempts per register (max 3)
    private readonly ConcurrentDictionary<string, int> _genesisRetryCount = new();

    public DocketBuildTriggerService(
        IServiceScopeFactory scopeFactory,
        IRegisterMonitoringRegistry registry,
        IOptions<DocketBuildConfiguration> config,
        ILogger<DocketBuildTriggerService> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Docket build trigger service starting. Time threshold: {TimeThreshold}, Size threshold: {SizeThreshold}",
            _config.TimeThreshold, _config.SizeThreshold);

        // Use time threshold as the check interval (or minimum of 1 second)
        var checkInterval = _config.TimeThreshold > TimeSpan.FromSeconds(1)
            ? _config.TimeThreshold
            : TimeSpan.FromSeconds(1);

        using var timer = new PeriodicTimer(checkInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                var activeRegisters = _registry.GetAll().ToList();
                _logger.LogTrace("Checking docket build triggers for {RegisterCount} active registers",
                    activeRegisters.Count);

                // Check each active register
                foreach (var registerId in activeRegisters)
                {
                    try
                    {
                        await CheckAndBuildDocketAsync(registerId, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error checking/building docket for register {RegisterId}", registerId);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Docket build trigger service stopping");
        }
    }

    /// <summary>
    /// Checks if a register should build a docket and triggers build if needed
    /// </summary>
    private async Task CheckAndBuildDocketAsync(string registerId, CancellationToken cancellationToken)
    {
        // Create a scope to resolve scoped services
        using var scope = _scopeFactory.CreateScope();
        var docketBuilder = scope.ServiceProvider.GetRequiredService<IDocketBuilder>();

        // Get last build time (or use epoch if never built)
        var lastBuildTime = _lastBuildTimes.GetOrAdd(registerId, DateTimeOffset.UnixEpoch);

        // Skip if genesis was already written — subsequent dockets only needed when
        // there are verified transactions, which the normal ShouldBuild check handles
        if (_genesisWritten.ContainsKey(registerId))
        {
            var verifiedQueue = scope.ServiceProvider.GetRequiredService<IVerifiedTransactionQueue>();
            var txCount = verifiedQueue.GetCount(registerId);
            if (txCount == 0)
            {
                _logger.LogTrace("Register {RegisterId} has genesis docket and no verified transactions", registerId);
                return;
            }
        }

        // Check if we should build
        var shouldBuild = await docketBuilder.ShouldBuildDocketAsync(registerId, lastBuildTime, cancellationToken);

        if (!shouldBuild)
        {
            _logger.LogTrace("Register {RegisterId} does not meet build thresholds yet", registerId);
            return;
        }

        // Check minValidators from policy before building
        try
        {
            var validatorRegistry = scope.ServiceProvider.GetRequiredService<IValidatorRegistry>();
            var registerClient = scope.ServiceProvider.GetRequiredService<IRegisterServiceClient>();
            var activeCount = await validatorRegistry.GetActiveCountAsync(registerId, cancellationToken);

            var policyResponse = await registerClient.GetRegisterPolicyAsync(registerId, cancellationToken);
            var minValidators = policyResponse?.Policy?.Validators?.MinValidators ?? 1;

            if (activeCount < minValidators)
            {
                _logger.LogWarning(
                    "Skipping docket build for register {RegisterId}: active validators ({ActiveCount}) below minimum ({MinValidators})",
                    registerId, activeCount, minValidators);
                return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to check minValidators for register {RegisterId}, proceeding with build",
                registerId);
        }

        _logger.LogInformation("Triggering docket build for register {RegisterId}", registerId);

        // Build docket
        var docket = await docketBuilder.BuildDocketAsync(registerId, forceBuild: false, cancellationToken);

        if (docket != null)
        {
            _logger.LogInformation("Successfully built docket {DocketNumber} for register {RegisterId}",
                docket.DocketNumber, registerId);

            // Update last build time
            _lastBuildTimes[registerId] = DateTimeOffset.UtcNow;

            // Trigger consensus process — if consensus succeeds, write docket to Register Service
            var consensusEngine = scope.ServiceProvider.GetService<IConsensusEngine>();
            if (consensusEngine != null)
            {
                var consensusResult = await consensusEngine.AchieveConsensusAsync(docket, cancellationToken);
                if (consensusResult.Achieved)
                {
                    _logger.LogInformation("Consensus achieved for docket {DocketNumber}, writing to Register Service",
                        docket.DocketNumber);

                    try
                    {
                        await WriteDocketAndTransactionsAsync(scope, docket, cancellationToken);

                        // Only mark genesis as written on successful write
                        if (docket.DocketNumber == 0)
                            _genesisWritten[registerId] = true;
                    }
                    catch (Exception ex) when (docket.DocketNumber == 0)
                    {
                        var retryCount = _genesisRetryCount.AddOrUpdate(registerId, 1, (_, count) => count + 1);
                        if (retryCount >= 3)
                        {
                            _logger.LogWarning(
                                "Genesis docket write failed {RetryCount} times for register {RegisterId}. Unmonitoring register. Admin attention needed. Error: {Message}",
                                retryCount, registerId, ex.Message);
                            _registry.UnregisterFromMonitoring(registerId);
                        }
                        else
                        {
                            _logger.LogInformation(
                                "Genesis docket write failed for register {RegisterId} (attempt {RetryCount}/3, will retry): {Message}",
                                registerId, retryCount, ex.Message);
                        }
                    }
                }
                else
                {
                    _logger.LogWarning("Consensus failed for docket {DocketNumber}: {Reason}",
                        docket.DocketNumber, consensusResult.FailureReason ?? "Unknown");
                }
            }
            else
            {
                // No consensus engine registered — write directly (single-validator mode)
                _logger.LogDebug("No consensus engine available — writing docket directly (single-validator mode)");

                try
                {
                    await WriteDocketAndTransactionsAsync(scope, docket, cancellationToken);

                    // Only mark genesis as written on successful write
                    if (docket.DocketNumber == 0)
                        _genesisWritten[registerId] = true;
                }
                catch (Exception ex) when (docket.DocketNumber == 0)
                {
                    var retryCount = _genesisRetryCount.AddOrUpdate(registerId, 1, (_, count) => count + 1);
                    if (retryCount >= 3)
                    {
                        _logger.LogWarning(
                            "Genesis docket write failed {RetryCount} times for register {RegisterId}. Unmonitoring register. Admin attention needed. Error: {Message}",
                            retryCount, registerId, ex.Message);
                        _registry.UnregisterFromMonitoring(registerId);
                    }
                    else
                    {
                        _logger.LogInformation(
                            "Genesis docket write failed for register {RegisterId} (attempt {RetryCount}/3, will retry): {Message}",
                            registerId, retryCount, ex.Message);
                    }
                }
            }
        }
        else
        {
            _logger.LogDebug("No docket built for register {RegisterId} (verified queue empty or not ready)", registerId);
        }
    }

    /// <summary>
    /// Manually triggers a docket build for a register
    /// </summary>
    public async Task<bool> TriggerManualBuildAsync(string registerId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Manual docket build triggered for register {RegisterId}", registerId);

        // Create a scope to resolve scoped services
        using var scope = _scopeFactory.CreateScope();
        var docketBuilder = scope.ServiceProvider.GetRequiredService<IDocketBuilder>();

        var docket = await docketBuilder.BuildDocketAsync(registerId, forceBuild: true, cancellationToken);

        if (docket != null)
        {
            _lastBuildTimes[registerId] = DateTimeOffset.UtcNow;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Writes docket and transactions to Register Service after successful build
    /// </summary>
    private async Task WriteDocketAndTransactionsAsync(
        IServiceScope scope,
        Sorcha.Validator.Service.Models.Docket docket,
        CancellationToken cancellationToken)
    {
        var registerClient = scope.ServiceProvider.GetRequiredService<IRegisterServiceClient>();
        var poolPoller = scope.ServiceProvider.GetRequiredService<ITransactionPoolPoller>();

        try
        {
            // Convert docket to DocketModel  (Sorcha.ServiceClients.Register.DocketModel)
            var docketModel = new DocketModel
            {
                DocketId = docket.DocketId,
                RegisterId = docket.RegisterId,
                DocketNumber = docket.DocketNumber,
                PreviousHash = docket.PreviousHash,
                DocketHash = docket.DocketHash,
                CreatedAt = docket.CreatedAt,
                Transactions = docket.Transactions.Select(t =>
                {
                    var firstSig = t.Signatures.FirstOrDefault();
                    var payloadData = t.Payload.ValueKind != System.Text.Json.JsonValueKind.Undefined
                        ? Base64Url.EncodeToString(System.Text.Encoding.UTF8.GetBytes(t.Payload.GetRawText()))
                        : string.Empty;

                    // Extract transaction type from metadata
                    var txType = Sorcha.Register.Models.Enums.TransactionType.Action;
                    if (t.Metadata.TryGetValue("Type", out var typeStr)
                        && Enum.TryParse<Sorcha.Register.Models.Enums.TransactionType>(typeStr, ignoreCase: true, out var parsed))
                    {
                        txType = parsed;
                    }

                    return new Sorcha.Register.Models.TransactionModel
                    {
                        TxId = t.TransactionId,
                        RegisterId = t.RegisterId,
                        PrevTxId = t.PreviousTransactionId ?? string.Empty,
                        TimeStamp = t.CreatedAt.UtcDateTime,
                        SenderWallet = firstSig != null
                            ? Base64Url.EncodeToString(firstSig.PublicKey)
                            : "system",
                        Signature = firstSig != null
                            ? Base64Url.EncodeToString(firstSig.SignatureValue)
                            : string.Empty,
                        PayloadCount = 1,
                        Payloads = new[]
                        {
                            new Sorcha.Register.Models.PayloadModel
                            {
                                Data = payloadData,
                                Hash = t.PayloadHash,
                                PayloadSize = (ulong)System.Text.Encoding.UTF8.GetByteCount(
                                    t.Payload.ValueKind != System.Text.Json.JsonValueKind.Undefined
                                        ? t.Payload.GetRawText()
                                        : string.Empty),
                                ContentEncoding = "base64url"
                            }
                        },
                        MetaData = new Sorcha.Register.Models.TransactionMetaData
                        {
                            RegisterId = t.RegisterId,
                            BlueprintId = t.BlueprintId,
                            ActionId = uint.TryParse(t.ActionId, out var actionId) ? actionId : null,
                            TransactionType = txType
                        }
                    };
                }).ToList(),
                ProposerValidatorId = docket.ProposerValidatorId,
                MerkleRoot = docket.MerkleRoot
            };

            // Write docket to Register Service
            var written = await registerClient.WriteDocketAsync(docketModel, cancellationToken);

            if (!written)
            {
                throw new InvalidOperationException(
                    $"Failed to write docket {docket.DocketNumber} to Register Service for register {docket.RegisterId}");
            }

            _logger.LogInformation("Wrote docket {DocketNumber} to Register Service for register {RegisterId}",
                docket.DocketNumber, docket.RegisterId);

            // Clean up from unverified pool (in case ValidationEngineService hasn't consumed them yet)
            foreach (var tx in docket.Transactions)
            {
                await poolPoller.RemoveTransactionAsync(docket.RegisterId, tx.TransactionId, cancellationToken);
                _logger.LogDebug("Removed transaction {TransactionId} from unverified pool", tx.TransactionId);
            }

            _logger.LogInformation("Wrote {Count} transactions to register {RegisterId} docket {DocketNumber}",
                docket.Transactions.Count, docket.RegisterId, docket.DocketNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write docket {DocketNumber} and transactions to Register Service",
                docket.DocketNumber);
            throw;
        }
    }
}
