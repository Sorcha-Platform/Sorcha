// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.ServiceClients.Blueprint.Models;

namespace Sorcha.McpServer.Tools.Designer;

/// <summary>
/// Result of a rehearsal tool call (<c>sorcha_rehearsal_start</c>, <c>sorcha_rehearsal_step</c>,
/// <c>sorcha_rehearsal_get</c>) — the rehearsal's state plus what to do next (#1691).
/// </summary>
public sealed record RehearsalToolResult
{
    /// <summary>
    /// Operation status: InProgress, Passed, Failed (the rehearsal ran and failed — read
    /// <see cref="Log"/>), Refused (the Blueprint Service refused the call), ValidationError,
    /// Unauthorized, Unavailable, Timeout, or Error.
    /// </summary>
    public required string Status { get; init; }

    /// <summary>Human-readable message, including what to call next.</summary>
    public required string Message { get; init; }

    /// <summary>When the operation was performed.</summary>
    public required DateTimeOffset CheckedAt { get; init; }

    /// <summary>Response time in milliseconds.</summary>
    public int ResponseTimeMs { get; init; }

    /// <summary>Why the call could not proceed — input errors, or the blueprint's blocking validation errors.</summary>
    public IReadOnlyList<string> ValidationErrors { get; init; } = [];

    /// <summary>The rehearsal id — pass it to <c>sorcha_rehearsal_step</c> and <c>sorcha_rehearsal_get</c>.</summary>
    public Guid? RehearsalId { get; init; }

    /// <summary>The draft blueprint being rehearsed.</summary>
    public string? BlueprintId { get; init; }

    /// <summary>
    /// The executable-definition hash being rehearsed. A pass is recorded against THIS value, and
    /// <c>sorcha_blueprint_publish</c> clears its safety gate only while the draft still hashes to it.
    /// </summary>
    public string? ExecDefHash { get; init; }

    /// <summary>The org's devMode sandbox register the rehearsal runs on — not a register you publish to.</summary>
    public string? SandboxRegisterId { get; init; }

    /// <summary>The rehearsal's own outcome: InProgress, Passed, Abandoned, or Failed.</summary>
    public string? Outcome { get; init; }

    /// <summary>The action to submit next, or null when no step is current.</summary>
    public int? CurrentActionId { get; init; }

    /// <summary>The participant role the next step is submitted as.</summary>
    public string? CurrentActingRole { get; init; }

    /// <summary>Every step of the walk-through, in order.</summary>
    public IReadOnlyList<RehearsalToolStep> Steps { get; init; } = [];

    /// <summary>The rehearsal's activity log, oldest first — the reason a step failed is recorded here.</summary>
    public IReadOnlyList<RehearsalToolLogEntry> Log { get; init; } = [];
}

/// <summary>One step of a rehearsal walk-through.</summary>
/// <param name="ActionId">The blueprint action the step exercises.</param>
/// <param name="ActingRole">The participant role that submits it.</param>
/// <param name="Status">Pending, Current, or Done.</param>
public sealed record RehearsalToolStep(int ActionId, string ActingRole, string Status);

/// <summary>One entry in a rehearsal's activity log.</summary>
/// <param name="At">When it was recorded (UTC).</param>
/// <param name="Kind">Info, Sealed, Gate, Routed, Delivered, or Error.</param>
/// <param name="Message">What happened.</param>
public sealed record RehearsalToolLogEntry(DateTimeOffset At, string Kind, string Message);

/// <summary>
/// Maps rehearsal state and refusals onto <see cref="RehearsalToolResult"/>, and says what to call
/// next. Shared by the three rehearsal tools so they cannot drift on what a state means.
/// </summary>
internal static class RehearsalResultMapper
{
    /// <summary>
    /// Maps a rehearsal the server returned. <paramref name="submittedActionId"/> is set only by
    /// <c>sorcha_rehearsal_step</c>, and lets the message say when a step was accepted but did not
    /// advance (for example, an action waiting on a credential presentation).
    /// </summary>
    public static RehearsalToolResult FromRehearsal(Rehearsal rehearsal, int elapsedMs, int? submittedActionId = null)
    {
        var current = rehearsal.Steps.FirstOrDefault(s => s.Status == RehearsalStepStatus.Current);

        var (status, message) = rehearsal.Outcome switch
        {
            RehearsalOutcome.Passed => ("Passed",
                $"The rehearsal of blueprint '{rehearsal.BlueprintId}' PASSED: every branch reached a "
                + "terminal state on the sandbox register, and a rehearsal pass is recorded for "
                + $"executable-definition hash {rehearsal.ExecDefHash}. Publish it with "
                + "sorcha_blueprint_publish — its safety gate is now cleared, so no person has to "
                + "waive it. A behavioural change to the draft (sorcha_blueprint_update) produces a new "
                + "hash and needs a new rehearsal; a purely presentational change (titles, "
                + "descriptions) does not."),
            RehearsalOutcome.Failed => ("Failed",
                $"The rehearsal of blueprint '{rehearsal.BlueprintId}' FAILED and no rehearsal pass was "
                + $"recorded. {LastError(rehearsal) ?? "The rehearsal log records no reason."} "
                + "A failed rehearsal cannot be resumed: fix the draft if the failure is in the "
                + "blueprint, then start a new one with sorcha_rehearsal_start."),
            RehearsalOutcome.Abandoned => ("Failed",
                $"The rehearsal of blueprint '{rehearsal.BlueprintId}' was reset before it finished, so "
                + "no rehearsal pass was recorded. Start a new one with sorcha_rehearsal_start."),
            _ => ("InProgress", InProgressMessage(rehearsal, current, submittedActionId)),
        };

        return new RehearsalToolResult
        {
            Status = status,
            Message = message,
            CheckedAt = DateTimeOffset.UtcNow,
            ResponseTimeMs = elapsedMs,
            RehearsalId = rehearsal.RehearsalId,
            BlueprintId = rehearsal.BlueprintId,
            ExecDefHash = rehearsal.ExecDefHash,
            SandboxRegisterId = rehearsal.SandboxRegisterId,
            Outcome = rehearsal.Outcome.ToString(),
            CurrentActionId = current?.ActionId,
            CurrentActingRole = current is null
                ? null
                : string.IsNullOrWhiteSpace(rehearsal.CurrentActingRole) ? current.ActingRole : rehearsal.CurrentActingRole,
            Steps = rehearsal.Steps
                .Select(s => new RehearsalToolStep(s.ActionId, s.ActingRole, s.Status.ToString()))
                .ToList(),
            Log = rehearsal.Log
                .Select(e => new RehearsalToolLogEntry(e.At, e.Kind.ToString(), e.Message))
                .ToList(),
        };
    }

    /// <summary>Maps a refusal the server gave, keeping its reason verbatim.</summary>
    public static RehearsalToolResult FromRefusal(RehearsalRefusal refusal, string attempted, int elapsedMs)
    {
        var reason = string.IsNullOrWhiteSpace(refusal.Reason)
            ? $"The Blueprint Service answered {refusal.StatusCode} without an explanation."
            : refusal.Reason.TrimEnd('.') + ".";

        var hint = refusal.StatusCode switch
        {
            403 => " Rehearsing needs a platform-tier token with an organisation context — the same "
                   + "authority that authors blueprints.",
            404 => " Check the blueprint id with sorcha_blueprint_list. A rehearsal id is only known to "
                   + "the Blueprint Service instance that started it, and does not survive a restart of "
                   + "that service — start a new rehearsal if it has gone.",
            409 => " The draft has blocking validation errors, listed in validationErrors. Fix them with "
                   + "sorcha_blueprint_update, then start the rehearsal again.",
            503 => " This is temporary rather than a decision about the blueprint — try the same call "
                   + "again in a few seconds.",
            _ => string.Empty,
        };

        return new RehearsalToolResult
        {
            Status = refusal.StatusCode switch
            {
                409 => "ValidationError",
                503 => "Unavailable",
                _ => "Refused",
            },
            Message = $"Could not {attempted}. {reason}{hint}",
            CheckedAt = DateTimeOffset.UtcNow,
            ResponseTimeMs = elapsedMs,
            ValidationErrors = refusal.Errors,
        };
    }

    /// <summary>A result for a failure that happened before or instead of a server answer.</summary>
    public static RehearsalToolResult Fail(string status, string message, IReadOnlyList<string>? errors = null, int elapsedMs = 0) => new()
    {
        Status = status,
        Message = message,
        CheckedAt = DateTimeOffset.UtcNow,
        ResponseTimeMs = elapsedMs,
        ValidationErrors = errors ?? [],
    };

    private static string InProgressMessage(Rehearsal rehearsal, RehearsalStep? current, int? submittedActionId)
    {
        if (current is null)
        {
            // In progress with nothing current is not a state the orchestration is meant to produce;
            // say so rather than invent a next step.
            return $"The rehearsal of blueprint '{rehearsal.BlueprintId}' is in progress but has no current "
                   + "step. Read the log; if it does not explain this, start a new rehearsal with "
                   + "sorcha_rehearsal_start.";
        }

        var role = string.IsNullOrWhiteSpace(rehearsal.CurrentActingRole) ? current.ActingRole : rehearsal.CurrentActingRole;
        var next =
            $"Submit action {current.ActionId} next with sorcha_rehearsal_step; it is signed as the '{role}' "
            + "participant by a stand-in sandbox wallet. Its payload must satisfy that action's data "
            + "schema — sorcha_blueprint_get shows it, and sorcha_blueprint_validate checks a payload "
            + "without spending a rehearsal step.";

        if (submittedActionId is { } submitted && submitted == current.ActionId)
        {
            var latest = rehearsal.Log.LastOrDefault()?.Message;
            return $"Action {submitted} was accepted but the rehearsal did not advance past it"
                   + (latest is null ? "." : $": {latest.TrimEnd('.')}.")
                   + " A step that waits on something outside the workflow, such as a credential "
                   + "presentation, cannot be completed by an agent in a rehearsal.";
        }

        return submittedActionId is { } done
            ? $"Action {done} was applied on the sandbox register. {next}"
            : next;
    }

    private static string? LastError(Rehearsal rehearsal) =>
        rehearsal.Log.LastOrDefault(e => e.Kind == RehearsalEventKind.Error)?.Message is { } message
            ? message.TrimEnd('.') + "."
            : null;
}
