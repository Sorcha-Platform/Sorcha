// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Register.Models;

/// <summary>
/// Reason for revoking a transaction.
/// </summary>
public enum RevocationReason
{
    /// <summary>Replaced by a newer version (points to superseding transaction).</summary>
    Superseded = 0,

    /// <summary>Data error in the original transaction.</summary>
    Erroneous = 1,

    /// <summary>Signing key was compromised.</summary>
    Compromised = 2,

    /// <summary>Credential has passed its validity period.</summary>
    Expired = 3,

    /// <summary>Issuer voluntarily withdraws the credential.</summary>
    Withdrawn = 4,

    /// <summary>Revoked due to regulatory requirement.</summary>
    Regulatory = 5
}

/// <summary>
/// Payload of a revocation transaction. Stored as the transaction's payload field.
/// </summary>
public record RevocationPayload
{
    /// <summary>Transaction ID being revoked.</summary>
    public required string OriginalTxId { get; init; }

    /// <summary>Docket number of the target transaction (for cross-reference).</summary>
    public required long OriginalDocketNumber { get; init; }

    /// <summary>Why the transaction is being revoked.</summary>
    public required RevocationReason Reason { get; init; }

    /// <summary>
    /// Replacement transaction ID. Required when Reason is Superseded, forbidden otherwise.
    /// </summary>
    public string? SupersededByTxId { get; init; }

    /// <summary>Additional context (max 10 entries, keys max 50 chars, values max 500 chars).</summary>
    public Dictionary<string, string>? Metadata { get; init; }
}
