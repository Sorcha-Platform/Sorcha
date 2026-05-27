// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics.Metrics;

namespace Sorcha.ServiceClients.Did;

/// <summary>
/// OpenTelemetry meter and counters for DID resolver instrumentation
/// (Feature 120 T015 — <c>Sorcha.Did.Resolver</c>).
/// </summary>
/// <remarks>
/// Counter set covers cache outcomes, cross-resolution mismatches, and
/// alsoKnownAs unreachability. Counters are tagged with <c>method</c>
/// (did method, e.g. <c>web</c>, <c>sorcha</c>, <c>key</c>) and, where
/// applicable, <c>kind</c> (<c>primary</c> | <c>alsoKnownAs</c>).
/// </remarks>
public sealed class DidResolverMetrics : IDisposable
{
    /// <summary>Meter name — kept stable for OTel exporters and dashboards.</summary>
    public const string MeterName = "Sorcha.Did.Resolver";

    private readonly Meter _meter;

    /// <summary>Cache-hit counter (tagged: method, kind).</summary>
    public Counter<long> CacheHit { get; }

    /// <summary>Cache-miss counter (tagged: method, kind).</summary>
    public Counter<long> CacheMiss { get; }

    /// <summary>Cross-resolution mismatch counter — incremented when no shared keys are found across the alsoKnownAs chain.</summary>
    public Counter<long> CrossResolveMismatch { get; }

    /// <summary>alsoKnownAs unreachable counter — incremented when a linked DID fails to resolve.</summary>
    public Counter<long> AlsoKnownAsUnreachable { get; }

    /// <summary>DI-friendly constructor.</summary>
    public DidResolverMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        _meter = meterFactory.Create(MeterName, "1.0.0");

        CacheHit = _meter.CreateCounter<long>(
            "sorcha_did_resolver_cache_hit_total",
            description: "DID resolver cache hits, tagged by method and kind (primary|alsoKnownAs).");
        CacheMiss = _meter.CreateCounter<long>(
            "sorcha_did_resolver_cache_miss_total",
            description: "DID resolver cache misses, tagged by method and kind.");
        CrossResolveMismatch = _meter.CreateCounter<long>(
            "sorcha_did_resolver_cross_resolve_mismatch_total",
            description: "alsoKnownAs cross-resolution found no shared verification keys.");
        AlsoKnownAsUnreachable = _meter.CreateCounter<long>(
            "sorcha_did_resolver_alsoKnownAs_unreachable_total",
            description: "alsoKnownAs link failed to resolve.");
    }

    /// <inheritdoc />
    public void Dispose() => _meter.Dispose();
}
