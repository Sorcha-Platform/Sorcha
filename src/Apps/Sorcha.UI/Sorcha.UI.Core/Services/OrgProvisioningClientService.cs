// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using Sorcha.UI.Core.Extensions;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Result of an org provisioning request from the API.
/// </summary>
public record OrgProvisioningClientResult
{
    public bool Success { get; init; }
    public Guid? OrganizationId { get; init; }
    public string? OrganizationName { get; init; }
    public string? Subdomain { get; init; }
    public string? Error { get; init; }
    public string? ErrorCode { get; init; }
}

/// <summary>
/// Client service for self-service organisation creation.
/// </summary>
public interface IOrgProvisioningClientService
{
    /// <summary>
    /// Creates a new private organisation via the authenticated user's platform identity.
    /// </summary>
    Task<OrgProvisioningClientResult> CreateOrganizationAsync(
        string name, string subdomain, string? description = null,
        CancellationToken ct = default);
}

/// <summary>
/// HTTP client implementation for self-service organisation provisioning.
/// </summary>
public class OrgProvisioningClientService : IOrgProvisioningClientService
{
    private readonly HttpClient _http;

    public OrgProvisioningClientService(HttpClient http)
    {
        _http = http;
    }

    /// <inheritdoc />
    public async Task<OrgProvisioningClientResult> CreateOrganizationAsync(
        string name, string subdomain, string? description = null,
        CancellationToken ct = default)
    {
        var request = new { Name = name, Subdomain = subdomain, Description = description };
        var response = await _http.PostAsJsonAsync("/api/auth/create-org", request, JsonDefaults.Api, ct);

        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<OrgProvisioningClientResult>(JsonDefaults.Api, ct)
                ?? new OrgProvisioningClientResult { Success = false, Error = "Empty response." };
        }

        // Try to read error from response body
        try
        {
            var errorResult = await response.Content.ReadFromJsonAsync<OrgProvisioningClientResult>(JsonDefaults.Api, ct);
            if (errorResult is not null)
                return errorResult;
        }
        catch
        {
            // Fall through to generic error
        }

        return new OrgProvisioningClientResult
        {
            Success = false,
            Error = $"Request failed with status {(int)response.StatusCode}."
        };
    }
}
