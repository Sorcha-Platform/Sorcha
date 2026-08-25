// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.Text.Json.Serialization;

namespace Sorcha.Cli.Models;

/// <summary>
/// Organization status enumeration.
/// </summary>
public enum OrganizationStatus
{
    /// <summary>
    /// Organization is active and operational.
    /// </summary>
    Active = 0,

    /// <summary>
    /// Organization is suspended.
    /// </summary>
    Suspended = 1,

    /// <summary>
    /// Organization is inactive/deleted.
    /// </summary>
    Inactive = 2
}

/// <summary>
/// Represents an organization (tenant) in the Sorcha platform.
/// </summary>
public class Organization
{
    /// <summary>
    /// Unique identifier for the organization.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Organization name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Subdomain for the organization.
    /// </summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// Organization status.
    /// </summary>
    public OrganizationStatus Status { get; set; }

    /// <summary>
    /// Creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Branding configuration.
    /// </summary>
    /// <summary>
    /// The organisation's wallet address, when it has been provisioned. Present on the wire since
    /// the org-wallet work; the CLI previously dropped it silently.
    /// </summary>
    public string? WalletAddress { get; set; }

    public BrandingConfiguration? Branding { get; set; }
}

/// <summary>
/// Branding configuration for an organization.
/// </summary>
public class BrandingConfiguration
{
    /// <summary>
    /// URL to organization logo.
    /// </summary>
    public string? LogoUrl { get; set; }

    /// <summary>
    /// Primary brand color (hex format).
    /// </summary>
    public string? PrimaryColor { get; set; }

    /// <summary>
    /// Secondary brand color (hex format).
    /// </summary>
    public string? SecondaryColor { get; set; }

    /// <summary>
    /// Company tagline.
    /// </summary>
    public string? CompanyTagline { get; set; }
}

/// <summary>
/// Response containing a list of organizations.
/// </summary>
public class OrganizationListResponse
{
    /// <summary>
    /// List of organizations.
    /// </summary>
    public List<Organization> Organizations { get; set; } = [];

    /// <summary>
    /// Total count of organizations.
    /// </summary>
    public int TotalCount { get; set; }
}

/// <summary>
/// Request to create a new organization.
/// </summary>
public class CreateOrganizationRequest
{
    /// <summary>
    /// Organization name.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Unique subdomain (3-50 alphanumeric characters with hyphens).
    /// </summary>
    public required string Subdomain { get; set; }

    /// <summary>
    /// Optional branding configuration.
    /// </summary>
    public BrandingConfiguration? Branding { get; set; }
}

/// <summary>
/// Request to update an organization.
/// </summary>
public class UpdateOrganizationRequest
{
    /// <summary>
    /// Updated organization name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Updated organization status.
    /// </summary>
    public OrganizationStatus? Status { get; set; }

    /// <summary>
    /// Updated branding configuration.
    /// </summary>
    public BrandingConfiguration? Branding { get; set; }
}

/// <summary>
/// Body of <c>POST /api/organizations/{organizationId}/wallet</c> (#1525).
/// </summary>
/// <remarks>
/// Only the address travels — the recovery phrase stays with the admin who created the wallet.
/// </remarks>
public class LinkOrganizationWalletRequest
{
    /// <summary>Address of the wallet created with this organisation as owner.</summary>
    [JsonPropertyName("walletAddress")]
    public string WalletAddress { get; set; } = string.Empty;
}
