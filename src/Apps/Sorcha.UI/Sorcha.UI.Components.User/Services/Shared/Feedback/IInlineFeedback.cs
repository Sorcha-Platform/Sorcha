// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Services.Feedback;

/// <summary>
/// Page-scoped, in-place feedback surface for the actor's-own-action result —
/// the replacement for <c>ISnackbar</c> in the wider Snackbar retirement effort.
/// </summary>
/// <remarks>
/// <para>
/// Used by both end-user pages (wallet, credentials) and admin/designer pages
/// (the latter explicitly stay inline-only per the Phase A5 audit; admin events
/// don't fan out to the durable inbox). Workflow events that need durable
/// persistence go through the server-side inbox writers and render in the bell
/// drawer (Feature 118); this surface is strictly for ephemeral confirmation
/// of an action the user just took in this tab.
/// </para>
/// <para>
/// Messages auto-dismiss after <c>autoDismissMs</c> (default 4000). Pass a
/// value &lt;= 0 to disable auto-dismiss and require an explicit
/// <see cref="Dismiss"/> call (e.g. for errors that warrant acknowledgement).
/// </para>
/// </remarks>
public interface IInlineFeedback
{
    /// <summary>Surface a success banner. Returns the assigned message id.</summary>
    Guid ShowSuccess(string message, string? detailHref = null, int? autoDismissMs = null);

    /// <summary>Surface an error banner. Returns the assigned message id.</summary>
    Guid ShowError(string message, string? detailHref = null, int? autoDismissMs = null);

    /// <summary>Surface an info banner. Returns the assigned message id.</summary>
    Guid ShowInfo(string message, string? detailHref = null, int? autoDismissMs = null);

    /// <summary>Surface a warning banner. Returns the assigned message id.</summary>
    Guid ShowWarning(string message, string? detailHref = null, int? autoDismissMs = null);

    /// <summary>
    /// Dismiss a previously-shown message by id. Idempotent — no-op if the
    /// message has already been auto-dismissed or doesn't exist.
    /// </summary>
    void Dismiss(Guid messageId);

    /// <summary>Drop every currently-displayed message. Used when a page unmounts or navigates away.</summary>
    void Clear();

    /// <summary>Snapshot of the messages the host should currently render.</summary>
    IReadOnlyList<InlineFeedbackMessage> CurrentMessages { get; }

    /// <summary>
    /// Raised whenever the message set changes — host components subscribe and
    /// call <c>StateHasChanged</c>. Always invoked on a background thread; the
    /// host is responsible for marshalling onto the renderer via
    /// <c>InvokeAsync</c>.
    /// </summary>
    event Action? Changed;
}

/// <summary>Severity for an inline feedback message.</summary>
public enum InlineFeedbackSeverity
{
    /// <summary>Informational / neutral feedback.</summary>
    Info,
    /// <summary>Confirms a successful action.</summary>
    Success,
    /// <summary>Reports a recoverable issue.</summary>
    Warning,
    /// <summary>Reports an error or failed action.</summary>
    Error,
}

/// <summary>
/// One in-flight inline feedback message. Returned by <see cref="IInlineFeedback.CurrentMessages"/>
/// and consumed by the host renderer.
/// </summary>
/// <param name="Id">Unique identifier; pass to <see cref="IInlineFeedback.Dismiss"/> to remove.</param>
/// <param name="Severity">Visual treatment selector.</param>
/// <param name="Message">User-visible text.</param>
/// <param name="DetailHref">Optional click-through link. When set, the host renders the message as a link.</param>
/// <param name="CreatedAt">When the message was queued. Used by the host for ordering.</param>
/// <param name="AutoDismissMs">Milliseconds until the message self-dismisses. &lt;= 0 means no auto-dismiss.</param>
public sealed record InlineFeedbackMessage(
    Guid Id,
    InlineFeedbackSeverity Severity,
    string Message,
    string? DetailHref,
    DateTimeOffset CreatedAt,
    int AutoDismissMs);
