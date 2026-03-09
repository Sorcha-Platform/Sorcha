// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Services.Identity;

/// <summary>
/// Client-side service for managing Identity Provider configurations.
/// Wraps HTTP calls to the Tenant Service IDP configuration API.
/// </summary>
public interface IIdpConfigurationClientService
{
    /// <summary>
    /// Gets all IDP configurations for an organization.
    /// </summary>
    /// <param name="organizationId">Organization ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of IDP configurations.</returns>
    Task<IReadOnlyList<IdpConfigurationDto>> GetIdpConfigurationsAsync(
        Guid organizationId,
        CancellationToken ct = default);

    /// <summary>
    /// Gets a specific IDP configuration by ID.
    /// </summary>
    /// <param name="organizationId">Organization ID.</param>
    /// <param name="configId">Configuration ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>IDP configuration, or null if not found.</returns>
    Task<IdpConfigurationDto?> GetIdpConfigurationAsync(
        Guid organizationId,
        Guid configId,
        CancellationToken ct = default);

    /// <summary>
    /// Discovers IDP endpoints via OIDC discovery document.
    /// </summary>
    /// <param name="organizationId">Organization ID.</param>
    /// <param name="request">Discovery request containing the issuer URL.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Discovery result with resolved endpoints.</returns>
    Task<IdpDiscoveryResult> DiscoverIdpAsync(
        Guid organizationId,
        DiscoverIdpRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Creates a new IDP configuration for an organization.
    /// </summary>
    /// <param name="organizationId">Organization ID.</param>
    /// <param name="request">Create request with provider details.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Created IDP configuration.</returns>
    Task<IdpConfigurationDto> CreateIdpConfigurationAsync(
        Guid organizationId,
        CreateIdpConfigurationRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Updates an existing IDP configuration.
    /// </summary>
    /// <param name="organizationId">Organization ID.</param>
    /// <param name="configId">Configuration ID.</param>
    /// <param name="request">Update request with changed fields.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Updated IDP configuration, or null if not found.</returns>
    Task<IdpConfigurationDto?> UpdateIdpConfigurationAsync(
        Guid organizationId,
        Guid configId,
        UpdateIdpConfigurationRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Tests connectivity to a configured IDP.
    /// </summary>
    /// <param name="organizationId">Organization ID.</param>
    /// <param name="configId">Configuration ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Connection test result with timing information.</returns>
    Task<IdpConnectionTestResult> TestIdpConnectionAsync(
        Guid organizationId,
        Guid configId,
        CancellationToken ct = default);

    /// <summary>
    /// Enables or disables an IDP configuration.
    /// </summary>
    /// <param name="organizationId">Organization ID.</param>
    /// <param name="configId">Configuration ID.</param>
    /// <param name="enabled">Whether to enable or disable the IDP.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the toggle was successful.</returns>
    Task<bool> ToggleIdpAsync(
        Guid organizationId,
        Guid configId,
        bool enabled,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes an IDP configuration.
    /// </summary>
    /// <param name="organizationId">Organization ID.</param>
    /// <param name="configId">Configuration ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the deletion was successful.</returns>
    Task<bool> DeleteIdpConfigurationAsync(
        Guid organizationId,
        Guid configId,
        CancellationToken ct = default);
}

/// <summary>
/// IDP configuration DTO for client-side use.
/// </summary>
public record IdpConfigurationDto
{
    /// <summary>Configuration identifier.</summary>
    public Guid Id { get; init; }

    /// <summary>Provider preset name (e.g., MicrosoftEntra, Google, Okta).</summary>
    public string ProviderPreset { get; init; } = string.Empty;

    /// <summary>UI display name for this provider.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>OIDC issuer URL.</summary>
    public string Issuer { get; init; } = string.Empty;

    /// <summary>OAuth2 client ID.</summary>
    public string ClientId { get; init; } = string.Empty;

    /// <summary>Whether this IDP is currently enabled.</summary>
    public bool IsEnabled { get; init; }

    /// <summary>When the OIDC discovery document was last fetched.</summary>
    public DateTimeOffset? DiscoveryFetchedAt { get; init; }

    /// <summary>Configuration creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>
/// Request to discover IDP endpoints via OIDC discovery.
/// </summary>
public record DiscoverIdpRequest
{
    /// <summary>OIDC issuer URL to discover endpoints for.</summary>
    public required string Issuer { get; init; }
}

/// <summary>
/// Result of an IDP OIDC discovery attempt.
/// </summary>
public record IdpDiscoveryResult
{
    /// <summary>Whether the discovery succeeded.</summary>
    public bool Success { get; init; }

    /// <summary>Discovered authorization endpoint URL.</summary>
    public string? AuthorizationEndpoint { get; init; }

    /// <summary>Discovered token endpoint URL.</summary>
    public string? TokenEndpoint { get; init; }

    /// <summary>Discovered userinfo endpoint URL.</summary>
    public string? UserInfoEndpoint { get; init; }

    /// <summary>Discovered JWKS URI for token signature validation.</summary>
    public string? JwksUri { get; init; }

    /// <summary>Error message if discovery failed.</summary>
    public string? Error { get; init; }
}

/// <summary>
/// Request to create a new IDP configuration.
/// </summary>
public record CreateIdpConfigurationRequest
{
    /// <summary>Provider preset (MicrosoftEntra, Google, Okta, Apple, AmazonCognito, GenericOidc).</summary>
    public required string ProviderPreset { get; init; }

    /// <summary>UI display name for this provider.</summary>
    public required string DisplayName { get; init; }

    /// <summary>OIDC issuer URL.</summary>
    public required string Issuer { get; init; }

    /// <summary>OAuth2 client ID.</summary>
    public required string ClientId { get; init; }

    /// <summary>OAuth2 client secret (encrypted before storage).</summary>
    public required string ClientSecret { get; init; }
}

/// <summary>
/// Request to update an existing IDP configuration.
/// </summary>
public record UpdateIdpConfigurationRequest
{
    /// <summary>Updated display name.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Updated client ID.</summary>
    public string? ClientId { get; init; }

    /// <summary>Updated client secret.</summary>
    public string? ClientSecret { get; init; }
}

/// <summary>
/// Result of an IDP connection test.
/// </summary>
public record IdpConnectionTestResult
{
    /// <summary>Whether the connection test succeeded.</summary>
    public bool Success { get; init; }

    /// <summary>Error message if the test failed.</summary>
    public string? Error { get; init; }

    /// <summary>Response time in milliseconds.</summary>
    public int? ResponseTimeMs { get; init; }
}
