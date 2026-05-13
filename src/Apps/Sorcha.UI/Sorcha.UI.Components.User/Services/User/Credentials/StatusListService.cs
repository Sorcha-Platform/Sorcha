// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sorcha.UI.Core.Models.Credentials;

namespace Sorcha.UI.Core.Services.Credentials;

public class StatusListService : IStatusListService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<StatusListService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public StatusListService(HttpClient httpClient, ILogger<StatusListService> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<StatusListViewModel?> GetStatusListAsync(string listId, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"/api/v1/credentials/status-lists/{listId}", ct);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("Status list {ListId} not found", listId);
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to get status list {ListId}: {StatusCode}", listId, response.StatusCode);
                return null;
            }

            _logger.LogInformation("Successfully retrieved status list {ListId}", listId);
            return await response.Content.ReadFromJsonAsync<StatusListViewModel>(JsonOptions, ct);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Network error getting status list {ListId}", listId);
            return null;
        }
    }
}
