// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Models;

/// <summary>
/// Organization context extracted from JWT token.
/// Used by services to scope operations to a specific tenant/organization.
/// </summary>
public class OrganizationContext
{
    /// <summary>
    /// Organization ID. Null for public identities (PassKey users without org).
    /// </summary>
    public Guid? OrganizationId { get; init; }

    /// <summary>
    /// Organization subdomain (e.g., "acme" from "acme.sorcha.io").
    /// Null for public identities.
    /// </summary>
    public string? Subdomain { get; init; }

    /// <summary>
    /// Organization display name (e.g., "Acme Corporation").
    /// Null for public identities.
    /// </summary>
    public string? OrganizationName { get; init; }

    /// <summary>
    /// User ID within the organization (null for service principals and public identities).
    /// </summary>
    public Guid? UserId { get; init; }

    /// <summary>
    /// User email (null for service principals and public identities).
    /// </summary>
    public string? UserEmail { get; init; }

    /// <summary>
    /// User roles within organization (Administrator, Auditor, Member).
    /// Empty array for service principals and public identities.
    /// </summary>
    public string[] Roles { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Whether this is a service principal (inter-service authentication).
    /// </summary>
    public bool IsServicePrincipal { get; init; }

    /// <summary>
    /// Whether this is a platform user (e.g., PassKey user without organization).
    /// </summary>
    public bool IsPlatformUser { get; init; }

    /// <summary>
    /// Service name if this is a service principal (e.g., "Blueprint", "Wallet").
    /// Null for user identities.
    /// </summary>
    public string? ServiceName { get; init; }
}
