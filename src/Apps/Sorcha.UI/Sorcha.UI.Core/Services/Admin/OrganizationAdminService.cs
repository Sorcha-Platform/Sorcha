// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Sorcha.UI.Core.Extensions;
using Sorcha.UI.Core.Models.Admin;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Implementation of organization administration service.
/// </summary>
public class OrganizationAdminService : IOrganizationAdminService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OrganizationAdminService> _logger;
    private const string BaseUrl = "/api/organizations";

    public OrganizationAdminService(
        HttpClient httpClient,
        ILogger<OrganizationAdminService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }


    public async Task<OrganizationListResult> ListOrganizationsAsync(
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var url = includeInactive ? $"{BaseUrl}?includeInactive=true" : BaseUrl;

        try
        {
            var response = await _httpClient.GetFromJsonAsync<OrganizationListResult>(
                url, JsonDefaults.Api, cancellationToken);

            return response ?? new OrganizationListResult();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to list organizations");
            throw;
        }
    }

    public async Task<OrganizationDto?> GetOrganizationAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<OrganizationDto>(
                $"{BaseUrl}/{id}", JsonDefaults.Api, cancellationToken);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to get organization {OrganizationId}", id);
            throw;
        }
    }

    public async Task<OrganizationDto> CreateOrganizationAsync(
        CreateOrganizationDto request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync(BaseUrl, request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<OrganizationDto>(JsonDefaults.Api, cancellationToken);

            return result ?? throw new InvalidOperationException("Failed to parse response");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to create organization {Name}", request.Name);
            throw;
        }
    }

    public async Task<OrganizationDto?> UpdateOrganizationAsync(
        Guid id,
        UpdateOrganizationDto request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.PutAsJsonAsync($"{BaseUrl}/{id}", request, cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<OrganizationDto>(JsonDefaults.Api, cancellationToken);

            return result;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to update organization {OrganizationId}", id);
            throw;
        }
    }

    public async Task<bool> DeactivateOrganizationAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.DeleteAsync($"{BaseUrl}/{id}", cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return false;

            response.EnsureSuccessStatusCode();

            return true;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to deactivate organization {OrganizationId}", id);
            throw;
        }
    }

    public async Task<SubdomainValidationResult> ValidateSubdomainAsync(
        string subdomain,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"{BaseUrl}/validate-subdomain/{subdomain}", cancellationToken);

            var result = await response.Content.ReadFromJsonAsync<SubdomainValidationResult>(
                JsonDefaults.Api, cancellationToken);

            return result ?? new SubdomainValidationResult
            {
                Subdomain = subdomain,
                IsValid = false,
                ErrorMessage = "Failed to validate subdomain"
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to validate subdomain {Subdomain}", subdomain);
            return new SubdomainValidationResult
            {
                Subdomain = subdomain,
                IsValid = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<PlatformKpis> GetPlatformStatsAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetFromJsonAsync<StatsResponse>(
                $"{BaseUrl}/stats", JsonDefaults.Api, cancellationToken);

            return new PlatformKpis
            {
                TotalOrganizations = response?.TotalOrganizations ?? 0,
                TotalUsers = response?.TotalUsers ?? 0,
                LastUpdated = DateTimeOffset.UtcNow
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to get platform stats");
            return new PlatformKpis { LastUpdated = DateTimeOffset.UtcNow };
        }
    }


    public async Task<UserListResult> GetOrganizationUsersAsync(
        Guid organizationId,
        bool includeInactive = false,
        bool? emailVerified = null,
        string? provisionedVia = null,
        bool includePending = false,
        CancellationToken cancellationToken = default)
    {
        var queryParams = new List<string>();
        if (includeInactive) queryParams.Add("includeInactive=true");
        if (emailVerified.HasValue) queryParams.Add($"emailVerified={emailVerified.Value.ToString().ToLower()}");
        if (!string.IsNullOrEmpty(provisionedVia)) queryParams.Add($"provisionedVia={Uri.EscapeDataString(provisionedVia)}");
        if (includePending) queryParams.Add("includePending=true");

        var query = queryParams.Count > 0 ? "?" + string.Join("&", queryParams) : "";
        var url = $"{BaseUrl}/{organizationId}/users{query}";

        try
        {
            var response = await _httpClient.GetFromJsonAsync<UserListResult>(
                url, JsonDefaults.Api, cancellationToken);

            return response ?? new UserListResult();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to list users for organization {OrganizationId}",
                organizationId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> VerifyEmailAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.PostAsync(
                $"{BaseUrl}/{organizationId}/users/{userId}/verify-email",
                null, cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                return false; // Already verified
            }

            response.EnsureSuccessStatusCode();
            _logger.LogInformation("Admin verified email for user {UserId} in org {OrganizationId}",
                userId, organizationId);
            return true;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to verify email for user {UserId} in org {OrganizationId}",
                userId, organizationId);
            throw;
        }
    }

    public async Task<UserDto?> GetOrganizationUserAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<UserDto>(
                $"{BaseUrl}/{organizationId}/users/{userId}", JsonDefaults.Api, cancellationToken);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to get user {UserId} in organization {OrganizationId}",
                userId, organizationId);
            throw;
        }
    }

    public async Task<UserDto> AddUserToOrganizationAsync(
        Guid organizationId,
        AddUserDto request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync(
                $"{BaseUrl}/{organizationId}/users", request, cancellationToken);

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<UserDto>(JsonDefaults.Api, cancellationToken);

            return result ?? throw new InvalidOperationException("Failed to parse response");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to add user {Email} to organization {OrganizationId}",
                request.Email, organizationId);
            throw;
        }
    }

    public async Task<UserDto?> UpdateOrganizationUserAsync(
        Guid organizationId,
        Guid userId,
        UpdateUserDto request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.PutAsJsonAsync(
                $"{BaseUrl}/{organizationId}/users/{userId}", request, cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<UserDto>(JsonDefaults.Api, cancellationToken);

            return result;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to update user {UserId} in organization {OrganizationId}",
                userId, organizationId);
            throw;
        }
    }

    public async Task<bool> RemoveUserFromOrganizationAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.DeleteAsync(
                $"{BaseUrl}/{organizationId}/users/{userId}", cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return false;

            response.EnsureSuccessStatusCode();

            return true;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to remove user {UserId} from organization {OrganizationId}",
                userId, organizationId);
            throw;
        }
    }

    /// <summary>
    /// Internal response class for stats endpoint.
    /// </summary>
    private record StatsResponse
    {
        public int TotalOrganizations { get; init; }
        public int TotalUsers { get; init; }
    }
}
