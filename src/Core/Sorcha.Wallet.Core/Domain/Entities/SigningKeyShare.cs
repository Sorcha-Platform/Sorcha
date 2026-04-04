// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Wallet.Core.Domain.Enums;

namespace Sorcha.Wallet.Core.Domain.Entities;

/// <summary>
/// An individual participant's share of a threshold key group. The encrypted
/// share data can only be decrypted by the participant's device or custody agent.
/// Schema only — threshold signing is a future capability.
/// </summary>
public class SigningKeyShare
{
    /// <summary>
    /// Unique identifier for this key share.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Foreign key to the parent threshold key group.
    /// </summary>
    public required Guid ThresholdKeyGroupId { get; set; }

    /// <summary>
    /// Participant identity that holds this share.
    /// </summary>
    public required string ParticipantId { get; set; }

    /// <summary>
    /// Ordinal index of this share within the group (1-based).
    /// </summary>
    public required int ShareIndex { get; set; }

    /// <summary>
    /// Share data encrypted at rest for the participant.
    /// </summary>
    public required byte[] EncryptedShareData { get; set; }

    /// <summary>
    /// Identifier of the wrapping key used to encrypt the share data.
    /// </summary>
    public required string ProtectionKeyId { get; set; }

    /// <summary>
    /// Lifecycle status of this key share (Active or Revoked).
    /// </summary>
    public ThresholdKeyGroupStatus Status { get; set; } = ThresholdKeyGroupStatus.Active;

    /// <summary>
    /// Timestamp when this key share was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Navigation to the parent threshold key group.
    /// </summary>
    public ThresholdKeyGroup ThresholdKeyGroup { get; set; } = null!;
}
