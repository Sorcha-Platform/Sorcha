// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Text;

namespace Sorcha.Blueprint.Service.Services.Implementation;

/// <summary>
/// OpenTelemetry metrics for the Timebound Presentation Lifecycle.
/// Counters and histograms are registered with the
/// <c>Sorcha.Blueprint.Service.Presentation</c> meter so they surface via
/// the standard Prometheus exporter configured in ServiceDefaults.
/// </summary>
public sealed class PresentationLifecycleMetrics
{
    /// <summary>Meter name used when publishing Presentation-related metrics.</summary>
    public const string MeterName = "Sorcha.Blueprint.Service.Presentation";

    private readonly Meter _meter;

    private readonly Counter<long> _initiatedTotal;
    private readonly Counter<long> _outcomeTotal;
    private readonly Counter<long> _abandonedTotal;
    private readonly Counter<long> _rateLimitRejected;
    private readonly Histogram<double> _durationSeconds;

    // Feature 119 — seal-aware ordering instruments.
    private readonly Histogram<double> _sealWaitSeconds;
    private readonly Counter<long> _sealTimeoutTotal;
    private readonly Counter<long> _sealRecoveredViaSweeperTotal;
    private long _queueDepthOutcome;
    private long _queueDepthAbandonment;
    private long _queueDepthAdvance;

    // Feature 127 — credential-gate instruments.
    private readonly Histogram<double> _outcomeSignalLatencySeconds;
    private readonly Counter<long> _disclosedClaimsFetchTotal;

    public PresentationLifecycleMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        _meter = meterFactory.Create(MeterName, "1.0.0");

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

        // Feature 119 — seal-aware ordering instruments.
        _sealWaitSeconds = _meter.CreateHistogram<double>(
            name: "sorcha_presentation_seal_wait_seconds",
            unit: "s",
            description: "Duration a queued submission or advancement waited from enqueue to drain.");

        _sealTimeoutTotal = _meter.CreateCounter<long>(
            name: "sorcha_presentation_seal_timeout_total",
            description: "Count of queued entries that timed out waiting for predecessor seal (TTL).");

        _sealRecoveredViaSweeperTotal = _meter.CreateCounter<long>(
            name: "sorcha_presentation_seal_recovered_via_sweeper_total",
            description: "Count of queued entries drained by the recovery sweeper after a missed seal event.");

        // Feature 127 instruments.
        _outcomeSignalLatencySeconds = _meter.CreateHistogram<double>(
            name: "sorcha_presentation_outcome_signal_latency_seconds",
            unit: "s",
            description: "Wall-clock duration from PresentationOutcome write to PresentationOutcomeReady hub publish (server-side; primary SC-004 verification mechanism for Feature 127).");

        _disclosedClaimsFetchTotal = _meter.CreateCounter<long>(
            name: "sorcha_presentation_disclosed_claims_fetch_total",
            description: "Total disclosed-claims endpoint hits, tagged by outcome status (Feature 127).");

        _meter.CreateObservableGauge(
            name: "sorcha_presentation_seal_queue_depth",
            observeValues: () => new[]
            {
                new Measurement<long>(Interlocked.Read(ref _queueDepthOutcome),
                    new KeyValuePair<string, object?>("site", "outcome")),
                new Measurement<long>(Interlocked.Read(ref _queueDepthAbandonment),
                    new KeyValuePair<string, object?>("site", "abandonment")),
                new Measurement<long>(Interlocked.Read(ref _queueDepthAdvance),
                    new KeyValuePair<string, object?>("site", "advance"))
            },
            description: "Current depth of the seal-wait queues, per site.");
    }

    /// <summary>Record enqueue→drain wait latency for a seal-aware queue entry.</summary>
    public void RecordSealWait(string site, double seconds)
    {
        _sealWaitSeconds.Record(seconds, new KeyValuePair<string, object?>("site", site));
    }

    /// <summary>Record a TTL-fail of a queued entry (predecessor never sealed).</summary>
    public void RecordSealTimeout(string site)
    {
        _sealTimeoutTotal.Add(1, new KeyValuePair<string, object?>("site", site));
    }

    /// <summary>Record a sweeper-recovery drain (missed transaction:confirmed event).</summary>
    public void RecordSealRecoveredViaSweeper(string site)
    {
        _sealRecoveredViaSweeperTotal.Add(1, new KeyValuePair<string, object?>("site", site));
    }

    /// <summary>Increment the observable gauge for the given site.</summary>
    public void IncrementSealQueueDepth(string site)
    {
        switch (site)
        {
            case "outcome": Interlocked.Increment(ref _queueDepthOutcome); break;
            case "abandonment": Interlocked.Increment(ref _queueDepthAbandonment); break;
            case "advance": Interlocked.Increment(ref _queueDepthAdvance); break;
        }
    }

    /// <summary>Decrement the observable gauge for the given site (floored at zero).</summary>
    public void DecrementSealQueueDepth(string site)
    {
        switch (site)
        {
            case "outcome":
                if (Interlocked.Read(ref _queueDepthOutcome) > 0) Interlocked.Decrement(ref _queueDepthOutcome);
                break;
            case "abandonment":
                if (Interlocked.Read(ref _queueDepthAbandonment) > 0) Interlocked.Decrement(ref _queueDepthAbandonment);
                break;
            case "advance":
                if (Interlocked.Read(ref _queueDepthAdvance) > 0) Interlocked.Decrement(ref _queueDepthAdvance);
                break;
        }
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
    /// Record a rate-limit rejection. <paramref name="walletAddress"/> is
    /// SHA-256-hashed and the first 8 hex characters used as the label value,
    /// so the full wallet address never lands in metrics or log output and the
    /// label cardinality stays bounded. Hashing (not raw truncation) means the
    /// structural version/prefix bytes of the wallet address do not encode
    /// into the label.
    /// </summary>
    public void RecordRateLimitRejected(string walletAddress, string registerId)
    {
        _rateLimitRejected.Add(1,
            new KeyValuePair<string, object?>("wallet_prefix", HashPrefix(walletAddress)),
            new KeyValuePair<string, object?>("register", registerId));
    }

    private static string HashPrefix(string walletAddress)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(walletAddress));
        return Convert.ToHexString(hash)[..8].ToLowerInvariant();
    }

    public void RecordDuration(string consumer, string kind, double seconds)
    {
        _durationSeconds.Record(seconds,
            new KeyValuePair<string, object?>("consumer", consumer),
            new KeyValuePair<string, object?>("kind", kind));
    }

    /// <summary>
    /// Feature 127 — record the server-side time from outcome-tx write to
    /// PresentationOutcomeReady hub publish. Primary SC-004 verification:
    /// "the presentation-completion signal reaches the council page within
    /// 2 s of PWA signing in 95% of attempts".
    /// </summary>
    public void RecordOutcomeSignalLatency(string kind, double seconds)
    {
        _outcomeSignalLatencySeconds.Record(seconds,
            new KeyValuePair<string, object?>("kind", kind));
    }

    /// <summary>
    /// Feature 127 — record a disclosed-claims endpoint hit. <paramref name="outcome"/>
    /// is the resolved status: <c>"success"</c>, <c>"pending"</c>, <c>"decline"</c>,
    /// <c>"abandoned"</c>, <c>"claims-expired"</c>, <c>"token-invalid"</c>,
    /// <c>"token-mismatch"</c>, or <c>"not-found"</c>.
    /// </summary>
    public void RecordDisclosedClaimsFetch(string outcome)
    {
        _disclosedClaimsFetchTotal.Add(1,
            new KeyValuePair<string, object?>("outcome", outcome));
    }
}
