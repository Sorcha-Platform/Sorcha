// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Wallet.Pwa.Services.Applications;

namespace Sorcha.Wallet.Pwa.Services.Drafts;

/// <summary>
/// Feature 152 (US4) — pure mapping from an action-submission result to a queue
/// <see cref="SubmitOutcome"/>: detect (stale → hold), retry (transient), or submitted. Keeps the
/// "detect / hold / ask" decision in one testable place. A deterministic client error is held
/// rather than retried forever; a transient error is retried; a recognised conflict is held with a
/// specific reason so the citizen can be told what changed.
/// </summary>
public static class SubmitConflictClassifier
{
    /// <summary>Classifies a submission result into a queue outcome.</summary>
    public static SubmitOutcome Classify(ApplicationSubmissionResult result)
    {
        if (result.Status == ApplicationSubmissionStatus.Success)
        {
            return SubmitOutcome.Submitted;
        }

        // Transient: server/network/signing errors are worth retrying.
        if (result.Status is ApplicationSubmissionStatus.ServerError or ApplicationSubmissionStatus.SigningFailed)
        {
            return SubmitOutcome.Retry;
        }

        // 4xx (ValidationFailed) — distinguish a recognised conflict from a transient throttle and
        // from a deterministic rejection (which must NOT loop forever).
        return HttpStatusOf(result.ErrorCode) switch
        {
            409 => SubmitOutcome.StepMovedOn,      // conflict — no longer the current action / already submitted
            410 => SubmitOutcome.InstanceClosed,   // gone — instance closed
            404 => SubmitOutcome.StepMovedOn,      // instance/action not found — moved on
            408 or 429 => SubmitOutcome.Retry,     // timeout / rate-limit — transient
            _ => SubmitOutcome.StepMovedOn,        // other deterministic 4xx — hold, don't infinite-retry
        };
    }

    private static int? HttpStatusOf(string? errorCode)
    {
        if (string.IsNullOrEmpty(errorCode) || !errorCode.StartsWith("HTTP_", StringComparison.Ordinal))
        {
            return null;
        }
        return int.TryParse(errorCode.AsSpan(5), out var code) ? code : null;
    }
}
