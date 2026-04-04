// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Wallet.Core.Domain.Enums;

namespace Sorcha.Wallet.Core.Domain.Entities;

/// <summary>
/// Group identity for threshold signing. Represents a (t, n) key group where
/// t-of-n participants must collaborate to produce a valid signature.
/// Schema only — threshold signing is a future capability.
/// </summary>
public class ThresholdKeyGroup
{
    /// <summary>
    /// Unique identifier for this threshold key group.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Combined group public key (base64-encoded). Verifiers use this single
    /// key without needing to know about the threshold structure.
    /// </summary>
    public required string GroupPublicKey { get; set; }

    /// <summary>
    /// Minimum number of shares required to produce a valid signature (t).
    /// </summary>
    public required int Threshold { get; set; }

    /// <summary>
    /// Total number of shares distributed across participants (n).
    /// </summary>
    public required int TotalShares { get; set; }

    /// <summary>
    /// Cryptographic algorithm for the group key (e.g. "ED25519", "FROST-ED25519").
    /// </summary>
    public required string Algorithm { get; set; }

    /// <summary>
    /// Distributed key generation session identifier. Null if key was imported.
    /// </summary>
    public string? DkgSessionId { get; set; }

    /// <summary>
    /// Organisation that owns this threshold key group.
    /// </summary>
    public required string OrganizationId { get; set; }

    /// <summary>
    /// Lifecycle status of this threshold key group.
    /// </summary>
    public ThresholdKeyGroupStatus Status { get; set; } = ThresholdKeyGroupStatus.Pending;

    /// <summary>
    /// Timestamp when this threshold key group was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Individual key shares belonging to this group.
    /// </summary>
    public ICollection<SigningKeyShare> Shares { get; set; } = new List<SigningKeyShare>();

    /// <summary>
    /// Signing sessions initiated for this group.
    /// </summary>
    public ICollection<SigningSession> Sessions { get; set; } = new List<SigningSession>();
}
