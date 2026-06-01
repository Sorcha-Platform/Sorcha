// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Register.Models;

namespace Sorcha.Blueprint.Service.Services.Interfaces;

/// <summary>
/// Feature 145 US6 — builds the signed <see cref="RoutingDecision"/> that a <b>successful</b>
/// presentation outcome transaction carries, so the <c>InstanceProjector</c> advances the instance
/// when that outcome seals (replacing the imperative post-seal advance). The presentation lifecycle
/// service computes the decision at outcome-build time and attaches it to the outcome tx's clear
/// metadata; the validator validates it (VAL_ROUTING_*) and every node folds it.
/// </summary>
/// <remarks>
/// Implemented by <c>ActionExecutionService</c>, which owns the blueprint/engine routing
/// evaluation. Decline / abandoned outcomes carry no decision (the projector leaves the action
/// current for retry), so this is only invoked for successful outcomes.
/// </remarks>
public interface IPresentationRoutingDecisionBuilder
{
    /// <summary>
    /// Evaluate routing for a completed presentation-gated action and return a sender-signed
    /// <see cref="RoutingDecision"/> describing the next-action set. Returns <c>null</c> when the
    /// instance is not found or the action is no longer current (idempotent replay) — in which case
    /// the caller attaches no decision and the (content-addressed, deduplicated) outcome tx does not
    /// advance the instance.
    /// </summary>
    /// <param name="instanceId">The workflow instance the presentation action belongs to.</param>
    /// <param name="completedActionId">The presentation-gated action that resolved successfully.</param>
    /// <param name="draftPayload">The non-credential fields carried from the original attempt (the
    /// routing context — prior decrypted state is not available here, mirroring the legacy
    /// presentation-advance path).</param>
    /// <param name="submitterWallet">The wallet that signs the routing attestation (the citizen/submitter).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<RoutingDecision?> BuildForPresentationOutcomeAsync(
        string instanceId,
        int completedActionId,
        IReadOnlyDictionary<string, object>? draftPayload,
        string submitterWallet,
        CancellationToken cancellationToken = default);
}
