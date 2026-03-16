// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Result of an organisation provisioning operation.
/// </summary>
public record OrgProvisioningResult
{
    /// <summary>Whether the provisioning succeeded.</summary>
    public bool Success { get; init; }

    /// <summary>The newly created organisation ID (if successful).</summary>
    public Guid? OrganizationId { get; init; }

    /// <summary>The newly created organisation name.</summary>
    public string? OrganizationName { get; init; }

    /// <summary>The newly created organisation subdomain.</summary>
    public string? Subdomain { get; init; }

    /// <summary>Error message if provisioning failed.</summary>
    public string? Error { get; init; }

    /// <summary>Error code for programmatic handling.</summary>
    public string? ErrorCode { get; init; }
}

/// <summary>
/// Request to create a new private organisation via self-service or admin invite.
/// </summary>
public record ProvisionOrgRequest
{
    /// <summary>Organisation display name (3-100 characters).</summary>
    public required string Name { get; init; }

    /// <summary>Unique subdomain (3-50 chars, lowercase alphanumeric + hyphens).</summary>
    public required string Subdomain { get; init; }

    /// <summary>Optional organisation description (max 500 characters).</summary>
    public string? Description { get; init; }
}

/// <summary>
/// Service for atomic organisation provisioning.
/// Creates Organisation + admin UserIdentity + PlatformUserOrgMembership + increments CreatedOrgsCount
/// in a single transaction.
/// </summary>
public interface IOrgProvisioningService
{
    /// <summary>
    /// Validates whether a platform user is eligible to create a new organisation.
    /// Checks: email verified, CreatedOrgsCount &lt; MaxOrgsPerUser, subdomain available.
    /// </summary>
    /// <param name="platformUserId">The platform user requesting org creation.</param>
    /// <param name="request">The org creation request with name and subdomain.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Null if valid; error result if validation fails.</returns>
    Task<OrgProvisioningResult?> ValidateAsync(Guid platformUserId, ProvisionOrgRequest request, CancellationToken ct);

    /// <summary>
    /// Atomically provisions a new private organisation.
    /// Creates: Organisation entity, admin UserIdentity for the creator, PlatformUserOrgMembership,
    /// and increments the creator's CreatedOrgsCount. Rolls back on any failure.
    /// </summary>
    /// <param name="platformUserId">The platform user creating the org.</param>
    /// <param name="request">The org creation request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result with the new org details or error information.</returns>
    Task<OrgProvisioningResult> ProvisionAsync(Guid platformUserId, ProvisionOrgRequest request, CancellationToken ct);

    /// <summary>
    /// Admin-initiated organisation provisioning. Creates org, then either directly adds
    /// the admin user (if they exist as a PlatformUser) or creates a pending invitation.
    /// Bypasses user quota checks — system admin authority.
    /// </summary>
    /// <param name="adminPlatformUserId">The system admin performing the operation.</param>
    /// <param name="name">Organisation display name.</param>
    /// <param name="subdomain">Unique subdomain.</param>
    /// <param name="description">Optional description.</param>
    /// <param name="adminEmail">Email of the user to invite.</param>
    /// <param name="role">Role to assign (Administrator, Designer, Auditor, Member). SystemAdmin is rejected.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<AdminProvisionResult> AdminProvisionAsync(
        Guid adminPlatformUserId, string name, string subdomain,
        string? description, string adminEmail, UserRole role, CancellationToken ct);
}

/// <summary>
/// Result of an admin-initiated organisation provisioning operation.
/// </summary>
public record AdminProvisionResult
{
    /// <summary>Whether the provisioning succeeded.</summary>
    public bool Success { get; init; }

    /// <summary>The newly created organisation ID.</summary>
    public Guid? OrganizationId { get; init; }

    /// <summary>The organisation name.</summary>
    public string? OrganizationName { get; init; }

    /// <summary>The organisation subdomain.</summary>
    public string? Subdomain { get; init; }

    /// <summary>Whether the admin was directly added (true) or invited via pending invitation (false).</summary>
    public bool AdminDirectlyAdded { get; init; }

    /// <summary>The invitation ID if the admin was invited (not directly added).</summary>
    public Guid? InvitationId { get; init; }

    /// <summary>Error message if provisioning failed.</summary>
    public string? Error { get; init; }

    /// <summary>Error code for programmatic handling.</summary>
    public string? ErrorCode { get; init; }
}
