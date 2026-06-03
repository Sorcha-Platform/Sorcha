// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Models.Dtos;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Manages IDP configuration lifecycle: create, update, delete, discover, test, toggle.
/// Client secrets are encrypted with AES-256-GCM before storage.
/// </summary>
public class IdpConfigurationService : IIdpConfigurationService
{
    private readonly TenantDbContext _dbContext;
    private readonly IOidcDiscoveryService _discoveryService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ISecretProtectionProvider _secretProtection;
    private readonly ILogger<IdpConfigurationService> _logger;

    /// <summary>
    /// Provider preset issuer URL templates.
    /// The admin provides their tenant/domain-specific part, and we construct the full URL.
    /// </summary>
    private static readonly Dictionary<IdentityProviderType, string> ProviderIssuerTemplates = new()
    {
        [IdentityProviderType.MicrosoftEntra] = "https://login.microsoftonline.com/{0}/v2.0",
        [IdentityProviderType.Google] = "https://accounts.google.com",
        [IdentityProviderType.Okta] = "https://{0}.okta.com",
        [IdentityProviderType.Apple] = "https://appleid.apple.com",
        [IdentityProviderType.AmazonCognito] = "https://cognito-idp.{0}.amazonaws.com/{1}",
    };

    public IdpConfigurationService(
        TenantDbContext dbContext,
        IOidcDiscoveryService discoveryService,
        IHttpClientFactory httpClientFactory,
        ISecretProtectionProvider secretProtection,
        ILogger<IdpConfigurationService> logger)
    {
        _dbContext = dbContext;
        _discoveryService = discoveryService;
        _httpClientFactory = httpClientFactory;
        _secretProtection = secretProtection;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IdpConfigurationResponse?> GetConfigurationAsync(
        Guid organizationId, CancellationToken cancellationToken = default)
    {
        var config = await _dbContext.IdentityProviderConfigurations
            .FirstOrDefaultAsync(c => c.OrganizationId == organizationId, cancellationToken);

        return config is null ? null : MapToResponse(config);
    }

    /// <inheritdoc />
    public async Task<IdpConfigurationResponse> CreateOrUpdateAsync(
        Guid organizationId, IdpConfigurationRequest request, CancellationToken cancellationToken = default)
    {
        var providerPreset = Enum.Parse<IdentityProviderType>(request.ProviderPreset, ignoreCase: true);

        // Protect the client secret at rest with AES-256-GCM — reversible, so the OIDC token
        // exchange (and test-connection) can recover the real secret.
        var (encryptedSecret, secretKeyId) = await _secretProtection.EncryptAsync(
            Encoding.UTF8.GetBytes(request.ClientSecret), cancellationToken);

        var existing = await _dbContext.IdentityProviderConfigurations
            .FirstOrDefaultAsync(c => c.OrganizationId == organizationId, cancellationToken);

        if (existing is not null)
        {
            existing.ProviderPreset = providerPreset;
            existing.IssuerUrl = request.IssuerUrl;
            existing.ClientId = request.ClientId;
            existing.ClientSecretEncrypted = encryptedSecret;
            existing.ClientSecretKeyId = secretKeyId;
            existing.Scopes = request.Scopes.Length > 0 ? request.Scopes : ["openid", "profile", "email"];
            existing.DisplayName = request.DisplayName;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            existing = new IdentityProviderConfiguration
            {
                OrganizationId = organizationId,
                ProviderPreset = providerPreset,
                IssuerUrl = request.IssuerUrl,
                ClientId = request.ClientId,
                ClientSecretEncrypted = encryptedSecret,
                ClientSecretKeyId = secretKeyId,
                Scopes = request.Scopes.Length > 0 ? request.Scopes : ["openid", "profile", "email"],
                DisplayName = request.DisplayName,
                MetadataUrl = $"{request.IssuerUrl.TrimEnd('/')}/.well-known/openid-configuration",
            };
            _dbContext.IdentityProviderConfigurations.Add(existing);
        }

        // Auto-discover endpoints
        try
        {
            var discovery = await _discoveryService.DiscoverAsync(request.IssuerUrl, cancellationToken);
            existing.AuthorizationEndpoint = discovery.AuthorizationEndpoint;
            existing.TokenEndpoint = discovery.TokenEndpoint;
            existing.UserInfoEndpoint = discovery.UserInfoEndpoint;
            existing.JwksUri = discovery.JwksUri;
            existing.DiscoveryDocumentJson = JsonSerializer.Serialize(discovery);
            existing.DiscoveryFetchedAt = DateTimeOffset.UtcNow;

            _logger.LogInformation("Auto-discovered endpoints for org {OrgId} from {IssuerUrl}", organizationId, request.IssuerUrl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Discovery failed for {IssuerUrl} — saving config without endpoints", request.IssuerUrl);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return MapToResponse(existing);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        var config = await _dbContext.IdentityProviderConfigurations
            .FirstOrDefaultAsync(c => c.OrganizationId == organizationId, cancellationToken);

        if (config is null) return false;

        _dbContext.IdentityProviderConfigurations.Remove(config);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _discoveryService.InvalidateCache(config.IssuerUrl);
        _logger.LogInformation("Deleted IDP configuration for org {OrgId}", organizationId);
        return true;
    }

    /// <inheritdoc />
    public async Task<DiscoveryResponse> DiscoverAsync(
        Guid organizationId, string issuerUrl, CancellationToken cancellationToken = default)
    {
        _discoveryService.InvalidateCache(issuerUrl);
        return await _discoveryService.DiscoverAsync(issuerUrl, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<TestConnectionResponse> TestConnectionAsync(
        Guid organizationId, CancellationToken cancellationToken = default)
    {
        var config = await _dbContext.IdentityProviderConfigurations
            .FirstOrDefaultAsync(c => c.OrganizationId == organizationId, cancellationToken);

        if (config is null)
        {
            throw new InvalidOperationException("No IDP configuration found for this organization.");
        }

        if (string.IsNullOrEmpty(config.TokenEndpoint))
        {
            return new TestConnectionResponse
            {
                Success = false,
                Message = "Token endpoint not configured. Run discovery first."
            };
        }

        try
        {
            var httpClient = _httpClientFactory.CreateClient();
            var decryptedSecret = Encoding.UTF8.GetString(
                await _secretProtection.DecryptAsync(config.ClientSecretEncrypted, config.ClientSecretKeyId, cancellationToken));

            var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = config.ClientId,
                ["client_secret"] = decryptedSecret,
                ["scope"] = string.Join(" ", config.Scopes)
            });

            var response = await httpClient.PostAsync(config.TokenEndpoint, tokenRequest, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("IDP connection test succeeded for org {OrgId}", organizationId);
                return new TestConnectionResponse
                {
                    Success = true,
                    Message = "Connection successful. IDP credentials are valid.",
                    DiscoveredScopes = config.Scopes
                };
            }

            _logger.LogWarning(
                "IDP connection test failed for org {OrgId}: {StatusCode}",
                organizationId, response.StatusCode);

            return new TestConnectionResponse
            {
                Success = false,
                Message = $"Connection failed with status {(int)response.StatusCode}. Verify client ID and secret."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "IDP connection test error for org {OrgId}", organizationId);
            return new TestConnectionResponse
            {
                Success = false,
                Message = $"Connection error: {ex.Message}"
            };
        }
    }

    /// <inheritdoc />
    public async Task<IdpConfigurationResponse> ToggleAsync(
        Guid organizationId, bool enabled, CancellationToken cancellationToken = default)
    {
        var config = await _dbContext.IdentityProviderConfigurations
            .FirstOrDefaultAsync(c => c.OrganizationId == organizationId, cancellationToken);

        if (config is null)
        {
            throw new InvalidOperationException("No IDP configuration found for this organization.");
        }

        config.IsEnabled = enabled;
        config.UpdatedAt = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("IDP {Action} for org {OrgId}", enabled ? "enabled" : "disabled", organizationId);
        return MapToResponse(config);
    }

    private static IdpConfigurationResponse MapToResponse(IdentityProviderConfiguration config) => new()
    {
        Id = config.Id,
        ProviderPreset = config.ProviderPreset.ToString(),
        DisplayName = config.DisplayName,
        IssuerUrl = config.IssuerUrl,
        IsEnabled = config.IsEnabled,
        Scopes = config.Scopes,
        AuthorizationEndpoint = config.AuthorizationEndpoint,
        TokenEndpoint = config.TokenEndpoint,
        UserInfoEndpoint = config.UserInfoEndpoint,
        DiscoveryFetchedAt = config.DiscoveryFetchedAt
    };

}
