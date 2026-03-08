// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Models.Dtos;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Handles OIDC authorization code exchange flow: generates authorization URLs with PKCE,
/// exchanges authorization codes for tokens, and validates ID tokens.
/// </summary>
public interface IOidcExchangeService
{
    /// <summary>
    /// Generates an OIDC authorization URL with state, nonce, and PKCE parameters.
    /// Stores flow state in distributed cache with 10-minute TTL.
    /// </summary>
    /// <param name="orgId">Organization whose IDP configuration to use.</param>
    /// <param name="redirectUrl">Optional post-login redirect URL.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Authorization URL and state parameter for CSRF protection.</returns>
    /// <exception cref="InvalidOperationException">If no enabled IDP is configured for the organization.</exception>
    Task<OidcInitiateResponse> GenerateAuthorizationUrlAsync(
        Guid orgId, string? redirectUrl, CancellationToken cancellationToken = default);

    /// <summary>
    /// Exchanges an authorization code for tokens by calling the IDP's token endpoint.
    /// Validates the state parameter against cached flow state and sends the PKCE code_verifier.
    /// </summary>
    /// <param name="code">Authorization code received from the IDP callback.</param>
    /// <param name="state">State parameter for CSRF validation.</param>
    /// <param name="orgSubdomain">Organization subdomain to resolve the IDP config.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Callback result with extracted user claims or error details.</returns>
    Task<OidcCallbackResult> ExchangeCodeAsync(
        string code, string state, string orgSubdomain, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates an ID token's structure, issuer, audience, and expiry, then extracts user claims.
    /// </summary>
    /// <param name="idToken">Raw JWT ID token string.</param>
    /// <param name="config">IDP configuration with expected issuer and audience (client ID).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Extracted OIDC user claims.</returns>
    /// <exception cref="InvalidOperationException">If the token is invalid, expired, or has wrong issuer/audience.</exception>
    Task<OidcUserClaims> ValidateIdTokenAsync(
        string idToken, IdentityProviderConfiguration config, CancellationToken cancellationToken = default);
}
