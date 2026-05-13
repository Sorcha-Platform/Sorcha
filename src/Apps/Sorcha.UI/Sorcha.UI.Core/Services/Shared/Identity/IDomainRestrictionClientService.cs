// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Services.Identity;

/// <summary>
/// Client-side service for managing domain restrictions for OIDC auto-provisioning.
/// Wraps HTTP calls to the Tenant Service domain restriction API.
/// </summary>
public interface IDomainRestrictionClientService
{
    /// <summary>
    /// Gets the current domain restriction configuration for an organization.
    /// </summary>
    /// <param name="organizationId">Organization ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Domain restrictions DTO with allowed domains and active status.</returns>
    Task<DomainRestrictionsDto> GetRestrictionsAsync(
        Guid organizationId,
        CancellationToken ct = default);

    /// <summary>
    /// Updates the allowed domains for an organization.
    /// An empty array disables restrictions.
    /// </summary>
    /// <param name="organizationId">Organization ID.</param>
    /// <param name="allowedDomains">Array of allowed email domains.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the update was successful.</returns>
    Task<bool> UpdateRestrictionsAsync(
        Guid organizationId,
        string[] allowedDomains,
        CancellationToken ct = default);
}

/// <summary>
/// Domain restriction configuration DTO.
/// </summary>
public record DomainRestrictionsDto
{
    /// <summary>
    /// List of allowed email domains for auto-provisioning.
    /// </summary>
    public string[] AllowedDomains { get; init; } = [];

    /// <summary>
    /// Whether domain restrictions are currently active.
    /// </summary>
    public bool RestrictionsActive { get; init; }
}
