// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Wallet.Pwa.Services.Actions.Models;

/// <summary>
/// Feature 151 (citizen workflow inbox) — urgency of an outstanding action, used for ordering and
/// the row chip. Parsed case-insensitively from the server <c>urgency</c> string; an unrecognised
/// value maps to <see cref="Normal"/> (never throws).
/// </summary>
public enum ActionUrgency
{
    /// <summary>Default — no special urgency.</summary>
    Normal = 0,

    /// <summary>Approaching a deadline / elevated attention.</summary>
    Warning = 1,

    /// <summary>Time-critical.</summary>
    Urgent = 2,
}

/// <summary>
/// Feature 151 — the citizen-facing view of a workflow action currently awaiting their input (an
/// action where the citizen is the designated actor — "their turn"). Maps the subset of the
/// Blueprint Service's <c>PendingActionSummary</c> the "Things to do" inbox needs; fields the inbox
/// does not use (sender address, transaction id, prepopulated payload, data schema) are ignored —
/// the open-action flow fetches what the form needs itself.
/// </summary>
/// <param name="InstanceId">The workflow instance id; the inbox navigates to <c>applications/{InstanceId}</c>.</param>
/// <param name="ActionId">The action within the instance.</param>
/// <param name="Title">Display title (server action title, falling back to blueprint title, then "Action {id}").</param>
/// <param name="WorkflowTitle">Which application/workflow this belongs to.</param>
/// <param name="Reference">Human-readable instance reference, if present.</param>
/// <param name="Summary">Optional one-line context.</param>
/// <param name="Urgency">Drives ordering and the row chip.</param>
/// <param name="Deadline">Optional due date.</param>
/// <param name="ReceivedAt">When the action became outstanding (secondary sort / tiebreak).</param>
/// <param name="NavigationPath">An explicit server-supplied path, if any (still made base-relative by the caller).</param>
public sealed record PendingActionItem(
    string InstanceId,
    int ActionId,
    string Title,
    string WorkflowTitle,
    string? Reference,
    string? Summary,
    ActionUrgency Urgency,
    DateTimeOffset? Deadline,
    DateTimeOffset ReceivedAt,
    string? NavigationPath)
{
    /// <summary>
    /// Parses a server urgency string (<c>"normal"</c> / <c>"warning"</c> / <c>"urgent"</c>)
    /// case-insensitively. Any unrecognised or empty value maps to <see cref="ActionUrgency.Normal"/>.
    /// </summary>
    public static ActionUrgency ParseUrgency(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "urgent" => ActionUrgency.Urgent,
        "warning" => ActionUrgency.Warning,
        _ => ActionUrgency.Normal,
    };
}
