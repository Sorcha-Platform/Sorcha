// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics.Metrics;

namespace Sorcha.Register.Service.Services.Implementation;

/// <summary>
/// OpenTelemetry metrics for inbound transaction routing in the Register Service.
/// Tracks bloom filter hit rate and recovery throughput.
/// </summary>
public sealed class InboundRoutingMetrics
{
    public const string MeterName = "Sorcha.Register.InboundRouting";

    private readonly Counter<long> _bloomFilterChecks;
    private readonly Counter<long> _bloomFilterHits;
    private readonly Counter<long> _bloomFilterMisses;
    private readonly Counter<long> _recoveryDocketsProcessed;
    private readonly Histogram<double> _recoveryDocketsPerSecond;
    private readonly Counter<long> _bloomFilterRebuilds;

    public InboundRoutingMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);

        _bloomFilterChecks = meter.CreateCounter<long>(
            "sorcha.register.bloom_filter.checks",
            description: "Total bloom filter address checks");

        _bloomFilterHits = meter.CreateCounter<long>(
            "sorcha.register.bloom_filter.hits",
            description: "Bloom filter positive matches (address found)");

        _bloomFilterMisses = meter.CreateCounter<long>(
            "sorcha.register.bloom_filter.misses",
            description: "Bloom filter negative matches (address not found)");

        _recoveryDocketsProcessed = meter.CreateCounter<long>(
            "sorcha.register.recovery.dockets_processed",
            description: "Total dockets processed during recovery");

        _recoveryDocketsPerSecond = meter.CreateHistogram<double>(
            "sorcha.register.recovery.dockets_per_second",
            unit: "dockets/s",
            description: "Recovery docket processing throughput");

        _bloomFilterRebuilds = meter.CreateCounter<long>(
            "sorcha.register.bloom_filter.rebuilds",
            description: "Total bloom filter rebuild operations");
    }

    public void RecordBloomFilterCheck() => _bloomFilterChecks.Add(1);
    public void RecordBloomFilterHit() => _bloomFilterHits.Add(1);
    public void RecordBloomFilterMiss() => _bloomFilterMisses.Add(1);
    public void RecordRecoveryDocketProcessed() => _recoveryDocketsProcessed.Add(1);
    public void RecordRecoveryThroughput(double docketsPerSecond) => _recoveryDocketsPerSecond.Record(docketsPerSecond);
    public void RecordBloomFilterRebuild() => _bloomFilterRebuilds.Add(1);
}
