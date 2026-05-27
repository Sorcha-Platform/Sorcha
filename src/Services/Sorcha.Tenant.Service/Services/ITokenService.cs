// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.ServiceDefaults.Auth;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Models.Dtos;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Service interface for JWT token operations.
/// </summary>
public interface ITokenService
{
    /// <summary>
    /// Generates an access token for an organization user.
    /// </summary>
    /// <param name="user">The user identity.</param>
    /// <param name="organization">The organization.</param>
    /// <param name="platformUserId">The platform user ID to include as a claim.</param>
    /// <param name="tier">
    /// The human trust tier to mint (spec 136). <see cref="Tier.Platform"/> (default) carries org
    /// context + roles; <see cref="Tier.Consumer"/> omits them (inert on platform surfaces). Only
    /// the two human tiers are valid here.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Token response with access and refresh tokens.</returns>
    Task<TokenResponse> GenerateUserTokenAsync(
        UserIdentity user,
        Organization organization,
        Guid platformUserId,
        Tier tier = Tier.Platform,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a service-to-service token.
    /// </summary>
    /// <param name="servicePrincipal">The service principal.</param>
    /// <param name="delegatedUserId">Optional delegated user ID for delegation.</param>
    /// <param name="delegatedOrgId">Optional delegated organization ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Token response with access token (no refresh token for services).</returns>
    Task<TokenResponse> GenerateServiceTokenAsync(
        ServicePrincipal servicePrincipal,
        Guid? delegatedUserId = null,
        Guid? delegatedOrgId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Refreshes an access token using a refresh token.
    /// </summary>
    /// <param name="refreshToken">The refresh token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>New token response or null if refresh token is invalid.</returns>
    Task<TokenResponse?> RefreshTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes a token by its JTI.
    /// </summary>
    /// <param name="token">The token to revoke.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if successfully revoked.</returns>
    Task<bool> RevokeTokenAsync(
        string token,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Introspects a token to verify its validity and get claims.
    /// </summary>
    /// <param name="token">The token to introspect.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Introspection response.</returns>
    Task<TokenIntrospectionResponse> IntrospectTokenAsync(
        string token,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes all tokens for a user.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RevokeAllUserTokensAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes all tokens for an organization.
    /// </summary>
    /// <param name="organizationId">The organization ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RevokeAllOrganizationTokensAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default);
}
