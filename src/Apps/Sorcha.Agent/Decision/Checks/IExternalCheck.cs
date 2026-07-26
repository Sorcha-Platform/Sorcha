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
/// <param name="Value">
/// Boolean result merged at <c>/checks/{Name}</c> — but only when <paramref name="Numeric"/> is null.
/// When <paramref name="Numeric"/> IS set, <see cref="Value"/> is NOT separately reachable from rules:
/// <see cref="ExternalCheckRunner"/> merges one fact per check under a single key, and the numeric result
/// wins. A check that needs both a meaningful boolean AND a number visible to rules must emit them as two
/// distinct facts (e.g. by implementing two <see cref="IExternalCheck"/>s, or by putting the boolean in
/// <see cref="Detail"/>-adjacent form) — do not rely on <see cref="Value"/> surviving alongside a set
/// <see cref="Numeric"/>.
/// </param>
/// <param name="Detail">Optional human string merged at <c>/checks/{Name}Detail</c> (for rejection copy).</param>
/// <param name="Numeric">
/// Optional numeric result. When set, <see cref="ExternalCheckRunner"/> merges the fact at
/// <c>/checks/{Name}</c> as a JSON number instead of a boolean, so JSON-Logic rules can compare
/// it (<c>{"&lt;": [{"var": "checks.cyberScore"}, 12]}</c>). Optional and last so every existing
/// check compiles unchanged. Setting this deliberately shadows <see cref="Value"/> for that fact key —
/// see the note on <see cref="Value"/>.
/// </param>
public sealed record ExternalCheckResult(
    string Name, bool Value, string? Detail = null, double? Numeric = null);
