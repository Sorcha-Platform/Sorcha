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
/// Flow intent encoded into the social login state token. Feature 116 / Q6.
/// </summary>
public enum SocialFlowIntent
{
    /// <summary>Anonymous flow — resolve or create a PlatformUser and issue a JWT.</summary>
    Login = 0,

    /// <summary>Signed-in flow — add the social provider to the caller's existing PlatformUser.</summary>
    Link = 1,
}

/// <summary>
/// Result of exchanging an authorization code for user claims via a social login provider.
/// </summary>
/// <param name="Success">Whether the exchange completed successfully.</param>
/// <param name="Error">Error message if the exchange failed; null on success.</param>
/// <param name="Subject">The provider's unique user identifier (sub claim).</param>
/// <param name="Email">The user's email address from the provider.</param>
/// <param name="DisplayName">The user's display name from the provider.</param>
/// <param name="EmailVerified">
/// Whether the provider asserts the email address is verified. For OIDC
/// providers (Google, Microsoft, Apple) this is the <c>email_verified</c>
/// claim from the ID token or userinfo response. For GitHub it is
/// <c>true</c> only when the primary <c>/user/emails</c> entry has
/// <c>verified: true</c>. Defaults to <c>false</c> when the claim is
/// absent — never assume verified. Feature 115.
/// </param>
/// <param name="Provider">The provider name (e.g., "Google", "GitHub").</param>
/// <param name="Intent">
/// Flow intent recovered from the cached state token (Feature 116 Q6).
/// <see cref="SocialFlowIntent.Login"/> for the anonymous signup/login
/// flow; <see cref="SocialFlowIntent.Link"/> for the signed-in
/// link-to-existing-PlatformUser flow.
/// </param>
/// <param name="TargetPlatformUserId">
/// When <see cref="Intent"/> is <see cref="SocialFlowIntent.Link"/>, the
/// PlatformUser id captured at initiate time. The callback handler MUST
/// verify that the active bearer matches this id before persisting the
/// link — defence against a session swap mid-flight.
/// </param>
public record SocialAuthCallbackResult(
    bool Success,
    string? Error,
    string? Subject,
    string? Email,
    string? DisplayName,
    bool EmailVerified,
    string Provider,
    SocialFlowIntent Intent = SocialFlowIntent.Login,
    Guid? TargetPlatformUserId = null);

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
    /// Default <see cref="SocialFlowIntent.Login"/> intent.
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
    /// Generates an authorization URL with explicit flow intent. Use this overload
    /// when starting a <see cref="SocialFlowIntent.Link"/> flow — pass the active
    /// PlatformUser's id as <paramref name="targetPlatformUserId"/> so the callback
    /// can verify the session has not swapped between initiate and callback.
    /// Feature 116 Q6.
    /// </summary>
    Task<SocialAuthInitiateResult> GenerateAuthorizationUrlAsync(
        string provider,
        string redirectUri,
        SocialFlowIntent intent,
        Guid? targetPlatformUserId,
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

    /// <summary>
    /// Returns the names of providers configured with non-empty
    /// <c>ClientId</c> and <c>ClientSecret</c>. Drives the conditional
    /// rendering of "Continue with..." buttons on signup and login pages
    /// per feature 115 FR-001.
    /// </summary>
    /// <returns>Provider names (case as configured) for which the host has working credentials.</returns>
    IReadOnlyList<string> GetConfiguredProviderNames();
}
