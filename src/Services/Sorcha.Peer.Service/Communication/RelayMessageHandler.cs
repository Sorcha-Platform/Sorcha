// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sorcha.Peer.Service.Protos;
using Sorcha.Peer.Service.Replication;
using Sorcha.ServiceClients.Register;
using RelayModels = Sorcha.Peer.Service.Communication.Models;

namespace Sorcha.Peer.Service.Communication;

/// <summary>
/// Dispatches incoming relay messages to appropriate handlers.
/// Handles sync requests by reading local cache, completes pending correlations for responses,
/// and triggers sync on transaction notifications.
/// </summary>
public class RelayMessageHandler
{
    private readonly ILogger<RelayMessageHandler> _logger;
    private readonly RelayCommunicationService _relayCommunication;
    private readonly RegisterCache _registerCache;
    private readonly RegisterSyncBackgroundService _syncBackgroundService;
    private readonly DocketFinalizationService? _docketFinalizationService;
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>
    /// Maximum response payload size in bytes (3MB to leave headroom within 4MB/16MB proto limits)
    /// </summary>
    private const int MaxResponseSizeBytes = 3 * 1024 * 1024;

    /// <summary>
    /// Maximum dockets per relay response. Relay messages traverse the PeerRouter as a single
    /// proto payload, so batches must be small enough to fit within gRPC message limits.
    /// </summary>
    private const int MaxDocketsPerRelayResponse = 50;

    public RelayMessageHandler(
        ILogger<RelayMessageHandler> logger,
        RelayCommunicationService relayCommunication,
        RegisterCache registerCache,
        RegisterSyncBackgroundService syncBackgroundService,
        IServiceScopeFactory scopeFactory,
        DocketFinalizationService? docketFinalizationService = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _relayCommunication = relayCommunication ?? throw new ArgumentNullException(nameof(relayCommunication));
        _registerCache = registerCache ?? throw new ArgumentNullException(nameof(registerCache));
        _syncBackgroundService = syncBackgroundService ?? throw new ArgumentNullException(nameof(syncBackgroundService));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _docketFinalizationService = docketFinalizationService;
    }

    /// <summary>
    /// Dispatches a relay message to the appropriate handler based on MessageType.
    /// </summary>
    public async Task HandleAsync(PeerMessage message, CancellationToken cancellationToken = default)
    {
        switch (message.MessageType)
        {
            case MessageType.RegisterSyncRequest:
                await HandleRegisterSyncRequestAsync(message, cancellationToken);
                break;

            case MessageType.RegisterSyncResponse:
                HandleCorrelationResponse(message);
                break;

            case MessageType.TransactionDataRequest:
                await HandleTransactionDataRequestAsync(message, cancellationToken);
                break;

            case MessageType.TransactionDataResponse:
                HandleCorrelationResponse(message);
                break;

            case MessageType.TransactionNotification:
                await HandleTransactionNotificationAsync(message, cancellationToken);
                break;

            default:
                _logger.LogDebug("Unknown relay message type {MessageType} from {SenderPeerId}",
                    message.MessageType, message.SenderPeerId);
                break;
        }
    }

    /// <summary>
    /// Handles an incoming REGISTER_SYNC_REQUEST by reading local cache and sending response via relay.
    /// </summary>
    private async Task HandleRegisterSyncRequestAsync(PeerMessage message, CancellationToken cancellationToken)
    {
        var json = message.Payload.ToStringUtf8();
        var request = JsonSerializer.Deserialize<RelayModels.RegisterSyncRequest>(json);

        if (request == null)
        {
            _logger.LogWarning("Failed to deserialize RegisterSyncRequest from {SenderPeerId}", message.SenderPeerId);
            return;
        }

        _logger.LogDebug("Handling RegisterSyncRequest for register {RegisterId} from {SenderPeerId}",
            request.RegisterId, message.SenderPeerId);

        var cacheEntry = _registerCache.Get(request.RegisterId);
        var response = new RelayModels.RegisterSyncResponse
        {
            CorrelationId = request.CorrelationId,
            RegisterId = request.RegisterId
        };

        if (cacheEntry != null)
        {
            var dockets = cacheEntry.GetDocketsFromVersion(request.FromDocketVersion);
            var maxDockets = Math.Min(request.MaxDockets, MaxDocketsPerRelayResponse);
            var estimatedSize = 0;

            foreach (var docket in dockets)
            {
                if (response.Dockets.Count >= maxDockets)
                {
                    response.HasMore = true;
                    break;
                }

                var entry = new RelayModels.DocketEntry
                {
                    Version = docket.Version,
                    Data = docket.Data,
                    DocketHash = docket.DocketHash,
                    PreviousHash = docket.PreviousHash ?? string.Empty,
                    TransactionIds = docket.TransactionIds,
                    CreatedAt = docket.CreatedAt.ToUnixTimeMilliseconds()
                };

                estimatedSize += entry.Data.Length + 200; // Data + metadata overhead
                if (estimatedSize > MaxResponseSizeBytes)
                {
                    response.HasMore = true;
                    break;
                }

                response.Dockets.Add(entry);
            }
        }
        else
        {
            // Register not in cache — fall back to the co-located Register Service
            // for locally owned registers (same pattern as RegisterSyncGrpcService)
            await PopulateResponseFromRegisterServiceAsync(request, response, cancellationToken);
        }

        await _relayCommunication.SendViaRelayAsync(
            message.SenderPeerId,
            MessageType.RegisterSyncResponse,
            response,
            cancellationToken);
    }

    /// <summary>
    /// Handles an incoming TRANSACTION_DATA_REQUEST by reading local cache and sending response via relay.
    /// </summary>
    private async Task HandleTransactionDataRequestAsync(PeerMessage message, CancellationToken cancellationToken)
    {
        var json = message.Payload.ToStringUtf8();
        var request = JsonSerializer.Deserialize<RelayModels.TransactionDataRequest>(json);

        if (request == null)
        {
            _logger.LogWarning("Failed to deserialize TransactionDataRequest from {SenderPeerId}", message.SenderPeerId);
            return;
        }

        _logger.LogDebug("Handling TransactionDataRequest for register {RegisterId} from {SenderPeerId}",
            request.RegisterId, message.SenderPeerId);

        var cacheEntry = _registerCache.Get(request.RegisterId);
        var response = new RelayModels.TransactionDataResponse
        {
            CorrelationId = request.CorrelationId,
            RegisterId = request.RegisterId
        };

        if (cacheEntry != null)
        {
            // Cap transaction IDs to prevent unbounded response payloads
            var txIds = request.TransactionIds.Count > 500
                ? request.TransactionIds.Take(500)
                : request.TransactionIds;

            foreach (var txId in txIds)
            {
                var tx = cacheEntry.GetTransaction(txId);
                if (tx != null)
                {
                    response.Transactions.Add(new RelayModels.TransactionEntry
                    {
                        TransactionId = tx.TransactionId,
                        Data = tx.Data,
                        Checksum = tx.Checksum ?? string.Empty,
                        CreatedAt = tx.CreatedAt.ToUnixTimeMilliseconds()
                    });
                }
            }
        }
        else
        {
            // Register not in cache — fall back to Register Service for transaction data
            await PopulateTransactionResponseFromRegisterServiceAsync(request, response, cancellationToken);
        }

        await _relayCommunication.SendViaRelayAsync(
            message.SenderPeerId,
            MessageType.TransactionDataResponse,
            response,
            cancellationToken);
    }

    /// <summary>
    /// Handles response messages (REGISTER_SYNC_RESPONSE, TRANSACTION_DATA_RESPONSE)
    /// by completing the pending correlation.
    /// </summary>
    private void HandleCorrelationResponse(PeerMessage message)
    {
        var json = message.Payload.ToStringUtf8();
        string? correlationId = null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("CorrelationId", out var prop) ||
                doc.RootElement.TryGetProperty("correlationId", out prop))
            {
                correlationId = prop.GetString();
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse correlation ID from relay response");
            return;
        }

        if (string.IsNullOrEmpty(correlationId))
        {
            _logger.LogWarning("Relay response from {SenderPeerId} missing CorrelationId", message.SenderPeerId);
            return;
        }

        _relayCommunication.CompleteCorrelation(correlationId, message);
    }

    /// <summary>
    /// Reads docket data from the co-located Register Service when the peer cache is empty.
    /// Mirrors the fallback logic in RegisterSyncGrpcService.PullDocketChainFromRegisterServiceAsync.
    /// </summary>
    private async Task PopulateResponseFromRegisterServiceAsync(
        RelayModels.RegisterSyncRequest request,
        RelayModels.RegisterSyncResponse response,
        CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var registerClient = scope.ServiceProvider.GetRequiredService<IRegisterServiceClient>();

            var height = await registerClient.GetRegisterHeightAsync(request.RegisterId, cancellationToken);
            if (height < 0)
            {
                _logger.LogDebug(
                    "Register {RegisterId} not found in Register Service (relay fallback)",
                    request.RegisterId);
                return;
            }

            var fromVersion = request.FromDocketVersion;
            var maxDockets = Math.Min(
                request.MaxDockets > 0 ? request.MaxDockets : MaxDocketsPerRelayResponse,
                MaxDocketsPerRelayResponse);
            var estimatedSize = 0;

            // height is a COUNT (1 = one docket at index 0), so iterate from fromVersion+1 to height-1 inclusive
            for (var docketNum = fromVersion + 1; docketNum < height && response.Dockets.Count < maxDockets; docketNum++)
            {
                if (cancellationToken.IsCancellationRequested) break;

                var docket = await registerClient.ReadDocketAsync(request.RegisterId, docketNum, cancellationToken);
                if (docket == null) break;

                var docketData = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(docket);
                var entry = new RelayModels.DocketEntry
                {
                    Version = docket.DocketNumber,
                    Data = docketData,
                    DocketHash = docket.DocketHash,
                    PreviousHash = docket.PreviousHash ?? string.Empty,
                    TransactionIds = docket.Transactions.Select(t => t.TxId).ToList(),
                    CreatedAt = docket.CreatedAt.ToUnixTimeMilliseconds()
                };

                estimatedSize += entry.Data.Length + 200;
                if (estimatedSize > MaxResponseSizeBytes)
                {
                    response.HasMore = true;
                    break;
                }

                response.Dockets.Add(entry);
            }

            if (response.Dockets.Count < maxDockets && height > fromVersion + response.Dockets.Count)
                response.HasMore = true;

            _logger.LogInformation(
                "Relay sync fallback served {Count} dockets from Register Service for {RegisterId} (height={Height})",
                response.Dockets.Count, request.RegisterId, height);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to read docket data from Register Service for relay sync (register {RegisterId})",
                request.RegisterId);
        }
    }

    /// <summary>
    /// Reads transaction data from the co-located Register Service when the peer cache is empty.
    /// </summary>
    private async Task PopulateTransactionResponseFromRegisterServiceAsync(
        RelayModels.TransactionDataRequest request,
        RelayModels.TransactionDataResponse response,
        CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var registerClient = scope.ServiceProvider.GetRequiredService<IRegisterServiceClient>();

            var txIds = request.TransactionIds.Count > 500
                ? request.TransactionIds.Take(500)
                : request.TransactionIds;

            foreach (var txId in txIds)
            {
                if (cancellationToken.IsCancellationRequested) break;

                var tx = await registerClient.GetTransactionAsync(request.RegisterId, txId, cancellationToken);
                if (tx != null)
                {
                    var txData = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(tx);
                    response.Transactions.Add(new RelayModels.TransactionEntry
                    {
                        TransactionId = tx.TxId,
                        Data = txData,
                        Checksum = string.Empty,
                        CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    });
                }
            }

            _logger.LogInformation(
                "Relay transaction fallback served {Count}/{Requested} transactions from Register Service for {RegisterId}",
                response.Transactions.Count, request.TransactionIds.Count, request.RegisterId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to read transaction data from Register Service for relay sync (register {RegisterId})",
                request.RegisterId);
        }
    }

    /// <summary>
    /// Handles a relayed TRANSACTION_NOTIFICATION by triggering sync if the register is subscribed.
    /// </summary>
    public async Task HandleTransactionNotificationAsync(PeerMessage message, CancellationToken cancellationToken = default)
    {
        var json = message.Payload.ToStringUtf8();
        string? registerId = null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("RegisterId", out var prop) ||
                doc.RootElement.TryGetProperty("registerId", out prop))
            {
                registerId = prop.GetString();
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse register ID from relayed transaction notification");
            return;
        }

        if (string.IsNullOrEmpty(registerId))
        {
            _logger.LogDebug("Relayed transaction notification missing RegisterId, skipping");
            return;
        }

        var subscription = _syncBackgroundService.GetSubscription(registerId);
        if (subscription == null)
        {
            _logger.LogDebug("Received relayed transaction notification for unsubscribed register {RegisterId}", registerId);
            return;
        }

        var semaphore = _syncBackgroundService.GetSyncSemaphore(registerId);
        if (!await semaphore.WaitAsync(0, cancellationToken))
        {
            _logger.LogDebug("Sync already in progress for register {RegisterId}, skipping notification trigger", registerId);
            return;
        }

        try
        {
            _logger.LogDebug("Relayed transaction notification triggering sync for register {RegisterId}", registerId);

            var correlationId = Guid.NewGuid().ToString();
            var syncRequest = new RelayModels.RegisterSyncRequest
            {
                CorrelationId = correlationId,
                RegisterId = registerId,
                FromDocketVersion = subscription.LastSyncedDocketVersion
            };

            var syncResponse = await _relayCommunication.SendAndWaitAsync<RelayModels.RegisterSyncResponse>(
                message.SenderPeerId,
                MessageType.RegisterSyncRequest,
                syncRequest,
                correlationId,
                cancellationToken: cancellationToken);

            if (syncResponse != null)
            {
                var cacheEntry = _registerCache.GetOrCreate(registerId);
                foreach (var docket in syncResponse.Dockets)
                {
                    cacheEntry.AddOrUpdateDocket(new Replication.CachedDocket
                    {
                        RegisterId = registerId,
                        Version = docket.Version,
                        Data = docket.Data,
                        DocketHash = docket.DocketHash,
                        PreviousHash = docket.PreviousHash,
                        TransactionIds = docket.TransactionIds,
                        CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(docket.CreatedAt)
                    });
                }

                _logger.LogDebug(
                    "Notification-triggered sync applied {Count} dockets for register {RegisterId}",
                    syncResponse.Dockets.Count, registerId);

                // Trigger finalization for newly synced dockets
                if (_docketFinalizationService != null)
                {
                    foreach (var docket in syncResponse.Dockets)
                    {
                        var cachedDocket = new Replication.CachedDocket
                        {
                            RegisterId = registerId,
                            Version = docket.Version,
                            Data = docket.Data,
                            DocketHash = docket.DocketHash,
                            PreviousHash = docket.PreviousHash,
                            TransactionIds = docket.TransactionIds,
                            CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(docket.CreatedAt)
                        };

                        // Ensure the docket is in RegisterCache before finalization so
                        // chain integrity verification can find it and its predecessors.
                        cacheEntry.AddOrUpdateDocket(cachedDocket);

                        await _docketFinalizationService.FinalizeAsync(registerId, cachedDocket, cancellationToken);
                    }
                }
            }
        }
        finally
        {
            semaphore.Release();
        }
    }
}
