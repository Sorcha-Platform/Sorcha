// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Tenant.Models;
using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Data.Repositories;

/// <summary>
/// Repository interface for Participant Identity entity operations.
/// Handles ParticipantIdentity, LinkedWalletAddress, WalletLinkChallenge, and ParticipantAuditEntry entities.
/// </summary>
public interface IParticipantRepository
{

    /// <summary>
    /// Gets a participant identity by ID.
    /// </summary>
    Task<ParticipantIdentity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a participant identity by ID with linked wallet addresses.
    /// </summary>
    Task<ParticipantIdentity?> GetByIdWithWalletsAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a participant identity by user ID and organization ID.
    /// </summary>
    Task<ParticipantIdentity?> GetByUserAndOrgAsync(Guid userId, Guid organizationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all participant identities for a user across all organizations.
    /// </summary>
    Task<List<ParticipantIdentity>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets active participants for an organization with pagination.
    /// </summary>
    Task<(List<ParticipantIdentity> Participants, int TotalCount)> GetByOrganizationAsync(
        Guid organizationId,
        int page = 1,
        int pageSize = 20,
        ParticipantIdentityStatus? status = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches participants based on criteria with org-scoped visibility.
    /// </summary>
    Task<(List<ParticipantIdentity> Participants, int TotalCount)> SearchAsync(
        ParticipantSearchCriteria criteria,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new participant identity.
    /// </summary>
    Task<ParticipantIdentity> CreateAsync(ParticipantIdentity participant, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing participant identity.
    /// </summary>
    Task<ParticipantIdentity> UpdateAsync(ParticipantIdentity participant, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a participant identity exists for the given user and organization.
    /// </summary>
    Task<bool> ExistsAsync(Guid userId, Guid organizationId, CancellationToken cancellationToken = default);



    /// <summary>
    /// Gets a linked wallet address by ID.
    /// </summary>
    Task<LinkedWalletAddress?> GetWalletLinkByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an active linked wallet address by wallet address string.
    /// Returns null if address is not linked or link is revoked.
    /// </summary>
    Task<LinkedWalletAddress?> GetActiveWalletLinkByAddressAsync(string walletAddress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all linked wallet addresses for a participant.
    /// </summary>
    Task<List<LinkedWalletAddress>> GetWalletLinksAsync(Guid participantId, bool includeRevoked = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the count of active wallet links for a participant.
    /// </summary>
    Task<int> GetActiveWalletLinkCountAsync(Guid participantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new wallet link.
    /// </summary>
    Task<LinkedWalletAddress> CreateWalletLinkAsync(LinkedWalletAddress walletLink, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a wallet link (e.g., for revocation).
    /// </summary>
    Task<LinkedWalletAddress> UpdateWalletLinkAsync(LinkedWalletAddress walletLink, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a participant identity by wallet address (if active link exists).
    /// </summary>
    Task<ParticipantIdentity?> GetParticipantByWalletAddressAsync(string walletAddress, CancellationToken cancellationToken = default);



    /// <summary>
    /// Gets a wallet link challenge by ID.
    /// </summary>
    Task<WalletLinkChallenge?> GetChallengeByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a pending challenge for a participant and wallet address.
    /// </summary>
    Task<WalletLinkChallenge?> GetPendingChallengeAsync(Guid participantId, string walletAddress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new wallet link challenge.
    /// </summary>
    Task<WalletLinkChallenge> CreateChallengeAsync(WalletLinkChallenge challenge, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a wallet link challenge (e.g., completion or expiration).
    /// </summary>
    Task<WalletLinkChallenge> UpdateChallengeAsync(WalletLinkChallenge challenge, CancellationToken cancellationToken = default);

    /// <summary>
    /// Expires all pending challenges that have passed their expiration time.
    /// Returns the number of challenges expired.
    /// </summary>
    Task<int> ExpirePendingChallengesAsync(CancellationToken cancellationToken = default);



    /// <summary>
    /// Creates an audit entry for a participant action.
    /// </summary>
    Task<ParticipantAuditEntry> CreateAuditEntryAsync(ParticipantAuditEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets audit entries for a participant with pagination.
    /// </summary>
    Task<(List<ParticipantAuditEntry> Entries, int TotalCount)> GetAuditEntriesAsync(
        Guid participantId,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default);

}
