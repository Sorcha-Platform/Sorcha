// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using Sorcha.UI.Core.Extensions;
using Sorcha.UI.Core.Models.Authentication;

namespace Sorcha.UI.Core.Services.Admin;

/// <summary>
/// A single org membership entry for the org switcher.
/// </summary>
public record OrgMembershipEntry
{
    public Guid OrganizationId { get; init; }
    public string OrganizationName { get; init; } = string.Empty;
    public string Subdomain { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public bool IsCurrent { get; init; }
}

/// <summary>
/// Response containing all org memberships for the current user.
/// </summary>
public record OrgMembershipListResponse
{
    public IReadOnlyList<OrgMembershipEntry> Items { get; init; } = [];
}

/// <summary>
/// Client service for organisation switching operations.
/// </summary>
public interface IOrgSwitcherService
{
    /// <summary>
    /// Gets all org memberships for the current user.
    /// </summary>
    Task<OrgMembershipListResponse> GetMyOrganizationsAsync(CancellationToken ct = default);

    /// <summary>
    /// Switches to the target organisation, returning new tokens.
    /// </summary>
    Task<TokenResponse?> SwitchOrgAsync(Guid organizationId, CancellationToken ct = default);
}

/// <summary>
/// HTTP client implementation for org switching operations.
/// </summary>
public class OrgSwitcherService : IOrgSwitcherService
{
    private readonly HttpClient _http;

    public OrgSwitcherService(HttpClient http)
    {
        _http = http;
    }

    /// <inheritdoc />
    public async Task<OrgMembershipListResponse> GetMyOrganizationsAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _http.GetFromJsonAsync<OrgMembershipListResponse>(
                "/api/auth/me/organizations", JsonDefaults.Api, ct);

            return response ?? new OrgMembershipListResponse();
        }
        catch (HttpRequestException)
        {
            // Suppress 401 noise when token is expired or user is not yet authenticated
            return new OrgMembershipListResponse();
        }
    }

    /// <inheritdoc />
    public async Task<TokenResponse?> SwitchOrgAsync(Guid organizationId, CancellationToken ct = default)
    {
        var request = new { OrganizationId = organizationId };
        var response = await _http.PostAsJsonAsync("/api/auth/switch-org", request, JsonDefaults.Api, ct);

        if (!response.IsSuccessStatusCode)
            return null;

        return await response.Content.ReadFromJsonAsync<TokenResponse>(JsonDefaults.Api, ct);
    }
}
