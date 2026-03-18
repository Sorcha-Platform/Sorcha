// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;

namespace Sorcha.ApiGateway.Services;

/// <summary>
/// Service for aggregating statistics from backend services for the dashboard
/// </summary>
public class DashboardStatisticsService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<DashboardStatisticsService> _logger;
    private readonly IConfiguration _configuration;

    public DashboardStatisticsService(
        IHttpClientFactory httpClientFactory,
        ILogger<DashboardStatisticsService> logger,
        IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _configuration = configuration;
    }

    /// <summary>
    /// Gets aggregated dashboard statistics from all services
    /// </summary>
    public async Task<DashboardStatistics> GetDashboardStatisticsAsync(CancellationToken cancellationToken = default)
    {
        var stats = new DashboardStatistics
        {
            Timestamp = DateTimeOffset.UtcNow
        };

        // Run all queries in parallel
        await Task.WhenAll(
            GetBlueprintStatisticsAsync(stats, cancellationToken),
            GetWalletStatisticsAsync(stats, cancellationToken),
            GetRegisterStatisticsAsync(stats, cancellationToken),
            GetTenantStatisticsAsync(stats, cancellationToken),
            GetPeerStatisticsAsync(stats, cancellationToken)
        );

        return stats;
    }

    private async Task GetBlueprintStatisticsAsync(DashboardStatistics stats, CancellationToken cancellationToken)
    {
        try
        {
            var baseUrl = _configuration["Services:Blueprint:Url"] ?? "http://blueprint-service:8080";
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);

            var response = await client.GetAsync($"{baseUrl}/api/stats", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(content);

                if (doc.RootElement.TryGetProperty("blueprintCount", out var blueprintCount))
                {
                    stats.TotalBlueprints = blueprintCount.GetInt32();
                }

                if (doc.RootElement.TryGetProperty("instanceCount", out var instanceCount))
                {
                    stats.TotalBlueprintInstances = instanceCount.GetInt32();
                }

                if (doc.RootElement.TryGetProperty("activeInstanceCount", out var activeCount))
                {
                    stats.ActiveBlueprintInstances = activeCount.GetInt32();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get blueprint statistics");
        }
    }

    private async Task GetWalletStatisticsAsync(DashboardStatistics stats, CancellationToken cancellationToken)
    {
        try
        {
            var baseUrl = _configuration["Services:Wallet:Url"] ?? "http://wallet-service:8080";
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);

            var response = await client.GetAsync($"{baseUrl}/api/stats", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(content);

                if (doc.RootElement.TryGetProperty("walletCount", out var walletCount))
                {
                    stats.TotalWallets = walletCount.GetInt32();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get wallet statistics");
        }
    }

    private async Task GetRegisterStatisticsAsync(DashboardStatistics stats, CancellationToken cancellationToken)
    {
        try
        {
            var baseUrl = _configuration["Services:Register:Url"] ?? "http://register-service:8080";
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);

            var response = await client.GetAsync($"{baseUrl}/api/stats", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(content);

                if (doc.RootElement.TryGetProperty("registerCount", out var registerCount))
                {
                    stats.TotalRegisters = registerCount.GetInt32();
                }

                if (doc.RootElement.TryGetProperty("transactionCount", out var transactionCount))
                {
                    stats.TotalTransactions = transactionCount.GetInt32();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get register statistics");
        }
    }

    private async Task GetTenantStatisticsAsync(DashboardStatistics stats, CancellationToken cancellationToken)
    {
        try
        {
            var baseUrl = _configuration["Services:Tenant:Url"] ?? "http://tenant-service:8080";
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);

            // Query organization stats endpoint (public, no auth required)
            var response = await client.GetAsync($"{baseUrl}/api/organizations/stats", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(content);

                // The stats endpoint returns { "totalOrganizations": 5, "totalUsers": 10 }
                if (doc.RootElement.TryGetProperty("totalOrganizations", out var totalOrgsElement))
                {
                    stats.TotalTenants = totalOrgsElement.GetInt32();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get tenant statistics");
        }
    }

    private async Task GetPeerStatisticsAsync(DashboardStatistics stats, CancellationToken cancellationToken)
    {
        try
        {
            var baseUrl = _configuration["Services:Peer:Url"] ?? "http://peer-service:8080";
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);

            // Query connected peers endpoint
            var response = await client.GetAsync($"{baseUrl}/api/peers/connected", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(content);

                if (doc.RootElement.TryGetProperty("ConnectedPeerCount", out var countElement))
                {
                    stats.ConnectedPeers = countElement.GetInt32();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get peer statistics");
        }
    }
}

/// <summary>
/// Dashboard statistics model
/// </summary>
public class DashboardStatistics
{
    public DateTimeOffset Timestamp { get; set; }
    public int TotalBlueprints { get; set; }
    public int TotalBlueprintInstances { get; set; }
    public int ActiveBlueprintInstances { get; set; }
    public int TotalWallets { get; set; }
    public int TotalRegisters { get; set; }
    public int TotalTransactions { get; set; }
    public int TotalTenants { get; set; }
    public int ConnectedPeers { get; set; }
}
