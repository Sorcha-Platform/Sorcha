// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sorcha.Register.Core.Storage;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Enums;
using Sorcha.Register.Service.Services.Interfaces;
using Sorcha.ServiceClients.Grpc;
using Sorcha.Wallet.Service.Grpc;
using StackExchange.Redis;

namespace Sorcha.Register.Service.Services.Implementation;

/// <summary>
/// Background service that detects docket gaps on startup and recovers missing dockets
/// from the peer network. Runs bloom filter checks on recovered transactions and sends
/// batch notifications for any local address matches. Tracks recovery state in Redis
/// for health endpoint monitoring.
/// </summary>
public sealed class RegisterRecoveryService : BackgroundService, IRegisterRecoveryService
{
    private readonly IReadOnlyRegisterRepository _repository;
    private readonly IDocketSyncClient _docketSyncClient;
    private readonly IInboundTransactionRouter _transactionRouter;
    private readonly IWalletNotificationClient _walletNotificationClient;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RegisterRecoveryService> _logger;

    private readonly bool _enableAutoRecovery;
    private readonly int _maxDocketsPerBatch;
    private readonly int _retryDelaySeconds;
    private readonly int _maxRetries;
    private readonly int _healthCheckStalenessSeconds;

    private const string RecoveryKeyPrefix = "register:recovery:";

    public RegisterRecoveryService(
        IReadOnlyRegisterRepository repository,
        IDocketSyncClient docketSyncClient,
        IInboundTransactionRouter transactionRouter,
        IWalletNotificationClient walletNotificationClient,
        IConnectionMultiplexer redis,
        IConfiguration configuration,
        ILogger<RegisterRecoveryService> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _docketSyncClient = docketSyncClient ?? throw new ArgumentNullException(nameof(docketSyncClient));
        _transactionRouter = transactionRouter ?? throw new ArgumentNullException(nameof(transactionRouter));
        _walletNotificationClient = walletNotificationClient ?? throw new ArgumentNullException(nameof(walletNotificationClient));
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var recoverySection = configuration.GetSection("Recovery");
        _enableAutoRecovery = recoverySection.GetValue("EnableAutoRecovery", true);
        _maxDocketsPerBatch = recoverySection.GetValue("MaxDocketsPerBatch", 100);
        _retryDelaySeconds = recoverySection.GetValue("RetryDelaySeconds", 5);
        _maxRetries = recoverySection.GetValue("MaxRetries", 3);
        _healthCheckStalenessSeconds = recoverySection.GetValue("HealthCheckStalenessSeconds", 10);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_enableAutoRecovery)
        {
            _logger.LogInformation("Auto-recovery disabled via configuration");
            return;
        }

        // Give other services time to start
        await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);

        _logger.LogInformation("Register recovery service starting — checking all local registers");

        try
        {
            var registers = await _repository.GetRegistersAsync(stoppingToken);
            foreach (var register in registers)
            {
                if (stoppingToken.IsCancellationRequested)
                    break;

                await RecoverIfNeededAsync(register.Id, stoppingToken);
            }

            _logger.LogInformation("Register recovery startup check complete");
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Register recovery service cancelled during startup");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Register recovery service failed during startup");
        }
    }

    /// <inheritdoc />
    public async Task<bool> RecoverIfNeededAsync(string registerId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Checking recovery status for register {RegisterId}", registerId);

        try
        {
            // Get local latest docket
            var register = await _repository.GetRegisterAsync(registerId, cancellationToken);
            if (register == null)
            {
                _logger.LogWarning("Register {RegisterId} not found locally, skipping recovery", registerId);
                return false;
            }

            long localLatestDocket = register.Height > 0 ? (long)(register.Height - 1) : 0;

            // Query peer network for head
            var networkResponse = await _docketSyncClient.GetLatestDocketNumberAsync(registerId, cancellationToken);

            if (!networkResponse.NetworkAvailable)
            {
                _logger.LogWarning("Peer network unavailable for register {RegisterId}, skipping recovery", registerId);
                return false;
            }

            var networkHead = networkResponse.LatestDocketNumber;

            if (localLatestDocket >= networkHead)
            {
                _logger.LogDebug(
                    "Register {RegisterId} is synced (local: {Local}, network: {Network})",
                    registerId, localLatestDocket, networkHead);

                await UpdateRecoveryStateAsync(registerId, new RecoveryState
                {
                    RegisterId = registerId,
                    Status = RecoveryStatus.Synced,
                    LocalLatestDocket = localLatestDocket,
                    NetworkHeadDocket = networkHead,
                    LastProgressAt = DateTimeOffset.UtcNow
                });

                return false;
            }

            // Gap detected — enter recovery mode
            _logger.LogInformation(
                "Gap detected for register {RegisterId}: local={Local}, network={Network}, gap={Gap}",
                registerId, localLatestDocket, networkHead, networkHead - localLatestDocket);

            await ExecuteRecoveryAsync(registerId, localLatestDocket, networkHead, cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Recovery check failed for register {RegisterId}", registerId);

            await UpdateRecoveryStateAsync(registerId, new RecoveryState
            {
                RegisterId = registerId,
                Status = RecoveryStatus.Stalled,
                LastError = ex.Message,
                ErrorCount = 1,
                LastProgressAt = DateTimeOffset.UtcNow
            });

            return false;
        }
    }

    private async Task ExecuteRecoveryAsync(
        string registerId, long localLatestDocket, long networkHead,
        CancellationToken cancellationToken)
    {
        var state = new RecoveryState
        {
            RegisterId = registerId,
            Status = RecoveryStatus.Recovering,
            LocalLatestDocket = localLatestDocket,
            NetworkHeadDocket = networkHead,
            StartedAt = DateTimeOffset.UtcNow,
            LastProgressAt = DateTimeOffset.UtcNow,
            DocketsProcessed = 0,
            ErrorCount = 0
        };

        await UpdateRecoveryStateAsync(registerId, state);

        var currentDocket = localLatestDocket;
        var retryCount = 0;
        string? previousDocketHash = null;

        // If we have local dockets, get the hash of the latest one for chain verification
        if (localLatestDocket > 0)
        {
            var latestLocalDocket = await _repository.GetDocketAsync(registerId, (ulong)localLatestDocket, cancellationToken);
            previousDocketHash = latestLocalDocket?.Hash;
        }

        while (currentDocket < networkHead && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                var batchNotifications = new List<NotifyInboundTransactionRequest>();

                await foreach (var entry in _docketSyncClient.SyncDocketsAsync(
                    registerId, currentDocket, networkHead, _maxDocketsPerBatch, cancellationToken))
                {
                    // Verify chain integrity
                    if (previousDocketHash != null &&
                        !string.IsNullOrEmpty(entry.PreviousDocketHash) &&
                        entry.PreviousDocketHash != previousDocketHash)
                    {
                        var error = $"Chain integrity violation at docket {entry.DocketNumber}: " +
                                    $"expected previous hash {previousDocketHash}, got {entry.PreviousDocketHash}";
                        _logger.LogError(error);

                        state = state with
                        {
                            Status = RecoveryStatus.Stalled,
                            LastError = error,
                            ErrorCount = state.ErrorCount + 1,
                            LastProgressAt = DateTimeOffset.UtcNow
                        };
                        await UpdateRecoveryStateAsync(registerId, state);
                        return;
                    }

                    // Deserialize docket data and process transactions
                    await ProcessRecoveredDocketAsync(
                        registerId, entry, batchNotifications, cancellationToken);

                    previousDocketHash = entry.DocketHash;
                    currentDocket = entry.DocketNumber;

                    state = state with
                    {
                        DocketsProcessed = state.DocketsProcessed + 1,
                        LocalLatestDocket = currentDocket,
                        LastProgressAt = DateTimeOffset.UtcNow
                    };

                    await UpdateRecoveryStateAsync(registerId, state);
                }

                // Send batch notifications for recovered transactions
                if (batchNotifications.Count > 0)
                {
                    try
                    {
                        var batchRequest = new NotifyInboundTransactionBatchRequest();
                        batchRequest.Transactions.AddRange(batchNotifications);

                        var batchResponse = await _walletNotificationClient
                            .NotifyInboundTransactionBatchAsync(batchRequest, cancellationToken);

                        _logger.LogInformation(
                            "Recovery batch notification for register {RegisterId}: " +
                            "total={Total}, realTime={RealTime}, digest={Digest}, rateLimited={RateLimited}, noUser={NoUser}",
                            registerId, batchResponse.Total, batchResponse.DeliveredRealTime,
                            batchResponse.QueuedForDigest, batchResponse.RateLimited, batchResponse.NoUserFound);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "Failed to send batch notification during recovery for register {RegisterId}",
                            registerId);
                    }
                }

                retryCount = 0; // Reset retry count on success
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                retryCount++;
                _logger.LogWarning(ex,
                    "Recovery streaming failed for register {RegisterId} at docket {Docket} (attempt {Attempt}/{Max})",
                    registerId, currentDocket, retryCount, _maxRetries);

                if (retryCount >= _maxRetries)
                {
                    state = state with
                    {
                        Status = RecoveryStatus.Stalled,
                        LastError = $"Max retries exceeded: {ex.Message}",
                        ErrorCount = state.ErrorCount + 1,
                        LastProgressAt = DateTimeOffset.UtcNow
                    };
                    await UpdateRecoveryStateAsync(registerId, state);
                    return;
                }

                await Task.Delay(TimeSpan.FromSeconds(_retryDelaySeconds * retryCount), cancellationToken);
            }
        }

        // Recovery complete
        state = state with
        {
            Status = RecoveryStatus.Synced,
            LastProgressAt = DateTimeOffset.UtcNow
        };
        await UpdateRecoveryStateAsync(registerId, state);

        _logger.LogInformation(
            "Recovery complete for register {RegisterId}: processed {Count} dockets",
            registerId, state.DocketsProcessed);
    }

    private async Task ProcessRecoveredDocketAsync(
        string registerId,
        Sorcha.Peer.Service.Protos.SyncDocketEntry entry,
        List<NotifyInboundTransactionRequest> batchNotifications,
        CancellationToken cancellationToken)
    {
        if (entry.DocketData == null || entry.DocketData.IsEmpty)
        {
            _logger.LogDebug("Docket {DocketNumber} has no data, skipping", entry.DocketNumber);
            return;
        }

        // Deserialize the docket data to extract transactions
        List<TransactionModel>? transactions = null;
        try
        {
            transactions = JsonSerializer.Deserialize<List<TransactionModel>>(
                entry.DocketData.Span,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex,
                "Failed to deserialize docket {DocketNumber} data for register {RegisterId}",
                entry.DocketNumber, registerId);
            return;
        }

        if (transactions == null || transactions.Count == 0)
            return;

        foreach (var tx in transactions)
        {
            // Only route action-type transactions
            if (tx.MetaData?.TransactionType != TransactionType.Action)
                continue;

            var recipients = tx.RecipientsWallets?.ToList() ?? [];
            if (recipients.Count == 0)
                continue;

            // Use the router to check bloom filter matches
            var matchCount = await _transactionRouter.RouteTransactionAsync(
                registerId,
                tx.TxId,
                TransactionType.Action,
                recipients,
                tx.SenderWallet,
                tx.MetaData,
                entry.DocketNumber,
                isRecovery: true,
                cancellationToken);

            _logger.LogDebug(
                "Recovery docket {DocketNumber} tx {TxId}: {MatchCount} bloom filter matches",
                entry.DocketNumber, tx.TxId, matchCount);
        }
    }

    /// <inheritdoc />
    public async Task<RecoveryState?> GetRecoveryStateAsync(
        string registerId, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var key = $"{RecoveryKeyPrefix}{registerId}";

        var entries = await db.HashGetAllAsync(key);
        if (entries.Length == 0)
            return null;

        var dict = entries.ToDictionary(
            e => e.Name.ToString(),
            e => e.Value.ToString());

        return new RecoveryState
        {
            RegisterId = registerId,
            Status = System.Enum.TryParse<RecoveryStatus>(dict.GetValueOrDefault("Status"), out var s) ? s : RecoveryStatus.Synced,
            LocalLatestDocket = long.TryParse(dict.GetValueOrDefault("LocalLatestDocket"), out var l) ? l : 0,
            NetworkHeadDocket = long.TryParse(dict.GetValueOrDefault("NetworkHeadDocket"), out var n) ? n : 0,
            StartedAt = DateTimeOffset.TryParse(dict.GetValueOrDefault("StartedAt"), out var sa) ? sa : default,
            LastProgressAt = DateTimeOffset.TryParse(dict.GetValueOrDefault("LastProgressAt"), out var lp) ? lp : default,
            DocketsProcessed = long.TryParse(dict.GetValueOrDefault("DocketsProcessed"), out var dp) ? dp : 0,
            ErrorCount = int.TryParse(dict.GetValueOrDefault("ErrorCount"), out var ec) ? ec : 0,
            LastError = dict.GetValueOrDefault("LastError")
        };
    }

    private async Task UpdateRecoveryStateAsync(string registerId, RecoveryState state)
    {
        var db = _redis.GetDatabase();
        var key = $"{RecoveryKeyPrefix}{registerId}";

        var entries = new HashEntry[]
        {
            new("Status", state.Status.ToString()),
            new("LocalLatestDocket", state.LocalLatestDocket.ToString()),
            new("NetworkHeadDocket", state.NetworkHeadDocket.ToString()),
            new("StartedAt", state.StartedAt.ToString("O")),
            new("LastProgressAt", state.LastProgressAt.ToString("O")),
            new("DocketsProcessed", state.DocketsProcessed.ToString()),
            new("ErrorCount", state.ErrorCount.ToString()),
            new("LastError", state.LastError ?? string.Empty)
        };

        await db.HashSetAsync(key, entries);
    }
}
