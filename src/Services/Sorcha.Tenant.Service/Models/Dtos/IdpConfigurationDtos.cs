// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Service.Models.Dtos;

// Request to create/update IDP configuration
public record IdpConfigurationRequest
{
    public required string ProviderPreset { get; init; } // MicrosoftEntra, Google, Okta, Apple, AmazonCognito, GenericOidc
    public required string IssuerUrl { get; init; }
    public required string ClientId { get; init; }
    public required string ClientSecret { get; init; }
    public string? DisplayName { get; init; }
    public string[] Scopes { get; init; } = ["openid", "profile", "email"];
}

// Response for IDP configuration
public record IdpConfigurationResponse
{
    public Guid Id { get; init; }
    public string ProviderPreset { get; init; } = string.Empty;
    public string? DisplayName { get; init; }
    public string IssuerUrl { get; init; } = string.Empty;
    public bool IsEnabled { get; init; }
    public string[] Scopes { get; init; } = [];
    public string? AuthorizationEndpoint { get; init; }
    public string? TokenEndpoint { get; init; }
    public string? UserInfoEndpoint { get; init; }
    public DateTimeOffset? DiscoveryFetchedAt { get; init; }
}

// Request for discovery
public record DiscoverIdpRequest
{
    public required string IssuerUrl { get; init; }
}

// Response from discovery
public record DiscoveryResponse
{
    public string Issuer { get; init; } = string.Empty;
    public string? AuthorizationEndpoint { get; init; }
    public string? TokenEndpoint { get; init; }
    public string? UserInfoEndpoint { get; init; }
    public string? JwksUri { get; init; }
    public string[] SupportedScopes { get; init; } = [];
}

// Request to toggle IDP
public record ToggleIdpRequest
{
    public required bool Enabled { get; init; }
}

// Response from test connection
public record TestConnectionResponse
{
    public bool Success { get; init; }
    public string? Message { get; init; }
    public string[] DiscoveredScopes { get; init; } = [];
}
