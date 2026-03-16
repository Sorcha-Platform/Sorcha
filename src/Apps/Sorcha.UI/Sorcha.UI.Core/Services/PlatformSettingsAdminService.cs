// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;

using Microsoft.Extensions.Logging;

using Sorcha.UI.Core.Extensions;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Implementation of platform settings administration service.
/// </summary>
public class PlatformSettingsAdminService : IPlatformSettingsAdminService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<PlatformSettingsAdminService> _logger;
    private const string BaseUrl = "/api/platform/settings";

    /// <summary>
    /// Initializes a new instance of the <see cref="PlatformSettingsAdminService"/> class.
    /// </summary>
    /// <param name="httpClient">HTTP client configured with authentication handler.</param>
    /// <param name="logger">Logger instance.</param>
    public PlatformSettingsAdminService(
        HttpClient httpClient,
        ILogger<PlatformSettingsAdminService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<PlatformSettingsDto?> GetSettingsAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync(BaseUrl, ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<PlatformSettingsDto>(JsonDefaults.Api, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get platform settings");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<PlatformSettingsDto?> UpdatePublicOrgAsync(bool enabled, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.PutAsJsonAsync($"{BaseUrl}/public-org", new { enabled }, ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<PlatformSettingsDto>(JsonDefaults.Api, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update public org setting to {Enabled}", enabled);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<PlatformSettingsDto?> UpdateMaxOrgsPerUserAsync(int maxOrgs, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.PutAsJsonAsync($"{BaseUrl}/max-orgs", new { maxOrgsPerUser = maxOrgs }, ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<PlatformSettingsDto>(JsonDefaults.Api, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update max orgs per user to {MaxOrgs}", maxOrgs);
            return null;
        }
    }
}
