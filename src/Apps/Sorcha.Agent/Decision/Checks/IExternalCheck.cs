// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Agent.Decision.Checks;

/// <summary>
/// A single pre-decision check that inspects the action payload and produces one named boolean
/// fact (optionally with a human detail string) for the rules context. Checks keep the decision
/// declarative: they produce facts under <c>/checks/{Name}</c>, JSON-Logic rules decide.
/// </summary>
public interface IExternalCheck
{
    /// <summary>Stable fact key the rules reference (e.g. <c>postcodeExists</c>, <c>profane</c>).</summary>
    string Name { get; }

    /// <summary>
    /// Evaluate against the submitted action payload. MUST NOT throw on a normal "false" outcome;
    /// network faults degrade per the check's own fallback policy (see
    /// <see cref="PostcodeExistsCheck"/> offline mode). Unexpected faults are contained by
    /// <see cref="ExternalCheckRunner"/>, which resolves them to a safe <c>false</c>.
    /// </summary>
    /// <param name="payload">Top-level properties of the submitted application payload, keyed by
    /// property name with <see cref="System.Text.Json.Nodes.JsonNode"/> values.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<ExternalCheckResult> EvaluateAsync(IReadOnlyDictionary<string, object?> payload, CancellationToken ct);
}

/// <summary>
/// The output of one external check, merged into the rules context as a fact.
/// </summary>
/// <param name="Name">Fact key — merged at <c>/checks/{Name}</c>.</param>
/// <param name="Value">Boolean result merged at <c>/checks/{Name}</c>.</param>
/// <param name="Detail">Optional human string merged at <c>/checks/{Name}Detail</c> (for rejection copy).</param>
public sealed record ExternalCheckResult(string Name, bool Value, string? Detail = null);
