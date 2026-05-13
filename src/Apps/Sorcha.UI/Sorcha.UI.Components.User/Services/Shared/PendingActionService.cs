// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Sorcha.UI.Core.Models;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Client service for querying pending actions from the Blueprint Service.
/// </summary>
public interface IPendingActionService
{
    /// <summary>
    /// Gets pending actions for the authenticated user.
    /// </summary>
    Task<PendingActionListResponse> GetPendingActionsAsync(
        int page = 1, int pageSize = 20, string? urgency = null, string? blueprintId = null);

    /// <summary>
    /// Gets the count of pending actions (for badge display).
    /// </summary>
    Task<PendingActionCountResponse> GetPendingActionCountAsync();

    /// <summary>
    /// Marks pending action notifications as read via the activity events API.
    /// </summary>
    Task MarkAsReadAsync(Guid[] eventIds);
}

/// <summary>
/// HTTP client for pending actions API endpoints.
/// </summary>
public class PendingActionService : IPendingActionService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<PendingActionService> _logger;

    public PendingActionService(HttpClient httpClient, ILogger<PendingActionService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<PendingActionListResponse> GetPendingActionsAsync(
        int page = 1, int pageSize = 20, string? urgency = null, string? blueprintId = null)
    {
        try
        {
            var url = $"/api/actions/pending?page={page}&pageSize={pageSize}";
            if (!string.IsNullOrEmpty(urgency))
                url += $"&urgency={Uri.EscapeDataString(urgency)}";
            if (!string.IsNullOrEmpty(blueprintId))
                url += $"&blueprintId={Uri.EscapeDataString(blueprintId)}";

            var response = await _httpClient.GetFromJsonAsync<PendingActionListResponse>(url);
            return response ?? new PendingActionListResponse();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get pending actions");
            return new PendingActionListResponse();
        }
    }

    /// <inheritdoc/>
    public async Task<PendingActionCountResponse> GetPendingActionCountAsync()
    {
        try
        {
            var response = await _httpClient.GetFromJsonAsync<PendingActionCountResponse>("/api/actions/pending/count");
            return response ?? new PendingActionCountResponse();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get pending action count");
            return new PendingActionCountResponse();
        }
    }

    /// <inheritdoc/>
    public async Task MarkAsReadAsync(Guid[] eventIds)
    {
        try
        {
            await _httpClient.PostAsJsonAsync("/api/events/mark-read", new { eventIds });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to mark pending actions as read");
        }
    }
}

/// <summary>
/// Response model for pending actions list.
/// </summary>
public class PendingActionListResponse
{
    public List<PendingActionSummaryDto> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

/// <summary>
/// Lightweight pending action summary for inbox display.
/// </summary>
public class PendingActionSummaryDto
{
    public string InstanceId { get; set; } = string.Empty;
    public int ActionId { get; set; }
    public string ActionTitle { get; set; } = string.Empty;
    public string BlueprintId { get; set; } = string.Empty;
    public string BlueprintTitle { get; set; } = string.Empty;
    public string InstanceReference { get; set; } = string.Empty;
    public string SenderAddress { get; set; } = string.Empty;
    public string SenderDisplayName { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Urgency { get; set; } = "normal";
    public DateTimeOffset? Deadline { get; set; }
    public string RegisterId { get; set; } = string.Empty;
    public string TransactionId { get; set; } = string.Empty;
    public string? NavigationPath { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
}

/// <summary>
/// Response model for pending action count.
/// </summary>
public class PendingActionCountResponse
{
    public int Count { get; set; }
    public int UrgentCount { get; set; }
}
