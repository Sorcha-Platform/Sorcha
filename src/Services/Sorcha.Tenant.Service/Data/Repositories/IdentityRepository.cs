// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.EntityFrameworkCore;
using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Data.Repositories;

/// <summary>
/// Repository implementation for Identity entity operations.
/// Handles UserIdentity, PublicIdentity, and ServicePrincipal entities.
/// </summary>
public class IdentityRepository : IIdentityRepository
{
    private readonly TenantDbContext _context;

    public IdentityRepository(TenantDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }


    public async Task<UserIdentity?> GetUserByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.UserIdentities
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
    }

    public async Task<UserIdentity?> GetUserByExternalIdAsync(string externalIdpUserId, CancellationToken cancellationToken = default)
    {
        return await _context.UserIdentities
            .FirstOrDefaultAsync(u => u.ExternalIdpUserId == externalIdpUserId, cancellationToken);
    }

    public async Task<UserIdentity?> GetUserByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        return await _context.UserIdentities
            .FirstOrDefaultAsync(u => u.Email == email, cancellationToken);
    }

    public async Task<List<UserIdentity>> GetActiveUsersAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        return await _context.UserIdentities
            .Where(u => u.OrganizationId == organizationId && u.Status == IdentityStatus.Active)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<UserIdentity>> GetAllUsersAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        return await _context.UserIdentities
            .Where(u => u.OrganizationId == organizationId)
            .ToListAsync(cancellationToken);
    }

    public async Task<UserIdentity> CreateUserAsync(UserIdentity user, CancellationToken cancellationToken = default)
    {
        _context.UserIdentities.Add(user);
        await _context.SaveChangesAsync(cancellationToken);
        return user;
    }

    public async Task<UserIdentity> UpdateUserAsync(UserIdentity user, CancellationToken cancellationToken = default)
    {
        _context.UserIdentities.Update(user);
        await _context.SaveChangesAsync(cancellationToken);
        return user;
    }

    public async Task DeactivateUserAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await GetUserByIdAsync(id, cancellationToken);
        if (user != null)
        {
            user.Status = IdentityStatus.Suspended;
            await UpdateUserAsync(user, cancellationToken);
        }
    }

    public async Task<int> GetTotalActiveUserCountAsync(CancellationToken cancellationToken = default)
    {
        return await _context.UserIdentities
            .CountAsync(u => u.Status == IdentityStatus.Active, cancellationToken);
    }



    public async Task<PublicIdentity?> GetPublicIdentityByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.PublicIdentities
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public async Task<PublicIdentity?> GetPublicIdentityByCredentialIdAsync(byte[] credentialId, CancellationToken cancellationToken = default)
    {
        return await _context.PublicIdentities
            .FirstOrDefaultAsync(p => p.PassKeyCredentialId == credentialId, cancellationToken);
    }

    public async Task<PublicIdentity> CreatePublicIdentityAsync(PublicIdentity identity, CancellationToken cancellationToken = default)
    {
        _context.PublicIdentities.Add(identity);
        await _context.SaveChangesAsync(cancellationToken);
        return identity;
    }

    public async Task<PublicIdentity> UpdatePublicIdentityAsync(PublicIdentity identity, CancellationToken cancellationToken = default)
    {
        _context.PublicIdentities.Update(identity);
        await _context.SaveChangesAsync(cancellationToken);
        return identity;
    }

    public async Task DeletePublicIdentityAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var identity = await GetPublicIdentityByIdAsync(id, cancellationToken);
        if (identity != null)
        {
            _context.PublicIdentities.Remove(identity);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }



    public async Task<ServicePrincipal?> GetServicePrincipalByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.ServicePrincipals
            .FirstOrDefaultAsync(sp => sp.Id == id, cancellationToken);
    }

    public async Task<ServicePrincipal?> GetServicePrincipalByClientIdAsync(string clientId, CancellationToken cancellationToken = default)
    {
        return await _context.ServicePrincipals
            .FirstOrDefaultAsync(sp => sp.ClientId == clientId, cancellationToken);
    }

    public async Task<ServicePrincipal?> GetServicePrincipalByNameAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        return await _context.ServicePrincipals
            .FirstOrDefaultAsync(sp => sp.ServiceName == serviceName, cancellationToken);
    }

    public async Task<List<ServicePrincipal>> GetActiveServicePrincipalsAsync(CancellationToken cancellationToken = default)
    {
        return await _context.ServicePrincipals
            .Where(sp => sp.Status == ServicePrincipalStatus.Active)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<ServicePrincipal>> GetAllServicePrincipalsAsync(CancellationToken cancellationToken = default)
    {
        return await _context.ServicePrincipals
            .ToListAsync(cancellationToken);
    }

    public async Task<ServicePrincipal> CreateServicePrincipalAsync(ServicePrincipal servicePrincipal, CancellationToken cancellationToken = default)
    {
        _context.ServicePrincipals.Add(servicePrincipal);
        await _context.SaveChangesAsync(cancellationToken);
        return servicePrincipal;
    }

    public async Task<ServicePrincipal> UpdateServicePrincipalAsync(ServicePrincipal servicePrincipal, CancellationToken cancellationToken = default)
    {
        _context.ServicePrincipals.Update(servicePrincipal);
        await _context.SaveChangesAsync(cancellationToken);
        return servicePrincipal;
    }

    public async Task DeactivateServicePrincipalAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var servicePrincipal = await GetServicePrincipalByIdAsync(id, cancellationToken);
        if (servicePrincipal != null)
        {
            servicePrincipal.Status = ServicePrincipalStatus.Revoked;
            await UpdateServicePrincipalAsync(servicePrincipal, cancellationToken);
        }
    }

}
