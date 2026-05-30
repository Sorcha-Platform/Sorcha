// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sorcha.Peer.Service.Connection;
using Sorcha.Peer.Service.Core;
using Sorcha.Peer.Service.Discovery;
using Sorcha.Peer.Service.Observability;
using Sorcha.Peer.Service.Protos;

namespace Sorcha.Peer.Service.Communication;

/// <summary>
/// Core relay communication primitive. A NAT'd node dials out to one or more public anchors and
/// holds a persistent reverse stream to each (Feature 143 US2 — multi-anchor); brokered submit/sync
/// requests travel back over those streams. Routing prefers the anchor that IS the target, then the
/// lowest-latency connected anchor, with failover (US3). Supports fire-and-forget (SendViaRelayAsync)
/// and request/response correlation (SendAndWaitAsync).
/// </summary>
public class RelayCommunicationService
{
    private readonly ILogger<RelayCommunicationService> _logger;
    private readonly PeerConnectionPool _connectionPool;
    private readonly PeerListManager _peerListManager;
    private readonly PeerServiceConfiguration _configuration;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<PeerMessage>> _pendingCorrelations = new();
    private readonly ConcurrentDictionary<string, Anchor> _anchors = new();
    private readonly Lazy<RelayMessageHandler> _relayMessageHandler;
    private readonly ReverseStreamManager? _reverseStreams;
    private readonly PeerServiceMetrics? _metrics;

    /// <summary>
    /// Default timeout for relay request/response correlation (30 seconds)
    /// </summary>
    public static readonly TimeSpan DefaultCorrelationTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Keepalive interval for reverse stream ping messages.
    /// </summary>
    public static readonly TimeSpan KeepaliveInterval = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Maximum backoff delay for reverse stream reconnection.
    /// </summary>
    public static readonly TimeSpan MaxReconnectBackoff = TimeSpan.FromSeconds(60);

    /// <summary>
    /// True if this node currently holds at least one connected reverse stream to an anchor.
    /// </summary>
    public bool IsReverseStreamActive => _anchors.Values.Any(a => a.Connected);

    /// <summary>
    /// Snapshot of this node's anchors (Feature 143 US2/US3): the public peers it holds reverse
    /// streams to, each with its connection state and last-known latency.
    /// </summary>
    public IReadOnlyCollection<AnchorInfo> Anchors =>
        _anchors.Values.Select(a => new AnchorInfo(a.AnchorId, a.Connected, ResolveLatencyMs(a.AnchorId))).ToList();

    public RelayCommunicationService(
        ILogger<RelayCommunicationService> logger,
        PeerConnectionPool connectionPool,
        PeerListManager peerListManager,
        IOptions<PeerServiceConfiguration> configuration,
        Lazy<RelayMessageHandler> relayMessageHandler,
        ReverseStreamManager? reverseStreams = null,
        PeerServiceMetrics? metrics = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _connectionPool = connectionPool ?? throw new ArgumentNullException(nameof(connectionPool));
        _peerListManager = peerListManager ?? throw new ArgumentNullException(nameof(peerListManager));
        _configuration = configuration?.Value ?? throw new ArgumentNullException(nameof(configuration));
        _relayMessageHandler = relayMessageHandler ?? throw new ArgumentNullException(nameof(relayMessageHandler));
        // Feature 143: optional so existing call sites/tests compile unchanged; production DI injects both.
        // When present, this node can broker to NAT'd peers whose reverse streams it holds (rendezvous role).
        _reverseStreams = reverseStreams;
        _metrics = metrics;
    }

    /// <summary>Maps a relay message type to the metric flow tag (submit|sync|other).</summary>
    private static string FlowFor(MessageType messageType) => messageType switch
    {
        MessageType.TransactionNotification => "submit",
        MessageType.SubmitTransactionRequest or MessageType.SubmitTransactionResponse => "submit",
        MessageType.RegisterSyncRequest or MessageType.RegisterSyncResponse
            or MessageType.TransactionDataRequest or MessageType.TransactionDataResponse => "sync",
        _ => "other"
    };

    /// <summary>
    /// True when this node can reach <paramref name="peerId"/> over a held reverse stream — i.e. the
    /// peer is a NAT'd node that dialled in and is registered in the <see cref="ReverseStreamManager"/>.
    /// Replication source-peer selection uses this to engage the relay path for a NAT'd owner that
    /// self-registered with a placeholder (non-empty, undialable) address (Feature 143).
    /// </summary>
    public bool CanReachViaReverseStream(string peerId) =>
        _reverseStreams is not null && _reverseStreams.TryGetStream(peerId, out _);

    /// <summary>
    /// Sends a message to a peer via relay (fire-and-forget). Order: (1) rendezvous — if this node
    /// holds the target's reverse stream, broker over it; (2) outbound anchors — if this node is NAT'd
    /// and dialled anchors, write over the best one (target-match → lowest-latency) with failover;
    /// (3) unary SendMessage fallback. Returns true if forwarded.
    /// </summary>
    public async Task<bool> SendViaRelayAsync(
        string targetPeerId,
        MessageType messageType,
        object payload,
        CancellationToken cancellationToken = default)
    {
        var peerMessage = CreatePeerMessage(targetPeerId, messageType, payload);

        // (1) Rendezvous role (Feature 143): if this node holds the target's reverse stream — i.e. the
        // target is a NAT'd peer that dialled in — broker directly over that stream. Self-anchor path.
        if (_reverseStreams is not null && _reverseStreams.TryGetStream(targetPeerId, out _))
        {
            var startTs = Stopwatch.GetTimestamp();
            try
            {
                await _reverseStreams.DispatchAsync(targetPeerId, peerMessage, cancellationToken);
                _metrics?.RecordRelayForward(Stopwatch.GetElapsedTime(startTs).TotalMilliseconds, FlowFor(messageType));
                _metrics?.RecordPathSelection("self");
                _logger.LogDebug(
                    "Relay message {MessageType} brokered to NAT'd peer {TargetPeerId} over its reverse stream",
                    messageType, targetPeerId);
                return true;
            }
            catch (RpcException ex)
            {
                _logger.LogDebug(ex,
                    "Reverse-stream broker to {TargetPeerId} failed, falling back to outbound anchors / unary",
                    targetPeerId);
                // Fall through — the held stream may have just dropped.
            }
        }

        // (2) Outbound anchors (this node is NAT'd and dialled out to anchor(s); Feature 143 US2/US3).
        // Prefer the anchor that IS the target, then the lowest-latency connected anchor; fail over.
        var ordered = OrderAnchorsForSend(SnapshotAnchors(), targetPeerId);
        for (var i = 0; i < ordered.Count; i++)
        {
            var anchor = ordered[i];
            if (!anchor.Connected || anchor.Call is null) continue;
            try
            {
                await WriteToAnchorAsync(anchor, peerMessage, cancellationToken);
                _logger.LogDebug(
                    "Relay message {MessageType} to {TargetPeerId} via anchor {Anchor}",
                    messageType, targetPeerId, anchor.AnchorId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex,
                    "Anchor {Anchor} send failed for {MessageType} to {TargetPeerId}, failing over",
                    anchor.AnchorId, messageType, targetPeerId);
                if (i < ordered.Count - 1) _metrics?.RecordAnchorFailover();
            }
        }

        // (3) Fall back to unary SendMessage via the connection pool's seed channel.
        var seedChannel = GetSeedChannel();
        if (seedChannel == null)
        {
            _logger.LogWarning("No anchor or seed channel available for relay to peer {TargetPeerId}", targetPeerId);
            return false;
        }

        try
        {
            var client = new PeerCommunication.PeerCommunicationClient(seedChannel);
            var ack = await client.SendMessageAsync(peerMessage, cancellationToken: cancellationToken);

            if (ack.Received)
            {
                _logger.LogDebug("Relay message {MessageType} sent to {TargetPeerId} via unary relay",
                    messageType, targetPeerId);
            }

            return ack.Received;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to relay {MessageType} to peer {TargetPeerId}",
                messageType, targetPeerId);
            await _connectionPool.RecordFailureAsync(targetPeerId);
            return false;
        }
    }

    /// <summary>
    /// Sends a request via relay and waits for a correlated response.
    /// Returns the deserialized response, or null on timeout/failure.
    /// </summary>
    public async Task<TResponse?> SendAndWaitAsync<TResponse>(
        string targetPeerId,
        MessageType requestType,
        object request,
        string correlationId,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) where TResponse : class
    {
        var tcs = new TaskCompletionSource<PeerMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingCorrelations[correlationId] = tcs;

        try
        {
            var sent = await SendViaRelayAsync(targetPeerId, requestType, request, cancellationToken);
            if (!sent)
            {
                _pendingCorrelations.TryRemove(correlationId, out _);
                return null;
            }

            var effectiveTimeout = timeout ?? DefaultCorrelationTimeout;
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(effectiveTimeout);

            try
            {
                var responseMessage = await tcs.Task.WaitAsync(timeoutCts.Token);
                var json = responseMessage.Payload.ToStringUtf8();
                return JsonSerializer.Deserialize<TResponse>(json);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(
                    "Relay correlation timeout for {CorrelationId} to peer {TargetPeerId} ({Timeout}s)",
                    correlationId, targetPeerId, effectiveTimeout.TotalSeconds);
                return null;
            }
        }
        finally
        {
            _pendingCorrelations.TryRemove(correlationId, out _);
        }
    }

    /// <summary>
    /// Completes a pending correlation with an incoming response message.
    /// Called by RelayMessageHandler when a response arrives via relay.
    /// Returns true if a pending correlation was found and completed.
    /// </summary>
    public bool CompleteCorrelation(string correlationId, PeerMessage responseMessage)
    {
        if (_pendingCorrelations.TryRemove(correlationId, out var tcs))
        {
            tcs.TrySetResult(responseMessage);
            _logger.LogDebug("Completed relay correlation {CorrelationId}", correlationId);
            return true;
        }

        _logger.LogDebug("No pending correlation for {CorrelationId} (expired or already completed)", correlationId);
        return false;
    }

    /// <summary>
    /// Gets the number of pending correlation entries (for diagnostics).
    /// </summary>
    public int PendingCorrelationCount => _pendingCorrelations.Count;

    /// <summary>
    /// Establishes and maintains a reverse stream to EACH configured anchor (seed) concurrently
    /// (Feature 143 US2). Each anchor reconnects independently with backoff; losing one leaves the
    /// others serving. Runs until <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    public async Task EstablishReverseStreamAsync(CancellationToken cancellationToken = default)
    {
        var seeds = GetAnchorSeeds();
        if (seeds.Count == 0)
        {
            _logger.LogDebug("No seed nodes configured for reverse-stream anchoring; nothing to establish");
            return;
        }

        _logger.LogInformation("Establishing reverse streams to {Count} anchor(s): {Anchors}",
            seeds.Count, string.Join(", ", seeds.Select(s => s.AnchorId)));

        var tasks = seeds.Select(s => MaintainAnchorAsync(s.AnchorId, s.Address, cancellationToken)).ToArray();
        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Maintains a single anchor's reverse stream: connect → identify → receive + keepalive →
    /// reconnect with backoff on drop. One instance per configured anchor.
    /// </summary>
    private async Task MaintainAnchorAsync(string anchorId, string address, CancellationToken cancellationToken)
    {
        var anchor = _anchors.GetOrAdd(anchorId, id => new Anchor { AnchorId = id, Address = address });
        var backoffMs = 0;
        var everConnected = false;

        while (!cancellationToken.IsCancellationRequested)
        {
            Grpc.Net.Client.GrpcChannel? channel = null;
            CancellationTokenSource? cts = null;
            try
            {
                // Dedicated channel per anchor — do not share the heartbeat connection pool, which
                // cycles channels on failures and would kill an in-flight stream.
                channel = Grpc.Net.Client.GrpcChannel.ForAddress(address, new Grpc.Net.Client.GrpcChannelOptions
                {
                    HttpHandler = new SocketsHttpHandler
                    {
                        PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
                        KeepAlivePingDelay = TimeSpan.FromSeconds(30),
                        KeepAlivePingTimeout = TimeSpan.FromSeconds(15),
                        EnableMultipleHttp2Connections = true
                    }
                });

                cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var client = new PeerCommunication.PeerCommunicationClient(channel);
                var call = client.Stream(cancellationToken: cts.Token);
                anchor.Call = call;
                anchor.Connected = true;
                backoffMs = 0;

                if (everConnected)
                {
                    _metrics?.RecordAnchorReconnect();
                    _logger.LogInformation("Reverse stream re-established to anchor {Anchor}", anchorId);
                }
                else
                {
                    _logger.LogInformation("Reverse stream established to anchor {Anchor}", anchorId);
                }
                everConnected = true;

                // Identify immediately so the rendezvous registers this stream before the first keepalive.
                var hello = new PeerMessage
                {
                    SenderPeerId = _configuration.ResolvedPeerId,
                    RecipientPeerId = string.Empty,
                    MessageType = MessageType.Heartbeat,
                    Payload = Google.Protobuf.ByteString.Empty,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                };
                await WriteToAnchorAsync(anchor, hello, cts.Token);

                var receive = RunAnchorReceiveLoopAsync(anchor, cts.Token);
                var keepalive = RunAnchorKeepaliveLoopAsync(anchor, cts.Token);

                await Task.WhenAny(receive, keepalive);
                cts.Cancel();
                try { await Task.WhenAll(receive, keepalive); } catch (OperationCanceledException) { }

                _logger.LogWarning("Reverse stream to anchor {Anchor} disconnected, reconnecting after backoff", anchorId);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled && cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Reverse stream error to anchor {Anchor}, reconnecting after backoff", anchorId);
            }
            finally
            {
                anchor.Connected = false;
                try
                {
                    if (anchor.Call is not null) await anchor.Call.RequestStream.CompleteAsync();
                }
                catch { /* ignore cleanup errors */ }
                anchor.Call?.Dispose();
                anchor.Call = null;
                cts?.Dispose();
                channel?.Dispose();
            }

            backoffMs = await ApplyAnchorBackoffAsync(backoffMs, anchorId, cancellationToken);
        }

        anchor.Connected = false;
    }

    private async Task RunAnchorReceiveLoopAsync(Anchor anchor, CancellationToken cancellationToken)
    {
        var call = anchor.Call;
        if (call is null) return;

        try
        {
            await foreach (var message in call.ResponseStream.ReadAllAsync(cancellationToken))
            {
                anchor.LastActivityTicks = DateTimeOffset.UtcNow.UtcTicks;
                try
                {
                    await _relayMessageHandler.Value.HandleAsync(message, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Error handling reverse stream message {MessageType} from {SenderPeerId} (anchor {Anchor})",
                        message.MessageType, message.SenderPeerId, anchor.AnchorId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Reverse stream receive loop cancelled (anchor {Anchor})", anchor.AnchorId);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
        {
            _logger.LogDebug("Reverse stream receive loop cancelled (gRPC, anchor {Anchor})", anchor.AnchorId);
        }
    }

    private async Task RunAnchorKeepaliveLoopAsync(Anchor anchor, CancellationToken cancellationToken)
    {
        var senderId = _configuration.ResolvedPeerId;
        try
        {
            using var timer = new PeriodicTimer(KeepaliveInterval);
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                var ping = new PeerMessage
                {
                    SenderPeerId = senderId,
                    RecipientPeerId = string.Empty,
                    MessageType = MessageType.Heartbeat,
                    Payload = Google.Protobuf.ByteString.Empty,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                };

                await WriteToAnchorAsync(anchor, ping, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Reverse stream keepalive loop cancelled (anchor {Anchor})", anchor.AnchorId);
        }
    }

    /// <summary>
    /// Serializes writes to a single anchor's request stream — gRPC client streams are NOT safe for
    /// concurrent writes, and keepalive + send + hello can race on the same stream.
    /// </summary>
    private static async Task WriteToAnchorAsync(Anchor anchor, PeerMessage message, CancellationToken cancellationToken)
    {
        var call = anchor.Call ?? throw new InvalidOperationException($"Anchor {anchor.AnchorId} has no active stream");
        await anchor.WriteLock.WaitAsync(cancellationToken);
        try
        {
            await call.RequestStream.WriteAsync(message, cancellationToken);
            anchor.LastActivityTicks = DateTimeOffset.UtcNow.UtcTicks;
        }
        finally
        {
            anchor.WriteLock.Release();
        }
    }

    private async Task<int> ApplyAnchorBackoffAsync(int backoffMs, string anchorId, CancellationToken cancellationToken)
    {
        backoffMs = backoffMs == 0
            ? 2000
            : Math.Min(backoffMs * 2, (int)MaxReconnectBackoff.TotalMilliseconds);

        _logger.LogDebug("Anchor {Anchor} reconnect backoff: {BackoffMs}ms", anchorId, backoffMs);
        try
        {
            await Task.Delay(backoffMs, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }

        return backoffMs;
    }

    /// <summary>
    /// The configured anchor seeds (Feature 143 US2 — one reverse stream per seed). Each carries the
    /// anchor peer id (so a response to that anchor target-matches) and the gRPC channel address.
    /// </summary>
    private List<(string AnchorId, string Address)> GetAnchorSeeds()
    {
        var result = new List<(string, string)>();
        var seeds = _configuration.SeedNodes?.SeedNodes;
        if (seeds is not null)
        {
            foreach (var s in seeds.Where(s => !string.IsNullOrWhiteSpace(s.Hostname)))
            {
                var anchorId = string.IsNullOrWhiteSpace(s.NodeId) ? s.Hostname : s.NodeId;
                result.Add((anchorId, s.GrpcChannelAddress));
            }
        }

        return result;
    }

    private List<Anchor> SnapshotAnchors()
    {
        var snapshot = _anchors.Values.Where(a => a.Connected && a.Call is not null).ToList();
        // Refresh each anchor's latency from the heartbeat-measured value so send-ordering (US3)
        // prefers the genuinely lowest-latency anchor.
        foreach (var a in snapshot) a.LatencyMs = ResolveLatencyMs(a.AnchorId);
        return snapshot;
    }

    /// <summary>
    /// Orders candidate anchors for sending to <paramref name="targetPeerId"/> (Feature 143 US3):
    /// the anchor that IS the target first (zero-extra-hop), then lowest measured latency, then most
    /// recently active. Pure + static so the selection policy is unit-testable.
    /// </summary>
    internal static List<Anchor> OrderAnchorsForSend(IReadOnlyCollection<Anchor> anchors, string targetPeerId) =>
        anchors
            .OrderByDescending(a => a.AnchorId == targetPeerId)
            .ThenBy(a => a.LatencyMs <= 0 ? int.MaxValue : a.LatencyMs)
            .ThenByDescending(a => a.LastActivityTicks)
            .ToList();

    private int ResolveLatencyMs(string anchorId)
    {
        var peer = _peerListManager.GetPeer(anchorId);
        return peer?.AverageLatencyMs ?? 0;
    }

    private PeerMessage CreatePeerMessage(string targetPeerId, MessageType messageType, object payload)
    {
        var senderId = _configuration.ResolvedPeerId;
        var payloadJson = JsonSerializer.Serialize(payload);

        return new PeerMessage
        {
            SenderPeerId = senderId,
            RecipientPeerId = targetPeerId,
            MessageType = messageType,
            Payload = Google.Protobuf.ByteString.CopyFromUtf8(payloadJson),
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
    }

    private Grpc.Net.Client.GrpcChannel? GetSeedChannel()
    {
        var activeChannels = _connectionPool.GetAllActiveChannels();

        foreach (var (peerId, channel) in activeChannels)
        {
            var peer = _peerListManager.GetPeer(peerId);
            if (peer?.IsSeedNode == true)
            {
                return channel;
            }
        }

        return null;
    }

    /// <summary>One reverse stream this NAT'd node holds to a public anchor (Feature 143 US2).</summary>
    internal sealed class Anchor
    {
        public required string AnchorId { get; init; }
        public required string Address { get; init; }
        public volatile bool Connected;
        public AsyncDuplexStreamingCall<PeerMessage, PeerMessage>? Call;
        public long LastActivityTicks = DateTimeOffset.UtcNow.UtcTicks;
        /// <summary>Best-known latency to this anchor in ms (0 = unknown); used by send-ordering.</summary>
        public int LatencyMs;
        public readonly SemaphoreSlim WriteLock = new(1, 1);
    }

    /// <summary>Public snapshot of an anchor (Feature 143 US2/US3).</summary>
    public readonly record struct AnchorInfo(string AnchorId, bool Connected, int LatencyMs);
}
