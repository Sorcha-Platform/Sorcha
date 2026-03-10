// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Result of initiating a social login authorization flow.
/// </summary>
/// <param name="AuthorizationUrl">The URL to redirect the user to for provider authentication.</param>
/// <param name="State">The opaque state parameter for CSRF protection and flow correlation.</param>
public record SocialAuthInitiateResult(string AuthorizationUrl, string State);

/// <summary>
/// Result of exchanging an authorization code for user claims via a social login provider.
/// </summary>
/// <param name="Success">Whether the exchange completed successfully.</param>
/// <param name="Error">Error message if the exchange failed; null on success.</param>
/// <param name="Subject">The provider's unique user identifier (sub claim).</param>
/// <param name="Email">The user's email address from the provider.</param>
/// <param name="DisplayName">The user's display name from the provider.</param>
/// <param name="Provider">The provider name (e.g., "Google", "GitHub").</param>
public record SocialAuthCallbackResult(
    bool Success,
    string? Error,
    string? Subject,
    string? Email,
    string? DisplayName,
    string Provider);

/// <summary>
/// Service for public user social login via OAuth2/OIDC providers (Google, Microsoft, GitHub, Apple).
/// Handles authorization URL generation with PKCE, authorization code exchange, and user claim extraction.
/// Unlike <see cref="IOidcExchangeService"/>, this service is not org-scoped.
/// </summary>
public interface ISocialLoginService
{
    /// <summary>
    /// Generates an authorization URL for the specified social provider with PKCE and state parameter.
    /// The state and PKCE code verifier are cached for validation during the callback.
    /// </summary>
    /// <param name="provider">The social provider name (e.g., "Google", "Microsoft", "GitHub", "Apple").</param>
    /// <param name="redirectUri">The client's redirect URI to receive the authorization code.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The authorization URL and state parameter.</returns>
    /// <exception cref="ArgumentException">Thrown when the provider is not configured.</exception>
    Task<SocialAuthInitiateResult> GenerateAuthorizationUrlAsync(
        string provider,
        string redirectUri,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Exchanges an authorization code for user claims by calling the provider's token and user info endpoints.
    /// Validates the state parameter against the cached value to prevent CSRF attacks.
    /// </summary>
    /// <param name="provider">The social provider name.</param>
    /// <param name="code">The authorization code from the provider callback.</param>
    /// <param name="state">The state parameter from the provider callback.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result containing the user's claims or an error.</returns>
    Task<SocialAuthCallbackResult> ExchangeCodeAsync(
        string provider,
        string code,
        string state,
        CancellationToken cancellationToken = default);
}
