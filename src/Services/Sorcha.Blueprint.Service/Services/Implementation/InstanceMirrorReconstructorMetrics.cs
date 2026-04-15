// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics.Metrics;

namespace Sorcha.Blueprint.Service.Services.Implementation;

/// <summary>
/// Feature 106 Wave D — OpenTelemetry metrics for <c>InstanceMirrorReconstructor</c>.
/// Counters track how many <c>docket:confirmed</c> events arrive, how many transactions
/// carried locally-relevant wallets, and how often mirror rows are created vs updated
/// vs skipped.
/// </summary>
/// <remarks>
/// Contract: <c>specs/106-register-native-credentials/contracts/instance-mirror-reconstructor.md §Metrics</c>.
/// </remarks>
public sealed class InstanceMirrorReconstructorMetrics
{
    public const string MeterName = "Sorcha.Blueprint.InstanceMirrorReconstructor";

    private readonly Counter<long> _docketsObserved;
    private readonly Counter<long> _transactionsInspected;
    private readonly Counter<long> _mirrorsCreated;
    private readonly Counter<long> _mirrorsUpdated;
    private readonly Counter<long> _skippedLocallyAuthoritative;
    private readonly Counter<long> _skippedNoLocalWallet;
    private readonly Counter<long> _errored;
    private readonly Histogram<double> _reconstructionLatencyMs;

    public InstanceMirrorReconstructorMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);

        _docketsObserved = meter.CreateCounter<long>(
            "sorcha.blueprint.instance_mirror.dockets_observed",
            description: "docket:confirmed events received by the reconstructor");

        _transactionsInspected = meter.CreateCounter<long>(
            "sorcha.blueprint.instance_mirror.transactions_inspected",
            description: "Transactions inside observed dockets inspected by the reconstructor");

        _mirrorsCreated = meter.CreateCounter<long>(
            "sorcha.blueprint.instance_mirror.mirrors_created",
            description: "Read-only mirror rows inserted");

        _mirrorsUpdated = meter.CreateCounter<long>(
            "sorcha.blueprint.instance_mirror.mirrors_updated",
            description: "Read-only mirror rows updated by a later observation");

        _skippedLocallyAuthoritative = meter.CreateCounter<long>(
            "sorcha.blueprint.instance_mirror.skipped_locally_authoritative",
            description: "Transactions for instances already locally authoritative on this node");

        _skippedNoLocalWallet = meter.CreateCounter<long>(
            "sorcha.blueprint.instance_mirror.skipped_no_local_wallet",
            description: "Transactions whose participants contain no locally-registered wallet");

        _errored = meter.CreateCounter<long>(
            "sorcha.blueprint.instance_mirror.errored",
            description: "Unhandled exception during reconstruction (dependency throw caught by catch-all)");

        _reconstructionLatencyMs = meter.CreateHistogram<double>(
            "sorcha.blueprint.instance_mirror.reconstruction_latency_ms",
            unit: "ms",
            description: "End-to-end latency from docket:confirmed arrival to last mirror row persisted");
    }

    public void RecordDocketObserved() => _docketsObserved.Add(1);
    public void RecordTransactionInspected() => _transactionsInspected.Add(1);
    public void RecordMirrorCreated() => _mirrorsCreated.Add(1);
    public void RecordMirrorUpdated() => _mirrorsUpdated.Add(1);
    public void RecordSkippedLocallyAuthoritative() => _skippedLocallyAuthoritative.Add(1);
    public void RecordSkippedNoLocalWallet() => _skippedNoLocalWallet.Add(1);
    public void RecordErrored() => _errored.Add(1);
    public void RecordReconstructionLatency(double latencyMs) => _reconstructionLatencyMs.Record(latencyMs);
}
