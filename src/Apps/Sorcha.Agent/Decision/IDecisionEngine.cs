// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using Sorcha.Agent.Models;

namespace Sorcha.Agent.Decision;

public interface IDecisionEngine
{
    /// <summary>
    /// True when this engine's decisions depend on the disclosed prior-action payload (e.g. rules that
    /// reference external-check facts derived from it). When true, the host fetches the disclosed data
    /// for each pending action and holds (fail-closed) if it is unavailable, rather than deciding on a
    /// blank view (Feature 176). Engines that do not need it (AI persona mode) return false and are
    /// unaffected — no disclosed-data fetch is performed for them.
    /// </summary>
    bool RequiresDisclosedPayload { get; }

    Task<ActionDecision> DecideAsync(PendingAction action, CancellationToken cancellationToken = default);
}
