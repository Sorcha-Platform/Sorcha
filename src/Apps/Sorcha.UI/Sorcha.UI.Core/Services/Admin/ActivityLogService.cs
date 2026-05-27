// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Sorcha.UI.Core.Models;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Client service for the activity events REST API.
/// </summary>
public interface IActivityLogService
{
    Task<EventsPagedResponse> GetEventsAsync(int page = 1, int pageSize = 50, bool unreadOnly = false);
    Task<int> GetUnreadCountAsync();
    Task<int> MarkReadAsync(Guid[]? eventIds = null);
    Task<bool> DeleteEventAsync(Guid eventId);
}

public class ActivityLogService : IActivityLogService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ActivityLogService> _logger;

    public ActivityLogService(HttpClient httpClient, ILogger<ActivityLogService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<EventsPagedResponse> GetEventsAsync(int page = 1, int pageSize = 50, bool unreadOnly = false)
    {
        try
        {
            var url = $"/api/events?page={page}&pageSize={pageSize}&unreadOnly={unreadOnly}";
            var response = await _httpClient.GetFromJsonAsync<EventsPagedResponse>(url);
            return response ?? new EventsPagedResponse();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get activity events");
            return new EventsPagedResponse();
        }
    }

    public async Task<int> GetUnreadCountAsync()
    {
        try
        {
            var response = await _httpClient.GetFromJsonAsync<UnreadCountResponse>("/api/events/unread-count");
            return response?.Count ?? 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get unread count");
            return 0;
        }
    }

    public async Task<int> MarkReadAsync(Guid[]? eventIds = null)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("/api/events/mark-read", new { eventIds = eventIds ?? [] });
            var result = await response.Content.ReadFromJsonAsync<MarkReadResponse>();
            return result?.MarkedCount ?? 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to mark events as read");
            return 0;
        }
    }

    public async Task<bool> DeleteEventAsync(Guid eventId)
    {
        try
        {
            var response = await _httpClient.DeleteAsync($"/api/events/{eventId}");
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete event {EventId}", eventId);
            return false;
        }
    }
}
