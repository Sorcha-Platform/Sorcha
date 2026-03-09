// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Tenant.Service.Models.Dtos;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Service for managing custom domain mappings for organizations.
/// </summary>
public interface ICustomDomainService
{
    /// <summary>
    /// Gets the custom domain configuration for an organization.
    /// </summary>
    Task<CustomDomainResponse> GetCustomDomainAsync(Guid organizationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Configures a custom domain for an organization. Returns CNAME instructions.
    /// </summary>
    Task<CnameInstructionsResponse> ConfigureCustomDomainAsync(Guid organizationId, ConfigureCustomDomainRequest request, Guid adminUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies the DNS CNAME record for the organization's custom domain.
    /// </summary>
    Task<DomainVerificationResponse> VerifyCustomDomainAsync(Guid organizationId, Guid adminUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the custom domain configuration for an organization.
    /// </summary>
    Task RemoveCustomDomainAsync(Guid organizationId, Guid adminUserId, CancellationToken cancellationToken = default);
}
