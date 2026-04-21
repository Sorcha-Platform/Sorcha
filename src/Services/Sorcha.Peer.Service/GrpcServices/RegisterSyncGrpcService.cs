// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

using Google.Protobuf;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sorcha.Peer.Service.Core;
using Sorcha.Peer.Service.Replication;
using Sorcha.Register.Models;
using Sorcha.ServiceClients.Register;
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
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RegisterSyncGrpcService> _logger;
    private readonly PeerServiceConfiguration _configuration;

    /// <summary>
    /// Polling interval for the SubscribeToRegister live stream.
    /// </summary>
    private static readonly TimeSpan LivePollInterval = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Canonical JSON options that match Register Service's serialisation
    /// (<c>RegisterCreationOrchestrator._canonicalJsonOptions</c>). Aligning on the
    /// same wire format means a transaction served via the repository fallback is
    /// byte-identical to one served via the cache on another node, so a third peer
    /// re-serving this payload doesn't surface a case-sensitivity mismatch.
    /// </summary>
    private static readonly JsonSerializerOptions CanonicalTransactionJsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public RegisterSyncGrpcService(
        RegisterCache registerCache,
        RegisterSyncBackgroundService syncBackgroundService,
        IServiceScopeFactory scopeFactory,
        ILogger<RegisterSyncGrpcService> logger,
        IOptions<PeerServiceConfiguration> configuration)
    {
        _registerCache = registerCache ?? throw new ArgumentNullException(nameof(registerCache));
        _syncBackgroundService = syncBackgroundService ?? throw new ArgumentNullException(nameof(syncBackgroundService));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
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
        if (cacheEntry != null)
        {
            // Serve from in-memory cache
            var dockets = cacheEntry.GetDocketsFromVersion(request.FromVersion, request.MaxDockets);
            _logger.LogDebug(
                "Streaming {Count} dockets from cache for register {RegisterId}",
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
                "PullDocketChain completed for register {RegisterId}: streamed {Count} dockets from cache",
                request.RegisterId, dockets.Count);
            return;
        }

        // Fall back to Register Service for registers not in cache
        _logger.LogInformation(
            "Register {RegisterId} not in cache, falling back to Register Service",
            request.RegisterId);

        await PullDocketChainFromRegisterServiceAsync(
            request, responseStream, context.CancellationToken);
    }

    /// <summary>
    /// Streams transactions from the local cache matching the requested transaction IDs.
    /// Falls back to the co-located Register Service when the peer-service cache has
    /// no entry for the register — the cache is populated on replication, so registers
    /// this node owns (sealed locally, never replicated) are never in it.
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
            _logger.LogInformation(
                "Register {RegisterId} not in cache, falling back to Register Service for {Count} transaction IDs",
                request.RegisterId, request.TransactionIds.Count);

            await PullDocketTransactionsFromRegisterServiceAsync(
                request, responseStream, context.CancellationToken);
            return;
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
    /// Serves PullDocketTransactions by reading transactions from the local Register
    /// Service. Used when the register is not in the peer-service's in-memory cache
    /// (typically because this node owns the register and never replicated it).
    /// </summary>
    private async Task PullDocketTransactionsFromRegisterServiceAsync(
        Protos.DocketTransactionRequest request,
        IServerStreamWriter<Protos.TransactionEntry> responseStream,
        CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var registerClient = scope.ServiceProvider.GetRequiredService<IRegisterServiceClient>();

        // Short-circuit if the register genuinely doesn't exist anywhere — the caller
        // will see an empty stream and move on to its next sync cycle instead of
        // surfacing an RpcException that the background loop treats as transient.
        // A transient failure here (Register Service blip) also returns empty rather
        // than escalating to the subscriber; the next sync cycle retries.
        Sorcha.Register.Models.Register? register;
        try
        {
            register = await registerClient.GetRegisterAsync(request.RegisterId, cancellationToken);
        }
        // Guard `when !IsCancellationRequested` so a genuine cancellation (gRPC
        // deadline, server shutdown) propagates cleanly instead of being swallowed
        // as a transient HTTP failure. TaskCanceledException derives from
        // OperationCanceledException so the naive `is TaskCanceledException`
        // pattern masks both.
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(
                ex,
                "Transient Register Service failure looking up {RegisterId} — streaming empty response",
                request.RegisterId);
            return;
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                ex,
                "Register Service lookup for {RegisterId} timed out — streaming empty response",
                request.RegisterId);
            return;
        }

        if (register == null)
        {
            _logger.LogInformation(
                "Register {RegisterId} unknown to Register Service — streaming empty response",
                request.RegisterId);
            return;
        }

        // TODO(perf): per-tx HTTP round-trips are N+1 for large dockets. When
        // IRegisterServiceClient gains a batch endpoint, switch to that here.
        var streamed = 0;
        var notFound = 0;
        var transientErrors = 0;

        foreach (var txId in request.TransactionIds)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            TransactionModel? tx;
            try
            {
                tx = await registerClient.GetTransactionAsync(request.RegisterId, txId, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                // Transient — the subscriber's next sync cycle will re-request. Tracked
                // separately from genuinely-missing so the completion log doesn't
                // conflate "doesn't exist" with "couldn't reach Register Service".
                _logger.LogWarning(
                    ex,
                    "Transient Register Service fetch failure for tx {TransactionId} on register {RegisterId}",
                    txId, request.RegisterId);
                transientErrors++;
                continue;
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                // HTTP timeout — behaves the same as HttpRequestException. The
                // `when` guard lets a genuine caller cancellation propagate.
                _logger.LogWarning(
                    ex,
                    "Register Service fetch timed out for tx {TransactionId} on register {RegisterId}",
                    txId, request.RegisterId);
                transientErrors++;
                continue;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Unexpected Register Service fetch error for tx {TransactionId} on register {RegisterId}",
                    txId, request.RegisterId);
                transientErrors++;
                continue;
            }

            if (tx == null)
            {
                notFound++;
                continue;
            }

            // The cache-hit path streams the raw transaction bytes pre-computed during
            // replication ingest. Here we don't have pre-computed bytes, so serialise
            // using the same canonical options Register Service uses (camelCase +
            // UnsafeRelaxedJsonEscaping) so the wire shape matches what a subscriber
            // would have seen via direct sync — and hash the streamed bytes to match
            // the subscriber's SHA-256 integrity check in RegisterReplicationService.
            var txJson = JsonSerializer.SerializeToUtf8Bytes(tx, CanonicalTransactionJsonOptions);
            var checksum = Convert.ToHexString(SHA256.HashData(txJson)).ToLowerInvariant();

            // tx.TimeStamp is always UTC at the DB layer. The single-arg DateTimeOffset
            // ctor respects Kind; the two-arg form would silently treat an ever-non-UTC
            // value as UTC and drift by the local offset.
            var entry = new Protos.TransactionEntry
            {
                TransactionId = tx.TxId,
                RegisterId = tx.RegisterId,
                TransactionData = ByteString.CopyFrom(txJson),
                Checksum = checksum,
                CreatedAt = new DateTimeOffset(
                    DateTime.SpecifyKind(tx.TimeStamp, DateTimeKind.Utc)).ToUnixTimeMilliseconds()
            };

            await responseStream.WriteAsync(entry, cancellationToken);
            streamed++;
        }

        _logger.LogInformation(
            "PullDocketTransactions from Register Service completed for {RegisterId}: streamed {Streamed}, not found {NotFound}, transient errors {TransientErrors}",
            request.RegisterId, streamed, notFound, transientErrors);
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

        var localPeerId = _configuration.ResolvedPeerId;
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

    /// <summary>
    /// Serves PullDocketChain by reading dockets directly from the local Register Service.
    /// Used when the register is not in the peer service's in-memory cache but exists
    /// in the co-located Register Service (e.g., registers created locally).
    /// </summary>
    private async Task PullDocketChainFromRegisterServiceAsync(
        Protos.DocketChainRequest request,
        IServerStreamWriter<Protos.DocketEntry> responseStream,
        CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var registerClient = scope.ServiceProvider.GetRequiredService<IRegisterServiceClient>();

        var height = await registerClient.GetRegisterHeightAsync(request.RegisterId, cancellationToken);
        if (height < 0)
        {
            // NOTE: this throws NotFound while PullDocketTransactionsFromRegisterServiceAsync
            // returns an empty stream for the same condition. The asymmetry is intentional —
            // the chain-pull contract is entered by the subscriber only after it has
            // confirmed the register exists, so a NotFound here is a bug worth surfacing,
            // whereas the tx-pull fallback is entered speculatively per-docket and must
            // tolerate empty responses without escalating to the subscriber's retry loop.
            throw new RpcException(new Status(
                StatusCode.NotFound,
                $"Register '{request.RegisterId}' not found in Register Service"));
        }

        var fromVersion = request.FromVersion;
        var maxDockets = request.MaxDockets > 0 ? request.MaxDockets : 100;
        var streamed = 0L;

        // height is a COUNT (1 = one docket at index 0), so iterate to height-1 inclusive
        for (var docketNum = fromVersion + 1; docketNum < height && streamed < maxDockets; docketNum++)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var docket = await registerClient.ReadDocketAsync(request.RegisterId, docketNum, cancellationToken);
            if (docket == null)
            {
                _logger.LogWarning(
                    "Docket {DocketNumber} not found for register {RegisterId}, stopping chain",
                    docketNum, request.RegisterId);
                break;
            }

            var entry = new Protos.DocketEntry
            {
                RegisterId = request.RegisterId,
                Version = docket.DocketNumber,
                DocketData = ByteString.CopyFromUtf8(System.Text.Json.JsonSerializer.Serialize(docket)),
                DocketHash = docket.DocketHash,
                PreviousHash = docket.PreviousHash ?? string.Empty,
                CreatedAt = docket.CreatedAt.ToUnixTimeMilliseconds()
            };
            entry.TransactionIds.AddRange(
                docket.Transactions.Select(t => t.TxId));

            await responseStream.WriteAsync(entry, cancellationToken);
            streamed++;
        }

        _logger.LogInformation(
            "PullDocketChain from Register Service completed for {RegisterId}: streamed {Count} dockets (height={Height})",
            request.RegisterId, streamed, height);
    }
}
