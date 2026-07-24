// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
namespace Sorcha.Cli.Models;

/// <summary>
/// Request to bootstrap a fresh Sorcha installation.
/// </summary>
public class BootstrapRequest
{
    /// <summary>
    /// Organization name (e.g., "Acme Corporation").
    /// </summary>
    public string OrganizationName { get; set; } = string.Empty;

    /// <summary>
    /// Organization subdomain for multi-tenant routing (e.g., "acme").
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
    /// </summary>
    public string AdminPassword { get; set; } = string.Empty;

    /// <summary>
    /// Create default service principal for automation.
    /// </summary>
    public bool CreateServicePrincipal { get; set; } = false;

    /// <summary>
    /// Optional service principal name.
    /// </summary>
    public string? ServicePrincipalName { get; set; }
}

/// <summary>
/// Response from bootstrap operation.
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
    /// Administrator JWT access token.
    /// </summary>
    public string AdminAccessToken { get; set; } = string.Empty;

    /// <summary>
    /// Administrator refresh token.
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
    /// Optional: Service principal client secret.
    /// </summary>
    public string? ServicePrincipalClientSecret { get; set; }

    /// <summary>
    /// The system administration organisation created by bootstrap.
    /// </summary>
    public Guid SystemAdminOrgId { get; set; }

    /// <summary>
    /// The well-known public organisation created by bootstrap.
    /// </summary>
    public Guid PublicOrgId { get; set; }

    /// <summary>
    /// The cross-org platform user created for the administrator.
    /// </summary>
    public Guid PlatformUserId { get; set; }

    /// <summary>
    /// Timestamp of bootstrap operation.
    /// </summary>
    public DateTime CreatedAt { get; set; }
}
