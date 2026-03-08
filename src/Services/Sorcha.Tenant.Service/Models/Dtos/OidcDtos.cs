// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Service.Models.Dtos;

/// <summary>
/// Request to initiate an OIDC login flow.
/// The client provides the organization subdomain and optional redirect URL.
/// </summary>
public record OidcInitiateRequest
{
    /// <summary>
    /// Organization subdomain or slug identifying which IDP to use.
    /// </summary>
    public required string OrgSubdomain { get; init; }

    /// <summary>
    /// URL to redirect the user to after successful authentication.
    /// Defaults to the organization's home page if not specified.
    /// </summary>
    public string? RedirectUrl { get; init; }
}

/// <summary>
/// Response from initiating an OIDC login flow.
/// Contains the authorization URL to redirect the user to the external IDP.
/// </summary>
public record OidcInitiateResponse
{
    /// <summary>
    /// Full authorization URL to redirect the user to (includes state, nonce, PKCE challenge).
    /// </summary>
    public required string AuthorizationUrl { get; init; }

    /// <summary>
    /// Opaque state parameter for CSRF protection. Must match on callback.
    /// </summary>
    public required string State { get; init; }
}

/// <summary>
/// Request to complete a user profile after OIDC provisioning.
/// Required when the IDP didn't return all mandatory claims.
/// </summary>
public record OidcCompleteProfileRequest
{
    /// <summary>
    /// User's display name (required if not provided by IDP).
    /// </summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// User's email address (required if not provided by IDP).
    /// </summary>
    public string? Email { get; init; }
}

/// <summary>
/// Request to verify an email address using a verification token.
/// </summary>
public record VerifyEmailRequest
{
    /// <summary>
    /// Email verification token (32-byte URL-safe base64).
    /// </summary>
    public required string Token { get; init; }
}

/// <summary>
/// Request to resend a verification email. Rate limited to 3/hour.
/// </summary>
public record ResendVerificationRequest
{
    /// <summary>
    /// Email address to resend verification to.
    /// Must match the authenticated user's email.
    /// </summary>
    public required string Email { get; init; }
}

/// <summary>
/// Result of an OIDC callback processing.
/// Contains the Sorcha JWT or indicates further steps needed (profile completion, 2FA).
/// </summary>
public record OidcCallbackResult
{
    /// <summary>
    /// Whether authentication completed successfully.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// JWT access token (present only when fully authenticated).
    /// </summary>
    public string? AccessToken { get; init; }

    /// <summary>
    /// Refresh token (present only when fully authenticated).
    /// </summary>
    public string? RefreshToken { get; init; }

    /// <summary>
    /// Token expiration time in seconds.
    /// </summary>
    public int? ExpiresIn { get; init; }

    /// <summary>
    /// Whether the user needs to complete their profile (missing email or display name).
    /// </summary>
    public bool RequiresProfileCompletion { get; init; }

    /// <summary>
    /// Whether the user needs to complete 2FA verification.
    /// </summary>
    public bool Requires2FA { get; init; }

    /// <summary>
    /// Partial/login token for 2FA or profile completion flows.
    /// </summary>
    public string? PartialToken { get; init; }

    /// <summary>
    /// Error message if authentication failed.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// The provisioned or matched user's ID.
    /// </summary>
    public Guid? UserId { get; init; }

    /// <summary>
    /// Whether this was the user's first login (newly provisioned).
    /// </summary>
    public bool IsFirstLogin { get; init; }
}

/// <summary>
/// Internal representation of extracted OIDC claims from an ID token.
/// Used by the provisioning service to create or match user accounts.
/// </summary>
public record OidcUserClaims
{
    /// <summary>
    /// Subject claim (sub) — unique identifier from the IDP.
    /// </summary>
    public required string Subject { get; init; }

    /// <summary>
    /// Email address (from email, preferred_username, or upn claims).
    /// </summary>
    public string? Email { get; init; }

    /// <summary>
    /// Whether the IDP has verified the email (email_verified claim).
    /// </summary>
    public bool EmailVerified { get; init; }

    /// <summary>
    /// Display name (from name, or given_name + family_name).
    /// </summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// Given/first name (given_name claim).
    /// </summary>
    public string? GivenName { get; init; }

    /// <summary>
    /// Family/last name (family_name claim).
    /// </summary>
    public string? FamilyName { get; init; }

    /// <summary>
    /// Profile picture URL (picture claim).
    /// </summary>
    public string? Picture { get; init; }

    /// <summary>
    /// Preferred username (preferred_username claim). Used as email fallback.
    /// </summary>
    public string? PreferredUsername { get; init; }

    /// <summary>
    /// User principal name (upn claim). Azure AD specific email fallback.
    /// </summary>
    public string? Upn { get; init; }
}

/// <summary>
/// Response after email verification.
/// </summary>
public record EmailVerificationResponse
{
    /// <summary>
    /// Whether verification succeeded.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Status message.
    /// </summary>
    public string? Message { get; init; }
}
