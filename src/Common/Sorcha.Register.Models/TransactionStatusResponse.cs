// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Register.Models;

/// <summary>
/// Lifecycle status of a transaction on the register.
/// </summary>
public enum TransactionLifecycleStatus
{
    /// <summary>Transaction is valid and current.</summary>
    Active = 0,

    /// <summary>Transaction has been explicitly revoked.</summary>
    Revoked = 1,

    /// <summary>Transaction has been replaced by a newer transaction.</summary>
    Superseded = 2,

    /// <summary>
    /// The validator refused the transaction after it was accepted for validation, so it is not on
    /// the chain and never will be (#1669).
    /// </summary>
    /// <remarks>
    /// Appended deliberately. The Register Service applies <c>SorchaJson.Configure</c> to its HTTP
    /// JSON options, so this goes out as the NAME — but the enum carries no <c>[JsonConverter]</c>
    /// of its own, so any consumer serialising it under the web defaults gets the integer instead
    /// (CLAUDE.md §25). Appending is safe for both; reordering would silently reinterpret every
    /// numeric response ever written.
    /// </remarks>
    Rejected = 3
}

/// <summary>
/// Derived view of a transaction's lifecycle state, combining active/revoked/superseded
/// status with pointers to any revocation or superseding transactions.
/// </summary>
public record TransactionStatusResponse
{
    /// <summary>The queried transaction ID.</summary>
    public required string TransactionId { get; init; }

    /// <summary>Current lifecycle status.</summary>
    public required TransactionLifecycleStatus Status { get; init; }

    /// <summary>ID of the revocation transaction (if revoked or superseded).</summary>
    public string? RevocationTxId { get; init; }

    /// <summary>ID of the replacement transaction (if superseded).</summary>
    public string? SupersededByTxId { get; init; }

    /// <summary>When the revocation was sealed (if revoked or superseded).</summary>
    public DateTimeOffset? RevokedAt { get; init; }

    /// <summary>Revocation reason (if revoked or superseded).</summary>
    public RevocationReason? Reason { get; init; }

    /// <summary>
    /// The validation code that caused a rejection, e.g. <c>VAL_CHAIN_FORK</c> (if rejected).
    /// </summary>
    public string? RejectionCode { get; init; }

    /// <summary>The validator's own explanation of the rejection (if rejected).</summary>
    public string? RejectionReason { get; init; }

    /// <summary>When the validator refused the transaction (if rejected).</summary>
    public DateTimeOffset? RejectedAt { get; init; }
}
