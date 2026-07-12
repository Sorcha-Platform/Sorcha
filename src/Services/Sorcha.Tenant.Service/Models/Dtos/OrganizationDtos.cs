// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel.DataAnnotations;

namespace Sorcha.Tenant.Service.Models.Dtos;

/// <summary>
/// Request to create a new organization.
/// </summary>
public record CreateOrganizationRequest
{
    /// <summary>
    /// Organization display name.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    [StringLength(100, MinimumLength = 1)]
    public required string Name { get; init; }

    /// <summary>
    /// Unique subdomain (3-50 lowercase letters, digits, or hyphens).
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    [StringLength(50, MinimumLength = 3)]
    [RegularExpression("^[a-z0-9-]+$", ErrorMessage = "Subdomain must contain only lowercase letters, digits, and hyphens.")]
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
    [StringLength(100, MinimumLength = 1)]
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
    [StringLength(2048)]
    [RegularExpression(@"^https://.+", ErrorMessage = "LogoUrl must be an https:// URL.")]
    public string? LogoUrl { get; init; }

    /// <summary>
    /// Primary brand color (hex format, e.g. #1A2B3C).
    /// </summary>
    [RegularExpression("^#[0-9a-fA-F]{6}$", ErrorMessage = "PrimaryColor must be a 6-digit hex colour, e.g. #1A2B3C.")]
    public string? PrimaryColor { get; init; }

    /// <summary>
    /// Secondary brand color (hex format, e.g. #1A2B3C).
    /// </summary>
    [RegularExpression("^#[0-9a-fA-F]{6}$", ErrorMessage = "SecondaryColor must be a 6-digit hex colour, e.g. #1A2B3C.")]
    public string? SecondaryColor { get; init; }

    /// <summary>
    /// Company tagline.
    /// </summary>
    [StringLength(200)]
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
    /// The organisation's signing wallet address (the wallet holding the P-256 issuing key that
    /// certificates bind to). Null until the wallet is provisioned. Feature 181 US5 — the admin
    /// certificates panel needs this to address the org-cert endpoints; it is public org metadata.
    /// </summary>
    public string? WalletAddress { get; init; }

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
        WalletAddress = org.WalletAddress,
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
    [Required(AllowEmptyStrings = false)]
    [StringLength(100, MinimumLength = 3)]
    public required string Name { get; init; }

    /// <summary>
    /// Unique subdomain (3-50 chars, lowercase alphanumeric + hyphens).
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    [StringLength(50, MinimumLength = 3)]
    [RegularExpression("^[a-z0-9-]+$", ErrorMessage = "Subdomain must contain only lowercase letters, digits, and hyphens.")]
    public required string Subdomain { get; init; }

    /// <summary>
    /// Optional organisation description (max 500 characters).
    /// </summary>
    [StringLength(500)]
    public string? Description { get; init; }

    /// <summary>
    /// Email address of the user to be invited to the organisation.
    /// If the email matches an existing PlatformUser, they are added directly.
    /// If new, a pending invitation is created for acceptance on signup.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    [EmailAddress]
    [StringLength(254)]
    public required string AdminEmail { get; init; }

    /// <summary>
    /// Role to assign to the invited user (Administrator, Designer, Auditor, Member).
    /// Defaults to Administrator. SystemAdmin is not allowed.
    /// </summary>
    public UserRole Role { get; init; } = UserRole.Administrator;

    /// <summary>
    /// Optional. When set AND <see cref="AdminEmail"/> is NOT an existing PlatformUser, the admin
    /// is created directly as an org-scoped password user (PlatformUser + UserIdentity + membership
    /// in this org only — never a public-org account), instead of a pending invitation. Lets a
    /// SystemAdmin provision a ready, single-org operator in one call (the bootstrap pattern, for
    /// any org). Ignored when the email already exists (that user is added directly as before).
    /// </summary>
    public string? AdminPassword { get; init; }

    /// <summary>Optional display name for a directly-provisioned admin. Defaults to the email local-part.</summary>
    public string? AdminDisplayName { get; init; }

    /// <summary>
    /// When true (with <see cref="AdminPassword"/>), the provisioned admin's email is marked verified,
    /// bypassing the verification loop. GATED: only honoured when the installation enables
    /// <c>Platform:AllowAdminVerifiedUserCreation</c> (off by default in production) — otherwise the
    /// request is rejected. Always role-gated (SystemAdmin) and audit-logged.
    /// </summary>
    public bool AdminEmailVerified { get; init; }
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

/// <summary>
/// Request to update an organisation's status (Active/Suspended).
/// </summary>
public record UpdateOrgStatusRequest
{
    /// <summary>
    /// New status for the organisation. Only Active and Suspended are valid.
    /// </summary>
    public required OrganizationStatus Status { get; init; }
}

/// <summary>
/// Summary of an organisation for the platform list view.
/// </summary>
public record OrganizationSummaryResponse
{
    /// <summary>Organisation identifier.</summary>
    public Guid Id { get; init; }

    /// <summary>Organisation display name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Organisation subdomain.</summary>
    public string Subdomain { get; init; } = string.Empty;

    /// <summary>Current organisation status.</summary>
    public OrganizationStatus Status { get; init; }

    /// <summary>Organisation type (Standard or Public).</summary>
    public OrgType OrgType { get; init; }

    /// <summary>Whether this is a platform-level organisation.</summary>
    public bool IsPlatformOrg { get; init; }

    /// <summary>Number of users in this organisation.</summary>
    public int UserCount { get; init; }

    /// <summary>Organisation creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>
/// Paginated list of organisations for the platform admin view.
/// </summary>
public record PlatformOrgListResponse
{
    /// <summary>Page of organisation summaries.</summary>
    public IReadOnlyList<OrganizationSummaryResponse> Items { get; init; } = [];

    /// <summary>Total number of organisations matching the filter.</summary>
    public int TotalCount { get; init; }

    /// <summary>Current page number (1-based).</summary>
    public int Page { get; init; }

    /// <summary>Number of items per page.</summary>
    public int PageSize { get; init; }
}

/// <summary>
/// Summary of a user within an organisation for the audit view.
/// </summary>
public record OrgUserSummaryResponse
{
    /// <summary>Platform user identifier.</summary>
    public Guid Id { get; init; }

    /// <summary>User email address.</summary>
    public string Email { get; init; } = string.Empty;

    /// <summary>User display name.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Roles held in this organisation.</summary>
    public string[] Roles { get; init; } = [];

    /// <summary>Platform user account status.</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>When the user joined the organisation.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Last successful authentication timestamp.</summary>
    public DateTimeOffset? LastLoginAt { get; init; }
}

/// <summary>
/// Paginated list of users within an organisation for the audit view.
/// </summary>
public record OrgUserListResponse
{
    /// <summary>Page of user summaries.</summary>
    public IReadOnlyList<OrgUserSummaryResponse> Items { get; init; } = [];

    /// <summary>Total number of users in the organisation.</summary>
    public int TotalCount { get; init; }

    /// <summary>Current page number (1-based).</summary>
    public int Page { get; init; }

    /// <summary>Number of items per page.</summary>
    public int PageSize { get; init; }
}
