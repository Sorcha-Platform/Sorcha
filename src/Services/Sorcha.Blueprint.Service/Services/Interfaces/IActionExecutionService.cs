// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;
using Sorcha.Blueprint.Service.Models.Requests;
using Sorcha.Blueprint.Service.Models.Responses;

namespace Sorcha.Blueprint.Service.Services.Interfaces;

/// <summary>
/// Orchestrates the execution of workflow actions.
/// Coordinates state reconstruction, validation, routing, transaction building, and notifications.
/// </summary>
public interface IActionExecutionService
{
    /// <summary>
    /// Executes a workflow action with full orchestration:
    /// 1. Fetch prior transactions from Register
    /// 2. Decrypt payloads using delegated access
    /// 3. Reconstruct accumulated state
    /// 4. Validate input data against schema
    /// 5. Evaluate routing conditions (JSON Logic)
    /// 6. Apply calculations
    /// 7. Apply disclosure rules
    /// 8. Build transaction with encrypted payloads
    /// 9. Sign transaction
    /// 10. Submit to Register
    /// 11. Notify participants via SignalR
    /// </summary>
    /// <param name="instanceId">The workflow instance ID</param>
    /// <param name="actionId">The action ID being executed</param>
    /// <param name="request">The action submission request</param>
    /// <param name="delegationToken">The credential token for delegated operations</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <param name="caller">The authenticated caller's claims principal (null skips wallet validation)</param>
    /// <returns>The action submission response</returns>
    Task<ActionSubmissionResponse> ExecuteAsync(
        string instanceId,
        int actionId,
        ActionSubmissionRequest request,
        string delegationToken,
        ClaimsPrincipal? caller = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rejects a workflow action, routing to the configured rejection target.
    /// </summary>
    /// <param name="instanceId">The workflow instance ID</param>
    /// <param name="actionId">The action ID being rejected</param>
    /// <param name="request">The rejection request</param>
    /// <param name="delegationToken">The credential token for delegated operations</param>
    /// <param name="caller">The authenticated caller's claims principal (null skips wallet validation)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The rejection response</returns>
    Task<ActionRejectionResponse> RejectAsync(
        string instanceId,
        int actionId,
        ActionRejectionRequest request,
        string delegationToken,
        ClaimsPrincipal? caller = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Feature 111 / FR-015 — completes an action that was suspended by a HAIP
    /// presentation requirement, after the verifier callback has written a
    /// success-kind <c>presentation-outcome</c> transaction. Drives the same
    /// post-validator routing + notification path as <see cref="ExecuteAsync"/>:
    /// evaluates routes against the saved draft payload, applies the resulting
    /// state changes to the instance, and notifies participants of the next
    /// action.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Called by <c>PresentationLifecycleService.HandleOutcomeAsync</c> on the
    /// success path. Skipping decline / abandonment paths is deliberate — those
    /// terminate or reroute via the lifecycle service's own logic, not this
    /// router.
    /// </para>
    /// <para>
    /// State reconstruction from prior encrypted transactions is intentionally
    /// not invoked here: the caller is a service principal without a delegation
    /// token, so decrypting prior payloads is not possible. Routing decisions
    /// in the linear AssuredIdentity flow do not need that data; blueprints
    /// that route on JSON-Logic predicates over decrypted prior payloads will
    /// need a richer integration point in a follow-up.
    /// </para>
    /// </remarks>
    /// <param name="instanceId">The workflow instance ID.</param>
    /// <param name="completedActionId">The action ID being completed (the one that was awaiting presentation).</param>
    /// <param name="outcomeTransactionId">The TxID of the just-confirmed presentation-outcome (success) transaction.</param>
    /// <param name="draftPayload">The submitter's payload captured at <c>InitiateAsync</c> time, or null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task CompleteAfterPresentationAsync(
        string instanceId,
        int completedActionId,
        string outcomeTransactionId,
        IReadOnlyDictionary<string, object>? draftPayload,
        CancellationToken cancellationToken = default);
}
