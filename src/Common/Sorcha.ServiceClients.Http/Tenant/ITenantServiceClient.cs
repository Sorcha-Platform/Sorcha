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
    /// Lists users in an organisation. Calls <c>GET /api/organizations/{organizationId}/users</c>.
    /// </summary>
    /// <param name="organizationId">Organisation ID whose users to list.</param>
    /// <param name="queryString">
    /// Already-built query string (without leading '?'), or null. The endpoint binds only
    /// <c>includeInactive</c> (bool), <c>emailVerified</c> (bool), <c>provisionedVia</c> (string),
    /// and <c>includePending</c> (bool) — it has no role/status/search filter and no pagination.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The user-list JSON body, or null on non-success.</returns>
    Task<string?> ListUsersAsync(
        string organizationId,
        string? queryString = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a lifecycle or role action against a user within an organisation. Dispatches to
    /// <c>POST /api/organizations/{organizationId}/users/{userId}/suspend</c>,
    /// <c>/reactivate</c>, <c>/unlock</c> (no request body — <paramref name="requestJson"/> is
    /// ignored), or <c>PUT .../role</c> (<paramref name="requestJson"/> is the required
    /// <c>{ "role": "..." }</c> body).
    /// </summary>
    /// <param name="organizationId">Organisation the user belongs to.</param>
    /// <param name="userId">Target user ID.</param>
    /// <param name="action">One of: Suspend, Reactivate, Unlock, ChangeRole (case-insensitive).</param>
    /// <param name="requestJson">The <c>{ "role": "..." }</c> body — required for ChangeRole.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The action-result JSON body, or null on non-success. The lifecycle actions
    /// (suspend/reactivate/unlock) return an empty 200 body on success — a non-null,
    /// possibly-empty string, not JSON to parse.
    /// </returns>
    /// <exception cref="ArgumentException"><paramref name="action"/> is not one of the four supported actions.</exception>
    Task<string?> ManageUserAsync(
        string organizationId,
        string userId,
        string action,
        string? requestJson = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes all tokens for a user, or for every user in an organisation. Exactly one of
    /// <paramref name="userId"/> or <paramref name="organizationId"/> must be supplied — they
    /// address two mutually-exclusive endpoints, <c>POST /api/auth/token/revoke-user</c> and
    /// <c>POST /api/auth/token/revoke-organization</c>, not one combined route.
    /// </summary>
    /// <param name="userId">User ID to revoke all tokens for, or null.</param>
    /// <param name="organizationId">Organisation ID to revoke every member's tokens for, or null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The revoke-result JSON body (<c>{ "success": bool, "message": string }</c> — there is no
    /// token/user count in the response), or null on non-success.
    /// </returns>
    /// <exception cref="ArgumentException">Neither <paramref name="userId"/> nor <paramref name="organizationId"/> was supplied.</exception>
    Task<string?> RevokeTokenAsync(
        string? userId,
        string? organizationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the signed-in user's persona for a context (Feature 125, 140 Wave 3).
    /// Calls <c>GET /api/me/persona</c>. The caller's identity comes from the forwarded
    /// JWT, so a consumer-tier citizen reads only their own Personal-context persona.
    /// </summary>
    /// <param name="queryString">Already-built query string (without leading '?'), or null for the Personal context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The persona read-model JSON body, or null on non-success.</returns>
    Task<string?> GetMyPersonaAsync(
        string? queryString = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the signed-in user's persona for a context (full replace). Calls
    /// <c>PUT /api/me/persona</c>. The caller's identity comes from the forwarded JWT.
    /// </summary>
    /// <param name="requestJson">The PersonaAttributesV1 body as JSON.</param>
    /// <param name="queryString">Already-built query string (without leading '?'), or null for the Personal context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The canonical persona read-model JSON body, or null on non-success.</returns>
    Task<string?> ReplaceMyPersonaAsync(
        string requestJson,
        string? queryString = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets an organisation's status (Active / Suspended) via the platform-admin route
    /// (Feature 140 Wave 4). Calls <c>PUT /api/platform/organizations/{orgId}/status</c>.
    /// </summary>
    /// <param name="organizationId">Organisation ID.</param>
    /// <param name="requestJson">The <c>{ "status": "Active|Suspended" }</c> body as JSON.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated organisation-summary JSON body, or null on non-success.</returns>
    Task<string?> SetOrganizationStatusAsync(
        string organizationId,
        string requestJson,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the platform settings (public-org toggle, max-orgs-per-user) for system admins
    /// (Feature 140 Wave 4). Calls <c>GET /api/platform/settings</c>.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The platform-settings JSON body, or null on non-success.</returns>
    Task<string?> GetPlatformSettingsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Enables or disables the public organisation (atomic org-status + self-registration toggle)
    /// (Feature 140 Wave 4). Calls <c>PUT /api/platform/settings/public-org</c>.
    /// </summary>
    /// <param name="requestJson">The <c>{ "enabled": true|false }</c> body as JSON.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated platform-settings JSON body, or null on non-success.</returns>
    Task<string?> UpdatePublicOrgAsync(
        string requestJson,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a read-only paginated user list for an organisation (audit view)
    /// (Feature 140 Wave 4). Calls <c>GET /api/platform/organizations/{orgId}/users</c>.
    /// </summary>
    /// <param name="organizationId">Organisation ID.</param>
    /// <param name="queryString">Already-built query string (without leading '?'), or null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The org-user-list JSON body, or null on non-success.</returns>
    Task<string?> GetOrganizationUsersAsync(
        string organizationId,
        string? queryString = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Provisions a platform user into an organisation (Feature 140 Wave 4).
    /// Calls <c>POST /api/platform/users/</c>.
    /// </summary>
    /// <param name="requestJson">The AdminProvisionUserRequest body as JSON.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The provisioned-user JSON body, or null on non-success.</returns>
    Task<string?> ProvisionPlatformUserAsync(
        string requestJson,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resets a platform user's password (Feature 140 Wave 4).
    /// Calls <c>PUT /api/platform/users/{id}/password</c>.
    /// </summary>
    /// <param name="userId">The platform user's ID.</param>
    /// <param name="requestJson">The <c>{ "newPassword": "..." }</c> body as JSON.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The password-reset confirmation JSON body, or null on non-success.</returns>
    Task<string?> ResetPlatformUserPasswordAsync(
        string userId,
        string requestJson,
        CancellationToken cancellationToken = default);
}
