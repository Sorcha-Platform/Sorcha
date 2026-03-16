// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

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
}
