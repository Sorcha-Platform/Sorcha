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
}
