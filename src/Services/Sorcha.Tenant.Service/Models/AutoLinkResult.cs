// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Service.Models;

/// <summary>
/// Result of the automatic participant registration and wallet linking
/// that occurs after wallet creation.
/// </summary>
public record AutoLinkResult
{
    /// <summary>Whether a new participant was registered.</summary>
    public bool ParticipantCreated { get; init; }

    /// <summary>Whether the wallet was linked to the participant.</summary>
    public bool WalletLinked { get; init; }

    /// <summary>Participant identity ID (if participant exists or was created).</summary>
    public Guid? ParticipantId { get; init; }

    /// <summary>Reason auto-link was skipped (if applicable).</summary>
    public string? SkipReason { get; init; }

    /// <summary>Creates a successful auto-link result.</summary>
    public static AutoLinkResult Success(bool participantCreated, bool walletLinked, Guid participantId) =>
        new() { ParticipantCreated = participantCreated, WalletLinked = walletLinked, ParticipantId = participantId };

    /// <summary>Creates a skipped auto-link result.</summary>
    public static AutoLinkResult Skipped(string reason, Guid? participantId = null) =>
        new() { SkipReason = reason, ParticipantId = participantId };
}
