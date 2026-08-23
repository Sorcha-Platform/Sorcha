// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics.Metrics;

namespace Sorcha.Blueprint.Service.Services.Implementation;

/// <summary>
/// Feature 145 — OpenTelemetry instruments for the <see cref="InstanceProjector"/> (meter
/// <c>Sorcha.Blueprint.Instances</c>, on the ServiceDefaults export allowlist). Tracks docket
/// observation, transactions folded vs idempotently skipped, and projection latency.
/// </summary>
public sealed class InstanceProjectorMetrics
{
    public const string MeterName = "Sorcha.Blueprint.Instances";

    private readonly Counter<long> _docketsObserved;
    private readonly Counter<long> _transactionsFolded;
    private readonly Counter<long> _transactionsSkippedIdempotent;
    private readonly Counter<long> _transactionsSkippedNotInstanceScoped;
    private readonly Counter<long> _errored;
    private readonly Counter<long> _pinFallback;
    private readonly Counter<long> _pinMismatch;
    private readonly Histogram<double> _foldLatencyMs;

    public InstanceProjectorMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);

        _docketsObserved = meter.CreateCounter<long>(
            "sorcha.blueprint.instance_projection.dockets_observed",
            description: "docket:confirmed events observed by the instance projector");

        _transactionsFolded = meter.CreateCounter<long>(
            "sorcha.blueprint.instance_projection.transactions_folded",
            description: "Sealed action transactions folded into an instance projection");

        _transactionsSkippedIdempotent = meter.CreateCounter<long>(
            "sorcha.blueprint.instance_projection.transactions_skipped_idempotent",
            description: "Sealed transactions skipped because already applied (watermark)");

        _transactionsSkippedNotInstanceScoped = meter.CreateCounter<long>(
            "sorcha.blueprint.instance_projection.transactions_skipped_not_instance_scoped",
            description: "Transactions skipped because they carry no instance/blueprint metadata");

        _errored = meter.CreateCounter<long>(
            "sorcha.blueprint.instance_projection.errored",
            description: "Unhandled errors while folding a transaction (caught, projection continues)");

        // Feature 194. These two are how the pin is judged, and they answer different questions.
        //
        // The FALLBACK counter is the POSITIVE acceptance check for the whole feature: every failure
        // mode of pinning degrades to the old "always latest" behaviour rather than to an error, so
        // the absence of errors proves nothing. A register created after the deploy must show ZERO
        // here. It is also what makes the fallback removable on evidence rather than on hope.
        _pinFallback = meter.CreateCounter<long>(
            "sorcha.blueprint.instance_projection.pin_fallback",
            description: "Folds of a transaction carrying no blueprint pin (pre-Feature-194 fallback)");

        _pinMismatch = meter.CreateCounter<long>(
            "sorcha.blueprint.instance_projection.pin_mismatch",
            description: "Folds REFUSED because the transaction claimed a different blueprint definition than the instance is pinned to");

        _foldLatencyMs = meter.CreateHistogram<double>(
            "sorcha.blueprint.instance_projection.fold_latency_ms",
            unit: "ms",
            description: "End-to-end latency from docket:confirmed arrival to last instance row persisted");
    }

    public void RecordDocketObserved() => _docketsObserved.Add(1);
    public void RecordFolded() => _transactionsFolded.Add(1);
    public void RecordSkippedIdempotent() => _transactionsSkippedIdempotent.Add(1);
    public void RecordSkippedNotInstanceScoped() => _transactionsSkippedNotInstanceScoped.Add(1);
    public void RecordErrored() => _errored.Add(1);

    /// <summary>Feature 194 — a transaction carrying no pin was folded via the fallback.</summary>
    public void RecordPinFallback(string path) =>
        _pinFallback.Add(1, new KeyValuePair<string, object?>("path", path));

    /// <summary>Feature 194 — a fold was refused because the transaction claimed a foreign definition.</summary>
    public void RecordPinMismatch() => _pinMismatch.Add(1);
    public void RecordFoldLatency(double ms) => _foldLatencyMs.Record(ms);
}
