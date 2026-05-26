// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;

namespace Sorcha.ServiceClients.Validator;

/// <summary>
/// Client interface for Validator Service operations
/// </summary>
public interface IValidatorServiceClient
{
    /// <summary>
    /// Submits an action transaction to the Validator Service for validation and mempool inclusion
    /// </summary>
    /// <param name="request">Action transaction submission details</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result indicating success or failure with details</returns>
    Task<TransactionSubmissionResult> SubmitTransactionAsync(
        TransactionSubmission request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the next sequence number for a wallet on a register (replay protection).
    /// </summary>
    /// <param name="registerId">Target register ID</param>
    /// <param name="walletAddress">Sender wallet address</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The next valid sequence number to use for a transaction</returns>
    Task<long> GetNextSequenceNumberAsync(
        string registerId,
        string walletAddress,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts the validator for a register (Feature 140 Wave 4 — admin orchestration).
    /// Calls <c>POST /api/admin/validators/start</c>.
    /// </summary>
    /// <param name="registerId">The register whose validator to start.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True on success; false on a non-success status. Transport faults propagate.</returns>
    Task<bool> StartValidatorAsync(
        string registerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the validator for a register (Feature 140 Wave 4 — admin orchestration).
    /// Calls <c>POST /api/admin/validators/stop</c>.
    /// </summary>
    /// <param name="registerId">The register whose validator to stop.</param>
    /// <param name="persistMemPool">Whether to persist the mempool before stopping.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True on success; false on a non-success status. Transport faults propagate.</returns>
    Task<bool> StopValidatorAsync(
        string registerId,
        bool persistMemPool,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Request model for submitting action transactions to the Validator Service
/// </summary>
public record TransactionSubmission
{
    /// <summary>Identifier of the transaction.</summary>
    public required string TransactionId { get; init; }
    /// <summary>Identifier of the register.</summary>
    public required string RegisterId { get; init; }
    /// <summary>Identifier of the blueprint.</summary>
    public string? BlueprintId { get; init; }
    /// <summary>Identifier of the action.</summary>
    public string? ActionId { get; init; }
    /// <summary>Payload data for this request or response.</summary>
    public required JsonElement Payload { get; init; }
    /// <summary>The payload hash.</summary>
    public required string PayloadHash { get; init; }
    /// <summary>Collection of signatures associated with this resource.</summary>
    public required List<SignatureInfo> Signatures { get; init; }
    /// <summary>Server timestamp when the record was created (UTC).</summary>
    public required DateTimeOffset CreatedAt { get; init; }
    /// <summary>Identifier of the previous transaction.</summary>
    public string? PreviousTransactionId { get; init; }
    /// <summary>Free-form metadata associated with the resource.</summary>
    public Dictionary<string, string>? Metadata { get; init; }

    /// <summary>
    /// Per-sender monotonic sequence number for replay protection (SEC-AUDIT 4.2).
    /// Must equal sender's last sequence number + 1 on the target register.
    /// </summary>
    public long SequenceNumber { get; init; }

    /// <summary>
    /// Recipient wallet addresses. Populated from disclosure group recipients so the
    /// Register Service can route docket-sealed transactions to recipient Wallet Services.
    /// </summary>
    public List<string>? RecipientsWallets { get; init; }
}

/// <summary>
/// Signature information for transaction submission
/// </summary>
public record SignatureInfo
{
    /// <summary>Public key material.</summary>
    public required string PublicKey { get; init; }
    /// <summary>The signature value.</summary>
    public required string SignatureValue { get; init; }
    /// <summary>Cryptographic algorithm identifier.</summary>
    public required string Algorithm { get; init; }
    /// <summary>The signed by.</summary>
    public string? SignedBy { get; init; }
}

/// <summary>
/// Result of submitting a transaction to the Validator Service
/// </summary>
public record TransactionSubmissionResult
{
    /// <summary>Indicates whether the operation succeeded.</summary>
    public bool Success { get; init; }
    /// <summary>Identifier of the transaction.</summary>
    public string TransactionId { get; init; } = string.Empty;
    /// <summary>Identifier of the register.</summary>
    public string RegisterId { get; init; } = string.Empty;
    /// <summary>Timestamp at which added occurred (UTC).</summary>
    public DateTimeOffset? AddedAt { get; init; }
    /// <summary>Human-readable error message.</summary>
    public string? ErrorMessage { get; init; }
    /// <summary>Machine-readable error code.</summary>
    public string? ErrorCode { get; init; }
}
