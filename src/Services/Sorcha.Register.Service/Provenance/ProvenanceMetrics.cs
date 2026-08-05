// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Sorcha.Provenance.Engine;

namespace Sorcha.Register.Service.Provenance;

/// <summary>
/// OpenTelemetry instruments for the provenance surfaces.
/// </summary>
/// <remarks>
/// <para>
/// <b>No subject data on any dimension.</b> Register ids, docket numbers, validator ids and wallet
/// addresses are all absent by design: these are operational signals, and a metric label is the
/// easiest place to leak an identifier into a system with a very different retention policy from the
/// ledger.
/// </para>
/// <para>
/// <b><c>failed</c> and <c>unverified</c> must stay distinguishable.</b> A rising <c>failed</c> count
/// is an integrity signal worth alerting on. A rising <c>unverified</c> count usually means missing
/// evidence — a partial replica, a single-validator node, dockets predating the evidence a check
/// needs — and alerting on it would train operators to ignore the one that matters.
/// </para>
/// </remarks>
public sealed class ProvenanceMetrics : IDisposable
{
    /// <summary>Meter name. Allowlisted for export in <c>ServiceDefaults.Extensions</c>.</summary>
    public const string MeterName = "Sorcha.Provenance";

    private readonly Meter _meter;
    private readonly Counter<long> _checks;
    private readonly Histogram<double> _trailDuration;

    /// <summary>Creates the provenance instruments.</summary>
    public ProvenanceMetrics()
    {
        _meter = new Meter(MeterName);

        _checks = _meter.CreateCounter<long>(
            "sorcha_provenance_check_total",
            unit: "{check}",
            description: "Provenance checks performed, by layer and outcome.");

        _trailDuration = _meter.CreateHistogram<double>(
            "sorcha_provenance_trail_duration_seconds",
            unit: "s",
            description: "Time to serve a provenance surface, by surface (spine or trail).");
    }

    /// <summary>Records one counter increment per check in a trail.</summary>
    public void RecordChecks(ProvenanceTrail trail)
    {
        foreach (var check in trail.Checks)
        {
            _checks.Add(1,
                new KeyValuePair<string, object?>("layer", ProvenanceContractMapper.Wire(check.Layer)),
                new KeyValuePair<string, object?>("status", ProvenanceContractMapper.Wire(check.Status)));
        }
    }

    /// <summary>
    /// Times a surface, guarding SC-007. Dispose the returned value to record.
    /// </summary>
    /// <param name="surface">Either <c>spine</c> or <c>trail</c>.</param>
    public IDisposable TimeTrail(string surface) => new Timer(_trailDuration, surface);

    /// <inheritdoc/>
    public void Dispose() => _meter.Dispose();

    private sealed class Timer(Histogram<double> histogram, string surface) : IDisposable
    {
        private readonly long _start = Stopwatch.GetTimestamp();

        public void Dispose() => histogram.Record(
            Stopwatch.GetElapsedTime(_start).TotalSeconds,
            new KeyValuePair<string, object?>("surface", surface));
    }
}
