// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics.Metrics;

namespace Sorcha.Validator.Service.Services;

/// <summary>
/// Counters for administrative exemption decisions (Feature 196, FR-013).
/// </summary>
/// <remarks>
/// <para>
/// A transaction claiming an exemption it is not entitled to is the signature of an attempted
/// bypass, not a malformed transaction, and must be visible as such. Before Feature 196 the claim
/// was simply honoured, so there was nothing to count.
/// </para>
/// <para>
/// The <c>reason</c> dimension separates <b>not entitled</b> from <b>could not resolve</b>
/// deliberately. The first is someone trying it on; the second is this node being unable to check,
/// which withholds the exemption (FR-007) and will refuse legitimate administrative traffic until it
/// is fixed. Alerting on them identically would bury an outage inside a security signal.
/// </para>
/// </remarks>
public sealed class ExemptionMetrics
{
    /// <summary>Meter name — add to the OpenTelemetry meter allowlist to export these.</summary>
    public const string MeterName = "Sorcha.Validation";

    private readonly Counter<long> _refusedClaims;

    /// <summary>Creates the metric instruments.</summary>
    public ExemptionMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);

        var meter = meterFactory.Create(MeterName);

        _refusedClaims = meter.CreateCounter<long>(
            "sorcha_exemption_claim_refused_total",
            unit: "{claim}",
            description: "Administrative validation exemptions claimed but refused, by kind, claim route and reason.");
    }

    /// <summary>Records a claim that was made and refused.</summary>
    /// <param name="kind">The exemption kind claimed.</param>
    /// <param name="route">Which unsigned surface carried the claim.</param>
    /// <param name="reason">Not entitled, or authority unresolvable.</param>
    public void RecordRefusedClaim(string kind, string route, string reason) =>
        _refusedClaims.Add(1,
            new KeyValuePair<string, object?>("kind", kind),
            new KeyValuePair<string, object?>("route", route),
            new KeyValuePair<string, object?>("reason", reason));
}
