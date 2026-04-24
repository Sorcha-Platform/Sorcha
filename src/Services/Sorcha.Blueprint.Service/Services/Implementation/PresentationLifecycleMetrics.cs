// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics.Metrics;

namespace Sorcha.Blueprint.Service.Services.Implementation;

/// <summary>
/// OpenTelemetry metrics for the Timebound Presentation Lifecycle.
/// Counters and histograms are registered with the
/// <c>Sorcha.Blueprint.Service.Presentation</c> meter so they surface via
/// the standard Prometheus exporter configured in ServiceDefaults.
/// </summary>
public sealed class PresentationLifecycleMetrics : IDisposable
{
    /// <summary>Meter name used when publishing Presentation-related metrics.</summary>
    public const string MeterName = "Sorcha.Blueprint.Service.Presentation";

    private readonly Meter _meter;

    private readonly Counter<long> _initiatedTotal;
    private readonly Counter<long> _outcomeTotal;
    private readonly Counter<long> _abandonedTotal;
    private readonly Counter<long> _rateLimitRejected;
    private readonly Histogram<double> _durationSeconds;

    public PresentationLifecycleMetrics()
    {
        _meter = new Meter(MeterName, "1.0.0");

        _initiatedTotal = _meter.CreateCounter<long>(
            name: "sorcha_presentation_initiated_total",
            description: "Total number of PresentationInitiated transactions written.");

        _outcomeTotal = _meter.CreateCounter<long>(
            name: "sorcha_presentation_outcome_total",
            description: "Total number of PresentationOutcome transactions written.");

        _abandonedTotal = _meter.CreateCounter<long>(
            name: "sorcha_presentation_abandoned_total",
            description: "Total number of PresentationAbandoned transactions written.");

        _rateLimitRejected = _meter.CreateCounter<long>(
            name: "sorcha_presentation_ratelimit_rejected_total",
            description: "Total number of presentation submissions rejected for exceeding the per-wallet-per-register quota.");

        _durationSeconds = _meter.CreateHistogram<double>(
            name: "sorcha_presentation_duration_seconds",
            unit: "s",
            description: "Wall-clock duration from PresentationInitiated write to PresentationOutcome write.");
    }

    public void RecordInitiated(string consumer)
    {
        _initiatedTotal.Add(1, new KeyValuePair<string, object?>("consumer", consumer));
    }

    public void RecordOutcome(string consumer, string kind, string? reason = null)
    {
        _outcomeTotal.Add(1,
            new KeyValuePair<string, object?>("consumer", consumer),
            new KeyValuePair<string, object?>("kind", kind),
            new KeyValuePair<string, object?>("reason", reason ?? string.Empty));
    }

    public void RecordAbandoned(string consumer, string blueprintId)
    {
        _abandonedTotal.Add(1,
            new KeyValuePair<string, object?>("consumer", consumer),
            new KeyValuePair<string, object?>("blueprint", blueprintId));
    }

    /// <summary>
    /// Record a rate-limit rejection. <paramref name="walletAddress"/> is hashed/
    /// truncated to an 8-char prefix to avoid leaking full wallet addresses into
    /// metric label cardinality and operator logs.
    /// </summary>
    public void RecordRateLimitRejected(string walletAddress, string registerId)
    {
        var prefix = walletAddress.Length >= 8 ? walletAddress[..8] : walletAddress;
        _rateLimitRejected.Add(1,
            new KeyValuePair<string, object?>("wallet_prefix", prefix),
            new KeyValuePair<string, object?>("register", registerId));
    }

    public void RecordDuration(string consumer, string kind, double seconds)
    {
        _durationSeconds.Record(seconds,
            new KeyValuePair<string, object?>("consumer", consumer),
            new KeyValuePair<string, object?>("kind", kind));
    }

    public void Dispose() => _meter.Dispose();
}
