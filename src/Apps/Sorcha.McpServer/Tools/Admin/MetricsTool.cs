// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Sorcha.McpServer.Services;

namespace Sorcha.McpServer.Tools.Admin;

/// <summary>
/// Admin tool for getting system metrics.
/// </summary>
/// <remarks>
/// Spec 139 US4 (LOCKED DECISION): the platform exposes no metrics-query API yet, so this tool
/// is marked <c>NotSupported</c>. It keeps its admin authorization gate and stays advertised so
/// the catalogue/manifest is stable, but it fails honestly instead of calling a phantom endpoint.
/// Wire it up when an observability/metrics surface lands.
/// </remarks>
[McpServerToolType]
public sealed class MetricsTool
{
    private readonly IMcpAuthorizationService _authService;
    private readonly ILogger<MetricsTool> _logger;

    public MetricsTool(
        IMcpAuthorizationService authService,
        ILogger<MetricsTool> logger)
    {
        _authService = authService;
        _logger = logger;
    }

    /// <summary>
    /// Gets system metrics for monitoring. Currently not supported (no backend API).
    /// </summary>
    /// <param name="service">Filter by service name (optional).</param>
    /// <param name="metricType">Type of metrics: All, Performance, Throughput, Errors (default: All).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A NotSupported result until a metrics-query API exists.</returns>
    [McpServerTool(Name = "sorcha_metrics")]
    [Description("Returns numeric performance, throughput, and error-rate metrics for one or all services, optionally filtered to a metric category (Performance, Throughput, Errors). NOTE: the platform exposes no metrics-query API yet, so this tool currently returns a NotSupported result; it will be wired up when an observability/metrics surface lands. Call this when you need quantitative trends to support capacity planning, latency analysis, or anomaly detection; prefer this over sorcha_health_check when you need rate-of-change or volumetric data rather than a binary up/down verdict.")]
    public Task<MetricsResult> GetMetricsAsync(
        [Description("Filter by service name (e.g., Blueprint, Register, Wallet)")] string? service = null,
        [Description("Type of metrics: All, Performance, Throughput, Errors (default: All)")] string metricType = "All",
        CancellationToken cancellationToken = default)
    {
        // Authorization check
        if (!_authService.CanInvokeTool("sorcha_metrics"))
        {
            return Task.FromResult(new MetricsResult
            {
                Status = "Unauthorized",
                Message = "Access denied. This tool requires the sorcha:admin role.",
                CheckedAt = DateTimeOffset.UtcNow
            });
        }

        _logger.LogInformation("sorcha_metrics invoked but no metrics-query API is available (NotSupported).");

        return Task.FromResult(new MetricsResult
        {
            Status = "NotSupported",
            Message = "The platform exposes no metrics-query API yet; this tool will be wired up when an observability/metrics surface lands.",
            CheckedAt = DateTimeOffset.UtcNow
        });
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
