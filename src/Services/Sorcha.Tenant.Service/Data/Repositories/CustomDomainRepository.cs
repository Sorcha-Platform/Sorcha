// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.EntityFrameworkCore;

using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Data.Repositories;

/// <summary>
/// Repository implementation for CustomDomainMapping operations.
/// </summary>
public class CustomDomainRepository : ICustomDomainRepository
{
    private readonly TenantDbContext _context;

    public CustomDomainRepository(TenantDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<CustomDomainMapping?> GetByOrganizationIdAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        return await _context.CustomDomainMappings
            .FirstOrDefaultAsync(m => m.OrganizationId == organizationId, cancellationToken);
    }

    public async Task<CustomDomainMapping?> GetByDomainAsync(string domain, CancellationToken cancellationToken = default)
    {
        return await _context.CustomDomainMappings
            .FirstOrDefaultAsync(m => m.Domain == domain, cancellationToken);
    }

    public async Task<List<CustomDomainMapping>> GetVerifiedDomainsAsync(CancellationToken cancellationToken = default)
    {
        return await _context.CustomDomainMappings
            .Where(m => m.Status == CustomDomainStatus.Verified)
            .ToListAsync(cancellationToken);
    }

    public async Task<CustomDomainMapping> CreateAsync(CustomDomainMapping mapping, CancellationToken cancellationToken = default)
    {
        _context.CustomDomainMappings.Add(mapping);
        await _context.SaveChangesAsync(cancellationToken);
        return mapping;
    }

    public async Task<CustomDomainMapping> UpdateAsync(CustomDomainMapping mapping, CancellationToken cancellationToken = default)
    {
        _context.CustomDomainMappings.Update(mapping);
        await _context.SaveChangesAsync(cancellationToken);
        return mapping;
    }

    public async Task DeleteAsync(CustomDomainMapping mapping, CancellationToken cancellationToken = default)
    {
        _context.CustomDomainMappings.Remove(mapping);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
