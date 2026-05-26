// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

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
