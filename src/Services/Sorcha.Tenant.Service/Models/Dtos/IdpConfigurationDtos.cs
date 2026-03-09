// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Service.Models.Dtos;

/// <summary>
/// Request to create or update IDP configuration for an organization.
/// </summary>
public record IdpConfigurationRequest
{
    /// <summary>Provider preset (MicrosoftEntra, Google, Okta, Apple, AmazonCognito, GenericOidc).</summary>
    public required string ProviderPreset { get; init; }

    /// <summary>OIDC issuer URL.</summary>
    public required string IssuerUrl { get; init; }

    /// <summary>OAuth2 client ID.</summary>
    public required string ClientId { get; init; }

    /// <summary>OAuth2 client secret (encrypted before storage).</summary>
    public required string ClientSecret { get; init; }

    /// <summary>UI display name for this provider.</summary>
    public string? DisplayName { get; init; }

    /// <summary>OAuth2 scopes to request.</summary>
    public string[] Scopes { get; init; } = ["openid", "profile", "email"];
}

/// <summary>
/// Response containing IDP configuration details.
/// </summary>
public record IdpConfigurationResponse
{
    /// <summary>Configuration identifier.</summary>
    public Guid Id { get; init; }

    /// <summary>Provider preset name.</summary>
    public string ProviderPreset { get; init; } = string.Empty;

    /// <summary>UI display name for this provider.</summary>
    public string? DisplayName { get; init; }

    /// <summary>OIDC issuer URL.</summary>
    public string IssuerUrl { get; init; } = string.Empty;

    /// <summary>Whether this IDP is currently enabled.</summary>
    public bool IsEnabled { get; init; }

    /// <summary>Configured OAuth2 scopes.</summary>
    public string[] Scopes { get; init; } = [];

    /// <summary>Discovered authorization endpoint URL.</summary>
    public string? AuthorizationEndpoint { get; init; }

    /// <summary>Discovered token endpoint URL.</summary>
    public string? TokenEndpoint { get; init; }

    /// <summary>Discovered userinfo endpoint URL.</summary>
    public string? UserInfoEndpoint { get; init; }

    /// <summary>When the OIDC discovery document was last fetched.</summary>
    public DateTimeOffset? DiscoveryFetchedAt { get; init; }
}

/// <summary>
/// Request to discover IDP endpoints via OIDC discovery.
/// </summary>
public record DiscoverIdpRequest
{
    /// <summary>OIDC issuer URL to discover endpoints for.</summary>
    public required string IssuerUrl { get; init; }
}

/// <summary>
/// Response from an OIDC discovery document fetch.
/// </summary>
public record DiscoveryResponse
{
    /// <summary>Verified issuer identifier.</summary>
    public string Issuer { get; init; } = string.Empty;

    /// <summary>Authorization endpoint URL.</summary>
    public string? AuthorizationEndpoint { get; init; }

    /// <summary>Token endpoint URL.</summary>
    public string? TokenEndpoint { get; init; }

    /// <summary>UserInfo endpoint URL.</summary>
    public string? UserInfoEndpoint { get; init; }

    /// <summary>JWKS URI for token signature validation.</summary>
    public string? JwksUri { get; init; }

    /// <summary>Scopes supported by the IDP.</summary>
    public string[] SupportedScopes { get; init; } = [];
}

/// <summary>
/// Request to toggle an IDP's enabled state.
/// </summary>
public record ToggleIdpRequest
{
    /// <summary>Whether to enable or disable the IDP.</summary>
    public required bool Enabled { get; init; }
}

/// <summary>
/// Response from an IDP connection test.
/// </summary>
public record TestConnectionResponse
{
    /// <summary>Whether the connection test succeeded.</summary>
    public bool Success { get; init; }

    /// <summary>Human-readable result message.</summary>
    public string? Message { get; init; }

    /// <summary>Scopes discovered during the test.</summary>
    public string[] DiscoveredScopes { get; init; } = [];
}
