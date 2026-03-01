// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Data.Repositories;

/// <summary>
/// Repository interface for Identity entity operations.
/// Handles UserIdentity, PublicIdentity, and ServicePrincipal entities.
/// </summary>
public interface IIdentityRepository
{
    // UserIdentity operations (per-org schema)

    /// <summary>
    /// Gets a user identity by ID.
    /// </summary>
    Task<UserIdentity?> GetUserByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a user identity by external IDP user ID.
    /// </summary>
    Task<UserIdentity?> GetUserByExternalIdAsync(string externalIdpUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a user identity by email address.
    /// </summary>
    Task<UserIdentity?> GetUserByEmailAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all active users for an organization.
    /// </summary>
    Task<List<UserIdentity>> GetActiveUsersAsync(Guid organizationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all users for an organization (including inactive).
    /// </summary>
    Task<List<UserIdentity>> GetAllUsersAsync(Guid organizationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new user identity.
    /// </summary>
    Task<UserIdentity> CreateUserAsync(UserIdentity user, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing user identity.
    /// </summary>
    Task<UserIdentity> UpdateUserAsync(UserIdentity user, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deactivates a user identity (soft delete).
    /// </summary>
    Task DeactivateUserAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the total count of active users across all organizations.
    /// </summary>
    Task<int> GetTotalActiveUserCountAsync(CancellationToken cancellationToken = default);

    // PublicIdentity operations (PassKey/FIDO2, public schema)

    /// <summary>
    /// Gets a public identity by ID.
    /// </summary>
    Task<PublicIdentity?> GetPublicIdentityByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a public identity by PassKey credential ID.
    /// </summary>
    Task<PublicIdentity?> GetPublicIdentityByCredentialIdAsync(byte[] credentialId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new public identity (PassKey registration).
    /// </summary>
    Task<PublicIdentity> CreatePublicIdentityAsync(PublicIdentity identity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing public identity (e.g., signature counter).
    /// </summary>
    Task<PublicIdentity> UpdatePublicIdentityAsync(PublicIdentity identity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a public identity (PassKey revocation).
    /// </summary>
    Task DeletePublicIdentityAsync(Guid id, CancellationToken cancellationToken = default);

    // ServicePrincipal operations (service-to-service auth, public schema)

    /// <summary>
    /// Gets a service principal by ID.
    /// </summary>
    Task<ServicePrincipal?> GetServicePrincipalByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a service principal by client ID.
    /// </summary>
    Task<ServicePrincipal?> GetServicePrincipalByClientIdAsync(string clientId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a service principal by service name.
    /// </summary>
    Task<ServicePrincipal?> GetServicePrincipalByNameAsync(string serviceName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all active service principals.
    /// </summary>
    Task<List<ServicePrincipal>> GetActiveServicePrincipalsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all service principals regardless of status.
    /// </summary>
    Task<List<ServicePrincipal>> GetAllServicePrincipalsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new service principal.
    /// </summary>
    Task<ServicePrincipal> CreateServicePrincipalAsync(ServicePrincipal servicePrincipal, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing service principal.
    /// </summary>
    Task<ServicePrincipal> UpdateServicePrincipalAsync(ServicePrincipal servicePrincipal, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deactivates a service principal (soft delete).
    /// </summary>
    Task DeactivateServicePrincipalAsync(Guid id, CancellationToken cancellationToken = default);
}
