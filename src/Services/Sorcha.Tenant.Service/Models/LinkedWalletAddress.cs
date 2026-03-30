// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Tenant.Models;

namespace Sorcha.Tenant.Service.Models;

/// <summary>
/// Represents a verified link between a participant and a wallet address.
/// Stored in public schema for platform-wide uniqueness enforcement.
/// An active wallet address can only be linked to one participant at a time.
/// </summary>
public class LinkedWalletAddress
{
    /// <summary>
    /// Unique link identifier.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Owning participant identifier.
    /// </summary>
    public Guid ParticipantId { get; set; }

    /// <summary>
    /// Organization identifier (denormalized for queries).
    /// </summary>
    public Guid OrganizationId { get; set; }

    /// <summary>
    /// Wallet address string (e.g., base58 encoded, hex).
    /// Must be unique across the platform when Status is Active.
    /// </summary>
    public string WalletAddress { get; set; } = string.Empty;

    /// <summary>
    /// Public key bytes for signature verification.
    /// </summary>
    public byte[] PublicKey { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Signing algorithm used by this wallet (ED25519, P-256, RSA-4096).
    /// </summary>
    public string Algorithm { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp when the wallet was successfully linked (UTC).
    /// </summary>
    public DateTimeOffset LinkedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Timestamp when the link was revoked (UTC). Null if active.
    /// </summary>
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>
    /// Link status (Active, Revoked).
    /// </summary>
    public WalletLinkStatus Status { get; set; } = WalletLinkStatus.Active;

    /// <summary>
    /// How wallet ownership was verified. Use constants from <see cref="WalletVerificationMethod"/>.
    /// </summary>
    public string VerificationMethod { get; set; } = WalletVerificationMethod.ChallengeVerify;

    /// <summary>
    /// Navigation property to the owning participant.
    /// </summary>
    public ParticipantIdentity? Participant { get; set; }
}

/// <summary>
/// Constants for wallet ownership verification methods.
/// </summary>
public static class WalletVerificationMethod
{
    /// <summary>Standard challenge/verify signature flow.</summary>
    public const string ChallengeVerify = "challenge-verify";

    /// <summary>Auto-link during wallet creation (ownership proven by mnemonic generation).</summary>
    public const string SelfCreated = "self-created";
}
