// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics.Metrics;

namespace Sorcha.Wallet.Service.Services.Implementation;

/// <summary>
/// OpenTelemetry metrics for notification delivery in the Wallet Service.
/// Tracks delivery latency, delivery outcomes, and digest processing.
/// </summary>
public sealed class NotificationMetrics
{
    public const string MeterName = "Sorcha.Wallet.Notifications";

    private readonly Histogram<double> _deliveryLatencyMs;
    private readonly Counter<long> _deliveredRealTime;
    private readonly Counter<long> _queuedForDigest;
    private readonly Counter<long> _rateLimited;
    private readonly Counter<long> _noUserFound;
    private readonly Counter<long> _digestsDelivered;
    private readonly Histogram<double> _digestEventCount;

    public NotificationMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);

        _deliveryLatencyMs = meter.CreateHistogram<double>(
            "sorcha.wallet.notification.delivery_latency_ms",
            unit: "ms",
            description: "End-to-end notification delivery latency in milliseconds");

        _deliveredRealTime = meter.CreateCounter<long>(
            "sorcha.wallet.notification.delivered_realtime",
            description: "Notifications delivered in real-time via SignalR");

        _queuedForDigest = meter.CreateCounter<long>(
            "sorcha.wallet.notification.queued_for_digest",
            description: "Notifications queued for digest batching");

        _rateLimited = meter.CreateCounter<long>(
            "sorcha.wallet.notification.rate_limited",
            description: "Notifications rate-limited and overflowed to digest");

        _noUserFound = meter.CreateCounter<long>(
            "sorcha.wallet.notification.no_user_found",
            description: "Notifications with no matching user (bloom filter false positive)");

        _digestsDelivered = meter.CreateCounter<long>(
            "sorcha.wallet.digest.delivered",
            description: "Total digest notifications delivered");

        _digestEventCount = meter.CreateHistogram<double>(
            "sorcha.wallet.digest.event_count",
            description: "Number of events per digest notification");
    }

    public void RecordDeliveryLatency(double latencyMs) => _deliveryLatencyMs.Record(latencyMs);
    public void RecordDeliveredRealTime() => _deliveredRealTime.Add(1);
    public void RecordQueuedForDigest() => _queuedForDigest.Add(1);
    public void RecordRateLimited() => _rateLimited.Add(1);
    public void RecordNoUserFound() => _noUserFound.Add(1);
    public void RecordDigestDelivered(int eventCount)
    {
        _digestsDelivered.Add(1);
        _digestEventCount.Record(eventCount);
    }
}
