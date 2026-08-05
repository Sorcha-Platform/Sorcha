// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Validator.Service.Models;
using Sorcha.Register.Models;

namespace Sorcha.Validator.Service.Services.Interfaces;

/// <summary>
/// Validates and signs dockets received from other validators (confirmer role).
/// Used when this validator is NOT the leader and receives a docket for confirmation.
/// </summary>
public interface IDocketConfirmer
{
    /// <summary>
    /// Validate a docket and provide a signed confirmation or rejection
    /// </summary>
    /// <param name="docket">Docket to confirm</param>
    /// <param name="initiatorSignature">Signature from the initiating validator</param>
    /// <param name="term">Election term the docket was created in</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Confirmation result with signature or rejection reason</returns>
    Task<DocketConfirmationResult> ConfirmDocketAsync(
        Docket docket,
        RegisterSignature initiatorSignature,
        long term,
        CancellationToken ct = default);

    /// <summary>
    /// Validate a single transaction within a docket confirmation
    /// </summary>
    /// <param name="transaction">Transaction to validate</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Validation result</returns>
    Task<TransactionValidationResult> ValidateTransactionAsync(
        Transaction transaction,
        CancellationToken ct = default);
}

/// <summary>
/// Result of docket confirmation attempt
/// </summary>
public record DocketConfirmationResult
{
    /// <summary>Whether the docket was confirmed (approved)</summary>
    public required bool Confirmed { get; init; }

    /// <summary>Validator's signature (only if confirmed)</summary>
    public RegisterSignature? Signature { get; init; }

    /// <summary>Rejection reason (only if not confirmed)</summary>
    public DocketRejectionReason? RejectionReason { get; init; }

    /// <summary>Detailed rejection message</summary>
    public string? RejectionDetails { get; init; }

    /// <summary>Time taken to validate</summary>
    public required TimeSpan ValidationDuration { get; init; }

    /// <summary>Number of transactions validated</summary>
    public int TransactionsValidated { get; init; }

    /// <summary>Creates a confirmed result</summary>
    public static DocketConfirmationResult CreateConfirmed(
        RegisterSignature signature,
        TimeSpan duration,
        int transactionCount) => new()
    {
        Confirmed = true,
        Signature = signature,
        ValidationDuration = duration,
        TransactionsValidated = transactionCount
    };

    /// <summary>Creates a rejected result</summary>
    public static DocketConfirmationResult CreateRejected(
        DocketRejectionReason reason,
        string? details,
        TimeSpan duration) => new()
    {
        Confirmed = false,
        RejectionReason = reason,
        RejectionDetails = details,
        ValidationDuration = duration
    };
}

/// <summary>
/// Reasons for rejecting a docket during confirmation
/// </summary>
public enum DocketRejectionReason
{
    /// <summary>Initiator's signature is invalid</summary>
    InvalidInitiatorSignature,

    /// <summary>Merkle root doesn't match computed value</summary>
    InvalidMerkleRoot,

    /// <summary>One or more transactions failed validation</summary>
    InvalidTransaction,

    /// <summary>Chain validation failed for one or more transactions</summary>
    ChainValidationFailed,

    /// <summary>Blueprint referenced by transaction not found</summary>
    BlueprintNotFound,

    /// <summary>Initiator is not a registered validator</summary>
    UnauthorizedInitiator,

    /// <summary>Docket structure is invalid</summary>
    InvalidDocketStructure,

    /// <summary>Docket hash doesn't match computed value</summary>
    InvalidDocketHash,

    /// <summary>Docket sequence number is wrong</summary>
    InvalidSequenceNumber,

    /// <summary>Term number is invalid or stale</summary>
    InvalidTerm,

    /// <summary>Validation timed out</summary>
    Timeout,

    /// <summary>Internal error during validation</summary>
    InternalError
}

/// <summary>
/// Result of validating a single transaction
/// </summary>
public record TransactionValidationResult
{
    /// <summary>Whether the transaction is valid</summary>
    public required bool IsValid { get; init; }

    /// <summary>Transaction ID that was validated</summary>
    public required string TransactionId { get; init; }

    /// <summary>Error details if invalid</summary>
    public IReadOnlyList<string>? Errors { get; init; }

    /// <summary>Time taken to validate</summary>
    public TimeSpan ValidationDuration { get; init; }
}
