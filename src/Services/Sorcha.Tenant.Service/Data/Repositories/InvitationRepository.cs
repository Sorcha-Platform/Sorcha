// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.EntityFrameworkCore;

using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Data.Repositories;

/// <summary>
/// Repository implementation for OrgInvitation operations.
/// </summary>
public class InvitationRepository : IInvitationRepository
{
    private readonly TenantDbContext _context;

    /// <summary>
    /// Initializes a new instance of <see cref="InvitationRepository"/>.
    /// </summary>
    public InvitationRepository(TenantDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <inheritdoc />
    public async Task<OrgInvitation?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.OrgInvitations
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OrgInvitation?> GetByTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        return await _context.OrgInvitations
            .FirstOrDefaultAsync(i => i.Token == token, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<List<OrgInvitation>> GetByOrganizationAsync(
        Guid organizationId,
        InvitationStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.OrgInvitations
            .Where(i => i.OrganizationId == organizationId);

        if (status.HasValue)
        {
            query = query.Where(i => i.Status == status.Value);
        }

        return await query
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> HasActiveInvitationAsync(
        Guid organizationId,
        string email,
        CancellationToken cancellationToken = default)
    {
        return await _context.OrgInvitations
            .AnyAsync(i => i.OrganizationId == organizationId
                        && i.Email == email
                        && i.Status == InvitationStatus.Pending
                        && i.ExpiresAt > DateTimeOffset.UtcNow,
                cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OrgInvitation> CreateAsync(OrgInvitation invitation, CancellationToken cancellationToken = default)
    {
        _context.OrgInvitations.Add(invitation);
        await _context.SaveChangesAsync(cancellationToken);
        return invitation;
    }

    /// <inheritdoc />
    public async Task<OrgInvitation> UpdateAsync(OrgInvitation invitation, CancellationToken cancellationToken = default)
    {
        _context.OrgInvitations.Update(invitation);
        await _context.SaveChangesAsync(cancellationToken);
        return invitation;
    }
}
