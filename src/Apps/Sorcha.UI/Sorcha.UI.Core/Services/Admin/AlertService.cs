// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Sorcha.UI.Core.Models.Admin;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Implementation of <see cref="IAlertService"/> that fetches alerts from the API Gateway.
/// </summary>
public class AlertService : IAlertService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AlertService> _logger;
    private HashSet<string> _previousAlertIds = [];

    public AlertService(HttpClient httpClient, ILogger<AlertService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public AlertsResponse? CurrentAlerts { get; private set; }

    public event EventHandler<AlertsChangedEventArgs>? AlertsChanged;

    public async Task<AlertsResponse> GetAlertsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/alerts", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch alerts: {StatusCode}", response.StatusCode);
                return CurrentAlerts ?? new AlertsResponse();
            }

            var alertsResponse = await response.Content.ReadFromJsonAsync<AlertsResponse>(cancellationToken: cancellationToken);
            if (alertsResponse == null)
            {
                return CurrentAlerts ?? new AlertsResponse();
            }

            DetectChanges(alertsResponse);
            CurrentAlerts = alertsResponse;
            return alertsResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching alerts");
            return CurrentAlerts ?? new AlertsResponse();
        }
    }

    private void DetectChanges(AlertsResponse newResponse)
    {
        var newAlertIds = newResponse.Alerts.Select(a => a.Id).ToHashSet();

        var addedIds = newAlertIds.Except(_previousAlertIds).ToList();
        var removedIds = _previousAlertIds.Except(newAlertIds).ToList();

        if (addedIds.Count > 0 || removedIds.Count > 0)
        {
            var newAlerts = newResponse.Alerts.Where(a => addedIds.Contains(a.Id)).ToList();
            var resolvedAlerts = CurrentAlerts?.Alerts.Where(a => removedIds.Contains(a.Id)).ToList() ?? [];

            AlertsChanged?.Invoke(this, new AlertsChangedEventArgs
            {
                NewAlerts = newAlerts,
                ResolvedAlerts = resolvedAlerts
            });
        }

        _previousAlertIds = newAlertIds;
    }
}
