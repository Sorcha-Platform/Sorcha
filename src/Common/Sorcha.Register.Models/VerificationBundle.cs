// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;

namespace Sorcha.Register.Models;

/// <summary>
/// A portable verification bundle for offline credential verification.
/// Contains all data needed to verify a credential's authenticity, ledger inclusion,
/// and revocation status without network access or register access.
/// </summary>
public record VerificationBundle
{
    /// <summary>Bundle format version.</summary>
    public int Version { get; init; } = 1;

    /// <summary>Transaction ID being verified.</summary>
    public required string TransactionId { get; init; }

    /// <summary>Register containing the transaction.</summary>
    public required string RegisterId { get; init; }

    /// <summary>The verifiable credential / payload data.</summary>
    public required JsonElement Credential { get; init; }

    /// <summary>Signed receipt with embedded inclusion proof.</summary>
    public required TransactionReceipt Receipt { get; init; }

    /// <summary>Point-in-time revocation status snapshot.</summary>
    public required TransactionStatusResponse RevocationStatus { get; init; }

    /// <summary>When this bundle was exported.</summary>
    public required DateTimeOffset ExportedAt { get; init; }

    /// <summary>Public keys for verifying validator signatures.</summary>
    public required IReadOnlyList<ValidatorKeyInfo> ValidatorPublicKeys { get; init; }
}
