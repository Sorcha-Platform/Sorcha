// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Google.Protobuf;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sorcha.Peer.Service.Core;
using Sorcha.Peer.Service.Replication;
using ProtoSyncState = Sorcha.Peer.Service.Protos.SyncState;

namespace Sorcha.Peer.Service.GrpcServices;

/// <summary>
/// gRPC service implementation for register synchronization.
/// Serves the RegisterSync service defined in register_sync.proto,
/// enabling peer-to-peer register replication via docket chain pull,
/// transaction pull, live subscription, and sync status queries.
/// </summary>
public class RegisterSyncGrpcService : Protos.RegisterSync.RegisterSyncBase
{
    private readonly RegisterCache _registerCache;
    private readonly RegisterSyncBackgroundService _syncBackgroundService;
    private readonly ILogger<RegisterSyncGrpcService> _logger;
    private readonly PeerServiceConfiguration _configuration;

    /// <summary>
    /// Polling interval for the SubscribeToRegister live stream.
    /// </summary>
    private static readonly TimeSpan LivePollInterval = TimeSpan.FromSeconds(2);

    public RegisterSyncGrpcService(
        RegisterCache registerCache,
        RegisterSyncBackgroundService syncBackgroundService,
        ILogger<RegisterSyncGrpcService> logger,
        IOptions<PeerServiceConfiguration> configuration)
    {
        _registerCache = registerCache ?? throw new ArgumentNullException(nameof(registerCache));
        _syncBackgroundService = syncBackgroundService ?? throw new ArgumentNullException(nameof(syncBackgroundService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration?.Value ?? throw new ArgumentNullException(nameof(configuration));
    }

    /// <summary>
    /// Streams dockets from the local cache for the requested register,
    /// starting from request.FromVersion, limited by request.MaxDockets.
    /// </summary>
    public override async Task PullDocketChain(
        Protos.DocketChainRequest request,
        IServerStreamWriter<Protos.DocketEntry> responseStream,
        ServerCallContext context)
    {
        _logger.LogDebug(
            "PullDocketChain requested by peer {PeerId} for register {RegisterId} from version {FromVersion} (max {MaxDockets})",
            request.PeerId, request.RegisterId, request.FromVersion, request.MaxDockets);

        var cacheEntry = _registerCache.Get(request.RegisterId);
        if (cacheEntry == null)
        {
            _logger.LogDebug("Register {RegisterId} not found in cache", request.RegisterId);
            throw new RpcException(new Status(
                StatusCode.NotFound,
                $"Register '{request.RegisterId}' not found in local cache"));
        }

        var dockets = cacheEntry.GetDocketsFromVersion(request.FromVersion, request.MaxDockets);

        _logger.LogDebug(
            "Streaming {Count} dockets for register {RegisterId}",
            dockets.Count, request.RegisterId);

        foreach (var docket in dockets)
        {
            if (context.CancellationToken.IsCancellationRequested)
                break;

            var entry = new Protos.DocketEntry
            {
                RegisterId = docket.RegisterId,
                Version = docket.Version,
                DocketData = ByteString.CopyFrom(docket.Data),
                DocketHash = docket.DocketHash,
                PreviousHash = docket.PreviousHash ?? string.Empty,
                CreatedAt = docket.CreatedAt.ToUnixTimeMilliseconds()
            };
            entry.TransactionIds.AddRange(docket.TransactionIds);

            await responseStream.WriteAsync(entry, context.CancellationToken);
        }

        _logger.LogDebug(
            "PullDocketChain completed for register {RegisterId}: streamed {Count} dockets",
            request.RegisterId, dockets.Count);
    }

    /// <summary>
    /// Streams transactions from the local cache matching the requested transaction IDs.
    /// </summary>
    public override async Task PullDocketTransactions(
        Protos.DocketTransactionRequest request,
        IServerStreamWriter<Protos.TransactionEntry> responseStream,
        ServerCallContext context)
    {
        _logger.LogDebug(
            "PullDocketTransactions requested by peer {PeerId} for register {RegisterId} ({Count} transaction IDs)",
            request.PeerId, request.RegisterId, request.TransactionIds.Count);

        var cacheEntry = _registerCache.Get(request.RegisterId);
        if (cacheEntry == null)
        {
            _logger.LogDebug("Register {RegisterId} not found in cache", request.RegisterId);
            throw new RpcException(new Status(
                StatusCode.NotFound,
                $"Register '{request.RegisterId}' not found in local cache"));
        }

        var streamed = 0;
        var notFound = 0;

        foreach (var txId in request.TransactionIds)
        {
            if (context.CancellationToken.IsCancellationRequested)
                break;

            var tx = cacheEntry.GetTransaction(txId);
            if (tx == null)
            {
                notFound++;
                _logger.LogDebug(
                    "Transaction {TransactionId} not found in cache for register {RegisterId}",
                    txId, request.RegisterId);
                continue;
            }

            var entry = new Protos.TransactionEntry
            {
                TransactionId = tx.TransactionId,
                RegisterId = tx.RegisterId,
                TransactionData = ByteString.CopyFrom(tx.Data),
                Checksum = tx.Checksum ?? string.Empty,
                CreatedAt = tx.CreatedAt.ToUnixTimeMilliseconds()
            };

            await responseStream.WriteAsync(entry, context.CancellationToken);
            streamed++;
        }

        _logger.LogDebug(
            "PullDocketTransactions completed for register {RegisterId}: streamed {Streamed}, not found {NotFound}",
            request.RegisterId, streamed, notFound);
    }

    /// <summary>
    /// Long-lived server stream that delivers live transactions to a subscribing peer.
    /// Writes initial cached transactions with version > request.FromVersion,
    /// then polls the cache every 2 seconds for new transactions until cancelled.
    /// </summary>
    public override async Task SubscribeToRegister(
        Protos.RegisterSubscriptionRequest request,
        IServerStreamWriter<Protos.LiveTransactionEvent> responseStream,
        ServerCallContext context)
    {
        _logger.LogDebug(
            "SubscribeToRegister requested by peer {PeerId} for register {RegisterId} from version {FromVersion}",
            request.PeerId, request.RegisterId, request.FromVersion);

        var cacheEntry = _registerCache.Get(request.RegisterId);
        if (cacheEntry == null)
        {
            _logger.LogDebug("Register {RegisterId} not found in cache", request.RegisterId);
            throw new RpcException(new Status(
                StatusCode.NotFound,
                $"Register '{request.RegisterId}' not found in local cache"));
        }

        var localPeerId = _configuration.NodeId ?? Environment.MachineName;
        var lastSentVersion = request.FromVersion;

        // Send initial cached transactions with version > fromVersion
        var initialTransactions = cacheEntry.GetTransactionsFromVersion(lastSentVersion);
        foreach (var tx in initialTransactions)
        {
            if (context.CancellationToken.IsCancellationRequested)
                return;

            if (!MatchesFilters(tx, request.Filters))
                continue;

            var evt = CreateLiveTransactionEvent(tx, localPeerId);
            await responseStream.WriteAsync(evt, context.CancellationToken);

            if (tx.Version > lastSentVersion)
                lastSentVersion = tx.Version;
        }

        _logger.LogDebug(
            "Sent {Count} initial transactions for register {RegisterId}, now entering live poll loop",
            initialTransactions.Count, request.RegisterId);

        // Poll for new transactions periodically
        while (!context.CancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(LivePollInterval, context.CancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            var newTransactions = cacheEntry.GetTransactionsFromVersion(lastSentVersion);
            foreach (var tx in newTransactions)
            {
                if (context.CancellationToken.IsCancellationRequested)
                    return;

                if (!MatchesFilters(tx, request.Filters))
                    continue;

                var evt = CreateLiveTransactionEvent(tx, localPeerId);
                await responseStream.WriteAsync(evt, context.CancellationToken);

                if (tx.Version > lastSentVersion)
                    lastSentVersion = tx.Version;
            }
        }

        _logger.LogDebug(
            "SubscribeToRegister stream ended for register {RegisterId} (peer {PeerId})",
            request.RegisterId, request.PeerId);
    }

    /// <summary>
    /// Returns the sync state from the background service and cache statistics.
    /// </summary>
    public override Task<Protos.RegisterSyncStatus> GetRegisterSyncStatus(
        Protos.RegisterSyncStatusRequest request,
        ServerCallContext context)
    {
        _logger.LogDebug(
            "GetRegisterSyncStatus requested by peer {PeerId} for register {RegisterId}",
            request.PeerId, request.RegisterId);

        var subscription = _syncBackgroundService.GetSubscription(request.RegisterId);
        var cacheEntry = _registerCache.Get(request.RegisterId);

        if (subscription == null && cacheEntry == null)
        {
            throw new RpcException(new Status(
                StatusCode.NotFound,
                $"Register '{request.RegisterId}' not found on this peer"));
        }

        var stats = cacheEntry?.GetStatistics();

        var status = new Protos.RegisterSyncStatus
        {
            RegisterId = request.RegisterId,
            SyncState = subscription != null
                ? MapToProtoSyncState(subscription.SyncState)
                : ProtoSyncState.Unknown,
            LatestVersion = stats?.LatestTransactionVersion ?? 0,
            LatestDocketVersion = stats?.LatestDocketVersion ?? 0,
            TotalTransactions = stats?.TransactionCount ?? 0,
            TotalDockets = stats?.DocketCount ?? 0,
            CanServeFullReplica = subscription?.SyncState == RegisterSyncState.FullyReplicated,
            LastSyncAt = subscription?.LastSyncAt?.ToUnixTimeMilliseconds() ?? 0
        };

        return Task.FromResult(status);
    }

    /// <summary>
    /// Maps the domain RegisterSyncState enum to the proto SyncState enum.
    /// </summary>
    private static ProtoSyncState MapToProtoSyncState(RegisterSyncState state) => state switch
    {
        RegisterSyncState.Subscribing => ProtoSyncState.Subscribing,
        RegisterSyncState.Syncing => ProtoSyncState.Syncing,
        RegisterSyncState.FullyReplicated => ProtoSyncState.FullyReplicated,
        RegisterSyncState.Active => ProtoSyncState.Active,
        RegisterSyncState.Error => ProtoSyncState.Error,
        _ => ProtoSyncState.Unknown
    };

    /// <summary>
    /// Creates a LiveTransactionEvent from a cached transaction.
    /// </summary>
    private static Protos.LiveTransactionEvent CreateLiveTransactionEvent(
        CachedTransaction tx, string senderPeerId)
    {
        return new Protos.LiveTransactionEvent
        {
            TransactionId = tx.TransactionId,
            RegisterId = tx.RegisterId,
            Version = tx.Version,
            TransactionData = ByteString.CopyFrom(tx.Data),
            Checksum = tx.Checksum ?? string.Empty,
            SenderPeerId = senderPeerId,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            EventType = Protos.LiveEventType.Transaction
        };
    }

    /// <summary>
    /// Checks whether a transaction matches the subscription filters.
    /// Returns true if no filters are set or the transaction matches at least one filter criterion.
    /// </summary>
    private static bool MatchesFilters(CachedTransaction tx, Protos.RegisterSubscriptionFilters? filters)
    {
        if (filters == null)
            return true;

        // If transaction_types are specified, we cannot filter because CachedTransaction
        // does not carry a type field. Allow all through.
        // If participant_ids are specified, we cannot filter because CachedTransaction
        // does not carry a participant field. Allow all through.
        // Filters are a future extension point.
        return true;
    }
}
