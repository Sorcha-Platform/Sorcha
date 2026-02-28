// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Sorcha.UI.Core.Models.Admin;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Implementation of organization administration service.
/// </summary>
public class OrganizationAdminService : IOrganizationAdminService
{
    private readonly HttpClient _httpClient;
    private readonly IAuditService _auditService;
    private readonly ILogger<OrganizationAdminService> _logger;
    private const string BaseUrl = "/api/organizations";

    public OrganizationAdminService(
        HttpClient httpClient,
        IAuditService auditService,
        ILogger<OrganizationAdminService> logger)
    {
        _httpClient = httpClient;
        _auditService = auditService;
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
                url, cancellationToken);

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
                $"{BaseUrl}/{id}", cancellationToken);
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

            var result = await response.Content.ReadFromJsonAsync<OrganizationDto>(cancellationToken);

            if (result != null)
            {
                await _auditService.LogOrganizationEventAsync(
                    AuditEventType.OrganizationCreated,
                    result.Id,
                    new Dictionary<string, object>
                    {
                        ["name"] = result.Name,
                        ["subdomain"] = result.Subdomain
                    },
                    cancellationToken);
            }

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

            var result = await response.Content.ReadFromJsonAsync<OrganizationDto>(cancellationToken);

            if (result != null)
            {
                await _auditService.LogOrganizationEventAsync(
                    AuditEventType.OrganizationUpdated,
                    id,
                    new Dictionary<string, object>
                    {
                        ["updatedFields"] = GetUpdatedFields(request)
                    },
                    cancellationToken);
            }

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

            await _auditService.LogOrganizationEventAsync(
                AuditEventType.OrganizationDeactivated,
                id,
                null,
                cancellationToken);

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
                cancellationToken);

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
                $"{BaseUrl}/stats", cancellationToken);

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
        CancellationToken cancellationToken = default)
    {
        var url = includeInactive
            ? $"{BaseUrl}/{organizationId}/users?includeInactive=true"
            : $"{BaseUrl}/{organizationId}/users";

        try
        {
            var response = await _httpClient.GetFromJsonAsync<UserListResult>(
                url, cancellationToken);

            return response ?? new UserListResult();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to list users for organization {OrganizationId}",
                organizationId);
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
                $"{BaseUrl}/{organizationId}/users/{userId}", cancellationToken);
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

            var result = await response.Content.ReadFromJsonAsync<UserDto>(cancellationToken);

            if (result != null)
            {
                await _auditService.LogUserEventAsync(
                    AuditEventType.UserAddedToOrganization,
                    organizationId,
                    result.Id,
                    new Dictionary<string, object>
                    {
                        ["email"] = result.Email,
                        ["displayName"] = result.DisplayName,
                        ["roles"] = result.Roles
                    },
                    cancellationToken);
            }

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

            var result = await response.Content.ReadFromJsonAsync<UserDto>(cancellationToken);

            if (result != null)
            {
                await _auditService.LogUserEventAsync(
                    AuditEventType.UserUpdatedInOrganization,
                    organizationId,
                    userId,
                    new Dictionary<string, object>
                    {
                        ["updatedFields"] = GetUpdatedFields(request)
                    },
                    cancellationToken);
            }

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

            await _auditService.LogUserEventAsync(
                AuditEventType.UserRemovedFromOrganization,
                organizationId,
                userId,
                null,
                cancellationToken);

            return true;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to remove user {UserId} from organization {OrganizationId}",
                userId, organizationId);
            throw;
        }
    }


    private static string[] GetUpdatedFields(UpdateOrganizationDto request)
    {
        var fields = new List<string>();
        if (request.Name != null) fields.Add("name");
        if (request.Status != null) fields.Add("status");
        if (request.Branding != null) fields.Add("branding");
        return [.. fields];
    }

    private static string[] GetUpdatedFields(UpdateUserDto request)
    {
        var fields = new List<string>();
        if (request.DisplayName != null) fields.Add("displayName");
        if (request.Roles != null) fields.Add("roles");
        if (request.Status != null) fields.Add("status");
        return [.. fields];
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
