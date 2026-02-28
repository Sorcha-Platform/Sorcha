// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using Refit;
using Sorcha.Cli.Models;

namespace Sorcha.Cli.Services;

/// <summary>
/// Refit client interface for the Tenant Service API.
/// </summary>
public interface ITenantServiceClient
{
    // --- Organizations ---

    /// <summary>
    /// Lists all organizations.
    /// </summary>
    [Get("/api/organizations")]
    Task<OrganizationListResponse> ListOrganizationsAsync([Header("Authorization")] string authorization);

    /// <summary>
    /// Gets an organization by ID.
    /// </summary>
    [Get("/api/organizations/{id}")]
    Task<Organization> GetOrganizationAsync(string id, [Header("Authorization")] string authorization);

    /// <summary>
    /// Creates a new organization.
    /// </summary>
    [Post("/api/organizations")]
    Task<Organization> CreateOrganizationAsync([Body] CreateOrganizationRequest request, [Header("Authorization")] string authorization);

    /// <summary>
    /// Updates an organization.
    /// </summary>
    [Put("/api/organizations/{id}")]
    Task<Organization> UpdateOrganizationAsync(string id, [Body] UpdateOrganizationRequest request, [Header("Authorization")] string authorization);

    /// <summary>
    /// Deletes an organization.
    /// </summary>
    [Delete("/api/organizations/{id}")]
    Task DeleteOrganizationAsync(string id, [Header("Authorization")] string authorization);

    // --- Users ---

    /// <summary>
    /// Lists all users in an organization.
    /// </summary>
    [Get("/api/organizations/{organizationId}/users")]
    Task<List<User>> ListUsersAsync(string organizationId, [Header("Authorization")] string authorization);

    /// <summary>
    /// Gets a user by ID.
    /// </summary>
    [Get("/api/organizations/{organizationId}/users/{userId}")]
    Task<User> GetUserAsync(string organizationId, string userId, [Header("Authorization")] string authorization);

    /// <summary>
    /// Creates a new user in an organization.
    /// </summary>
    [Post("/api/organizations/{organizationId}/users")]
    Task<User> CreateUserAsync(string organizationId, [Body] CreateUserRequest request, [Header("Authorization")] string authorization);

    /// <summary>
    /// Updates a user.
    /// </summary>
    [Put("/api/organizations/{organizationId}/users/{userId}")]
    Task<User> UpdateUserAsync(string organizationId, string userId, [Body] UpdateUserRequest request, [Header("Authorization")] string authorization);

    /// <summary>
    /// Deletes a user.
    /// </summary>
    [Delete("/api/organizations/{organizationId}/users/{userId}")]
    Task DeleteUserAsync(string organizationId, string userId, [Header("Authorization")] string authorization);

    // --- Service Principals ---

    /// <summary>
    /// Lists all service principals in an organization.
    /// </summary>
    [Get("/api/organizations/{organizationId}/principals")]
    Task<List<ServicePrincipal>> ListServicePrincipalsAsync(string organizationId, [Header("Authorization")] string authorization);

    /// <summary>
    /// Gets a service principal by client ID.
    /// </summary>
    [Get("/api/organizations/{organizationId}/principals/{clientId}")]
    Task<ServicePrincipal> GetServicePrincipalAsync(string organizationId, string clientId, [Header("Authorization")] string authorization);

    /// <summary>
    /// Creates a new service principal in an organization.
    /// </summary>
    [Post("/api/organizations/{organizationId}/principals")]
    Task<CreateServicePrincipalResponse> CreateServicePrincipalAsync(string organizationId, [Body] CreateServicePrincipalRequest request, [Header("Authorization")] string authorization);

    /// <summary>
    /// Deletes a service principal.
    /// </summary>
    [Delete("/api/organizations/{organizationId}/principals/{clientId}")]
    Task DeleteServicePrincipalAsync(string organizationId, string clientId, [Header("Authorization")] string authorization);

    /// <summary>
    /// Rotates the client secret for a service principal.
    /// </summary>
    [Post("/api/organizations/{organizationId}/principals/{clientId}/rotate-secret")]
    Task<RotateSecretResponse> RotateSecretAsync(string organizationId, string clientId, [Header("Authorization")] string authorization);

    // --- Bootstrap ---

    /// <summary>
    /// Bootstraps a fresh Sorcha installation with initial organization and admin user.
    /// </summary>
    [Post("/api/tenants/bootstrap")]
    Task<BootstrapResponse> BootstrapAsync([Body] BootstrapRequest request);

}
