// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Sorcha.UI.Core.Extensions;
using Sorcha.UI.Core.Models.Admin;

namespace Sorcha.UI.Core.Services.Admin;

/// <summary>
/// HttpClient implementation for system event admin endpoints.
/// </summary>
public class EventAdminService : IEventAdminService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<EventAdminService> _logger;

    public EventAdminService(HttpClient httpClient, ILogger<EventAdminService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<EventListResponse> GetEventsAsync(EventFilterModel? filter = null, CancellationToken ct = default)
    {
        try
        {
            var queryParams = new List<string>();

            if (filter is not null)
            {
                if (!string.IsNullOrEmpty(filter.Severity))
                    queryParams.Add($"severity={Uri.EscapeDataString(filter.Severity)}");

                if (filter.Since.HasValue)
                    queryParams.Add($"since={Uri.EscapeDataString(filter.Since.Value.ToString("O"))}");

                queryParams.Add($"page={filter.Page}");
                queryParams.Add($"pageSize={filter.PageSize}");
            }

            var queryString = queryParams.Count > 0 ? "?" + string.Join("&", queryParams) : "";
            var response = await _httpClient.GetAsync($"/api/events/admin{queryString}", ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch events: {StatusCode}", response.StatusCode);
                return new EventListResponse();
            }

            return await response.Content.ReadFromJsonAsync<EventListResponse>(JsonDefaults.Api, ct)
                   ?? new EventListResponse();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Network error fetching system events");
            return new EventListResponse();
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteEventAsync(string eventId, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.DeleteAsync($"/api/events/{Uri.EscapeDataString(eventId)}", ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to delete event {EventId}: {StatusCode}", eventId, response.StatusCode);
            }

            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Network error deleting event {EventId}", eventId);
            return false;
        }
    }
}
