// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics.Metrics;

namespace Sorcha.Validator.Service.Services;

/// <summary>
/// OpenTelemetry instruments for governance authorisation decisions (Feature 189).
/// </summary>
/// <remarks>
/// <para>
/// Registered on the <c>Sorcha.Governance</c> meter. Wire it up with
/// <c>metrics.AddMeter("Sorcha.Governance")</c>.
/// </para>
/// <para>
/// <b>No subject data.</b> Tags carry the register id, the outcome and a coarse reason — never a
/// signing key, a wallet address, a DID, or anything identifying which organisation was refused.
/// A metrics backend is a far wider audience than the ledger, and "which org keeps failing
/// governance" is exactly the kind of inference this feature should not hand out for free.
/// </para>
/// <para>
/// <b>Why refusals are worth a counter.</b> The defects that motivated this feature were silent:
/// an endpoint returned success while nothing happened, and a refusal that should have been loud
/// was a single warning line among thousands. A non-zero <c>refused</c> rate with reason
/// <c>no-roster-match</c> across many registers is the signature of a signing-key regression — the
/// exact failure that took a live investigation to find.
/// </para>
/// </remarks>
public sealed class GovernanceMetrics
{
    /// <summary>Meter name — add to the OpenTelemetry meter allowlist to export these.</summary>
    public const string MeterName = "Sorcha.Governance";

    private readonly Counter<long> _decisions;

    public GovernanceMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);

        _decisions = meter.CreateCounter<long>(
            "sorcha_governance_decision_total",
            unit: "{decision}",
            description: "Governance authorisation decisions, tagged by outcome and coarse reason. No subject data.");
    }

    /// <summary>Records an authorised governance transaction.</summary>
    public void RecordAuthorised(string registerId, int distinctSigners) =>
        _decisions.Add(1,
            new KeyValuePair<string, object?>("outcome", "authorised"),
            new KeyValuePair<string, object?>("register_id", registerId),
            new KeyValuePair<string, object?>("distinct_signers", distinctSigners));

    /// <summary>Records a refused governance transaction.</summary>
    /// <param name="reason">
    /// Coarse, non-identifying reason: <c>no-roster</c>, <c>no-roster-match</c>,
    /// <c>no-governance-role</c>, <c>insufficient-signers</c>, <c>no-signature</c>,
    /// <c>proposal-invalid</c>, <c>quorum-not-met</c>, <c>error</c>.
    /// </param>
    public void RecordRefused(string registerId, string reason) =>
        _decisions.Add(1,
            new KeyValuePair<string, object?>("outcome", "refused"),
            new KeyValuePair<string, object?>("register_id", registerId),
            new KeyValuePair<string, object?>("reason", reason));
}
