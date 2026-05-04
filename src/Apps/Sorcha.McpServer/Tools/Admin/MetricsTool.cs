// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;

namespace Sorcha.McpServer.Tools.Admin;

/// <summary>
/// Admin tool for getting system metrics.
/// </summary>
[McpServerToolType]
public sealed class MetricsTool
{
    private readonly IMcpSessionService _sessionService;
    private readonly IMcpAuthorizationService _authService;
    private readonly IMcpErrorHandler _errorHandler;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<MetricsTool> _logger;
    private readonly string _apiGatewayEndpoint;

    public MetricsTool(
        IMcpSessionService sessionService,
        IMcpAuthorizationService authService,
        IMcpErrorHandler errorHandler,
        IServiceAvailabilityTracker availabilityTracker,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<MetricsTool> logger)
    {
        _sessionService = sessionService;
        _authService = authService;
        _errorHandler = errorHandler;
        _availabilityTracker = availabilityTracker;
        _httpClientFactory = httpClientFactory;
        _logger = logger;

        _apiGatewayEndpoint = configuration["ServiceClients:ApiGateway:Address"] ?? "http://localhost:80";
    }

    /// <summary>
    /// Gets system metrics for monitoring.
    /// </summary>
    /// <param name="service">Filter by service name (optional).</param>
    /// <param name="metricType">Type of metrics: All, Performance, Throughput, Errors (default: All).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>System metrics.</returns>
    [McpServerTool(Name = "sorcha_metrics")]
    [Description("Returns numeric performance, throughput, and error-rate metrics for one or all services, optionally filtered to a metric category (Performance, Throughput, Errors). Call this when you need quantitative trends to support capacity planning, latency analysis, or anomaly detection; prefer this over sorcha_health_check when you need rate-of-change or volumetric data rather than a binary up/down verdict, and call before sorcha_log_query so the log search can be narrowed by the time window where the metrics deviated.")]
    public async Task<MetricsResult> GetMetricsAsync(
        [Description("Filter by service name (e.g., Blueprint, Register, Wallet)")] string? service = null,
        [Description("Type of metrics: All, Performance, Throughput, Errors (default: All)")] string metricType = "All",
        CancellationToken cancellationToken = default)
    {
        // Authorization check
        if (!_authService.CanInvokeTool("sorcha_metrics"))
        {
            return new MetricsResult
            {
                Status = "Unauthorized",
                Message = "Access denied. This tool requires the sorcha:admin role.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // Validate metric type
        var validTypes = new[] { "All", "Performance", "Throughput", "Errors" };
        if (!validTypes.Contains(metricType, StringComparer.OrdinalIgnoreCase))
        {
            return new MetricsResult
            {
                Status = "Error",
                Message = "Invalid metric type. Must be All, Performance, Throughput, or Errors.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // Check service availability
        if (!_availabilityTracker.IsServiceAvailable("ApiGateway"))
        {
            return new MetricsResult
            {
                Status = "Unavailable",
                Message = "API Gateway is currently unavailable. Please try again later.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        _logger.LogInformation("Getting metrics. Service: {Service}, Type: {Type}", service ?? "all", metricType);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(30);

            // Build query string
            var queryParams = new List<string> { $"type={Uri.EscapeDataString(metricType)}" };

            if (!string.IsNullOrWhiteSpace(service))
                queryParams.Add($"service={Uri.EscapeDataString(service)}");

            var url = $"{_apiGatewayEndpoint.TrimEnd('/')}/api/admin/metrics?{string.Join("&", queryParams)}";

            var response = await client.GetAsync(url, cancellationToken);

            stopwatch.Stop();

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Metrics request failed: HTTP {StatusCode} - {Error}", response.StatusCode, errorContent);

                _availabilityTracker.RecordSuccess("ApiGateway");

                return new MetricsResult
                {
                    Status = "Error",
                    Message = $"Metrics request failed with status {(int)response.StatusCode}.",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

            _availabilityTracker.RecordSuccess("ApiGateway");

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<MetricsResponse>(responseContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (result == null)
            {
                return new MetricsResult
                {
                    Status = "Error",
                    Message = "Failed to parse metrics response.",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

            _logger.LogInformation(
                "Retrieved metrics for {Count} services in {ElapsedMs}ms",
                result.Services?.Count ?? 0, stopwatch.ElapsedMilliseconds);

            return new MetricsResult
            {
                Status = "Success",
                Message = $"Retrieved metrics for {result.Services?.Count ?? 0} service(s).",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                Services = result.Services?.Select(s => new ServiceMetrics
                {
                    ServiceName = s.ServiceName ?? "Unknown",
                    RequestsPerSecond = s.RequestsPerSecond,
                    AverageLatencyMs = s.AverageLatencyMs,
                    P95LatencyMs = s.P95LatencyMs,
                    P99LatencyMs = s.P99LatencyMs,
                    ErrorRate = s.ErrorRate,
                    ActiveConnections = s.ActiveConnections,
                    MemoryUsageMb = s.MemoryUsageMb,
                    CpuUsagePercent = s.CpuUsagePercent
                }).ToList() ?? [],
                SystemMetrics = result.System != null ? new SystemMetricsInfo
                {
                    TotalRequestsPerSecond = result.System.TotalRequestsPerSecond,
                    TotalActiveConnections = result.System.TotalActiveConnections,
                    OverallErrorRate = result.System.OverallErrorRate,
                    UptimeHours = result.System.UptimeHours
                } : null
            };
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("ApiGateway");

            return new MetricsResult
            {
                Status = "Timeout",
                Message = "Metrics request timed out.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("ApiGateway", ex);

            return new MetricsResult
            {
                Status = "Error",
                Message = $"Failed to connect to API Gateway: {ex.Message}",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("ApiGateway", ex);

            _logger.LogError(ex, "Unexpected error getting metrics");

            return new MetricsResult
            {
                Status = "Error",
                Message = "An unexpected error occurred while getting metrics.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
    }

    // Internal response models
    private sealed class MetricsResponse
    {
        public List<ServiceMetricsDto>? Services { get; set; }
        public SystemMetricsDto? System { get; set; }
    }

    private sealed class ServiceMetricsDto
    {
        public string? ServiceName { get; set; }
        public double RequestsPerSecond { get; set; }
        public double AverageLatencyMs { get; set; }
        public double P95LatencyMs { get; set; }
        public double P99LatencyMs { get; set; }
        public double ErrorRate { get; set; }
        public int ActiveConnections { get; set; }
        public double MemoryUsageMb { get; set; }
        public double CpuUsagePercent { get; set; }
    }

    private sealed class SystemMetricsDto
    {
        public double TotalRequestsPerSecond { get; set; }
        public int TotalActiveConnections { get; set; }
        public double OverallErrorRate { get; set; }
        public double UptimeHours { get; set; }
    }
}

/// <summary>
/// Result of getting metrics.
/// </summary>
public sealed record MetricsResult
{
    /// <summary>
    /// Operation status: Success, Error, Unavailable, Timeout, or Unauthorized.
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// Human-readable message about the operation result.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// When the operation was performed.
    /// </summary>
    public required DateTimeOffset CheckedAt { get; init; }

    /// <summary>
    /// Response time in milliseconds.
    /// </summary>
    public int ResponseTimeMs { get; init; }

    /// <summary>
    /// Metrics per service.
    /// </summary>
    public IReadOnlyList<ServiceMetrics> Services { get; init; } = [];

    /// <summary>
    /// System-wide metrics.
    /// </summary>
    public SystemMetricsInfo? SystemMetrics { get; init; }
}

/// <summary>
/// Metrics for a single service.
/// </summary>
public sealed record ServiceMetrics
{
    /// <summary>
    /// Service name.
    /// </summary>
    public required string ServiceName { get; init; }

    /// <summary>
    /// Requests per second.
    /// </summary>
    public double RequestsPerSecond { get; init; }

    /// <summary>
    /// Average request latency in milliseconds.
    /// </summary>
    public double AverageLatencyMs { get; init; }

    /// <summary>
    /// 95th percentile latency in milliseconds.
    /// </summary>
    public double P95LatencyMs { get; init; }

    /// <summary>
    /// 99th percentile latency in milliseconds.
    /// </summary>
    public double P99LatencyMs { get; init; }

    /// <summary>
    /// Error rate (0-1).
    /// </summary>
    public double ErrorRate { get; init; }

    /// <summary>
    /// Number of active connections.
    /// </summary>
    public int ActiveConnections { get; init; }

    /// <summary>
    /// Memory usage in megabytes.
    /// </summary>
    public double MemoryUsageMb { get; init; }

    /// <summary>
    /// CPU usage percentage.
    /// </summary>
    public double CpuUsagePercent { get; init; }
}

/// <summary>
/// System-wide metrics.
/// </summary>
public sealed record SystemMetricsInfo
{
    /// <summary>
    /// Total requests per second across all services.
    /// </summary>
    public double TotalRequestsPerSecond { get; init; }

    /// <summary>
    /// Total active connections across all services.
    /// </summary>
    public int TotalActiveConnections { get; init; }

    /// <summary>
    /// Overall error rate across all services.
    /// </summary>
    public double OverallErrorRate { get; init; }

    /// <summary>
    /// System uptime in hours.
    /// </summary>
    public double UptimeHours { get; init; }
}
