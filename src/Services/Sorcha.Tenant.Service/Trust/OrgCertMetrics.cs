// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics.Metrics;

namespace Sorcha.Tenant.Service.Trust;

/// <summary>
/// Feature 181 US4 (T046 / FR-028, data-model §7) — org-certificate issuance observability on the shared
/// <c>Sorcha.Trust</c> meter. <c>sorcha_org_cert_issuance_total</c> counts CSR / import / auto-enrol
/// outcomes, tagged by provenance, outcome, and reason.
/// </summary>
public static class OrgCertMetrics
{
    private static readonly Meter Meter = new("Sorcha.Trust", "1.0.0");

    private static readonly Counter<long> IssuanceTotal = Meter.CreateCounter<long>(
        "sorcha_org_cert_issuance_total",
        unit: "operations",
        description: "Org-certificate operations by provenance (internal|imported), outcome (success|failure), and reason.");

    /// <summary>Record an org-certificate operation outcome.</summary>
    /// <param name="provenance"><c>internal</c> | <c>imported</c>.</param>
    /// <param name="outcome"><c>success</c> | <c>failure</c>.</param>
    /// <param name="reason">Typed reason (e.g. an error code) or <c>ok</c>.</param>
    public static void RecordIssuance(string provenance, string outcome, string reason)
        => IssuanceTotal.Add(1,
            new KeyValuePair<string, object?>("provenance", provenance),
            new KeyValuePair<string, object?>("outcome", outcome),
            new KeyValuePair<string, object?>("reason", reason));
}
