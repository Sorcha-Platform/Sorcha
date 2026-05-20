// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics.Metrics;

using Sorcha.Blueprint.Models.Credentials;

namespace Sorcha.Blueprint.Engine.Credentials;

/// <summary>
/// OpenTelemetry meter for unified trust decisions (feature 135). Records the outcome, deciding
/// source, credential format, assurance level, and (on rejection) failure reason of each trust
/// decision — never credential subject data (FR-024). Register the meter with
/// <c>AddMeter(TrustMetrics.MeterName)</c> to export the instruments.
/// </summary>
public static class TrustMetrics
{
    /// <summary>The meter name used for all trust-decision instruments.</summary>
    public const string MeterName = "Sorcha.Trust";

    private static readonly Meter Meter = new(MeterName, "1.0.0");

    private static readonly Counter<long> DecisionCounter = Meter.CreateCounter<long>(
        "sorcha_trust_decision_total", unit: "decisions",
        description: "Unified trust decisions by outcome, source, format, assurance, and failure reason.");

    /// <summary>
    /// Records one trust decision. Tags carry only non-identifying classification data:
    /// outcome, deciding source, credential format, established assurance, and failure reason.
    /// </summary>
    public static void RecordDecision(TrustDecision decision, CredentialFormat format)
    {
        ArgumentNullException.ThrowIfNull(decision);

        var source = decision.IsTrusted
            ? decision.Evidence.VouchingSource.ToString()
            : (decision.DecidingSources.Count > 0 ? decision.DecidingSources[0].ToString() : "none");

        DecisionCounter.Add(1,
            new KeyValuePair<string, object?>("outcome", decision.IsTrusted ? "trusted" : "rejected"),
            new KeyValuePair<string, object?>("source", source),
            new KeyValuePair<string, object?>("format", FormatTag(format)),
            new KeyValuePair<string, object?>("assurance", decision.EstablishedAssurance.ToString()),
            new KeyValuePair<string, object?>("reason", decision.FailureReason?.ToString() ?? "none"));
    }

    private static string FormatTag(CredentialFormat format) => format switch
    {
        CredentialFormat.SdJwtVc => "sd-jwt-vc",
        CredentialFormat.MsoMdoc => "mso_mdoc",
        _ => format.ToString()
    };
}
