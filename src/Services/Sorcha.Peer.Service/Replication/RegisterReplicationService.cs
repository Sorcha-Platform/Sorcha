// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sorcha.Peer.Service.Communication;
using Sorcha.Peer.Service.Connection;
using Sorcha.Peer.Service.Core;
using Sorcha.Peer.Service.Discovery;
using Sorcha.Peer.Service.Protos;
using RelayModels = Sorcha.Peer.Service.Communication.Models;

namespace Sorcha.Peer.Service.Replication;

/// <summary>
/// Orchestrates per-register replication from peer nodes.
/// Supports two replication modes:
/// - ForwardOnly: subscribe to live transactions from point of subscription
/// - FullReplica: pull entire docket chain, then subscribe to live transactions
/// </summary>
public class RegisterReplicationService
{
    private readonly ILogger<RegisterReplicationService> _logger;
    private readonly PeerConnectionPool _connectionPool;
    private readonly PeerListManager _peerListManager;
    private readonly RegisterCache _registerCache;
    private readonly RelayCommunicationService? _relayCommunication;
    private readonly RegisterSyncConfiguration _syncConfig;

    public RegisterReplicationService(
        ILogger<RegisterReplicationService> logger,
        PeerConnectionPool connectionPool,
        PeerListManager peerListManager,
        RegisterCache registerCache,
        IOptions<PeerServiceConfiguration>? configuration = null,
        RelayCommunicationService? relayCommunication = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _connectionPool = connectionPool ?? throw new ArgumentNullException(nameof(connectionPool));
        _peerListManager = peerListManager ?? throw new ArgumentNullException(nameof(peerListManager));
        _registerCache = registerCache ?? throw new ArgumentNullException(nameof(registerCache));
        _relayCommunication = relayCommunication;
        _syncConfig = configuration?.Value?.RegisterSync ?? new RegisterSyncConfiguration();
    }

    /// <summary>
    /// Performs full docket chain pull for a register.
    /// Walks the docket chain from genesis, pulling all transactions for each docket.
    /// </summary>
    public async Task<FullReplicaSyncResult> PullFullReplicaAsync(
        RegisterSubscription subscription,
        CancellationToken cancellationToken = default)
    {
        var registerId = subscription.RegisterId;
        _logger.LogInformation(
            "Starting full replica sync for register {RegisterId} from docket version {FromVersion}",
            registerId, subscription.LastSyncedDocketVersion);

        // Find peers that can serve full replica
        var sourcePeers = _peerListManager.GetFullReplicaPeersForRegister(registerId);
        if (sourcePeers.Count == 0)
        {
            // Fall back to any peer with the register
            sourcePeers = _peerListManager.GetPeersForRegister(registerId);
        }

        if (sourcePeers.Count == 0)
        {
            _logger.LogWarning("No source peers found for register {RegisterId}", registerId);
            return new FullReplicaSyncResult { Success = false, ErrorMessage = "No source peers available" };
        }

        var cacheEntry = _registerCache.GetOrCreate(registerId);
        var totalDockets = 0L;
        var totalTransactions = 0L;

        // Apply overall replication timeout
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromMinutes(_syncConfig.ReplicationTimeoutMinutes));
        var replicationToken = timeoutCts.Token;

        foreach (var sourcePeer in sourcePeers)
        {
            var channel = _connectionPool.GetChannel(sourcePeer.PeerId);

            if (channel == null)
            {
                // No direct channel — try relay batch sync for NAT'd peers
                if (string.IsNullOrEmpty(sourcePeer.Address) && _relayCommunication != null)
                {
                    var (relayResult, relayDockets, relayTransactions) = await TryRelayBatchSyncAsync(
                        sourcePeer, subscription, cacheEntry, replicationToken);

                    totalDockets += relayDockets;
                    totalTransactions += relayTransactions;

                    if (relayResult != null)
                        return relayResult;
                }

                continue;
            }

            try
            {
                var client = new RegisterSync.RegisterSyncClient(channel);
                var fromVersion = subscription.LastSyncedDocketVersion;
                var batchSize = _syncConfig.DocketPullBatchSize;
                var batchDocketCount = 0;

                // Pull dockets in batches
                do
                {
                    batchDocketCount = 0;

                    var docketRequest = new DocketChainRequest
                    {
                        RegisterId = registerId,
                        PeerId = Environment.MachineName,
                        FromVersion = fromVersion,
                        MaxDockets = batchSize
                    };

                    using var docketStream = client.PullDocketChain(docketRequest, cancellationToken: replicationToken);

                    await foreach (var docketEntry in docketStream.ResponseStream.ReadAllAsync(replicationToken))
                    {
                        cacheEntry.AddOrUpdateDocket(new CachedDocket
                        {
                            RegisterId = registerId,
                            Version = docketEntry.Version,
                            Data = docketEntry.DocketData.ToByteArray(),
                            DocketHash = docketEntry.DocketHash,
                            PreviousHash = docketEntry.PreviousHash,
                            TransactionIds = docketEntry.TransactionIds.ToList(),
                            CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(docketEntry.CreatedAt)
                        });

                        batchDocketCount++;
                        totalDockets++;
                        fromVersion = docketEntry.Version;

                        // Pull transactions for this docket
                        if (docketEntry.TransactionIds.Count > 0)
                        {
                            var txRequest = new DocketTransactionRequest
                            {
                                RegisterId = registerId,
                                PeerId = Environment.MachineName
                            };
                            txRequest.TransactionIds.AddRange(docketEntry.TransactionIds);

                            using var txStream = client.PullDocketTransactions(txRequest, cancellationToken: replicationToken);

                            await foreach (var txEntry in txStream.ResponseStream.ReadAllAsync(replicationToken))
                            {
                                cacheEntry.AddOrUpdateTransaction(new CachedTransaction
                                {
                                    TransactionId = txEntry.TransactionId,
                                    RegisterId = registerId,
                                    Data = txEntry.TransactionData.ToByteArray(),
                                    Checksum = txEntry.Checksum,
                                    CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(txEntry.CreatedAt)
                                });

                                totalTransactions++;
                            }
                        }
                    }
                } while (batchDocketCount >= batchSize); // More batches if we got a full batch

                // Update subscription state
                subscription.TotalDocketsInChain = totalDockets;
                subscription.RecordSyncSuccess(
                    cacheEntry.GetLatestDocketVersion(),
                    cacheEntry.GetLatestTransactionVersion());

                _logger.LogInformation(
                    "Full replica sync completed for register {RegisterId}: {Dockets} dockets, {Txs} transactions from peer {PeerId}",
                    registerId, totalDockets, totalTransactions, sourcePeer.PeerId);

                _connectionPool.RecordSuccess(sourcePeer.PeerId);

                return new FullReplicaSyncResult
                {
                    Success = true,
                    DocketsSynced = totalDockets,
                    TransactionsSynced = totalTransactions,
                    SourcePeerId = sourcePeer.PeerId
                };
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(
                    "Replication timeout ({Timeout}min) reached for register {RegisterId} from peer {PeerId}",
                    _syncConfig.ReplicationTimeoutMinutes, registerId, sourcePeer.PeerId);
                await _connectionPool.RecordFailureAsync(sourcePeer.PeerId);
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.Unavailable)
            {
                _logger.LogWarning(
                    "Source peer {PeerId} unavailable for register {RegisterId}, trying next",
                    sourcePeer.PeerId, registerId);
                await _connectionPool.RecordFailureAsync(sourcePeer.PeerId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error syncing register {RegisterId} from peer {PeerId}",
                    registerId, sourcePeer.PeerId);
                await _connectionPool.RecordFailureAsync(sourcePeer.PeerId);
            }
        }

        subscription.RecordSyncFailure("All source peers failed");
        return new FullReplicaSyncResult
        {
            Success = false,
            ErrorMessage = "All source peers failed",
            DocketsSynced = totalDockets,
            TransactionsSynced = totalTransactions
        };
    }

    /// <summary>
    /// Attempts relay batch sync for a NAT'd peer (empty address, no direct channel).
    /// Sends RegisterSyncRequest via relay, processes docket batches and transaction pulls.
    /// Returns (result, docketsSynced, transactionsSynced) — result is non-null on success or null to fall through.
    /// </summary>
    private async Task<(FullReplicaSyncResult? Result, long DocketsSynced, long TransactionsSynced)> TryRelayBatchSyncAsync(
        PeerNode sourcePeer,
        RegisterSubscription subscription,
        RegisterCacheEntry cacheEntry,
        CancellationToken cancellationToken)
    {
        var registerId = subscription.RegisterId;
        var docketsSynced = 0L;
        var transactionsSynced = 0L;

        _logger.LogInformation(
            "Attempting relay batch sync for register {RegisterId} from NAT'd peer {PeerId}",
            registerId, sourcePeer.PeerId);

        try
        {
            var fromVersion = subscription.LastSyncedDocketVersion;
            var maxDockets = _syncConfig.DocketPullBatchSize;
            var hasMore = true;

            while (hasMore)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var correlationId = Guid.NewGuid().ToString();
                var syncRequest = new RelayModels.RegisterSyncRequest
                {
                    CorrelationId = correlationId,
                    RegisterId = registerId,
                    FromDocketVersion = fromVersion,
                    MaxDockets = maxDockets
                };

                var syncResponse = await _relayCommunication!.SendAndWaitAsync<RelayModels.RegisterSyncResponse>(
                    sourcePeer.PeerId,
                    MessageType.RegisterSyncRequest,
                    syncRequest,
                    correlationId,
                    cancellationToken: cancellationToken);

                if (syncResponse == null)
                {
                    // Response size may be too large — retry with halved batch size
                    if (maxDockets > 1)
                    {
                        maxDockets = Math.Max(1, maxDockets / 2);
                        _logger.LogDebug(
                            "Relay sync response null for register {RegisterId}, retrying with MaxDockets={MaxDockets}",
                            registerId, maxDockets);
                        continue;
                    }

                    _logger.LogWarning(
                        "Relay batch sync failed for register {RegisterId} from peer {PeerId} — no response",
                        registerId, sourcePeer.PeerId);
                    await _connectionPool.RecordFailureAsync(sourcePeer.PeerId);
                    return (null, docketsSynced, transactionsSynced);
                }

                // Process dockets from response
                foreach (var docket in syncResponse.Dockets)
                {
                    cacheEntry.AddOrUpdateDocket(new CachedDocket
                    {
                        RegisterId = registerId,
                        Version = docket.Version,
                        Data = docket.Data,
                        DocketHash = docket.DocketHash,
                        PreviousHash = docket.PreviousHash,
                        TransactionIds = docket.TransactionIds,
                        CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(docket.CreatedAt)
                    });

                    docketsSynced++;
                    fromVersion = docket.Version;

                    // Pull transactions for this docket's transaction IDs
                    if (docket.TransactionIds.Count > 0)
                    {
                        var txCorrelationId = Guid.NewGuid().ToString();
                        var txRequest = new RelayModels.TransactionDataRequest
                        {
                            CorrelationId = txCorrelationId,
                            RegisterId = registerId,
                            TransactionIds = docket.TransactionIds
                        };

                        var txResponse = await _relayCommunication.SendAndWaitAsync<RelayModels.TransactionDataResponse>(
                            sourcePeer.PeerId,
                            MessageType.TransactionDataRequest,
                            txRequest,
                            txCorrelationId,
                            cancellationToken: cancellationToken);

                        if (txResponse != null)
                        {
                            foreach (var tx in txResponse.Transactions)
                            {
                                cacheEntry.AddOrUpdateTransaction(new CachedTransaction
                                {
                                    TransactionId = tx.TransactionId,
                                    RegisterId = registerId,
                                    Data = tx.Data,
                                    Checksum = tx.Checksum,
                                    CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(tx.CreatedAt)
                                });

                                transactionsSynced++;
                            }
                        }
                    }
                }

                hasMore = syncResponse.HasMore;
            }

            // Update subscription state
            subscription.TotalDocketsInChain = docketsSynced;
            subscription.RecordSyncSuccess(
                cacheEntry.GetLatestDocketVersion(),
                cacheEntry.GetLatestTransactionVersion());

            _logger.LogInformation(
                "Relay batch sync completed for register {RegisterId}: {Dockets} dockets, {Txs} transactions from peer {PeerId}",
                registerId, docketsSynced, transactionsSynced, sourcePeer.PeerId);

            return (new FullReplicaSyncResult
            {
                Success = true,
                DocketsSynced = docketsSynced,
                TransactionsSynced = transactionsSynced,
                SourcePeerId = sourcePeer.PeerId
            }, docketsSynced, transactionsSynced);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "Relay batch sync cancelled for register {RegisterId} from peer {PeerId}",
                registerId, sourcePeer.PeerId);
            await _connectionPool.RecordFailureAsync(sourcePeer.PeerId);
            return (null, docketsSynced, transactionsSynced);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error during relay batch sync for register {RegisterId} from peer {PeerId}",
                registerId, sourcePeer.PeerId);
            await _connectionPool.RecordFailureAsync(sourcePeer.PeerId);
            return (null, docketsSynced, transactionsSynced);
        }
    }

    /// <summary>
    /// Subscribes to live transactions for a register (forward-only mode).
    /// </summary>
    public async Task SubscribeToLiveTransactionsAsync(
        RegisterSubscription subscription,
        CancellationToken cancellationToken = default)
    {
        var registerId = subscription.RegisterId;
        var sourcePeers = _peerListManager.GetPeersForRegister(registerId);

        if (sourcePeers.Count == 0)
        {
            _logger.LogWarning("No source peers for live subscription to register {RegisterId}", registerId);
            return;
        }

        foreach (var sourcePeer in sourcePeers)
        {
            var channel = _connectionPool.GetChannel(sourcePeer.PeerId);
            if (channel == null) continue;

            try
            {
                var client = new RegisterSync.RegisterSyncClient(channel);
                var request = new RegisterSubscriptionRequest
                {
                    RegisterId = registerId,
                    PeerId = Environment.MachineName,
                    FromVersion = subscription.LastSyncedTransactionVersion
                };

                using var stream = client.SubscribeToRegister(request, cancellationToken: cancellationToken);

                _logger.LogInformation(
                    "Subscribed to live transactions for register {RegisterId} from peer {PeerId}",
                    registerId, sourcePeer.PeerId);

                var cacheEntry = _registerCache.GetOrCreate(registerId);

                await foreach (var evt in stream.ResponseStream.ReadAllAsync(cancellationToken))
                {
                    cacheEntry.AddOrUpdateTransaction(new CachedTransaction
                    {
                        TransactionId = evt.TransactionId,
                        RegisterId = registerId,
                        Version = evt.Version,
                        Data = evt.TransactionData.ToByteArray(),
                        Checksum = evt.Checksum,
                        CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(evt.Timestamp)
                    });

                    subscription.RecordSyncSuccess(
                        subscription.LastSyncedDocketVersion,
                        evt.Version);
                }
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
            {
                _logger.LogDebug("Live subscription cancelled for register {RegisterId}", registerId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Live subscription failed for register {RegisterId} from peer {PeerId}",
                    registerId, sourcePeer.PeerId);
                await _connectionPool.RecordFailureAsync(sourcePeer.PeerId);
            }
        }
    }
}

/// <summary>
/// Result of a full replica sync operation.
/// </summary>
public class FullReplicaSyncResult
{
    public bool Success { get; init; }
    public long DocketsSynced { get; init; }
    public long TransactionsSynced { get; init; }
    public string? SourcePeerId { get; init; }
    public string? ErrorMessage { get; init; }
}
