// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Blueprint.Service.Models;
using Sorcha.PresentationLifecycle.Abstractions;
using ActionModel = Sorcha.Blueprint.Models.Action;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;
using CredentialRequirementModel = Sorcha.Blueprint.Models.Credentials.CredentialRequirement;

namespace Sorcha.Blueprint.Service.Services.Interfaces;

/// <summary>
/// Orchestrates the three-event Timebound Presentation Lifecycle (Feature 111).
/// Consumer-agnostic — dispatches verifier callbacks to registered
/// <see cref="IPresentationConsumer"/> implementations by name.
/// </summary>
public interface IPresentationLifecycleService
{
    /// <summary>
    /// Begin a new presentation attempt. Writes the <c>presentation-initiated</c>
    /// transaction to the register and stores pending state in Redis. Returns the
    /// QR-ready details for the citizen to scan.
    /// </summary>
    /// <remarks>
    /// Scope note: the current implementation dispatches to the HAIP consumer
    /// only. Non-HAIP consumers (e.g. file-upload-deadline) will land in a
    /// future phase by extending <see cref="IPresentationConsumer"/> with an
    /// initiation contract. The <see cref="HandleOutcomeAsync"/> path is
    /// already consumer-agnostic.
    /// </remarks>
    Task<PresentationInitiationResult> InitiateAsync(
        BlueprintModel blueprint,
        Instance instance,
        ActionModel action,
        CredentialRequirementModel credentialRequirement,
        string submitterWallet,
        string? delegationToken,
        IReadOnlyDictionary<string, object> draftPayload,
        string? previousTransactionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Handle a verifier callback. Dispatches to the named consumer, writes the
    /// outcome transaction, and (on success) resumes the action. Idempotent —
    /// duplicate callbacks for the same requestId are no-ops.
    /// </summary>
    Task<PresentationOutcomeResult> HandleOutcomeAsync(
        string consumerName,
        Guid presentationRequestId,
        object verifierPayload,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Handle TTL expiry for a pending presentation. Invoked by the
    /// <c>AbandonmentSweeper</c> background service. Writes the
    /// <c>presentation-abandoned</c> transaction when the blueprint opts in and
    /// no outcome has been recorded.
    /// </summary>
    Task HandleAbandonmentAsync(
        Guid presentationRequestId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of <see cref="IPresentationLifecycleService.InitiateAsync"/>. Contains
/// the QR URI for rendering and the transaction id of the initiated record for
/// audit and client polling.
/// </summary>
public sealed record PresentationInitiationResult(
    Guid PresentationRequestId,
    string AuthorizationRequestUri,
    string? RequestUri,
    string? Nonce,
    DateTimeOffset ExpiresAt,
    string InitiatedTransactionId);

/// <summary>
/// Result of <see cref="IPresentationLifecycleService.HandleOutcomeAsync"/>.
/// </summary>
public sealed record PresentationOutcomeResult(
    PresentationOutcomeKind Kind,
    string OutcomeTransactionId,
    bool IsIdempotentReplay,
    bool IsLateAfterAbandonment);
