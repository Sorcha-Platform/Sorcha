// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.ApiGateway.Models;
using System.Text.Json;

namespace Sorcha.ApiGateway.Services;

/// <summary>
/// Service for aggregating health checks from all backend services
/// </summary>
public class HealthAggregationService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<HealthAggregationService> _logger;
    private readonly Dictionary<string, string> _serviceEndpoints;

    public HealthAggregationService(
        IHttpClientFactory httpClientFactory,
        ILogger<HealthAggregationService> logger,
        IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;

        // Load service endpoints from configuration
        _serviceEndpoints = new Dictionary<string, string>
        {
            { "blueprint", configuration["Services:Blueprint:Url"] ?? "http://blueprint-service:8080" },
            { "wallet", configuration["Services:Wallet:Url"] ?? "http://wallet-service:8080" },
            { "register", configuration["Services:Register:Url"] ?? "http://register-service:8080" },
            { "tenant", configuration["Services:Tenant:Url"] ?? "http://tenant-service:8080" },
            { "peer", configuration["Services:Peer:Url"] ?? "http://peer-service:8080" },
            { "validator", configuration["Services:Validator:Url"] ?? "http://validator-service:8080" }
        };
    }

    /// <summary>
    /// Gets aggregated health status from all services
    /// </summary>
    public async Task<AggregatedHealthResponse> GetAggregatedHealthAsync(CancellationToken cancellationToken = default)
    {
        var response = new AggregatedHealthResponse
        {
            Timestamp = DateTimeOffset.UtcNow
        };

        var healthCheckTasks = _serviceEndpoints.Select(async kvp =>
        {
            var (serviceName, endpoint) = (kvp.Key, kvp.Value);
            return (serviceName, await CheckServiceHealthAsync(serviceName, endpoint, cancellationToken));
        });

        var results = await Task.WhenAll(healthCheckTasks);

        foreach (var (serviceName, health) in results)
        {
            response.Services[serviceName] = health;
        }

        // Determine overall status
        var allHealthy = response.Services.Values.All(s => s.Status == "healthy");
        var anyHealthy = response.Services.Values.Any(s => s.Status == "healthy");

        response.Status = allHealthy ? "healthy" : anyHealthy ? "degraded" : "unhealthy";

        return response;
    }

    /// <summary>
    /// Gets system-wide statistics
    /// </summary>
    public async Task<SystemStatistics> GetSystemStatisticsAsync(CancellationToken cancellationToken = default)
    {
        var healthResponse = await GetAggregatedHealthAsync(cancellationToken);

        var stats = new SystemStatistics
        {
            Timestamp = DateTimeOffset.UtcNow,
            TotalServices = healthResponse.Services.Count,
            HealthyServices = healthResponse.Services.Values.Count(s => s.Status == "healthy"),
            UnhealthyServices = healthResponse.Services.Values.Count(s => s.Status != "healthy")
        };

        // Collect metrics from each service
        foreach (var (serviceName, endpoint) in _serviceEndpoints)
        {
            try
            {
                var metrics = await GetServiceMetricsAsync(serviceName, endpoint, cancellationToken);
                if (metrics != null)
                {
                    stats.ServiceMetrics[serviceName] = metrics;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get metrics for service {Service}", serviceName);
            }
        }

        return stats;
    }

    private async Task<ServiceHealth> CheckServiceHealthAsync(
        string serviceName,
        string endpoint,
        CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);

            // Use Aspire default health endpoint
            var healthUrl = $"{endpoint}/health";
            var response = await client.GetAsync(healthUrl, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken);

                // Aspire health endpoint returns plain text "Healthy" or "Unhealthy"
                // Not JSON with a status property
                var status = content.Trim().ToLowerInvariant();

                return new ServiceHealth
                {
                    Status = status == "healthy" ? "healthy" : status == "unhealthy" ? "unhealthy" : "unknown",
                    Endpoint = endpoint
                };
            }

            return new ServiceHealth
            {
                Status = "unhealthy",
                Endpoint = endpoint,
                Error = $"HTTP {response.StatusCode}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Health check failed for service {Service}", serviceName);

            return new ServiceHealth
            {
                Status = "unhealthy",
                Endpoint = endpoint,
                Error = ex.Message
            };
        }
    }

    private async Task<object?> GetServiceMetricsAsync(
        string serviceName,
        string endpoint,
        CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);

            // Route to the appropriate metrics endpoint per service
            var metricsUrl = serviceName switch
            {
                "validator" => $"{endpoint}/api/metrics/",
                "peer" => $"{endpoint}/api/peers/stats",
                _ => null
            };

            if (metricsUrl == null)
                return null;

            var response = await client.GetAsync(metricsUrl, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = System.Text.Json.JsonDocument.Parse(content);
                return System.Text.Json.JsonSerializer.Deserialize<object>(doc.RootElement.GetRawText());
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get metrics for service");
            return null;
        }
    }
}
