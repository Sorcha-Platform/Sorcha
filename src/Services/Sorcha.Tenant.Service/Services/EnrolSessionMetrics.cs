// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics.Metrics;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// OpenTelemetry instruments for Feature 126 enrolment-session flow.
/// Exposed on the <c>Sorcha.Enrolment</c> meter per research §R-010.
/// </summary>
public sealed class EnrolSessionMetrics : IDisposable
{
    /// <summary>The meter name surfaced on dashboards.</summary>
    public const string MeterName = "Sorcha.Enrolment";

    private readonly Meter _meter;

    private readonly Counter<long> _sessionMinted;
    private readonly Counter<long> _sessionRedeemed;
    private readonly Histogram<double> _pairingSignalLatency;

    public EnrolSessionMetrics(IMeterFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _meter = factory.Create(MeterName);

        _sessionMinted = _meter.CreateCounter<long>(
            "sorcha_enrol_session_minted_total",
            unit: "count",
            description: "Enrolment session tokens minted, tagged by purpose.");

        _sessionRedeemed = _meter.CreateCounter<long>(
            "sorcha_enrol_session_redeemed_total",
            unit: "count",
            description: "Enrolment session redeem attempts, tagged by outcome.");

        _pairingSignalLatency = _meter.CreateHistogram<double>(
            "sorcha_enrol_pairing_signal_latency_seconds",
            unit: "s",
            description: "Latency from device registration to pairing-completion signal delivery.");
    }

    /// <summary>
    /// Records a successful mint with a purpose tag (e.g. <c>tier3_first_qr</c>,
    /// <c>regenerate</c>) and a F128 <paramref name="mode"/> tag
    /// (<c>gated</c> | <c>standalone</c>).
    /// </summary>
    public void RecordMint(string purpose, string mode) =>
        _sessionMinted.Add(
            1,
            KeyValuePair.Create<string, object?>("purpose", purpose),
            KeyValuePair.Create<string, object?>("mode", mode));

    /// <summary>
    /// Records a redeem attempt with an outcome tag — one of
    /// <c>success</c>, <c>expired</c>, <c>replay</c>, <c>scope_mismatch</c>,
    /// <c>signature_fail</c>, <c>malformed</c> — and the F128
    /// <paramref name="mode"/> tag (<c>gated</c> | <c>standalone</c> |
    /// <c>unknown</c> for failures that surface before the mode claim is read).
    /// </summary>
    public void RecordRedeem(string outcome, string mode) =>
        _sessionRedeemed.Add(
            1,
            KeyValuePair.Create<string, object?>("outcome", outcome),
            KeyValuePair.Create<string, object?>("mode", mode));

    /// <summary>Records the pairing-signal latency in seconds, tagged by transport (<c>signalr</c> / <c>polling</c>).</summary>
    public void RecordPairingSignalLatency(double seconds, string path) =>
        _pairingSignalLatency.Record(seconds, KeyValuePair.Create<string, object?>("path", path));

    public void Dispose() => _meter.Dispose();
}
