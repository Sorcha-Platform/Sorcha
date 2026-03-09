// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace Sorcha.UI.Core.Services.Identity;

/// <summary>
/// Implementation of IDP configuration client service.
/// Communicates with the Tenant Service IDP configuration API.
/// </summary>
public class IdpConfigurationClientService : IIdpConfigurationClientService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<IdpConfigurationClientService> _logger;

    public IdpConfigurationClientService(
        HttpClient httpClient,
        ILogger<IdpConfigurationClientService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    private static string BaseUrl(Guid organizationId) =>
        $"/api/organizations/{Uri.EscapeDataString(organizationId.ToString())}/idp-config";

    /// <inheritdoc />
    public async Task<IReadOnlyList<IdpConfigurationDto>> GetIdpConfigurationsAsync(
        Guid organizationId,
        CancellationToken ct = default)
    {
        try
        {
            var result = await _httpClient.GetFromJsonAsync<IReadOnlyList<IdpConfigurationDto>>(
                BaseUrl(organizationId), ct);

            return result ?? [];
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to list IDP configurations for organization {OrganizationId}",
                organizationId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IdpConfigurationDto?> GetIdpConfigurationAsync(
        Guid organizationId,
        Guid configId,
        CancellationToken ct = default)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<IdpConfigurationDto>(
                $"{BaseUrl(organizationId)}/{Uri.EscapeDataString(configId.ToString())}", ct);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to get IDP configuration {ConfigId} for organization {OrganizationId}",
                configId, organizationId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IdpDiscoveryResult> DiscoverIdpAsync(
        Guid organizationId,
        DiscoverIdpRequest request,
        CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync(
                $"{BaseUrl(organizationId)}/discover", request, ct);

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<IdpDiscoveryResult>(ct);

            return result ?? new IdpDiscoveryResult { Success = false, Error = "Failed to parse discovery response" };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to discover IDP for organization {OrganizationId} with issuer {Issuer}",
                organizationId, request.Issuer);
            return new IdpDiscoveryResult { Success = false, Error = ex.Message };
        }
    }

    /// <inheritdoc />
    public async Task<IdpConfigurationDto> CreateIdpConfigurationAsync(
        Guid organizationId,
        CreateIdpConfigurationRequest request,
        CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync(
                BaseUrl(organizationId), request, ct);

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<IdpConfigurationDto>(ct);

            return result ?? throw new InvalidOperationException("Failed to parse response");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to create IDP configuration for organization {OrganizationId}",
                organizationId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IdpConfigurationDto?> UpdateIdpConfigurationAsync(
        Guid organizationId,
        Guid configId,
        UpdateIdpConfigurationRequest request,
        CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.PutAsJsonAsync(
                $"{BaseUrl(organizationId)}/{Uri.EscapeDataString(configId.ToString())}", request, ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return null;

            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<IdpConfigurationDto>(ct);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to update IDP configuration {ConfigId} for organization {OrganizationId}",
                configId, organizationId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IdpConnectionTestResult> TestIdpConnectionAsync(
        Guid organizationId,
        Guid configId,
        CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.PostAsync(
                $"{BaseUrl(organizationId)}/{Uri.EscapeDataString(configId.ToString())}/test", null, ct);

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<IdpConnectionTestResult>(ct);

            return result ?? new IdpConnectionTestResult { Success = false, Error = "Failed to parse test response" };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to test IDP connection {ConfigId} for organization {OrganizationId}",
                configId, organizationId);
            return new IdpConnectionTestResult { Success = false, Error = ex.Message };
        }
    }

    /// <inheritdoc />
    public async Task<bool> ToggleIdpAsync(
        Guid organizationId,
        Guid configId,
        bool enabled,
        CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.PutAsJsonAsync(
                $"{BaseUrl(organizationId)}/{Uri.EscapeDataString(configId.ToString())}/toggle",
                new { enabled },
                ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return false;

            response.EnsureSuccessStatusCode();
            return true;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to toggle IDP configuration {ConfigId} for organization {OrganizationId}",
                configId, organizationId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteIdpConfigurationAsync(
        Guid organizationId,
        Guid configId,
        CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.DeleteAsync(
                $"{BaseUrl(organizationId)}/{Uri.EscapeDataString(configId.ToString())}", ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return false;

            response.EnsureSuccessStatusCode();
            return true;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to delete IDP configuration {ConfigId} for organization {OrganizationId}",
                configId, organizationId);
            throw;
        }
    }
}
