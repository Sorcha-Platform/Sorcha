// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics.Metrics;

namespace Sorcha.Peer.Service.Observability;

/// <summary>
/// OpenTelemetry instruments for peer-boundary federation trust-hardening rejections
/// (Feature 138 US2, FR-022). Registration refusals and per-message rejections are counted
/// with a coarse reason only — no peer address or payload data. Register as a singleton;
/// the meter source <see cref="MeterName"/> is added to the OTel meter provider in
/// <c>Sorcha.ServiceDefaults</c>.
/// </summary>
public sealed class FederationPeerMetrics
{
    /// <summary>The OpenTelemetry meter name. Added via <c>metrics.AddMeter("Sorcha.Peer")</c>.</summary>
    public const string MeterName = "Sorcha.Peer";

    private static readonly Meter _meter = new(MeterName, "1.0.0");

    private static readonly Counter<long> _registrationRejected = _meter.CreateCounter<long>(
        "sorcha_peer_registration_rejected_total",
        unit: "{rejection}",
        description: "Peer registrations refused (US2), by reason.");

    private static readonly Counter<long> _messageRejected = _meter.CreateCounter<long>(
        "sorcha_peer_message_rejected_total",
        unit: "{rejection}",
        description: "Peer messages (heartbeats/advertisements) rejected (US2), by reason.");

    /// <summary>
    /// Record a refused registration. <paramref name="reason"/> is one of
    /// <c>signature</c>, <c>id_mismatch</c>, <c>stale</c>, <c>challenge</c>, <c>rate_limited</c>.
    /// </summary>
    public void RegistrationRejected(string reason) =>
        _registrationRejected.Add(1, new KeyValuePair<string, object?>("reason", reason));

    /// <summary>
    /// Record a rejected message. <paramref name="reason"/> is one of
    /// <c>replay_seq</c>, <c>stale_timestamp</c>, <c>unsigned_ad</c>, <c>bad_signature</c>.
    /// </summary>
    public void MessageRejected(string reason) =>
        _messageRejected.Add(1, new KeyValuePair<string, object?>("reason", reason));
}
