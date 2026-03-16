// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Service.Models.Dtos;

/// <summary>
/// Request to create a new organization.
/// </summary>
public record CreateOrganizationRequest
{
    /// <summary>
    /// Organization display name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Unique subdomain (3-50 alphanumeric characters with hyphens).
    /// </summary>
    public required string Subdomain { get; init; }

    /// <summary>
    /// Optional branding configuration.
    /// </summary>
    public BrandingConfigurationDto? Branding { get; init; }
}

/// <summary>
/// Request to update an existing organization.
/// </summary>
public record UpdateOrganizationRequest
{
    /// <summary>
    /// Updated organization name.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Updated organization status.
    /// </summary>
    public OrganizationStatus? Status { get; init; }

    /// <summary>
    /// Updated branding configuration.
    /// </summary>
    public BrandingConfigurationDto? Branding { get; init; }
}

/// <summary>
/// Branding configuration DTO.
/// </summary>
public record BrandingConfigurationDto
{
    /// <summary>
    /// URL to organization logo (HTTPS required).
    /// </summary>
    public string? LogoUrl { get; init; }

    /// <summary>
    /// Primary brand color (hex format).
    /// </summary>
    public string? PrimaryColor { get; init; }

    /// <summary>
    /// Secondary brand color (hex format).
    /// </summary>
    public string? SecondaryColor { get; init; }

    /// <summary>
    /// Company tagline.
    /// </summary>
    public string? CompanyTagline { get; init; }
}

/// <summary>
/// Organization response DTO.
/// </summary>
public record OrganizationResponse
{
    /// <summary>
    /// Organization ID.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Organization name.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Organization subdomain.
    /// </summary>
    public string Subdomain { get; init; } = string.Empty;

    /// <summary>
    /// Organization status.
    /// </summary>
    public OrganizationStatus Status { get; init; }

    /// <summary>
    /// Creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Branding configuration.
    /// </summary>
    public BrandingConfigurationDto? Branding { get; init; }

    /// <summary>
    /// Creates a response from an Organization entity.
    /// </summary>
    public static OrganizationResponse FromEntity(Organization org) => new()
    {
        Id = org.Id,
        Name = org.Name,
        Subdomain = org.Subdomain,
        Status = org.Status,
        CreatedAt = org.CreatedAt,
        Branding = org.Branding != null ? new BrandingConfigurationDto
        {
            LogoUrl = org.Branding.LogoUrl,
            PrimaryColor = org.Branding.PrimaryColor,
            SecondaryColor = org.Branding.SecondaryColor,
            CompanyTagline = org.Branding.CompanyTagline
        } : null
    };
}

/// <summary>
/// Request for admin-initiated organisation creation with admin invite.
/// Used by system admins to create private orgs and assign an administrator.
/// </summary>
public record AdminCreateOrganizationRequest
{
    /// <summary>
    /// Organisation display name (3-100 characters).
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Unique subdomain (3-50 chars, lowercase alphanumeric + hyphens).
    /// </summary>
    public required string Subdomain { get; init; }

    /// <summary>
    /// Optional organisation description (max 500 characters).
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Email address of the user to be invited to the organisation.
    /// If the email matches an existing PlatformUser, they are added directly.
    /// If new, a pending invitation is created for acceptance on signup.
    /// </summary>
    public required string AdminEmail { get; init; }

    /// <summary>
    /// Role to assign to the invited user (Administrator, Designer, Auditor, Member).
    /// Defaults to Administrator. SystemAdmin is not allowed.
    /// </summary>
    public UserRole Role { get; init; } = UserRole.Administrator;
}

/// <summary>
/// Response for admin-initiated organisation creation.
/// </summary>
public record AdminCreateOrganizationResponse
{
    /// <summary>Whether the provisioning succeeded.</summary>
    public bool Success { get; init; }

    /// <summary>The newly created organisation ID.</summary>
    public Guid? OrganizationId { get; init; }

    /// <summary>The organisation name.</summary>
    public string? OrganizationName { get; init; }

    /// <summary>The organisation subdomain.</summary>
    public string? Subdomain { get; init; }

    /// <summary>Whether the admin was directly added (true) or invited (false).</summary>
    public bool AdminDirectlyAdded { get; init; }

    /// <summary>The invitation ID if the admin was invited (not directly added).</summary>
    public Guid? InvitationId { get; init; }

    /// <summary>Error message if provisioning failed.</summary>
    public string? Error { get; init; }

    /// <summary>Error code for programmatic handling.</summary>
    public string? ErrorCode { get; init; }
}

/// <summary>
/// Organization list response with pagination.
/// </summary>
public record OrganizationListResponse
{
    /// <summary>
    /// List of organizations.
    /// </summary>
    public IReadOnlyList<OrganizationResponse> Organizations { get; init; } = [];

    /// <summary>
    /// Total count of organizations.
    /// </summary>
    public int TotalCount { get; init; }
}
