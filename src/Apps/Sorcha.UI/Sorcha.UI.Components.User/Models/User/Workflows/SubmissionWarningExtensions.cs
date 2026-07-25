// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.UI.Core.Services.Feedback;

namespace Sorcha.UI.Core.Models.Workflows;

/// <summary>
/// Surfaces the warnings a submission came back with.
/// </summary>
/// <remarks>
/// <para>
/// Issue #1277: the server drops an oversized portrait claim and says so, and the citizen never
/// heard about it. The plumbing has existed since #340 — <c>BuildClaimsFromMappings</c> appends a
/// client-facing message, <c>ActionSubmissionResponse.Warnings</c> carries it over the wire, and
/// <see cref="ActionSubmissionResultViewModel.Warnings"/> models it on the client — but #340 wired
/// the DISPLAY at exactly one of five submission call sites. The other four, including both
/// async-encryption completion paths (where a portrait-bearing submission with a large payload
/// actually lands), silently discarded it.
/// </para>
/// <para>
/// One implementation rather than a loop copied into five files, because "remember to also show the
/// warnings" is precisely the instruction that was not remembered four times.
/// </para>
/// </remarks>
public static class SubmissionWarningExtensions
{
    /// <summary>
    /// Shows every warning the submission returned. No-op when there are none. Warnings never
    /// auto-dismiss: a citizen whose credential is missing something they supplied should have to
    /// acknowledge that, not catch it inside four seconds.
    /// </summary>
    public static void SurfaceWarnings(this ActionSubmissionResultViewModel? result, IInlineFeedback feedback)
    {
        ArgumentNullException.ThrowIfNull(feedback);
        result.SurfaceWarnings(w => feedback.ShowWarning(w, autoDismissMs: 0));
    }

    /// <summary>
    /// Sink overload for surfaces that cannot use <see cref="IInlineFeedback"/> — dialogs, per
    /// CLAUDE.md #12, because <c>InlineFeedbackHost</c> mounts in the layout and not inside a dialog.
    /// Those render an inline alert in their own body instead, but route through here so there is
    /// still exactly one place deciding what counts as a warning worth surfacing.
    /// </summary>
    public static void SurfaceWarnings(this ActionSubmissionResultViewModel? result, Action<string> show)
    {
        ArgumentNullException.ThrowIfNull(show);

        if (result?.Warnings is not { Count: > 0 } warnings) return;

        foreach (var warning in warnings)
        {
            show(warning);
        }
    }
}
