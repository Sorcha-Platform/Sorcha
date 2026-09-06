// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sorcha.ServiceClients.Auth;
using Sorcha.ServiceClients.Helpers;
using Sorcha.ServiceClients.Configuration;

namespace Sorcha.ServiceClients.Tenant;

/// <summary>
/// HTTP client for Tenant Service organisation / user / token operations (spec 139 US4).
/// </summary>
public class TenantServiceClient : ITenantServiceClient
{
    private readonly HttpClient _httpClient;
    private readonly IServiceAuthClient _serviceAuth;
    private readonly ILogger<TenantServiceClient> _logger;
    private readonly string _serviceAddress;

    public TenantServiceClient(
        HttpClient httpClient,
        IServiceAuthClient serviceAuth,
        IConfiguration configuration,
        ILogger<TenantServiceClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _serviceAuth = serviceAuth ?? throw new ArgumentNullException(nameof(serviceAuth));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _serviceAddress = SorchaServiceAddresses.TryResolve(configuration, SorchaService.Tenant)
            ?? configuration["GrpcClients:TenantService:Address"]
            ?? throw new InvalidOperationException("Tenant Service address not configured");

        if (_httpClient.BaseAddress == null)
        {
            _httpClient.BaseAddress = new Uri(_serviceAddress.TrimEnd('/') + "/");
        }

        _logger.LogInformation("TenantServiceClient initialized (Address: {Address})", _serviceAddress);
    }

    private Task SetAuthHeaderAsync(CancellationToken cancellationToken) =>
        ServiceClientAuthHelper.SetAuthHeaderAsync(
            _httpClient, _serviceAuth, _logger, "TenantService", cancellationToken);

    /// <inheritdoc />
    public Task<string?> ListOrganizationsAsync(string? queryString = null, CancellationToken cancellationToken = default)
    {
        var url = string.IsNullOrWhiteSpace(queryString) ? "api/organizations" : $"api/organizations?{queryString}";
        return GetRawAsync(url, "list organizations", cancellationToken);
    }

    /// <inheritdoc />
    public Task<string?> CreateOrganizationAsync(string requestJson, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Post, "api/platform/organizations", requestJson, "create organization", cancellationToken);

    /// <inheritdoc />
    public Task<string?> UpdateOrganizationAsync(string organizationId, string requestJson, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Put, $"api/organizations/{Uri.EscapeDataString(organizationId)}", requestJson, "update organization", cancellationToken);

    /// <inheritdoc />
    public Task<string?> ListUsersAsync(string organizationId, string? queryString = null, CancellationToken cancellationToken = default)
    {
        var url = $"api/organizations/{Uri.EscapeDataString(organizationId)}/users";
        if (!string.IsNullOrWhiteSpace(queryString))
        {
            url += $"?{queryString}";
        }

        return GetRawAsync(url, "list users", cancellationToken);
    }

    /// <inheritdoc />
    public Task<string?> ManageUserAsync(
        string organizationId,
        string userId,
        string action,
        string? requestJson = null,
        CancellationToken cancellationToken = default)
    {
        var orgSegment = Uri.EscapeDataString(organizationId);
        var userSegment = Uri.EscapeDataString(userId);

        return action.ToLowerInvariant() switch
        {
            "suspend" => SendRawAsync(HttpMethod.Post, $"api/organizations/{orgSegment}/users/{userSegment}/suspend", string.Empty, "suspend user", cancellationToken),
            "reactivate" => SendRawAsync(HttpMethod.Post, $"api/organizations/{orgSegment}/users/{userSegment}/reactivate", string.Empty, "reactivate user", cancellationToken),
            "unlock" => SendRawAsync(HttpMethod.Post, $"api/organizations/{orgSegment}/users/{userSegment}/unlock", string.Empty, "unlock user", cancellationToken),
            "changerole" => SendRawAsync(HttpMethod.Put, $"api/organizations/{orgSegment}/users/{userSegment}/role", requestJson ?? "{}", "change user role", cancellationToken),
            _ => throw new ArgumentException($"Unknown action '{action}'. Must be Suspend, Reactivate, Unlock, or ChangeRole.", nameof(action))
        };
    }

    /// <inheritdoc />
    public Task<string?> RevokeTokenAsync(string? userId, string? organizationId, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(userId))
        {
            var body = JsonSerializer.Serialize(new { userId });
            return SendRawAsync(HttpMethod.Post, "api/auth/token/revoke-user", body, "revoke user tokens", cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(organizationId))
        {
            var body = JsonSerializer.Serialize(new { organizationId });
            return SendRawAsync(HttpMethod.Post, "api/auth/token/revoke-organization", body, "revoke organization tokens", cancellationToken);
        }

        throw new ArgumentException("Either userId or organizationId must be supplied.");
    }

    /// <inheritdoc />
    public Task<string?> GetMyPersonaAsync(string? queryString = null, CancellationToken cancellationToken = default)
    {
        var url = string.IsNullOrWhiteSpace(queryString) ? "api/me/persona" : $"api/me/persona?{queryString}";
        return GetRawAsync(url, "get my persona", cancellationToken);
    }

    /// <inheritdoc />
    public Task<string?> ReplaceMyPersonaAsync(string requestJson, string? queryString = null, CancellationToken cancellationToken = default)
    {
        var url = string.IsNullOrWhiteSpace(queryString) ? "api/me/persona" : $"api/me/persona?{queryString}";
        return SendRawAsync(HttpMethod.Put, url, requestJson, "replace my persona", cancellationToken);
    }

    /// <inheritdoc />
    public Task<string?> SetOrganizationStatusAsync(string organizationId, string requestJson, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Put, $"api/platform/organizations/{Uri.EscapeDataString(organizationId)}/status", requestJson, "set organization status", cancellationToken);

    /// <inheritdoc />
    public Task<string?> GetPlatformSettingsAsync(CancellationToken cancellationToken = default) =>
        GetRawAsync("api/platform/settings", "get platform settings", cancellationToken);

    /// <inheritdoc />
    public Task<string?> UpdatePublicOrgAsync(string requestJson, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Put, "api/platform/settings/public-org", requestJson, "update public org settings", cancellationToken);

    /// <inheritdoc />
    public Task<string?> GetOrganizationUsersAsync(string organizationId, string? queryString = null, CancellationToken cancellationToken = default)
    {
        var url = string.IsNullOrWhiteSpace(queryString)
            ? $"api/platform/organizations/{Uri.EscapeDataString(organizationId)}/users"
            : $"api/platform/organizations/{Uri.EscapeDataString(organizationId)}/users?{queryString}";
        return GetRawAsync(url, "get organization users", cancellationToken);
    }

    /// <inheritdoc />
    public Task<string?> ProvisionPlatformUserAsync(string requestJson, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Post, "api/platform/users/", requestJson, "provision platform user", cancellationToken);

    /// <inheritdoc />
    public Task<string?> ResetPlatformUserPasswordAsync(string userId, string requestJson, CancellationToken cancellationToken = default) =>
        SendRawAsync(HttpMethod.Put, $"api/platform/users/{Uri.EscapeDataString(userId)}/password", requestJson, "reset platform user password", cancellationToken);

    private async Task<string?> GetRawAsync(string url, string operation, CancellationToken cancellationToken)
    {
        try
        {
            await SetAuthHeaderAsync(cancellationToken);
            var response = await _httpClient.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Tenant {Operation} failed: {StatusCode}", operation, response.StatusCode);
                return null;
            }

            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (HttpRequestException)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed Tenant {Operation}", operation);
            return null;
        }
    }

    private async Task<string?> SendRawAsync(HttpMethod method, string url, string bodyJson, string operation, CancellationToken cancellationToken)
    {
        try
        {
            await SetAuthHeaderAsync(cancellationToken);

            using var request = new HttpRequestMessage(method, url)
            {
                Content = new StringContent(bodyJson, Encoding.UTF8, "application/json")
            };
            var response = await _httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Tenant {Operation} failed: {StatusCode}", operation, response.StatusCode);
                return null;
            }

            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (HttpRequestException)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed Tenant {Operation}", operation);
            return null;
        }
    }
}
