// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Tenant.Service.Models.Dtos;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Manages identity provider configuration for organizations.
/// Handles CRUD operations, OIDC discovery, connection testing, and enable/disable toggling.
/// </summary>
public interface IIdpConfigurationService
{
    /// <summary>
    /// Gets the IDP configuration for an organization.
    /// </summary>
    /// <param name="organizationId">Organization ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Configuration response, or null if no IDP is configured.</returns>
    Task<IdpConfigurationResponse?> GetConfigurationAsync(Guid organizationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates or updates the IDP configuration for an organization.
    /// Automatically triggers discovery to populate endpoints.
    /// </summary>
    /// <param name="organizationId">Organization ID.</param>
    /// <param name="request">Configuration request with provider details.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The saved configuration response.</returns>
    Task<IdpConfigurationResponse> CreateOrUpdateAsync(Guid organizationId, IdpConfigurationRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the IDP configuration for an organization.
    /// </summary>
    /// <param name="organizationId">Organization ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if a configuration was removed.</returns>
    Task<bool> DeleteAsync(Guid organizationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Discovers IDP endpoints by fetching the OIDC discovery document.
    /// </summary>
    /// <param name="organizationId">Organization ID.</param>
    /// <param name="issuerUrl">Issuer URL to discover.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Discovered endpoints.</returns>
    Task<DiscoveryResponse> DiscoverAsync(Guid organizationId, string issuerUrl, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tests the IDP connection by attempting a client_credentials grant.
    /// </summary>
    /// <param name="organizationId">Organization ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Connection test result.</returns>
    Task<TestConnectionResponse> TestConnectionAsync(Guid organizationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enables or disables the IDP for an organization.
    /// </summary>
    /// <param name="organizationId">Organization ID.</param>
    /// <param name="enabled">Whether to enable or disable.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Updated configuration response.</returns>
    Task<IdpConfigurationResponse> ToggleAsync(Guid organizationId, bool enabled, CancellationToken cancellationToken = default);
}
