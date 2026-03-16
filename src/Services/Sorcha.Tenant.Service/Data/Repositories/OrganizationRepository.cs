// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.EntityFrameworkCore;
using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Data.Repositories;

/// <summary>
/// Repository implementation for Organization entity operations.
/// </summary>
public class OrganizationRepository : IOrganizationRepository
{
    private readonly TenantDbContext _context;

    public OrganizationRepository(TenantDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<Organization?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Organizations
            .Include(o => o.IdentityProviders)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
    }

    public async Task<Organization?> GetBySubdomainAsync(string subdomain, CancellationToken cancellationToken = default)
    {
        return await _context.Organizations
            .Include(o => o.IdentityProviders)
            .FirstOrDefaultAsync(o => o.Subdomain == subdomain, cancellationToken);
    }

    public async Task<List<Organization>> GetAllActiveAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Organizations
            .Where(o => o.Status == OrganizationStatus.Active)
            .Include(o => o.IdentityProviders)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<Organization>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Organizations
            .Include(o => o.IdentityProviders)
            .ToListAsync(cancellationToken);
    }

    public async Task<Organization> CreateAsync(Organization organization, CancellationToken cancellationToken = default)
    {
        _context.Organizations.Add(organization);
        await _context.SaveChangesAsync(cancellationToken);
        return organization;
    }

    public async Task<Organization> UpdateAsync(Organization organization, CancellationToken cancellationToken = default)
    {
        _context.Organizations.Update(organization);
        await _context.SaveChangesAsync(cancellationToken);
        return organization;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var organization = await GetByIdAsync(id, cancellationToken);
        if (organization != null)
        {
            organization.Status = OrganizationStatus.Deleted;
            await UpdateAsync(organization, cancellationToken);
        }
    }

    public async Task<bool> SubdomainExistsAsync(string subdomain, CancellationToken cancellationToken = default)
    {
        return await _context.Organizations
            .AnyAsync(o => o.Subdomain == subdomain, cancellationToken);
    }
}
