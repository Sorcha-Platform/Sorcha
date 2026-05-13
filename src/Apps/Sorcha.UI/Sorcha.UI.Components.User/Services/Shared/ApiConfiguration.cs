// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.UI.Core.Models.Configuration;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Configuration for API endpoints.
/// Can be configured from a Profile or manually via GatewayBaseUrl.
/// </summary>
public static class ApiConfiguration
{
    private static Profile? _activeProfile;

    /// <summary>
    /// Base URL for the API Gateway (legacy/fallback).
    /// Empty string means relative URLs (same origin), which works when served through the gateway.
    /// Prefer using ConfigureFromProfile() for profile-based configuration.
    /// </summary>
    public static string GatewayBaseUrl { get; set; } = "";

    /// <summary>
    /// Gets the currently configured profile name, if any.
    /// </summary>
    public static string? ActiveProfileName => _activeProfile?.Name;

    /// <summary>
    /// Configures API endpoints from a Profile.
    /// This allows using profile-specific service URLs or the base URL.
    /// </summary>
    /// <param name="profile">The profile to configure from</param>
    public static void ConfigureFromProfile(Profile profile)
    {
        _activeProfile = profile ?? throw new ArgumentNullException(nameof(profile));

        // Update legacy GatewayBaseUrl for backward compatibility
        GatewayBaseUrl = profile.SorchaServiceUrl ?? "";
    }

    /// <summary>
    /// Clears the profile configuration, reverting to GatewayBaseUrl-based URLs.
    /// </summary>
    public static void ClearProfile()
    {
        _activeProfile = null;
    }

    /// <summary>
    /// Blueprint API endpoint.
    /// Uses profile-specific URL if configured, otherwise derives from GatewayBaseUrl.
    /// </summary>
    public static string BlueprintApiUrl =>
        _activeProfile?.GetBlueprintServiceUrl() ??
        (string.IsNullOrEmpty(GatewayBaseUrl) ? "/api/blueprint" : $"{GatewayBaseUrl}/api/blueprint");

    /// <summary>
    /// Tenant Service endpoint.
    /// Uses profile-specific URL if configured, otherwise derives from GatewayBaseUrl.
    /// </summary>
    public static string TenantServiceUrl =>
        _activeProfile?.GetTenantServiceUrl() ??
        (string.IsNullOrEmpty(GatewayBaseUrl) ? "/api/tenant" : $"{GatewayBaseUrl}/api/tenant");

    /// <summary>
    /// Register Service endpoint.
    /// Uses profile-specific URL if configured, otherwise derives from GatewayBaseUrl.
    /// </summary>
    public static string RegisterServiceUrl =>
        _activeProfile?.GetRegisterServiceUrl() ??
        (string.IsNullOrEmpty(GatewayBaseUrl) ? "/api/register" : $"{GatewayBaseUrl}/api/register");

    /// <summary>
    /// Wallet Service endpoint.
    /// Uses profile-specific URL if configured, otherwise derives from GatewayBaseUrl.
    /// </summary>
    public static string WalletServiceUrl =>
        _activeProfile?.GetWalletServiceUrl() ??
        (string.IsNullOrEmpty(GatewayBaseUrl) ? "/api/wallet" : $"{GatewayBaseUrl}/api/wallet");

    /// <summary>
    /// Peer Service endpoint.
    /// Uses profile-specific URL if configured, otherwise derives from GatewayBaseUrl.
    /// </summary>
    public static string PeerServiceUrl =>
        _activeProfile?.GetPeerServiceUrl() ??
        (string.IsNullOrEmpty(GatewayBaseUrl) ? "/api/peer" : $"{GatewayBaseUrl}/api/peer");

    /// <summary>
    /// Auth Token endpoint.
    /// Uses profile-specific URL if configured, otherwise derives from TenantServiceUrl.
    /// </summary>
    public static string AuthTokenUrl =>
        _activeProfile?.GetAuthTokenUrl() ??
        $"{TenantServiceUrl}/api/service-auth/token";

    /// <summary>
    /// Aggregated health endpoint (via gateway)
    /// </summary>
    public static string HealthUrl =>
        string.IsNullOrEmpty(GatewayBaseUrl) ? "/api/health" : $"{GatewayBaseUrl}/api/health";

    /// <summary>
    /// System statistics endpoint (via gateway)
    /// </summary>
    public static string StatsUrl =>
        string.IsNullOrEmpty(GatewayBaseUrl) ? "/api/stats" : $"{GatewayBaseUrl}/api/stats";

    /// <summary>
    /// Blueprint service status endpoint
    /// </summary>
    public static string BlueprintStatusUrl => $"{BlueprintApiUrl}/status";

    /// <summary>
    /// Peer service status endpoint
    /// </summary>
    public static string PeerStatusUrl => $"{PeerServiceUrl}/status";
}
