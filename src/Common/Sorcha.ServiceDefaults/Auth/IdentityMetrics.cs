// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics.Metrics;

namespace Sorcha.ServiceDefaults.Auth;

/// <summary>
/// OpenTelemetry instruments for the tiered-audience identity model (spec 136, Constitution VIII).
/// No subject data is recorded — only the tier and a coarse reason. Register as a singleton and
/// add <see cref="MeterName"/> to the meter provider's sources where consumed (Tenant Service).
/// </summary>
public sealed class IdentityMetrics
{
    /// <summary>The OpenTelemetry meter name. Add this to the OTel meter sources to export.</summary>
    public const string MeterName = "Sorcha.Identity";

    private readonly Counter<long> _tokenMinted;
    private readonly Counter<long> _tierRequestRejected;

    /// <summary>Creates the meter + instruments via the host's <see cref="IMeterFactory"/>.</summary>
    public IdentityMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        var meter = meterFactory.Create(MeterName);

        _tokenMinted = meter.CreateCounter<long>(
            "sorcha_token_minted_total",
            unit: "{token}",
            description: "Access tokens minted, by trust tier.");

        _tierRequestRejected = meter.CreateCounter<long>(
            "sorcha_tier_request_rejected_total",
            unit: "{request}",
            description: "Tier requests refused (e.g. over-request of a tier the holder is not entitled to).");
    }

    /// <summary>Record that an access token of <paramref name="tier"/> was minted.</summary>
    public void TokenMinted(Tier tier) =>
        _tokenMinted.Add(1, new KeyValuePair<string, object?>("tier", TagFor(tier)));

    /// <summary>Record that a request for <paramref name="requested"/> was refused for <paramref name="reason"/>.</summary>
    public void TierRequestRejected(Tier requested, string reason) =>
        _tierRequestRejected.Add(1,
            new KeyValuePair<string, object?>("requested", TagFor(requested)),
            new KeyValuePair<string, object?>("reason", reason));

    private static string TagFor(Tier tier) => tier switch
    {
        Tier.Consumer => "consumer",
        Tier.Platform => "platform",
        Tier.Service => "service",
        Tier.EnrolSession => "enrol-session",
        _ => "unknown",
    };
}
