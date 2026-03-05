// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sorcha.UI.Core.Models.Admin;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// HTTP client implementation for System Register operations.
/// </summary>
public class SystemRegisterService : ISystemRegisterService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<SystemRegisterService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public SystemRegisterService(HttpClient httpClient, ILogger<SystemRegisterService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<SystemRegisterViewModel?> GetStatusAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/system-register", ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to get system register status: {StatusCode}", response.StatusCode);
                return null;
            }
            return await response.Content.ReadFromJsonAsync<SystemRegisterViewModel>(JsonOptions, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting system register status");
            return null;
        }
    }

    public async Task<BlueprintPageResult> GetBlueprintsAsync(int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/api/system-register/blueprints?page={page}&pageSize={pageSize}", ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to get system register blueprints: {StatusCode}", response.StatusCode);
                return new BlueprintPageResult();
            }
            return await response.Content.ReadFromJsonAsync<BlueprintPageResult>(JsonOptions, ct)
                ?? new BlueprintPageResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting system register blueprints");
            return new BlueprintPageResult();
        }
    }

    public async Task<BlueprintDetailViewModel?> GetBlueprintAsync(string blueprintId, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/api/system-register/blueprints/{Uri.EscapeDataString(blueprintId)}", ct);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadFromJsonAsync<BlueprintDetailViewModel>(JsonOptions, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting blueprint {BlueprintId}", blueprintId);
            return null;
        }
    }

    public async Task<BlueprintDetailViewModel?> GetBlueprintVersionAsync(string blueprintId, long version, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/api/system-register/blueprints/{Uri.EscapeDataString(blueprintId)}/versions/{version}", ct);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadFromJsonAsync<BlueprintDetailViewModel>(JsonOptions, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting blueprint {BlueprintId} version {Version}", blueprintId, version);
            return null;
        }
    }
}
