// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Sorcha.Peer.Service.Observability;

/// <summary>
/// OpenTelemetry metrics for Peer Service observability
/// </summary>
/// <remarks>
/// Provides comprehensive metrics for monitoring:
/// - Connection health and status
/// - Heartbeat latency and reliability
/// - Sync performance and throughput
/// - Push notification delivery rates
/// - Failover events
/// </remarks>
public sealed class PeerServiceMetrics : IDisposable
{
    private readonly Meter _meter;

    // Connection metrics
    private readonly ObservableGauge<int> _connectionStatus;
    private int _currentConnectionStatus = 0; // 0=disconnected, 1=connected, 2=isolated

    // Heartbeat metrics
    private readonly Histogram<double> _heartbeatLatency;

    // Sync metrics
    private readonly Histogram<double> _syncDuration;
    private readonly Counter<long> _syncBlueprintCount;

    // Push notification metrics
    private readonly Counter<long> _pushNotificationsDelivered;
    private readonly Counter<long> _pushNotificationsFailed;

    // Heartbeat rejection metrics
    private readonly Counter<long> _heartbeatRejectionsReceived;

    // Failover metrics
    private readonly Counter<long> _failoverCount;

    // NAT-traversal metrics (Feature 143)
    private readonly ObservableGauge<int> _reverseStreamsActive;
    private int _currentReverseStreamsActive = 0;
    private readonly Histogram<double> _relayForwardDuration;
    private readonly Counter<long> _pathSelectionCount;
    private readonly Counter<long> _anchorFailoverCount;
    private readonly Counter<long> _anchorReconnectCount;

    /// <summary>
    /// Meter name for Peer Service metrics
    /// </summary>
    public const string MeterName = "Sorcha.Peer.Service";

    /// <summary>
    /// Initializes a new instance of the <see cref="PeerServiceMetrics"/> class
    /// </summary>
    public PeerServiceMetrics()
    {
        _meter = new Meter(MeterName, "1.0.0");

        // Connection status gauge (0=disconnected, 1=connected, 2=isolated)
        _connectionStatus = _meter.CreateObservableGauge(
            name: "peer.connection.status",
            observeValue: () => _currentConnectionStatus,
            unit: "status",
            description: "Current connection status (0=disconnected, 1=connected, 2=isolated)");

        // Heartbeat latency histogram (milliseconds)
        _heartbeatLatency = _meter.CreateHistogram<double>(
            name: "peer.heartbeat.latency",
            unit: "ms",
            description: "Heartbeat round-trip time in milliseconds");

        // Sync duration histogram (seconds)
        _syncDuration = _meter.CreateHistogram<double>(
            name: "peer.sync.duration",
            unit: "s",
            description: "System register sync operation duration in seconds");

        // Sync blueprint count counter
        _syncBlueprintCount = _meter.CreateCounter<long>(
            name: "peer.sync.blueprints.count",
            unit: "blueprints",
            description: "Number of blueprints synchronized");

        // Push notification delivery counter
        _pushNotificationsDelivered = _meter.CreateCounter<long>(
            name: "peer.push.notifications.delivered",
            unit: "notifications",
            description: "Number of push notifications successfully delivered");

        // Push notification failure counter
        _pushNotificationsFailed = _meter.CreateCounter<long>(
            name: "peer.push.notifications.failed",
            unit: "notifications",
            description: "Number of failed push notification deliveries");

        // Heartbeat rejection counter (remote peer dropped our registration)
        _heartbeatRejectionsReceived = _meter.CreateCounter<long>(
            name: "peer.heartbeat.rejections",
            unit: "rejections",
            description: "Number of heartbeat rejections received from remote peers");

        // Failover event counter
        _failoverCount = _meter.CreateCounter<long>(
            name: "peer.failover.count",
            unit: "events",
            description: "Number of failover events to different hub nodes");

        // --- NAT-traversal metrics (Feature 143) ---

        // Active reverse streams held by this node as a rendezvous (gauge)
        _reverseStreamsActive = _meter.CreateObservableGauge(
            name: "peer.reverse_streams.active",
            observeValue: () => _currentReverseStreamsActive,
            unit: "streams",
            description: "Number of reverse streams currently held from NAT'd peers (rendezvous role)");

        // Brokered relay-forward latency, tagged by flow (submit|sync)
        _relayForwardDuration = _meter.CreateHistogram<double>(
            name: "peer.relay.forward.duration",
            unit: "ms",
            description: "Latency of brokering a request to a NAT'd peer over its reverse stream");

        // Path-selection outcomes, tagged by path (self|remote)
        _pathSelectionCount = _meter.CreateCounter<long>(
            name: "peer.path.selection",
            unit: "selections",
            description: "Path chosen when routing to a NAT'd peer (self-anchor vs remote anchor)");

        // Anchor-level failovers (next-best anchor chosen after a path failed)
        _anchorFailoverCount = _meter.CreateCounter<long>(
            name: "peer.anchor.failover",
            unit: "events",
            description: "Number of anchor failovers when reaching a NAT'd peer");

        // Anchor reconnects (a dropped reverse-stream anchor re-established)
        _anchorReconnectCount = _meter.CreateCounter<long>(
            name: "peer.anchor.reconnect",
            unit: "events",
            description: "Number of reverse-stream anchor reconnects");
    }

    /// <summary>
    /// Records connection status change
    /// </summary>
    /// <param name="status">Connection status (Disconnected=0, Connected=1, Isolated=2)</param>
    public void RecordConnectionStatus(Core.PeerConnectionStatus status)
    {
        _currentConnectionStatus = status switch
        {
            Core.PeerConnectionStatus.Disconnected => 0,
            Core.PeerConnectionStatus.Connected => 1,
            Core.PeerConnectionStatus.Isolated => 2,
            _ => 0
        };
    }

    /// <summary>
    /// Records heartbeat latency measurement
    /// </summary>
    /// <param name="latencyMs">Latency in milliseconds</param>
    /// <param name="centralNodeId">Hub node identifier</param>
    public void RecordHeartbeatLatency(double latencyMs, string centralNodeId)
    {
        var tags = new TagList
        {
            { "central_node_id", centralNodeId }
        };

        _heartbeatLatency.Record(latencyMs, tags);
    }

    /// <summary>
    /// Records sync operation duration
    /// </summary>
    /// <param name="durationSeconds">Duration in seconds</param>
    /// <param name="syncType">Sync type (Full or Incremental)</param>
    /// <param name="blueprintCount">Number of blueprints synced</param>
    public void RecordSyncOperation(double durationSeconds, string syncType, long blueprintCount)
    {
        var tags = new TagList
        {
            { "sync_type", syncType }
        };

        _syncDuration.Record(durationSeconds, tags);
        _syncBlueprintCount.Add(blueprintCount, tags);
    }

    /// <summary>
    /// Records successful push notification delivery
    /// </summary>
    /// <param name="count">Number of successful deliveries</param>
    public void RecordPushNotificationDelivered(long count = 1)
    {
        _pushNotificationsDelivered.Add(count);
    }

    /// <summary>
    /// Records failed push notification delivery
    /// </summary>
    /// <param name="count">Number of failed deliveries</param>
    public void RecordPushNotificationFailed(long count = 1)
    {
        _pushNotificationsFailed.Add(count);
    }

    /// <summary>
    /// Records a heartbeat rejection from a remote peer.
    /// Tracks per-peer rejection counts for future reliability scoring.
    /// </summary>
    /// <param name="peerId">The peer that rejected our heartbeat</param>
    /// <param name="reason">Rejection reason from the remote peer</param>
    public void RecordHeartbeatRejection(string peerId, string reason)
    {
        var tags = new TagList
        {
            { "peer_id", peerId },
            { "reason", reason }
        };

        _heartbeatRejectionsReceived.Add(1, tags);
    }

    /// <summary>
    /// Records a failover event
    /// </summary>
    /// <param name="fromNode">Source hub node</param>
    /// <param name="toNode">Target hub node</param>
    /// <param name="reason">Failover reason</param>
    public void RecordFailover(string fromNode, string toNode, string reason)
    {
        var tags = new TagList
        {
            { "from_node", fromNode },
            { "to_node", toNode },
            { "reason", reason }
        };

        _failoverCount.Add(1, tags);
    }

    // --- NAT-traversal recorders (Feature 143) ---

    /// <summary>
    /// Sets the current count of active reverse streams held as a rendezvous (gauge source).
    /// </summary>
    /// <param name="count">Number of active reverse streams.</param>
    public void SetReverseStreamsActive(int count) => _currentReverseStreamsActive = count;

    /// <summary>
    /// Records the latency of brokering a request to a NAT'd peer over its reverse stream.
    /// </summary>
    /// <param name="durationMs">Forward duration in milliseconds.</param>
    /// <param name="flow">Flow being brokered ("submit" or "sync").</param>
    public void RecordRelayForward(double durationMs, string flow)
    {
        var tags = new TagList { { "flow", flow } };
        _relayForwardDuration.Record(durationMs, tags);
    }

    /// <summary>
    /// Records the path selected when routing to a NAT'd peer.
    /// </summary>
    /// <param name="path">"self" (this node is an anchor) or "remote" (via another anchor).</param>
    public void RecordPathSelection(string path)
    {
        var tags = new TagList { { "path", path } };
        _pathSelectionCount.Add(1, tags);
    }

    /// <summary>
    /// Records an anchor-level failover to the next-best path when reaching a NAT'd peer.
    /// </summary>
    public void RecordAnchorFailover() => _anchorFailoverCount.Add(1);

    /// <summary>
    /// Records a reverse-stream anchor reconnect.
    /// </summary>
    public void RecordAnchorReconnect() => _anchorReconnectCount.Add(1);

    /// <summary>
    /// Disposes the meter
    /// </summary>
    public void Dispose()
    {
        _meter?.Dispose();
    }
}
