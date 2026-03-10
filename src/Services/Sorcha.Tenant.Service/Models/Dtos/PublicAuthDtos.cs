// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

using Fido2NetLib;

namespace Sorcha.Tenant.Service.Models.Dtos;

/// <summary>
/// Request to generate passkey registration options for a new public user signup.
/// </summary>
public record PublicPasskeyRegisterOptionsRequest
{
    /// <summary>
    /// Human-readable display name for the new user.
    /// </summary>
    [JsonPropertyName("display_name")]
    public required string DisplayName { get; init; }

    /// <summary>
    /// Optional email address for the new user. If provided, must be unique.
    /// </summary>
    [JsonPropertyName("email")]
    public string? Email { get; init; }
}

/// <summary>
/// Request to verify a public user passkey registration and complete signup.
/// </summary>
public record PublicPasskeyRegisterVerifyRequest
{
    /// <summary>
    /// Transaction ID from the registration options step.
    /// </summary>
    [JsonPropertyName("transaction_id")]
    public required string TransactionId { get; init; }

    /// <summary>
    /// Raw attestation response from the browser/authenticator.
    /// </summary>
    [JsonPropertyName("attestation_response")]
    public required AuthenticatorAttestationRawResponse AttestationResponse { get; init; }
}

/// <summary>
/// Request to generate passkey assertion options for sign-in.
/// </summary>
public record PublicPasskeyAssertionOptionsRequest
{
    /// <summary>
    /// Optional email to narrow down allowed credentials for non-discoverable flow.
    /// When omitted, discoverable credentials (resident keys) are used.
    /// </summary>
    [JsonPropertyName("email")]
    public string? Email { get; init; }
}

/// <summary>
/// Request to verify a passkey assertion and complete sign-in.
/// </summary>
public record PublicPasskeyAssertionVerifyRequest
{
    /// <summary>
    /// Transaction ID from the assertion options step.
    /// </summary>
    [JsonPropertyName("transaction_id")]
    public required string TransactionId { get; init; }

    /// <summary>
    /// Raw assertion response from the browser/authenticator.
    /// </summary>
    [JsonPropertyName("assertion_response")]
    public required AuthenticatorAssertionRawResponse AssertionResponse { get; init; }
}

/// <summary>
/// Token response for public auth endpoints, extending the standard token response
/// with a flag indicating whether the user was newly created during this request.
/// </summary>
public record PublicTokenResponse
{
    /// <summary>
    /// JWT access token.
    /// </summary>
    [JsonPropertyName("access_token")]
    public required string AccessToken { get; init; }

    /// <summary>
    /// Refresh token for obtaining new access tokens.
    /// </summary>
    [JsonPropertyName("refresh_token")]
    public required string RefreshToken { get; init; }

    /// <summary>
    /// Token type (always "Bearer").
    /// </summary>
    [JsonPropertyName("token_type")]
    public string TokenType { get; init; } = "Bearer";

    /// <summary>
    /// Access token expiration time in seconds.
    /// </summary>
    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; init; }

    /// <summary>
    /// Token scope.
    /// </summary>
    [JsonPropertyName("scope")]
    public string? Scope { get; init; }

    /// <summary>
    /// True if this is a newly registered user; false for returning users.
    /// </summary>
    [JsonPropertyName("is_new_user")]
    public bool IsNewUser { get; init; }
}

/// <summary>
/// Request to initiate a social login authorization flow.
/// </summary>
public record SocialInitiateRequest
{
    /// <summary>
    /// Social login provider name (e.g., "Google", "Microsoft", "GitHub", "Apple").
    /// </summary>
    [JsonPropertyName("provider")]
    public required string Provider { get; init; }

    /// <summary>
    /// Client redirect URI to receive the authorization code callback.
    /// </summary>
    [JsonPropertyName("redirect_uri")]
    public required string RedirectUri { get; init; }
}

/// <summary>
/// Request to complete a social login by exchanging the authorization code.
/// </summary>
public record SocialCallbackRequest
{
    /// <summary>
    /// Social login provider name (must match the initiate request).
    /// </summary>
    [JsonPropertyName("provider")]
    public required string Provider { get; init; }

    /// <summary>
    /// Authorization code from the provider callback.
    /// </summary>
    [JsonPropertyName("code")]
    public required string Code { get; init; }

    /// <summary>
    /// State parameter from the provider callback (for CSRF validation).
    /// </summary>
    [JsonPropertyName("state")]
    public required string State { get; init; }
}

/// <summary>
/// Response containing the social login authorization URL and state parameter.
/// </summary>
public record SocialInitiateResponse
{
    /// <summary>
    /// URL to redirect the user to for provider authentication.
    /// </summary>
    [JsonPropertyName("authorization_url")]
    public required string AuthorizationUrl { get; init; }

    /// <summary>
    /// Opaque state parameter for CSRF protection.
    /// </summary>
    [JsonPropertyName("state")]
    public required string State { get; init; }
}
