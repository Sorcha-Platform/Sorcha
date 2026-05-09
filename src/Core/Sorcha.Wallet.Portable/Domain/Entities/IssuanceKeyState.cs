// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Wallet.Core.Domain.Enums;

namespace Sorcha.Wallet.Core.Domain.Entities;

/// <summary>
/// Lifecycle state of one organisation's VC issuance key (Feature 120 data-model §4).
/// Persisted alongside the existing wallet/key infrastructure; this entity holds
/// no private key material — private keys remain in Wallet Service's existing
/// custodial storage (Feature 083) and are never returned by query.
/// </summary>
public class IssuanceKeyState
{
    /// <summary>Unique identifier for this row.</summary>
    public Guid Id { get; set; }

    /// <summary>Owning organisation. FK → Organization.Id.</summary>
    public required Guid OrganizationId { get; set; }

    /// <summary>Feature 083 derivation slot. v1 = 1 (<see cref="KeyUsage.VCIssuance"/>).</summary>
    public required int Slot { get; set; }

    /// <summary>
    /// Monotonic rotation counter; starts at 1 for the first derived key,
    /// increments on each rotation. Forms the kid suffix <c>#vc-issuance-{n}</c>.
    /// </summary>
    public required int RotationIndex { get; set; }

    /// <summary>Lifecycle status — at most one row per (Org, Slot) may be Active.</summary>
    public IssuanceKeyStatus Status { get; set; } = IssuanceKeyStatus.Active;

    /// <summary>Raw public key bytes (pre-multibase encoding).</summary>
    public required byte[] PublicKey { get; set; }

    /// <summary>Wallet algorithm string (e.g. <c>ED25519</c>, <c>NIST-P256</c>).</summary>
    public required string Algorithm { get; set; }

    /// <summary>RFC 7638 base64url SHA-256 thumbprint of the JWK form. 43 chars, no padding.</summary>
    public required string Thumbprint { get; set; }

    /// <summary>When this key was derived.</summary>
    public DateTimeOffset DerivedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>When the key was rotated. Null while Active.</summary>
    public DateTimeOffset? RotatedAt { get; set; }

    /// <summary>When the key was revoked. Null unless Status = Revoked.</summary>
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>Free-text reason recorded with the revocation governance op. Max 500 chars.</summary>
    public string? RevocationReason { get; set; }

    /// <summary>Governance op that revoked this key, if any.</summary>
    public Guid? RevokedByGovernanceOpId { get; set; }
}
