// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics.Metrics;

namespace Sorcha.Verifier.Engine;

/// <summary>
/// OpenTelemetry instruments for the verifier-side federation trust-hardening rejections
/// (Feature 138, FR-022). Status-list authenticity (US1) and presentation-replay (US5)
/// rejections are counted with a coarse reason only — no subject or credential data.
/// Register as a singleton; the meter source <see cref="MeterName"/> is added to the OTel
/// meter provider in <c>Sorcha.ServiceDefaults</c>.
/// </summary>
public sealed class FederationVerifierMetrics
{
    /// <summary>The OpenTelemetry meter name. Added via <c>metrics.AddMeter("Sorcha.Verifier")</c>.</summary>
    public const string MeterName = "Sorcha.Verifier";

    private static readonly Meter _meter = new(MeterName, "1.0.0");

    private static readonly Counter<long> _statusListRejected = _meter.CreateCounter<long>(
        "sorcha_statuslist_rejected_total",
        unit: "{rejection}",
        description: "Revocation status lists rejected during verification (US1), by reason.");

    private static readonly Counter<long> _presentationReplayRejected = _meter.CreateCounter<long>(
        "sorcha_presentation_replay_rejected_total",
        unit: "{rejection}",
        description: "Presentation key-binding proofs rejected at verify time (US5 / US1), by reason.");

    /// <summary>
    /// Record a rejected status list. <paramref name="reason"/> is one of
    /// <c>signature</c>, <c>issuer</c>, <c>unresolved</c>, <c>expired</c>, <c>fetch</c>.
    /// </summary>
    public void StatusListRejected(string reason) =>
        _statusListRejected.Add(1, new KeyValuePair<string, object?>("reason", reason));

    /// <summary>
    /// Record a rejected presentation proof. <paramref name="reason"/> is one of
    /// <c>kbjwt_expired</c>, <c>kbjwt_missing_exp</c>, <c>revoked_at_verify</c>.
    /// </summary>
    public void PresentationReplayRejected(string reason) =>
        _presentationReplayRejected.Add(1, new KeyValuePair<string, object?>("reason", reason));
}
