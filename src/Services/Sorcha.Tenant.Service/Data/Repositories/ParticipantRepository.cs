// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.EntityFrameworkCore;
using Sorcha.Tenant.Models;
using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Data.Repositories;

/// <summary>
/// Repository implementation for Participant Identity entity operations.
/// </summary>
public class ParticipantRepository : IParticipantRepository
{
    private readonly TenantDbContext _context;

    public ParticipantRepository(TenantDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }


    public async Task<ParticipantIdentity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.ParticipantIdentities
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public async Task<ParticipantIdentity?> GetByIdWithWalletsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.ParticipantIdentities
            .Include(p => p.LinkedWalletAddresses.Where(w => w.Status == WalletLinkStatus.Active))
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public async Task<ParticipantIdentity?> GetByUserAndOrgAsync(Guid userId, Guid organizationId, CancellationToken cancellationToken = default)
    {
        return await _context.ParticipantIdentities
            .FirstOrDefaultAsync(p => p.UserId == userId && p.OrganizationId == organizationId, cancellationToken);
    }

    public async Task<List<ParticipantIdentity>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _context.ParticipantIdentities
            .Where(p => p.UserId == userId)
            .Include(p => p.LinkedWalletAddresses.Where(w => w.Status == WalletLinkStatus.Active))
            .ToListAsync(cancellationToken);
    }

    public async Task<(List<ParticipantIdentity> Participants, int TotalCount)> GetByOrganizationAsync(
        Guid organizationId,
        int page = 1,
        int pageSize = 20,
        ParticipantIdentityStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.ParticipantIdentities
            .Where(p => p.OrganizationId == organizationId);

        if (status.HasValue)
        {
            query = query.Where(p => p.Status == status.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var participants = await query
            .OrderBy(p => p.DisplayName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (participants, totalCount);
    }

    public async Task<(List<ParticipantIdentity> Participants, int TotalCount)> SearchAsync(
        ParticipantSearchCriteria criteria,
        CancellationToken cancellationToken = default)
    {
        var query = _context.ParticipantIdentities.AsQueryable();

        // Apply org-scoped visibility (unless system admin)
        if (!criteria.IsSystemAdmin && criteria.AccessibleOrganizations?.Count > 0)
        {
            query = query.Where(p => criteria.AccessibleOrganizations.Contains(p.OrganizationId));
        }

        // Filter by specific organization
        if (criteria.OrganizationId.HasValue)
        {
            query = query.Where(p => p.OrganizationId == criteria.OrganizationId.Value);
        }

        // Filter by status
        if (criteria.Status.HasValue)
        {
            query = query.Where(p => p.Status == criteria.Status.Value);
        }

        // Filter by search query (display name, email)
        if (!string.IsNullOrWhiteSpace(criteria.Query))
        {
            var searchTerm = criteria.Query.ToLowerInvariant();
            query = query.Where(p =>
                p.DisplayName.ToLower().Contains(searchTerm) ||
                p.Email.ToLower().Contains(searchTerm));
        }

        // Filter by wallet link presence
        if (criteria.HasLinkedWallet.HasValue)
        {
            if (criteria.HasLinkedWallet.Value)
            {
                query = query.Where(p => p.LinkedWalletAddresses.Any(w => w.Status == WalletLinkStatus.Active));
            }
            else
            {
                query = query.Where(p => !p.LinkedWalletAddresses.Any(w => w.Status == WalletLinkStatus.Active));
            }
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var participants = await query
            .OrderBy(p => p.DisplayName)
            .Skip((criteria.Page - 1) * criteria.PageSize)
            .Take(criteria.PageSize)
            .ToListAsync(cancellationToken);

        return (participants, totalCount);
    }

    public async Task<ParticipantIdentity> CreateAsync(ParticipantIdentity participant, CancellationToken cancellationToken = default)
    {
        _context.ParticipantIdentities.Add(participant);
        await _context.SaveChangesAsync(cancellationToken);
        return participant;
    }

    public async Task<ParticipantIdentity> UpdateAsync(ParticipantIdentity participant, CancellationToken cancellationToken = default)
    {
        participant.UpdatedAt = DateTimeOffset.UtcNow;
        _context.ParticipantIdentities.Update(participant);
        await _context.SaveChangesAsync(cancellationToken);
        return participant;
    }

    public async Task<bool> ExistsAsync(Guid userId, Guid organizationId, CancellationToken cancellationToken = default)
    {
        return await _context.ParticipantIdentities
            .AnyAsync(p => p.UserId == userId && p.OrganizationId == organizationId, cancellationToken);
    }



    public async Task<LinkedWalletAddress?> GetWalletLinkByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.LinkedWalletAddresses
            .FirstOrDefaultAsync(w => w.Id == id, cancellationToken);
    }

    public async Task<LinkedWalletAddress?> GetActiveWalletLinkByAddressAsync(string walletAddress, CancellationToken cancellationToken = default)
    {
        return await _context.LinkedWalletAddresses
            .FirstOrDefaultAsync(w => w.WalletAddress == walletAddress && w.Status == WalletLinkStatus.Active, cancellationToken);
    }

    public async Task<List<LinkedWalletAddress>> GetWalletLinksAsync(Guid participantId, bool includeRevoked = false, CancellationToken cancellationToken = default)
    {
        var query = _context.LinkedWalletAddresses
            .Where(w => w.ParticipantId == participantId);

        if (!includeRevoked)
        {
            query = query.Where(w => w.Status == WalletLinkStatus.Active);
        }

        return await query.OrderByDescending(w => w.LinkedAt).ToListAsync(cancellationToken);
    }

    public async Task<int> GetActiveWalletLinkCountAsync(Guid participantId, CancellationToken cancellationToken = default)
    {
        return await _context.LinkedWalletAddresses
            .CountAsync(w => w.ParticipantId == participantId && w.Status == WalletLinkStatus.Active, cancellationToken);
    }

    public async Task<LinkedWalletAddress> CreateWalletLinkAsync(LinkedWalletAddress walletLink, CancellationToken cancellationToken = default)
    {
        _context.LinkedWalletAddresses.Add(walletLink);
        await _context.SaveChangesAsync(cancellationToken);
        return walletLink;
    }

    public async Task<LinkedWalletAddress> UpdateWalletLinkAsync(LinkedWalletAddress walletLink, CancellationToken cancellationToken = default)
    {
        _context.LinkedWalletAddresses.Update(walletLink);
        await _context.SaveChangesAsync(cancellationToken);
        return walletLink;
    }

    public async Task<ParticipantIdentity?> GetParticipantByWalletAddressAsync(string walletAddress, CancellationToken cancellationToken = default)
    {
        var walletLink = await _context.LinkedWalletAddresses
            .Include(w => w.Participant)
            .FirstOrDefaultAsync(w => w.WalletAddress == walletAddress && w.Status == WalletLinkStatus.Active, cancellationToken);

        return walletLink?.Participant;
    }



    public async Task<WalletLinkChallenge?> GetChallengeByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.WalletLinkChallenges
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
    }

    public async Task<WalletLinkChallenge?> GetPendingChallengeAsync(Guid participantId, string walletAddress, CancellationToken cancellationToken = default)
    {
        return await _context.WalletLinkChallenges
            .FirstOrDefaultAsync(c =>
                c.ParticipantId == participantId &&
                c.WalletAddress == walletAddress &&
                c.Status == ChallengeStatus.Pending &&
                c.ExpiresAt > DateTimeOffset.UtcNow,
                cancellationToken);
    }

    public async Task<WalletLinkChallenge> CreateChallengeAsync(WalletLinkChallenge challenge, CancellationToken cancellationToken = default)
    {
        _context.WalletLinkChallenges.Add(challenge);
        await _context.SaveChangesAsync(cancellationToken);
        return challenge;
    }

    public async Task<WalletLinkChallenge> UpdateChallengeAsync(WalletLinkChallenge challenge, CancellationToken cancellationToken = default)
    {
        _context.WalletLinkChallenges.Update(challenge);
        await _context.SaveChangesAsync(cancellationToken);
        return challenge;
    }

    public async Task<int> ExpirePendingChallengesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var expiredChallenges = await _context.WalletLinkChallenges
            .Where(c => c.Status == ChallengeStatus.Pending && c.ExpiresAt <= now)
            .ToListAsync(cancellationToken);

        foreach (var challenge in expiredChallenges)
        {
            challenge.Status = ChallengeStatus.Expired;
        }

        await _context.SaveChangesAsync(cancellationToken);
        return expiredChallenges.Count;
    }



    public async Task<ParticipantAuditEntry> CreateAuditEntryAsync(ParticipantAuditEntry entry, CancellationToken cancellationToken = default)
    {
        _context.ParticipantAuditEntries.Add(entry);
        await _context.SaveChangesAsync(cancellationToken);
        return entry;
    }

    public async Task<(List<ParticipantAuditEntry> Entries, int TotalCount)> GetAuditEntriesAsync(
        Guid participantId,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var query = _context.ParticipantAuditEntries
            .Where(e => e.ParticipantId == participantId);

        var totalCount = await query.CountAsync(cancellationToken);

        var entries = await query
            .OrderByDescending(e => e.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (entries, totalCount);
    }

}
