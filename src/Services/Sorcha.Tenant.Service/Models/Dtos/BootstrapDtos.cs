// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
namespace Sorcha.Tenant.Service.Models.Dtos;

/// <summary>
/// Request to bootstrap a fresh Sorcha installation with initial organization and admin user.
/// </summary>
public class BootstrapRequest
{
    /// <summary>
    /// Organization name (e.g., "Acme Corporation").
    /// </summary>
    public string OrganizationName { get; set; } = string.Empty;

    /// <summary>
    /// Organization subdomain for multi-tenant routing (e.g., "acme").
    /// Must be unique across the platform.
    /// </summary>
    public string OrganizationSubdomain { get; set; } = string.Empty;

    /// <summary>
    /// Optional organization description.
    /// </summary>
    public string? OrganizationDescription { get; set; }

    /// <summary>
    /// Initial administrator email address.
    /// </summary>
    public string AdminEmail { get; set; } = string.Empty;

    /// <summary>
    /// Initial administrator display name.
    /// </summary>
    public string AdminName { get; set; } = string.Empty;

    /// <summary>
    /// Initial administrator password.
    /// Must meet password complexity requirements.
    /// </summary>
    public string AdminPassword { get; set; } = string.Empty;

    /// <summary>
    /// Optional: Create default service principal for automation.
    /// </summary>
    public bool CreateServicePrincipal { get; set; } = false;

    /// <summary>
    /// Optional: Service principal name (defaults to "bootstrap-principal").
    /// </summary>
    public string? ServicePrincipalName { get; set; }
}

/// <summary>
/// Response from bootstrap operation containing created resources and credentials.
/// </summary>
public class BootstrapResponse
{
    /// <summary>
    /// Created organization ID.
    /// </summary>
    public Guid OrganizationId { get; set; }

    /// <summary>
    /// Organization name.
    /// </summary>
    public string OrganizationName { get; set; } = string.Empty;

    /// <summary>
    /// Organization subdomain.
    /// </summary>
    public string OrganizationSubdomain { get; set; } = string.Empty;

    /// <summary>
    /// Created administrator user ID.
    /// </summary>
    public Guid AdminUserId { get; set; }

    /// <summary>
    /// Administrator email.
    /// </summary>
    public string AdminEmail { get; set; } = string.Empty;

    /// <summary>
    /// Administrator JWT access token (valid for 1 hour).
    /// </summary>
    public string AdminAccessToken { get; set; } = string.Empty;

    /// <summary>
    /// Administrator refresh token (valid for 7 days).
    /// </summary>
    public string AdminRefreshToken { get; set; } = string.Empty;

    /// <summary>
    /// Optional: Created service principal ID.
    /// </summary>
    public Guid? ServicePrincipalId { get; set; }

    /// <summary>
    /// Optional: Service principal client ID.
    /// </summary>
    public string? ServicePrincipalClientId { get; set; }

    /// <summary>
    /// Optional: Service principal client secret (only returned on creation).
    /// </summary>
    public string? ServicePrincipalClientSecret { get; set; }

    /// <summary>
    /// ID of the system admin organisation (platform governance).
    /// </summary>
    public Guid SystemAdminOrgId { get; set; }

    /// <summary>
    /// ID of the public organisation (self-registration target).
    /// </summary>
    public Guid PublicOrgId { get; set; }

    /// <summary>
    /// ID of the admin PlatformUser (platform-wide identity).
    /// </summary>
    public Guid PlatformUserId { get; set; }

    /// <summary>
    /// Timestamp of bootstrap operation.
    /// </summary>
    public DateTime CreatedAt { get; set; }
}
