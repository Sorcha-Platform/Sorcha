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
    private readonly ValidatorConfiguration _validatorConfig;
    private readonly ISystemWalletProvider _systemWalletProvider;
    private readonly ILogger<DocketBuildTriggerService> _logger;

    // Track last build time per register
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastBuildTimes = new();

    // Track registers that have had genesis dockets written (prevent re-creation)
    private readonly ConcurrentDictionary<string, bool> _genesisWritten = new();

    // Track genesis docket write retry attempts per register (max 3)
    private readonly ConcurrentDictionary<string, int> _genesisRetryCount = new();

    // Track last successful heartbeat per register to avoid hammering the registry
    // every tick. Re-register only when approaching the operational TTL expiry.
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastHeartbeatTimes = new();

    // Heartbeat at 2/3 of the operational TTL (e.g., every 40s for a 60s TTL).
    // This provides a full 1/3 TTL safety margin for retries if a heartbeat fails,
    // while eliminating ~93% of redundant registration calls.
    private const double HeartbeatTtlFraction = 2.0 / 3.0;

    // Default operational TTL (matches ValidatorRegistryConfiguration default).
    // Used when we haven't resolved the actual TTL yet.
    private const int DefaultOperationalTtlSeconds = 60;

    public DocketBuildTriggerService(
        IServiceScopeFactory scopeFactory,
        IRegisterMonitoringRegistry registry,
        IOptions<DocketBuildConfiguration> config,
        IOptions<ValidatorConfiguration> validatorConfig,
        ISystemWalletProvider systemWalletProvider,
        ILogger<DocketBuildTriggerService> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
        _validatorConfig = validatorConfig?.Value ?? throw new ArgumentNullException(nameof(validatorConfig));
        _systemWalletProvider = systemWalletProvider ?? throw new ArgumentNullException(nameof(systemWalletProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Docket build trigger service starting. Time threshold: {TimeThreshold}, Size threshold: {SizeThreshold}",
            _config.TimeThreshold, _config.SizeThreshold);

        // Reconcile genesis state for registers that were created before this service started.
        // Without this, _genesisWritten stays empty after restart and the minValidators check
        // is never applied (isGenesisBuild stays true for existing registers).
        await ReconcileGenesisStateAsync(stoppingToken);

        // Drain any transactions stranded in the unverified pool during downtime.
        // This ensures pending work is processed immediately after crash/restart
        // rather than waiting for the next submission to trigger polling.
        await ReconcileUnverifiedPoolAsync(stoppingToken);

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

                // Heartbeat: re-register this validator for monitored registers
                // to refresh the operational TTL on the Redis key.
                // Runs fire-and-forget so HTTP calls don't block the docket build loop.
                // Only fires when approaching TTL expiry (2/3 of TTL elapsed).
                var heartbeatThreshold = TimeSpan.FromSeconds(DefaultOperationalTtlSeconds * HeartbeatTtlFraction);
                var registersNeedingHeartbeat = activeRegisters
                    .Where(r => _genesisWritten.ContainsKey(r) &&
                                (!_lastHeartbeatTimes.TryGetValue(r, out var lastBeat) ||
                                 DateTimeOffset.UtcNow - lastBeat >= heartbeatThreshold))
                    .ToList();

                if (registersNeedingHeartbeat.Count > 0)
                {
                    // Fire-and-forget: heartbeat must never block docket builds.
                    // If it fails, _lastHeartbeatTimes is cleared and we retry next tick.
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            using var heartbeatScope = _scopeFactory.CreateScope();
                            foreach (var registerId in registersNeedingHeartbeat)
                            {
                                await AutoRegisterValidatorAsync(heartbeatScope, registerId, stoppingToken);
                            }
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            _logger.LogWarning(ex, "Background heartbeat failed — will retry next cycle");
                        }
                    }, stoppingToken);
                }

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
    /// On startup, checks all monitored registers and reconciles in-memory genesis state.
    /// For registers that already have a genesis docket (height > 0), marks them in
    /// _genesisWritten and ensures this validator is registered.
    /// </summary>
    private async Task ReconcileGenesisStateAsync(CancellationToken cancellationToken)
    {
        var activeRegisters = _registry.GetAll().ToList();
        if (activeRegisters.Count == 0)
            return;

        _logger.LogInformation(
            "Reconciling genesis state for {Count} monitored registers on startup",
            activeRegisters.Count);

        using var scope = _scopeFactory.CreateScope();
        var registerClient = scope.ServiceProvider.GetRequiredService<IRegisterServiceClient>();

        foreach (var registerId in activeRegisters)
        {
            try
            {
                var height = await registerClient.GetRegisterHeightAsync(registerId, cancellationToken);
                if (height > 0)
                {
                    _genesisWritten[registerId] = true;
                    _logger.LogDebug(
                        "Register {RegisterId} has genesis (height={Height}), reconciled",
                        registerId, height);

                    // Ensure this validator is registered for the register
                    await AutoRegisterValidatorAsync(scope, registerId, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to reconcile genesis state for register {RegisterId}, will detect on first build cycle",
                    registerId);
            }
        }
    }

    /// <summary>
    /// Drains any transactions stranded in the unverified pool during downtime.
    /// For each monitored register, checks the pool count and triggers validation if pending.
    /// </summary>
    private async Task ReconcileUnverifiedPoolAsync(CancellationToken cancellationToken)
    {
        var activeRegisters = _registry.GetAll().ToList();
        if (activeRegisters.Count == 0)
            return;

        _logger.LogInformation(
            "Checking unverified pool for {Count} monitored registers after startup",
            activeRegisters.Count);

        using var scope = _scopeFactory.CreateScope();
        var poolPoller = scope.ServiceProvider.GetRequiredService<ITransactionPoolPoller>();
        var validationEngine = scope.ServiceProvider.GetService<ValidationEngineService>();
        if (validationEngine is null)
        {
            _logger.LogDebug("ValidationEngineService not resolvable from scope — stranded transactions will be picked up by normal polling");
            return;
        }

        var totalPending = 0;
        foreach (var registerId in activeRegisters)
        {
            try
            {
                var pendingCount = await poolPoller.GetUnverifiedCountAsync(registerId, cancellationToken);
                if (pendingCount > 0)
                {
                    _logger.LogInformation(
                        "Found {PendingCount} stranded transactions in unverified pool for register {RegisterId} — triggering validation",
                        pendingCount, registerId);
                    totalPending += (int)pendingCount;

                    // Trigger immediate validation by processing the register
                    await validationEngine.ProcessRegisterAsync(registerId, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to reconcile unverified pool for register {RegisterId} — will be picked up by normal polling",
                    registerId);
            }
        }

        if (totalPending > 0)
        {
            _logger.LogInformation(
                "Startup reconciliation complete: processed {TotalPending} stranded transactions across {RegisterCount} registers",
                totalPending, activeRegisters.Count);
        }
        else
        {
            _logger.LogDebug("Startup reconciliation complete: no stranded transactions found");
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

        // Check minValidators from policy before building — but skip for genesis docket.
        // A brand-new register has no validators registered yet; the genesis docket must
        // be built first to bootstrap the register before validators can join.
        var isGenesisBuild = !_genesisWritten.ContainsKey(registerId);
        if (!isGenesisBuild)
        {
            try
            {
                var validatorRegistry = scope.ServiceProvider.GetRequiredService<IValidatorRegistry>();
                var registerClient = scope.ServiceProvider.GetRequiredService<IRegisterServiceClient>();
                var activeCount = await validatorRegistry.GetActiveCountAsync(registerId, cancellationToken);

                var policyResponse = await registerClient.GetRegisterPolicyAsync(registerId, cancellationToken);
                // Default to 0 when no policy exists — allows single-node operation
                // without registered validators. Production deployments should set
                // MinValidators explicitly in their register policy.
                var minValidators = policyResponse?.Policy?.Validators?.MinValidators ?? 0;

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
                        {
                            _genesisWritten[registerId] = true;
                            await AutoRegisterValidatorAsync(scope, registerId, cancellationToken);
                        }
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
                    {
                        _genesisWritten[registerId] = true;
                        await AutoRegisterValidatorAsync(scope, registerId, cancellationToken);
                    }
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
    /// Auto-registers this validator for a register after genesis docket is written.
    /// The genesis docket bootstraps the register, but post-genesis docket builds
    /// require at least MinValidators active validators. Without auto-registration,
    /// transactions would be stuck in the verified queue permanently.
    /// </summary>
    private async Task AutoRegisterValidatorAsync(
        IServiceScope scope,
        string registerId,
        CancellationToken cancellationToken)
    {
        try
        {
            var validatorRegistry = scope.ServiceProvider.GetRequiredService<IValidatorRegistry>();
            var systemWalletId = _systemWalletProvider.GetSystemWalletId() ?? _validatorConfig.SystemWalletAddress;

            var registration = new ValidatorRegistration
            {
                ValidatorId = _validatorConfig.ValidatorId,
                PublicKey = systemWalletId,
                GrpcEndpoint = _validatorConfig.GrpcEndpoint ?? string.Empty,
                Metadata = new Dictionary<string, string>
                {
                    ["autoRegistered"] = "true",
                    ["registeredAt"] = DateTimeOffset.UtcNow.ToString("O")
                }
            };

            var result = await validatorRegistry.RegisterAsync(registerId, registration, cancellationToken);

            if (result.Success)
            {
                // Track successful heartbeat so we don't re-register until TTL approaches
                _lastHeartbeatTimes[registerId] = DateTimeOffset.UtcNow;

                _logger.LogDebug(
                    "Validator {ValidatorId} heartbeat for register {RegisterId} (order: {OrderIndex})",
                    _validatorConfig.ValidatorId, registerId, result.OrderIndex);
            }
            else if (result.ErrorMessage?.Contains("already registered", StringComparison.OrdinalIgnoreCase) == true)
            {
                // Still registered — treat as successful heartbeat
                _lastHeartbeatTimes[registerId] = DateTimeOffset.UtcNow;
            }
            else
            {
                // Registration genuinely failed — clear cache to force immediate retry next tick
                _lastHeartbeatTimes.TryRemove(registerId, out _);

                _logger.LogWarning(
                    "Auto-registration failed for validator {ValidatorId} on register {RegisterId}: {Error}",
                    _validatorConfig.ValidatorId, registerId, result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            // Clear cache to force immediate retry — fail-safe: if we can't heartbeat,
            // the registration will expire at the TTL boundary and the validator will
            // be excluded from consensus until successfully re-registered.
            _lastHeartbeatTimes.TryRemove(registerId, out _);

            _logger.LogWarning(ex,
                "Failed to auto-register validator {ValidatorId} for register {RegisterId}. " +
                "Will retry next tick. If persistent, manual registration via POST /api/validators/register may be required.",
                _validatorConfig.ValidatorId, registerId);
        }
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

                    // Extract transaction type from metadata.
                    // "Genesis" is used by RegisterCreationOrchestrator but isn't an enum member —
                    // it maps to Control (genesis is a control record that bootstraps the register).
                    var txType = Sorcha.Register.Models.Enums.TransactionType.Action;
                    if (t.Metadata.TryGetValue("Type", out var typeStr))
                    {
                        if (typeStr.Equals("Genesis", StringComparison.OrdinalIgnoreCase))
                            txType = Sorcha.Register.Models.Enums.TransactionType.Control;
                        else if (Enum.TryParse<Sorcha.Register.Models.Enums.TransactionType>(typeStr, ignoreCase: true, out var parsed))
                            txType = parsed;
                    }

                    return new Sorcha.Register.Models.TransactionModel
                    {
                        TxId = t.TransactionId,
                        RegisterId = t.RegisterId,
                        PrevTxId = t.PreviousTransactionId ?? string.Empty,
                        TimeStamp = t.CreatedAt.UtcDateTime,
                        // Wave 12: prefer Signature.SignedBy (bech32 wallet
                        // address from the Wallet Service) so the persisted
                        // SenderWallet field stays in the same format that
                        // /api/register/query/wallets/{address}/transactions
                        // queries against. Falling back to base64url(PublicKey)
                        // produced strings that never matched a wallet lookup
                        // — see wave 11 audit for the empty-tx-list bug. Use
                        // IsNullOrWhiteSpace (not Length > 0) to align with
                        // DocketSerializer.GetSenderWallet's whitespace guard.
                        SenderWallet = firstSig != null
                            ? (!string.IsNullOrWhiteSpace(firstSig.SignedBy)
                                ? firstSig.SignedBy
                                : Base64Url.EncodeToString(firstSig.PublicKey))
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
                        RecipientsWallets = t.RecipientsWallets ?? new List<string>(),
                        MetaData = new Sorcha.Register.Models.TransactionMetaData
                        {
                            RegisterId = t.RegisterId,
                            BlueprintId = t.BlueprintId,
                            ActionId = uint.TryParse(t.ActionId, out var actionId) ? actionId : null,
                            TransactionType = txType,
                            // Carry InstanceId through from the in-memory submission so the
                            // Validator's Tier 3 chain-derived participant binding can walk
                            // in-instance transactions on the register. Prior to this, sealed
                            // txs were persisted with InstanceId=null and every Tier 3 lookup
                            // returned an empty list.
                            InstanceId = t.Metadata.TryGetValue("instanceId", out var iid)
                                         && !string.IsNullOrWhiteSpace(iid)
                                ? iid
                                : null,
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
