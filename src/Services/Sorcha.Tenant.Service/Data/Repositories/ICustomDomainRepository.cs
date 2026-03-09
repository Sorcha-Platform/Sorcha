// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Data.Repositories;

/// <summary>
/// Repository interface for CustomDomainMapping operations.
/// </summary>
public interface ICustomDomainRepository
{
    /// <summary>
    /// Gets the custom domain mapping for an organization.
    /// </summary>
    Task<CustomDomainMapping?> GetByOrganizationIdAsync(Guid organizationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a custom domain mapping by domain name.
    /// </summary>
    Task<CustomDomainMapping?> GetByDomainAsync(string domain, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all verified custom domain mappings.
    /// </summary>
    Task<List<CustomDomainMapping>> GetVerifiedDomainsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new custom domain mapping.
    /// </summary>
    Task<CustomDomainMapping> CreateAsync(CustomDomainMapping mapping, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing custom domain mapping.
    /// </summary>
    Task<CustomDomainMapping> UpdateAsync(CustomDomainMapping mapping, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a custom domain mapping.
    /// </summary>
    Task DeleteAsync(CustomDomainMapping mapping, CancellationToken cancellationToken = default);
}
