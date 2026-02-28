// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models.Configuration;

/// <summary>
/// User-defined profile for connecting to different backend environments.
/// Supports a hybrid URL pattern: use SorchaServiceUrl as default base,
/// with optional per-service URL overrides for development scenarios.
/// </summary>
public sealed record Profile
{
    /// <summary>
    /// Profile name (unique identifier, e.g., "local", "docker", "production")
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Display description for the profile
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Base URL for all Sorcha services. Used as the default when per-service URLs are not specified.
    /// For Docker/production: typically the API Gateway URL (e.g., http://localhost:80)
    /// For Aspire: can be empty if all per-service URLs are specified
    /// </summary>
    public string SorchaServiceUrl { get; init; } = string.Empty;

    /// <summary>
    /// Optional override for Tenant Service URL.
    /// If null or empty, derived from SorchaServiceUrl + /api/tenant
    /// Example: https://localhost:7110 (Aspire) or null (use gateway)
    /// </summary>
    public string? TenantServiceUrl { get; init; }

    /// <summary>
    /// Optional override for Register Service URL.
    /// If null or empty, derived from SorchaServiceUrl + /api/register
    /// Example: https://localhost:7290 (Aspire) or null (use gateway)
    /// </summary>
    public string? RegisterServiceUrl { get; init; }

    /// <summary>
    /// Optional override for Blueprint Service URL.
    /// If null or empty, derived from SorchaServiceUrl + /api/blueprint
    /// Example: https://localhost:7000 (Aspire) or null (use gateway)
    /// </summary>
    public string? BlueprintServiceUrl { get; init; }

    /// <summary>
    /// Optional override for Wallet Service URL.
    /// If null or empty, derived from SorchaServiceUrl + /api/wallet
    /// Example: https://localhost:7001 (Aspire) or null (use gateway)
    /// </summary>
    public string? WalletServiceUrl { get; init; }

    /// <summary>
    /// Optional override for Peer Service URL.
    /// If null or empty, derived from SorchaServiceUrl + /api/peer
    /// Example: https://localhost:7002 (Aspire) or null (use gateway)
    /// </summary>
    public string? PeerServiceUrl { get; init; }

    /// <summary>
    /// Optional override for Auth Token endpoint URL.
    /// If null or empty, derived from resolved Tenant Service URL + /api/service-auth/token
    /// </summary>
    public string? AuthTokenUrl { get; init; }

    /// <summary>
    /// Default OAuth2 client ID for this environment.
    /// Typically "sorcha-ui" for the main UI application.
    /// </summary>
    public string DefaultClientId { get; init; } = "sorcha-ui";

    /// <summary>
    /// Whether to verify SSL certificates for this profile.
    /// Should be false for local development (self-signed certs), true for production.
    /// </summary>
    public bool VerifySsl { get; init; } = true;

    /// <summary>
    /// HTTP request timeout in seconds. Default is 30 seconds.
    /// </summary>
    public int TimeoutSeconds { get; init; } = 30;

    /// <summary>
    /// Profile creation timestamp
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Last modified timestamp
    /// </summary>
    public DateTime UpdatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Indicates if this is a system-defined profile (cannot be deleted)
    /// </summary>
    public bool IsSystemProfile { get; init; }

    /// <summary>
    /// Gets the resolved Tenant Service URL.
    /// Returns the override if specified, otherwise derives from base URL.
    /// </summary>
    public string GetTenantServiceUrl()
    {
        if (!string.IsNullOrWhiteSpace(TenantServiceUrl))
            return TenantServiceUrl.TrimEnd('/');

        return string.IsNullOrWhiteSpace(SorchaServiceUrl)
            ? string.Empty
            : $"{SorchaServiceUrl.TrimEnd('/')}/api/tenant";
    }

    /// <summary>
    /// Gets the resolved Register Service URL.
    /// Returns the override if specified, otherwise derives from base URL.
    /// </summary>
    public string GetRegisterServiceUrl()
    {
        if (!string.IsNullOrWhiteSpace(RegisterServiceUrl))
            return RegisterServiceUrl.TrimEnd('/');

        return string.IsNullOrWhiteSpace(SorchaServiceUrl)
            ? string.Empty
            : $"{SorchaServiceUrl.TrimEnd('/')}/api/register";
    }

    /// <summary>
    /// Gets the resolved Blueprint Service URL.
    /// Returns the override if specified, otherwise derives from base URL.
    /// </summary>
    public string GetBlueprintServiceUrl()
    {
        if (!string.IsNullOrWhiteSpace(BlueprintServiceUrl))
            return BlueprintServiceUrl.TrimEnd('/');

        return string.IsNullOrWhiteSpace(SorchaServiceUrl)
            ? string.Empty
            : $"{SorchaServiceUrl.TrimEnd('/')}/api/blueprint";
    }

    /// <summary>
    /// Gets the resolved Wallet Service URL.
    /// Returns the override if specified, otherwise derives from base URL.
    /// </summary>
    public string GetWalletServiceUrl()
    {
        if (!string.IsNullOrWhiteSpace(WalletServiceUrl))
            return WalletServiceUrl.TrimEnd('/');

        return string.IsNullOrWhiteSpace(SorchaServiceUrl)
            ? string.Empty
            : $"{SorchaServiceUrl.TrimEnd('/')}/api/wallet";
    }

    /// <summary>
    /// Gets the resolved Peer Service URL.
    /// Returns the override if specified, otherwise derives from base URL.
    /// </summary>
    public string GetPeerServiceUrl()
    {
        if (!string.IsNullOrWhiteSpace(PeerServiceUrl))
            return PeerServiceUrl.TrimEnd('/');

        return string.IsNullOrWhiteSpace(SorchaServiceUrl)
            ? string.Empty
            : $"{SorchaServiceUrl.TrimEnd('/')}/api/peer";
    }

    /// <summary>
    /// Gets the resolved Auth Token endpoint URL.
    /// Returns the override if specified, otherwise derives from base URL.
    /// When using the API Gateway, uses the direct /api/service-auth/token route.
    /// When using explicit TenantServiceUrl, appends /api/service-auth/token to it.
    /// </summary>
    public string GetAuthTokenUrl()
    {
        if (!string.IsNullOrWhiteSpace(AuthTokenUrl))
            return AuthTokenUrl;

        // If TenantServiceUrl is explicitly set (e.g., Aspire direct connection),
        // use it as the base for the auth endpoint
        if (!string.IsNullOrWhiteSpace(TenantServiceUrl))
            return $"{TenantServiceUrl.TrimEnd('/')}/api/service-auth/token";

        // Otherwise, use the base URL with the gateway's direct route
        // The gateway has a direct route for /api/service-auth/* to tenant service
        return string.IsNullOrWhiteSpace(SorchaServiceUrl)
            ? string.Empty
            : $"{SorchaServiceUrl.TrimEnd('/')}/api/service-auth/token";
    }

    /// <summary>
    /// Validates the profile configuration.
    /// A profile is valid if it has a name and at least one way to reach services:
    /// - Empty base URL with null individual URLs (same-origin relative URLs via gateway)
    /// - A valid absolute base URL
    /// - All individual service URLs specified as valid absolute URLs
    /// </summary>
    public bool IsValid()
    {
        if (string.IsNullOrWhiteSpace(Name))
            return false;

        // Valid case 1: Empty base URL with null individual URLs = same-origin relative URLs
        // This is valid for gateway-served UIs where API calls use relative URLs (e.g., /api/tenant)
        if (string.IsNullOrWhiteSpace(SorchaServiceUrl) &&
            string.IsNullOrWhiteSpace(TenantServiceUrl) &&
            string.IsNullOrWhiteSpace(RegisterServiceUrl) &&
            string.IsNullOrWhiteSpace(BlueprintServiceUrl) &&
            string.IsNullOrWhiteSpace(WalletServiceUrl) &&
            string.IsNullOrWhiteSpace(PeerServiceUrl))
        {
            return true;
        }

        // Valid case 2: A valid absolute base URL
        if (!string.IsNullOrWhiteSpace(SorchaServiceUrl))
        {
            return Uri.TryCreate(SorchaServiceUrl, UriKind.Absolute, out _);
        }

        // Valid case 3: All individual URLs must be specified and valid
        return IsValidUrl(TenantServiceUrl) &&
               IsValidUrl(RegisterServiceUrl) &&
               IsValidUrl(BlueprintServiceUrl) &&
               IsValidUrl(WalletServiceUrl) &&
               IsValidUrl(PeerServiceUrl);
    }

    private static bool IsValidUrl(string? url)
    {
        return !string.IsNullOrWhiteSpace(url) &&
               Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    /// <summary>
    /// Creates a new profile with updated timestamp
    /// </summary>
    public Profile WithUpdatedTimestamp()
    {
        return this with { UpdatedAt = DateTime.UtcNow };
    }
}
