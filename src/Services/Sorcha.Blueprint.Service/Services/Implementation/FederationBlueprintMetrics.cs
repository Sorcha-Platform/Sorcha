// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics.Metrics;

namespace Sorcha.Blueprint.Service.Services.Implementation;

/// <summary>
/// OpenTelemetry instruments for blueprint-boundary federation trust-hardening rejections
/// (Feature 138 US4/US6, FR-022). Recovery provenance failures and open-participant
/// carried-key rejections are counted with a coarse reason only — no blueprint or key data.
/// Register as a singleton; the meter source <see cref="MeterName"/> is added to the OTel
/// meter provider in <c>Sorcha.ServiceDefaults</c>.
/// </summary>
public sealed class FederationBlueprintMetrics
{
    /// <summary>The OpenTelemetry meter name. Added via <c>metrics.AddMeter("Sorcha.Blueprint")</c>.</summary>
    public const string MeterName = "Sorcha.Blueprint";

    private static readonly Meter _meter = new(MeterName, "1.0.0");

    private static readonly Counter<long> _recoveryRejected = _meter.CreateCounter<long>(
        "sorcha_blueprint_recovery_rejected_total",
        unit: "{rejection}",
        description: "Recovered/synced blueprints rejected for failing sealed-provenance verification (US4), by reason.");

    private static readonly Counter<long> _carriedKeyRejected = _meter.CreateCounter<long>(
        "sorcha_carried_key_rejected_total",
        unit: "{rejection}",
        description: "Open-participant carried delivery keys rejected for failing binding verification (US6), by reason.");

    /// <summary>
    /// Record a rejected recovery. <paramref name="reason"/> is one of
    /// <c>hash_mismatch</c>, <c>no_provenance</c>.
    /// </summary>
    public void RecoveryRejected(string reason) =>
        _recoveryRejected.Add(1, new KeyValuePair<string, object?>("reason", reason));

    /// <summary>
    /// Record a rejected carried key. <paramref name="reason"/> is one of
    /// <c>unbound</c>, <c>commitment_mismatch</c>.
    /// </summary>
    public void CarriedKeyRejected(string reason) =>
        _carriedKeyRejected.Add(1, new KeyValuePair<string, object?>("reason", reason));
}
