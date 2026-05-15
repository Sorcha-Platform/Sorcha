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
/// <param name="PresentationRequestId">Unique id for this attempt.</param>
/// <param name="AuthorizationRequestUri">OID4VP authorization request URI (the QR / tap-link payload).</param>
/// <param name="RequestUri">Optional alternative request URI shape.</param>
/// <param name="Nonce">Optional nonce echoed in the verifiable presentation.</param>
/// <param name="ExpiresAt">When the presentation validity window ends.</param>
/// <param name="InitiatedTransactionId">Register transaction id for the <c>presentation-initiated</c> record.</param>
/// <param name="ClaimsFetchToken">
/// Feature 127 — single-use token bound to <paramref name="PresentationRequestId"/>, returned ONLY to
/// consumers that opt into claims-fetch (currently <c>"sorcha-wallet"</c>). The originator presents this
/// on <c>GET /api/presentations/{id}/disclosed-claims?token=…</c> to retrieve the disclosed claims in
/// plaintext for council-page autofill. <c>null</c> on HAIP and other consumers that do not produce
/// council-page-readable claims.
/// </param>
public sealed record PresentationInitiationResult(
    Guid PresentationRequestId,
    string AuthorizationRequestUri,
    string? RequestUri,
    string? Nonce,
    DateTimeOffset ExpiresAt,
    string InitiatedTransactionId,
    string? ClaimsFetchToken = null);

/// <summary>
/// Result of <see cref="IPresentationLifecycleService.HandleOutcomeAsync"/>.
/// </summary>
public sealed record PresentationOutcomeResult(
    PresentationOutcomeKind Kind,
    string OutcomeTransactionId,
    bool IsIdempotentReplay,
    bool IsLateAfterAbandonment);
