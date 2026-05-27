// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.UI.Core.Models.Participants;

namespace Sorcha.UI.Core.Services.Participants;

/// <summary>
/// Service interface for interacting with the Participant API.
/// </summary>
public interface IParticipantApiService
{
    /// <summary>
    /// Lists participants in an organization.
    /// </summary>
    Task<ParticipantListViewModel> ListParticipantsAsync(
        Guid organizationId,
        int page = 1,
        int pageSize = 20,
        string? status = null,
        CancellationToken ct = default);

    /// <summary>
    /// Gets a participant by ID.
    /// </summary>
    Task<ParticipantDetailViewModel?> GetParticipantAsync(
        Guid organizationId,
        Guid participantId,
        CancellationToken ct = default);

    /// <summary>
    /// Creates a new participant.
    /// </summary>
    Task<ParticipantDetailViewModel> CreateParticipantAsync(
        Guid organizationId,
        CreateParticipantViewModel request,
        CancellationToken ct = default);

    /// <summary>
    /// Updates a participant.
    /// </summary>
    Task<ParticipantDetailViewModel?> UpdateParticipantAsync(
        Guid organizationId,
        Guid participantId,
        UpdateParticipantViewModel request,
        CancellationToken ct = default);

    /// <summary>
    /// Deactivates a participant.
    /// </summary>
    Task<bool> DeactivateParticipantAsync(
        Guid organizationId,
        Guid participantId,
        CancellationToken ct = default);

    /// <summary>
    /// Suspends a participant.
    /// </summary>
    Task<bool> SuspendParticipantAsync(
        Guid organizationId,
        Guid participantId,
        CancellationToken ct = default);

    /// <summary>
    /// Reactivates a participant.
    /// </summary>
    Task<bool> ReactivateParticipantAsync(
        Guid organizationId,
        Guid participantId,
        CancellationToken ct = default);

    /// <summary>
    /// Searches participants across accessible organizations.
    /// </summary>
    Task<ParticipantSearchResultsViewModel> SearchParticipantsAsync(
        ParticipantSearchViewModel request,
        CancellationToken ct = default);

    /// <summary>
    /// Gets a participant by wallet address.
    /// </summary>
    Task<ParticipantListItemViewModel?> GetParticipantByWalletAsync(
        string walletAddress,
        CancellationToken ct = default);

    /// <summary>
    /// Gets all participant profiles for the current user.
    /// </summary>
    Task<List<ParticipantDetailViewModel>> GetMyProfilesAsync(CancellationToken ct = default);

    /// <summary>
    /// Self-registers as a participant in an organization.
    /// </summary>
    Task<ParticipantDetailViewModel> SelfRegisterAsync(
        Guid organizationId,
        string? displayName = null,
        CancellationToken ct = default);

    /// <summary>
    /// Initiates a wallet link challenge.
    /// </summary>
    Task<WalletLinkChallengeViewModel> InitiateWalletLinkAsync(
        Guid organizationId,
        Guid participantId,
        InitiateWalletLinkViewModel request,
        CancellationToken ct = default);

    /// <summary>
    /// Verifies a wallet link challenge.
    /// </summary>
    Task<LinkedWalletViewModel> VerifyWalletLinkAsync(
        Guid organizationId,
        Guid participantId,
        Guid challengeId,
        VerifyWalletLinkViewModel request,
        CancellationToken ct = default);

    /// <summary>
    /// Lists wallet links for a participant.
    /// </summary>
    Task<List<LinkedWalletViewModel>> ListWalletLinksAsync(
        Guid organizationId,
        Guid participantId,
        bool includeRevoked = false,
        CancellationToken ct = default);

    /// <summary>
    /// Revokes a wallet link.
    /// </summary>
    Task<bool> RevokeWalletLinkAsync(
        Guid organizationId,
        Guid participantId,
        Guid linkId,
        CancellationToken ct = default);
}
