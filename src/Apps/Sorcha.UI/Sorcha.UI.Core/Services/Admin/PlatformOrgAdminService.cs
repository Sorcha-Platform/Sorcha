// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using Sorcha.UI.Core.Extensions;

namespace Sorcha.UI.Core.Services.Admin;

/// <summary>
/// Result of an admin org creation request from the API.
/// </summary>
public record AdminOrgCreationResult
{
    public bool Success { get; init; }
    public Guid? OrganizationId { get; init; }
    public string? OrganizationName { get; init; }
    public string? Subdomain { get; init; }
    public bool AdminDirectlyAdded { get; init; }
    public Guid? InvitationId { get; init; }
    public string? Error { get; init; }
    public string? ErrorCode { get; init; }
}

/// <summary>
/// Summary of an organisation for the platform list view.
/// </summary>
public record PlatformOrgSummary
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Subdomain { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string OrgType { get; init; } = string.Empty;
    public bool IsPlatformOrg { get; init; }
    public int UserCount { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>
/// Paginated org list response from the platform API.
/// </summary>
public record PlatformOrgListResult
{
    public IReadOnlyList<PlatformOrgSummary> Items { get; init; } = [];
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
}

/// <summary>
/// Summary of a user within an organisation for the audit view.
/// </summary>
public record PlatformOrgUser
{
    public Guid Id { get; init; }
    public string Email { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string[] Roles { get; init; } = [];
    public string Status { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? LastLoginAt { get; init; }
}

/// <summary>
/// Paginated user list response from the platform API.
/// </summary>
public record PlatformOrgUserListResult
{
    public IReadOnlyList<PlatformOrgUser> Items { get; init; } = [];
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
}

/// <summary>
/// Client service for platform-level organisation management by system admins.
/// </summary>
public interface IPlatformOrgAdminService
{
    /// <summary>
    /// Creates a new private organisation and invites a user with the specified role.
    /// </summary>
    Task<AdminOrgCreationResult> CreateOrganizationAsync(
        string name, string subdomain, string adminEmail,
        string role = "Administrator", string? description = null,
        CancellationToken ct = default);

    /// <summary>
    /// Lists all organisations with optional status filter and pagination.
    /// </summary>
    Task<PlatformOrgListResult> ListOrganizationsAsync(
        string? statusFilter = null, int page = 1, int pageSize = 25,
        CancellationToken ct = default);

    /// <summary>
    /// Updates an organisation's status (Active/Suspended).
    /// </summary>
    Task<bool> UpdateOrganizationStatusAsync(
        Guid orgId, string status, CancellationToken ct = default);

    /// <summary>
    /// Gets the user list for an organisation.
    /// </summary>
    Task<PlatformOrgUserListResult> GetOrganizationUsersAsync(
        Guid orgId, int page = 1, int pageSize = 25,
        CancellationToken ct = default);
}

/// <summary>
/// HTTP client implementation for platform org admin operations.
/// </summary>
public class PlatformOrgAdminService : IPlatformOrgAdminService
{
    private readonly HttpClient _http;

    public PlatformOrgAdminService(HttpClient http)
    {
        _http = http;
    }

    /// <inheritdoc />
    public async Task<AdminOrgCreationResult> CreateOrganizationAsync(
        string name, string subdomain, string adminEmail,
        string role = "Administrator", string? description = null,
        CancellationToken ct = default)
    {
        var request = new
        {
            Name = name,
            Subdomain = subdomain,
            AdminEmail = adminEmail,
            Role = role,
            Description = description
        };

        var response = await _http.PostAsJsonAsync(
            "/api/platform/organizations", request, JsonDefaults.Api, ct);

        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<AdminOrgCreationResult>(JsonDefaults.Api, ct)
                ?? new AdminOrgCreationResult { Success = false, Error = "Empty response." };
        }

        // Try to read error from response body
        try
        {
            var errorResult = await response.Content.ReadFromJsonAsync<AdminOrgCreationResult>(JsonDefaults.Api, ct);
            if (errorResult is not null)
                return errorResult;
        }
        catch
        {
            // Fall through to generic error
        }

        return new AdminOrgCreationResult
        {
            Success = false,
            Error = $"Request failed with status {(int)response.StatusCode}."
        };
    }

    /// <inheritdoc />
    public async Task<PlatformOrgListResult> ListOrganizationsAsync(
        string? statusFilter = null, int page = 1, int pageSize = 25,
        CancellationToken ct = default)
    {
        var url = $"/api/platform/organizations?page={page}&pageSize={pageSize}";
        if (!string.IsNullOrEmpty(statusFilter))
            url += $"&status={Uri.EscapeDataString(statusFilter)}";

        var result = await _http.GetFromJsonAsync<PlatformOrgListResult>(url, JsonDefaults.Api, ct);
        return result ?? new PlatformOrgListResult();
    }

    /// <inheritdoc />
    public async Task<bool> UpdateOrganizationStatusAsync(
        Guid orgId, string status, CancellationToken ct = default)
    {
        var request = new { Status = status };
        var response = await _http.PutAsJsonAsync(
            $"/api/platform/organizations/{orgId}/status", request, JsonDefaults.Api, ct);
        return response.IsSuccessStatusCode;
    }

    /// <inheritdoc />
    public async Task<PlatformOrgUserListResult> GetOrganizationUsersAsync(
        Guid orgId, int page = 1, int pageSize = 25,
        CancellationToken ct = default)
    {
        var url = $"/api/platform/organizations/{orgId}/users?page={page}&pageSize={pageSize}";
        var result = await _http.GetFromJsonAsync<PlatformOrgUserListResult>(url, JsonDefaults.Api, ct);
        return result ?? new PlatformOrgUserListResult();
    }
}
