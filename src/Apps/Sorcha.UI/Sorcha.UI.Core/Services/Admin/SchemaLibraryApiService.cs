// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sorcha.UI.Core.Models.SchemaLibrary;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// HTTP client implementation for the Schema Library API endpoints.
/// </summary>
public class SchemaLibraryApiService : ISchemaLibraryApiService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<SchemaLibraryApiService> _logger;

    public SchemaLibraryApiService(HttpClient httpClient, ILogger<SchemaLibraryApiService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<SchemaIndexSearchResponse> SearchAsync(
        string? search = null,
        string[]? sectors = null,
        string? provider = null,
        int limit = 25,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var queryParams = new List<string>();
            if (!string.IsNullOrWhiteSpace(search))
                queryParams.Add($"search={Uri.EscapeDataString(search)}");
            if (sectors is { Length: > 0 })
                queryParams.Add($"sectors={Uri.EscapeDataString(string.Join(",", sectors))}");
            if (!string.IsNullOrWhiteSpace(provider))
                queryParams.Add($"provider={Uri.EscapeDataString(provider)}");
            if (limit != 25)
                queryParams.Add($"limit={limit}");
            if (!string.IsNullOrWhiteSpace(cursor))
                queryParams.Add($"cursor={Uri.EscapeDataString(cursor)}");

            var url = "/api/v1/schemas/index";
            if (queryParams.Count > 0)
                url += "?" + string.Join("&", queryParams);

            var response = await _httpClient.GetFromJsonAsync<SchemaIndexSearchResponse>(url, cancellationToken);
            return response ?? new SchemaIndexSearchResponse([], 0, null, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching schema index");
            return new SchemaIndexSearchResponse([], 0, null, null);
        }
    }

    public async Task<SchemaIndexEntryDetailViewModel?> GetDetailAsync(
        string shortCode,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<SchemaIndexEntryDetailViewModel>(
                $"/api/v1/schemas/index/{Uri.EscapeDataString(shortCode)}",
                cancellationToken);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching schema detail for {ShortCode}", shortCode);
            return null;
        }
    }

    public async Task<JsonDocument?> GetContentAsync(
        string shortCode,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<JsonDocument>(
                $"/api/v1/schemas/index/content/{Uri.EscapeDataString(shortCode)}",
                cancellationToken);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching schema content for {ShortCode}", shortCode);
            return null;
        }
    }

    public async Task<IReadOnlyList<SchemaSectorViewModel>> GetSectorsAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var sectors = await _httpClient.GetFromJsonAsync<List<SchemaSectorViewModel>>(
                "/api/v1/schemas/sectors", cancellationToken);
            return sectors ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching sectors");
            return [];
        }
    }

    public async Task<OrganisationSectorPreferencesViewModel?> GetSectorPreferencesAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<OrganisationSectorPreferencesViewModel>(
                "/api/v1/schemas/sectors/preferences", cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching sector preferences");
            return null;
        }
    }

    public async Task<bool> UpdateSectorPreferencesAsync(
        string[]? enabledSectors,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.PutAsJsonAsync(
                "/api/v1/schemas/sectors/preferences",
                new { enabledSectors },
                cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating sector preferences");
            return false;
        }
    }

    public async Task<IReadOnlyList<SchemaProviderStatusViewModel>> GetProviderStatusesAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var statuses = await _httpClient.GetFromJsonAsync<List<SchemaProviderStatusViewModel>>(
                "/api/v1/schemas/providers", cancellationToken);
            return statuses ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching provider statuses");
            return [];
        }
    }

    public async Task<bool> RefreshProviderAsync(
        string providerName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.PostAsync(
                $"/api/v1/schemas/providers/{Uri.EscapeDataString(providerName)}/refresh",
                null,
                cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error triggering refresh for provider {Provider}", providerName);
            return false;
        }
    }
}
