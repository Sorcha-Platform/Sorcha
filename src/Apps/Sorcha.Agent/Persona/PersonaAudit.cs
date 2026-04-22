// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Agent.Execution;
using Sorcha.Agent.Models;

namespace Sorcha.Agent.Persona;

/// <summary>
/// Bridges persona submissions into the agent's existing <see cref="AuditLogger"/>
/// JSONL stream (research.md R-007). Persona fires share the same shape as
/// reactive decisions so a single greppable audit file captures both agent
/// behaviours.
/// </summary>
/// <remarks>
/// Persona entries are distinguishable from reactive entries by two fields:
/// <list type="bullet">
///   <item><c>Decision == "persona-fire"</c> (reactive decisions are
///         <c>approve</c>/<c>reject</c>/<c>skip</c>).</item>
///   <item><c>ActionId</c> is prefixed <c>persona:{personaName}#{counter}</c>
///         — a synthetic ID since persona fires have no prior pending action.</item>
/// </list>
/// </remarks>
internal static class PersonaAudit
{
    public const string DecisionLabel = "persona-fire";

    public static Task WriteAsync(
        AuditLogger? logger,
        string personaName,
        int counter,
        PersonaSubmissionResult result,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (logger is null) return Task.CompletedTask;

        var entry = new ActionAuditEntry
        {
            Timestamp = timeProvider.GetUtcNow(),
            ActionId = $"persona:{personaName}#{counter}",
            ActionName = personaName,
            Decision = DecisionLabel,
            Success = result.Outcome == PersonaSubmissionOutcome.Submitted,
            Error = result.Error,
            DurationMs = result.DurationMs
        };

        return logger.LogAsync(entry, cancellationToken);
    }
}
