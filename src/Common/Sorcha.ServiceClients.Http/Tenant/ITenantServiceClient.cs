// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.ServiceClients.Tenant;

/// <summary>
/// Typed client for Tenant Service organisation / user / token operations (spec 139 US4).
/// </summary>
/// <remarks>
/// Methods return the raw response body (the caller does its own shaping) or <c>null</c> when
/// the service responds with a non-success status. Transport faults
/// (<see cref="HttpRequestException"/>, <see cref="TaskCanceledException"/>) propagate so callers
/// can map them to their own Timeout / Error result shapes.
/// </remarks>
public interface ITenantServiceClient
{
    /// <summary>
    /// Lists organisations with the supplied query string. Calls <c>GET /api/organizations</c>.
    /// </summary>
    /// <param name="queryString">Already-built query string (without leading '?'), or null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The organisation-list JSON body, or null on non-success.</returns>
    Task<string?> ListOrganizationsAsync(
        string? queryString = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates an organisation (with admin) via the platform-admin provisioning route.
    /// Calls <c>POST /api/platform/organizations</c>.
    /// </summary>
    /// <param name="requestJson">The provisioning request body as JSON.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created-organisation JSON body, or null on non-success.</returns>
    Task<string?> CreateOrganizationAsync(
        string requestJson,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an organisation. Calls <c>PUT /api/organizations/{id}</c>.
    /// </summary>
    /// <param name="organizationId">Organisation ID.</param>
    /// <param name="requestJson">The update request body as JSON.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated-organisation JSON body, or null on non-success.</returns>
    Task<string?> UpdateOrganizationAsync(
        string organizationId,
        string requestJson,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists users with the supplied query string. Calls <c>GET /api/users</c>.
    /// </summary>
    /// <param name="queryString">Already-built query string (without leading '?'), or null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The user-list JSON body, or null on non-success.</returns>
    Task<string?> ListUsersAsync(
        string? queryString = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a management action against a user. Calls <c>POST /api/users/{userId}/actions</c>.
    /// </summary>
    /// <param name="userId">Target user ID.</param>
    /// <param name="requestJson">The action request body as JSON.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The action-result JSON body, or null on non-success.</returns>
    Task<string?> ManageUserAsync(
        string userId,
        string requestJson,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes a token (or all of a user's tokens). Calls <c>POST /api/tokens/revoke</c>.
    /// </summary>
    /// <param name="requestJson">The revoke request body as JSON.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The revoke-result JSON body, or null on non-success.</returns>
    Task<string?> RevokeTokenAsync(
        string requestJson,
        CancellationToken cancellationToken = default);
}
