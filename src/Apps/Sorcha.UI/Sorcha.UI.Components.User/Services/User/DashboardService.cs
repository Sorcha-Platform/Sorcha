// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Sorcha.UI.Core.Models.Dashboard;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Implementation of <see cref="IDashboardService"/> that calls the API Gateway.
/// </summary>
public class DashboardService : IDashboardService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<DashboardService> _logger;

    public DashboardService(HttpClient httpClient, ILogger<DashboardService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<DashboardStatsViewModel> GetDashboardStatsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/dashboard", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch dashboard stats: {StatusCode}", response.StatusCode);
                return new DashboardStatsViewModel { IsLoaded = false };
            }

            var stats = await response.Content.ReadFromJsonAsync<DashboardStatsViewModel>(cancellationToken: cancellationToken);
            return stats is not null
                ? stats with { IsLoaded = true }
                : new DashboardStatsViewModel { IsLoaded = false };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching dashboard statistics");
            return new DashboardStatsViewModel { IsLoaded = false };
        }
    }
}
