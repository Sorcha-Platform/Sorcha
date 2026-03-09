// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Service.Models.Dtos;

/// <summary>
/// Response containing domain restriction configuration for an organization.
/// </summary>
public record DomainRestrictionsResponse
{
    /// <summary>
    /// List of allowed email domains for auto-provisioning.
    /// Empty means no restrictions (all domains allowed).
    /// </summary>
    public string[] AllowedDomains { get; init; } = [];

    /// <summary>
    /// Whether domain restrictions are currently active (AllowedDomains is non-empty).
    /// </summary>
    public bool RestrictionsActive { get; init; }
}

/// <summary>
/// Request to update domain restrictions for auto-provisioning.
/// </summary>
public record UpdateDomainRestrictionsRequest
{
    /// <summary>
    /// List of allowed email domains. Empty array disables restrictions.
    /// Each domain must be a valid domain format (e.g., "acme.com", "corp.example.org").
    /// </summary>
    public string[] AllowedDomains { get; init; } = [];
}
