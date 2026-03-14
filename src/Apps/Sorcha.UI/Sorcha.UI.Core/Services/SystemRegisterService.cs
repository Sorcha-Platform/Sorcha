// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Sorcha.UI.Core.Models.Admin;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Implementation of <see cref="ISystemRegisterService"/> that calls the Register Service API.
/// </summary>
public class SystemRegisterService : ISystemRegisterService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<SystemRegisterService> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="SystemRegisterService"/>.
    /// </summary>
    public SystemRegisterService(HttpClient httpClient, ILogger<SystemRegisterService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<SystemRegisterViewModel?> GetStatusAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/system-register", ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch system register status: {StatusCode}", response.StatusCode);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<SystemRegisterViewModel>(cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching system register status");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<BlueprintPageResult> GetBlueprintsAsync(int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"/api/system-register/blueprints?page={page}&pageSize={pageSize}", ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch system register blueprints: {StatusCode}", response.StatusCode);
                return new BlueprintPageResult();
            }

            return await response.Content.ReadFromJsonAsync<BlueprintPageResult>(cancellationToken: ct)
                ?? new BlueprintPageResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching system register blueprints");
            return new BlueprintPageResult();
        }
    }

    /// <inheritdoc />
    public async Task<BlueprintDetailViewModel?> GetBlueprintAsync(string blueprintId, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"/api/system-register/blueprints/{blueprintId}", ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch blueprint {BlueprintId}: {StatusCode}", blueprintId, response.StatusCode);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<BlueprintDetailViewModel>(cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching blueprint {BlueprintId}", blueprintId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<BlueprintDetailViewModel?> GetBlueprintVersionAsync(string blueprintId, long version, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"/api/system-register/blueprints/{blueprintId}/versions/{version}", ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch blueprint {BlueprintId} version {Version}: {StatusCode}",
                    blueprintId, version, response.StatusCode);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<BlueprintDetailViewModel>(cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching blueprint {BlueprintId} version {Version}", blueprintId, version);
            return null;
        }
    }
}
