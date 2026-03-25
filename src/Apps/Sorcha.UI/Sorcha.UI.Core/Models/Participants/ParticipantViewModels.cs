// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models.Participants;

/// <summary>
/// View model for participant display in lists.
/// </summary>
public class ParticipantListItemViewModel
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid OrganizationId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool HasLinkedWallet { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Publish status on register (None, Published, Revoked).
    /// </summary>
    public string? PublishStatus { get; set; }

    /// <summary>
    /// Name of the register this participant is published to.
    /// </summary>
    public string? PublishedRegisterName { get; set; }

    /// <summary>
    /// When the participant was published.
    /// </summary>
    public DateTimeOffset? PublishedAt { get; set; }

    /// <summary>
    /// Version number on the register.
    /// </summary>
    public int? PublishedVersion { get; set; }
}

/// <summary>
/// View model for participant details.
/// </summary>
public class ParticipantDetailViewModel
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid OrganizationId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset? DeactivatedAt { get; set; }
    public List<LinkedWalletViewModel> LinkedWallets { get; set; } = new();
}

/// <summary>
/// View model for linked wallet address.
/// </summary>
public class LinkedWalletViewModel
{
    public Guid Id { get; set; }
    public string WalletAddress { get; set; } = string.Empty;
    public string Algorithm { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset LinkedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

/// <summary>
/// View model for participant list response.
/// </summary>
public class ParticipantListViewModel
{
    public List<ParticipantListItemViewModel> Participants { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

/// <summary>
/// View model for participant search request.
/// </summary>
public class ParticipantSearchViewModel
{
    public string? Query { get; set; }
    public Guid? OrganizationId { get; set; }
    public string? Status { get; set; }
    public bool? HasLinkedWallet { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

/// <summary>
/// View model for participant search results.
/// </summary>
public class ParticipantSearchResultsViewModel
{
    public List<ParticipantListItemViewModel> Results { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public string? Query { get; set; }
}

/// <summary>
/// View model for creating a new participant.
/// </summary>
public class CreateParticipantViewModel
{
    public Guid UserId { get; set; }
    public string? DisplayName { get; set; }
}

/// <summary>
/// View model for updating a participant.
/// </summary>
public class UpdateParticipantViewModel
{
    public string? DisplayName { get; set; }
    public string? Status { get; set; }
}

/// <summary>
/// View model for initiating a wallet link.
/// </summary>
public class InitiateWalletLinkViewModel
{
    public string WalletAddress { get; set; } = string.Empty;
    public string Algorithm { get; set; } = "ED25519";
}

/// <summary>
/// View model for wallet link challenge response.
/// </summary>
public class WalletLinkChallengeViewModel
{
    public Guid ChallengeId { get; set; }
    public string Challenge { get; set; } = string.Empty;
    public string WalletAddress { get; set; } = string.Empty;
    public string Algorithm { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// View model for verifying a wallet link.
/// </summary>
public class VerifyWalletLinkViewModel
{
    public string Signature { get; set; } = string.Empty;
}
