// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics.Metrics;

namespace Sorcha.Validator.Service.Services;

/// <summary>
/// OpenTelemetry instruments for validator-boundary federation trust-hardening events
/// (Feature 138 US3, FR-022). Out-of-roster vote rejections and automatic ejections are
/// counted with a coarse reason only — no vote payload data. Register as a singleton; the
/// meter source <see cref="MeterName"/> is added to the OTel meter provider in
/// <c>Sorcha.ServiceDefaults</c>.
/// </summary>
public sealed class FederationValidatorMetrics
{
    /// <summary>The OpenTelemetry meter name. Added via <c>metrics.AddMeter("Sorcha.Validator")</c>.</summary>
    public const string MeterName = "Sorcha.Validator";

    private static readonly Meter _meter = new(MeterName, "1.0.0");

    private static readonly Counter<long> _voteRejected = _meter.CreateCounter<long>(
        "sorcha_validator_vote_rejected_total",
        unit: "{rejection}",
        description: "Consensus votes rejected because authority did not derive from the sealed roster (US3), by reason.");

    private static readonly Counter<long> _ejected = _meter.CreateCounter<long>(
        "sorcha_validator_ejected_total",
        unit: "{ejection}",
        description: "Validators automatically ejected via a sealed control transaction (US3), by reason.");

    /// <summary>
    /// Record a rejected vote. <paramref name="reason"/> is one of
    /// <c>not_in_sealed_roster</c>, <c>bad_signature</c>, <c>double_vote</c>.
    /// </summary>
    public void VoteRejected(string reason) =>
        _voteRejected.Add(1, new KeyValuePair<string, object?>("reason", reason));

    /// <summary>
    /// Record an automatic ejection. <paramref name="reason"/> is one of
    /// <c>equivocation</c>, <c>liveness_timeout</c>.
    /// </summary>
    public void Ejected(string reason) =>
        _ejected.Add(1, new KeyValuePair<string, object?>("reason", reason));
}
