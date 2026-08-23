// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.ServiceClients.Blueprint.Models;

namespace Sorcha.ServiceClients.Blueprint;

/// <summary>
/// Unified client interface for Blueprint Service operations
/// </summary>
public interface IBlueprintServiceClient
{
    /// <summary>
    /// Gets a blueprint by ID
    /// </summary>
    /// <param name="blueprintId">Blueprint ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Blueprint definition, or null if not found</returns>
    Task<string?> GetBlueprintAsync(
        string blueprintId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets one specific published <b>definition</b> of a blueprint by its executable-definition
    /// hash — the definition a running instance is pinned to (Feature 194).
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="GetBlueprintAsync"/>, which answers "the current one". A pinned
    /// instance must resolve the definition it started on even after the blueprint has been
    /// republished, so resolving by id here would silently return the wrong rules — which is the
    /// defect Feature 194 removes. A null result is a refusal, never licence to fall back to latest.
    /// </remarks>
    /// <param name="blueprintId">Blueprint ID.</param>
    /// <param name="execDefHash">The executable-definition hash (the pin).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The pinned blueprint definition as JSON, or null if this node cannot resolve it.</returns>
    Task<string?> GetBlueprintDefinitionAsync(
        string blueprintId,
        string execDefHash,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a transaction payload against blueprint schema
    /// </summary>
    /// <param name="blueprintId">Blueprint ID</param>
    /// <param name="actionId">Action ID within blueprint</param>
    /// <param name="payload">Payload to validate (JSON)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if payload is valid</returns>
    Task<bool> ValidatePayloadAsync(
        string blueprintId,
        string actionId,
        string payload,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a stored file chunk by its chunk transaction ID
    /// </summary>
    /// <param name="chunkId">The chunk transaction ID returned at upload time</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The chunk retrieval result containing the encrypted bytes, or null if not found</returns>
    Task<FileChunkRetrievalResult?> GetFileChunkAsync(
        string chunkId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Submits a single chunk of a file upload to the Blueprint Service
    /// </summary>
    /// <param name="senderWallet">Wallet address of the sender</param>
    /// <param name="registerAddress">Target register address</param>
    /// <param name="chunkIndex">Zero-based index of this chunk</param>
    /// <param name="totalChunks">Total number of chunks in the file</param>
    /// <param name="fileHash">SHA-256 hash of the complete file (hex)</param>
    /// <param name="contentType">MIME type of the file</param>
    /// <param name="chunkContent">Raw bytes of this chunk</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result containing the chunk transaction ID, chunk index, and timestamp; or null on failure</returns>
    Task<FileChunkSubmissionResult?> SubmitFileChunkAsync(
        string senderWallet,
        string registerAddress,
        int chunkIndex,
        int totalChunks,
        string fileHash,
        string contentType,
        byte[] chunkContent,
        CancellationToken cancellationToken = default);

    // =========================================================================
    // Spec 139 US4 — MCP reconciliation reads/writes.
    // Each returns the raw response body (the caller does its own shaping) or
    // null when the service responds with a non-success status / the call fails.
    // =========================================================================

    /// <summary>
    /// Lists blueprints with optional search/status filters and pagination.
    /// Calls <c>GET /api/blueprints/</c>.
    /// </summary>
    /// <returns>The raw paged-list JSON body, or null on non-success.</returns>
    Task<string?> ListBlueprintsAsync(
        int page = 1,
        int pageSize = 20,
        string? search = null,
        string? status = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a blueprint from a JSON definition. Calls <c>POST /api/blueprints/</c>.
    /// </summary>
    /// <returns>The created-blueprint JSON body, or null on non-success.</returns>
    Task<string?> CreateBlueprintAsync(
        string blueprintJson,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing blueprint from a JSON definition. Calls <c>PUT /api/blueprints/{id}</c>.
    /// </summary>
    /// <returns>The updated-blueprint JSON body, or null on non-success.</returns>
    Task<string?> UpdateBlueprintAsync(
        string blueprintId,
        string blueprintJson,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Computes the diff between two versions of a blueprint. Calls
    /// <c>GET /api/blueprints/{id}/diff?from=&amp;to=</c> (to omitted compares against latest).
    /// </summary>
    /// <returns>The diff JSON body, or null on non-success.</returns>
    Task<string?> GetBlueprintDiffAsync(
        string blueprintId,
        int fromVersion,
        int? toVersion = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Simulates action routing for a blueprint. Calls <c>POST /api/execution/route</c>.
    /// </summary>
    /// <returns>The route-simulation JSON body, or null on non-success.</returns>
    Task<string?> SimulateRouteAsync(
        string requestJson,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Calculates derived/computed action data for a blueprint. Calls <c>POST /api/execution/calculate</c>.
    /// </summary>
    /// <returns>The calculation JSON body, or null on non-success.</returns>
    Task<string?> SimulateCalculateAsync(
        string requestJson,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists workflow instances with the supplied query string. Calls <c>GET /api/workflows</c>.
    /// </summary>
    /// <param name="queryString">Already-built query string (without leading '?'), or null.</param>
    /// <returns>The workflow-list JSON body, or null on non-success.</returns>
    Task<string?> GetWorkflowInstancesAsync(
        string? queryString = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a workflow instance's current status. Calls <c>GET /api/workflows/{id}</c>.
    /// </summary>
    /// <returns>The workflow-status JSON body, or null on non-success.</returns>
    Task<string?> GetWorkflowStatusAsync(
        string workflowInstanceId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the details of an action instance. Calls <c>GET /api/actions/{id}</c>.
    /// </summary>
    /// <returns>The action-details JSON body, or null on non-success.</returns>
    Task<string?> GetActionDetailsAsync(
        string actionInstanceId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists inbox (pending action) items with the supplied query string. Calls <c>GET /api/inbox</c>.
    /// </summary>
    /// <param name="queryString">Already-built query string (without leading '?'), or null.</param>
    /// <returns>The inbox JSON body, or null on non-success.</returns>
    Task<string?> GetInboxAsync(
        string? queryString = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets disclosed data for a workflow instance, optionally scoped to a single action instance.
    /// Calls <c>GET /api/workflows/{id}/disclosures</c> (or the action-scoped variant).
    /// </summary>
    /// <returns>The disclosures JSON body, or null on non-success.</returns>
    Task<string?> GetDisclosedDataAsync(
        string workflowInstanceId,
        string? actionInstanceId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes (submits) an action against a workflow instance. Calls
    /// <c>POST /api/instances/{instanceId}/actions/{actionId}/execute</c>.
    /// </summary>
    /// <param name="instanceId">Workflow instance ID.</param>
    /// <param name="actionId">Action ID within the instance.</param>
    /// <param name="payloadJson">Action payload as a JSON object.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The execution-result JSON body, or null on non-success.</returns>
    Task<string?> ExecuteActionAsync(
        string instanceId,
        string actionId,
        string payloadJson,
        CancellationToken cancellationToken = default);

    // =========================================================================
    // Feature 140 Wave 2 — credential & presentation lifecycle MCP surface.
    // Each returns the raw response body (the caller does its own shaping) or
    // null when the service responds with a non-success status / the call fails.
    // =========================================================================

    /// <summary>
    /// Gets the current lifecycle state of a presentation attempt (Feature 111).
    /// Calls <c>GET /api/presentations/{presentationRequestId}/status</c>.
    /// </summary>
    /// <param name="presentationRequestId">The presentation request ID to poll.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The status JSON body (state + expiry), or null on non-success.</returns>
    Task<string?> GetPresentationStatusAsync(
        string presentationRequestId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes a previously-issued credential. Calls
    /// <c>POST /api/v1/credentials/{credentialId}/revoke</c>.
    /// </summary>
    /// <param name="credentialId">The credential ID to revoke.</param>
    /// <param name="issuerWallet">Wallet address of the issuing authority requesting revocation.</param>
    /// <param name="reason">Optional human-readable reason for revocation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The revocation-result JSON body, or null on non-success.</returns>
    Task<string?> RevokeCredentialAsync(
        string credentialId,
        string issuerWallet,
        string? reason = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Temporarily suspends an Active credential (reversible). Calls
    /// <c>POST /api/v1/credentials/{credentialId}/suspend</c>.
    /// </summary>
    /// <param name="credentialId">The credential ID to suspend.</param>
    /// <param name="issuerWallet">Wallet address of the issuing authority.</param>
    /// <param name="reason">Optional human-readable reason for the suspension.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The suspend-result JSON body, or null on non-success.</returns>
    Task<string?> SuspendCredentialAsync(
        string credentialId,
        string issuerWallet,
        string? reason = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reinstates a Suspended credential back to Active. Calls
    /// <c>POST /api/v1/credentials/{credentialId}/reinstate</c>.
    /// </summary>
    /// <param name="credentialId">The credential ID to reinstate.</param>
    /// <param name="issuerWallet">Wallet address of the issuing authority.</param>
    /// <param name="reason">Optional human-readable reason for the reinstatement.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The reinstate-result JSON body, or null on non-success.</returns>
    Task<string?> ReinstateCredentialAsync(
        string credentialId,
        string issuerWallet,
        string? reason = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reissues an Expired credential with a fresh expiry period. Calls
    /// <c>POST /api/v1/credentials/{credentialId}/refresh</c>.
    /// </summary>
    /// <param name="credentialId">The expired credential ID to refresh.</param>
    /// <param name="issuerWallet">Wallet address of the issuing authority.</param>
    /// <param name="newExpiryDuration">Optional ISO 8601 duration for the new expiry (default P365D).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The refresh-result JSON body (original + new credential), or null on non-success.</returns>
    Task<string?> RefreshCredentialAsync(
        string credentialId,
        string issuerWallet,
        string? newExpiryDuration = null,
        CancellationToken cancellationToken = default);

    // =========================================================================
    // Feature 142 — Blueprint Design Lifecycle Overhaul.
    // Rehearsal lifecycle, governance-hard / rehearsal-soft publish, and amend
    // (clone-to-draft). Signatures only — implementations land in US2/US3.
    // Wire shapes mirror specs/142-blueprint-lifecycle/contracts/blueprint-lifecycle.openapi.yaml.
    // =========================================================================

    /// <summary>
    /// Starts a full rehearsal on the org sandbox register (Feature 142). Lazily provisions
    /// (or reuses) the org devMode sandbox register, mints ephemeral per-role sandbox wallets,
    /// publishes the current draft to the sandbox, creates a fresh instance, and returns the
    /// initial walk-through. Calls <c>POST /api/blueprints/{id}/rehearsals</c>. Dry-run is
    /// client-side and does NOT use this method.
    /// </summary>
    /// <param name="blueprintId">The draft blueprint to rehearse.</param>
    /// <param name="request">The start request (mode = full).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The started rehearsal, or null on non-success (e.g. 409 blocking validation, 403 unauthorised).</returns>
    Task<Rehearsal?> StartRehearsalAsync(
        string blueprintId,
        StartRehearsalRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a rehearsal's status and log (Feature 142). Calls
    /// <c>GET /api/blueprints/{id}/rehearsals/{rehearsalId}</c>.
    /// </summary>
    /// <param name="blueprintId">The blueprint that owns the rehearsal.</param>
    /// <param name="rehearsalId">The rehearsal id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The rehearsal, or null if unknown (404).</returns>
    Task<Rehearsal?> GetRehearsalAsync(
        string blueprintId,
        Guid rehearsalId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resets/discards a rehearsal — clears the rehearsal instance and discards its ephemeral
    /// per-role sandbox wallets (the sandbox register itself persists). Idempotent (Feature 142).
    /// Calls <c>DELETE /api/blueprints/{id}/rehearsals/{rehearsalId}</c>.
    /// </summary>
    /// <param name="blueprintId">The blueprint that owns the rehearsal.</param>
    /// <param name="rehearsalId">The rehearsal id to discard.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True on 204 (discarded or already gone); false on non-success.</returns>
    Task<bool> ResetRehearsalAsync(
        string blueprintId,
        Guid rehearsalId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Switches the acting participant role within a rehearsal (Feature 142). Calls
    /// <c>POST /api/blueprints/{id}/rehearsals/{rehearsalId}/role</c>.
    /// </summary>
    /// <param name="blueprintId">The blueprint that owns the rehearsal.</param>
    /// <param name="rehearsalId">The rehearsal id.</param>
    /// <param name="request">The role to act as.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated rehearsal, or null on non-success.</returns>
    Task<Rehearsal?> SwitchRehearsalRoleAsync(
        string blueprintId,
        Guid rehearsalId,
        SwitchRehearsalRoleRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Submits the current action as the acting role (Feature 142). Runs the real execution
    /// pipeline against the sandbox register, advances the walk-through, appends to the log,
    /// and may record a RehearsalPass that unlocks Go live. Calls
    /// <c>POST /api/blueprints/{id}/rehearsals/{rehearsalId}/steps</c>.
    /// </summary>
    /// <param name="blueprintId">The blueprint that owns the rehearsal.</param>
    /// <param name="rehearsalId">The rehearsal id.</param>
    /// <param name="request">The action id + payload to submit.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The advanced rehearsal, or null on non-success (e.g. 422 payload/step error).</returns>
    Task<Rehearsal?> SubmitRehearsalStepAsync(
        string blueprintId,
        Guid rehearsalId,
        SubmitRehearsalStepRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes (Go live) a blueprint — now governance-hard + rehearsal-soft gated (Feature 142,
    /// CHANGED). Enforces register governance rights server-side, then checks the rehearsal soft
    /// gate. Calls <c>POST /api/blueprints/{id}/publish</c>. The returned outcome distinguishes a
    /// successful publish from a <c>409 REHEARSAL_REQUIRED</c> soft-gate block — resend with
    /// <see cref="PublishBlueprintRequest.Override"/> to proceed.
    /// </summary>
    /// <param name="blueprintId">The draft blueprint to publish.</param>
    /// <param name="request">The publish request (register + optional override).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The publish outcome (success result or rehearsal-required block), or null on non-success (e.g. 403).</returns>
    Task<PublishBlueprintOutcome?> PublishBlueprintAsync(
        string blueprintId,
        PublishBlueprintRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Derives a new draft from a published version — the amend / clone-to-draft flow (Feature 142).
    /// The new draft carries lineage (source register + prior version) and opens with Go live
    /// re-locked pending a fresh rehearsal. Calls <c>POST /api/blueprints/from-published</c>.
    /// </summary>
    /// <param name="request">The source register, blueprint id, and version to clone.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The new draft id + lineage, or null on non-success (e.g. 403).</returns>
    Task<CloneFromPublishedResult?> CloneFromPublishedAsync(
        CloneFromPublishedRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result returned after a file chunk is successfully submitted
/// </summary>
/// <param name="ChunkTransactionId">Transaction ID assigned to this chunk on the register</param>
/// <param name="ChunkIndex">Zero-based index of the submitted chunk</param>
/// <param name="Timestamp">Server-side timestamp at which the chunk was accepted</param>
public record FileChunkSubmissionResult(string ChunkTransactionId, int ChunkIndex, DateTimeOffset Timestamp);

/// <summary>
/// Result returned when retrieving a stored file chunk from the Blueprint Service
/// </summary>
/// <param name="ChunkId">The chunk transaction ID</param>
/// <param name="EncryptedContent">The encrypted chunk bytes (as stored by the Blueprint Service)</param>
/// <param name="ContentType">MIME type of the original file</param>
public record FileChunkRetrievalResult(string ChunkId, byte[] EncryptedContent, string ContentType);
