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
}

/// <summary>
/// Request model for submitting action transactions to the Validator Service
/// </summary>
public record TransactionSubmission
{
    public required string TransactionId { get; init; }
    public required string RegisterId { get; init; }
    public string? BlueprintId { get; init; }
    public string? ActionId { get; init; }
    public required JsonElement Payload { get; init; }
    public required string PayloadHash { get; init; }
    public required List<SignatureInfo> Signatures { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public string? PreviousTransactionId { get; init; }
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
    public required string PublicKey { get; init; }
    public required string SignatureValue { get; init; }
    public required string Algorithm { get; init; }
    public string? SignedBy { get; init; }
}

/// <summary>
/// Result of submitting a transaction to the Validator Service
/// </summary>
public record TransactionSubmissionResult
{
    public bool Success { get; init; }
    public string TransactionId { get; init; } = string.Empty;
    public string RegisterId { get; init; } = string.Empty;
    public DateTimeOffset? AddedAt { get; init; }
    public string? ErrorMessage { get; init; }
    public string? ErrorCode { get; init; }
}
