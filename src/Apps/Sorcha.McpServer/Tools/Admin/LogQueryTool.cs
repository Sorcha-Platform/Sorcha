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
/// Admin tool for querying application logs.
/// </summary>
[McpServerToolType]
public sealed class LogQueryTool
{
    private readonly IMcpSessionService _sessionService;
    private readonly IMcpAuthorizationService _authService;
    private readonly IMcpErrorHandler _errorHandler;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<LogQueryTool> _logger;
    private readonly string _apiGatewayEndpoint;

    public LogQueryTool(
        IMcpSessionService sessionService,
        IMcpAuthorizationService authService,
        IMcpErrorHandler errorHandler,
        IServiceAvailabilityTracker availabilityTracker,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<LogQueryTool> logger)
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
    /// Queries application logs with filtering options.
    /// </summary>
    /// <param name="service">Filter by service name (optional).</param>
    /// <param name="level">Filter by log level: Debug, Info, Warning, Error (optional).</param>
    /// <param name="search">Search text in log messages (optional).</param>
    /// <param name="startTime">Start time for log range (ISO 8601, optional).</param>
    /// <param name="endTime">End time for log range (ISO 8601, optional).</param>
    /// <param name="limit">Maximum number of log entries (default: 100, max: 1000).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Log query results.</returns>
    [McpServerTool(Name = "sorcha_log_query")]
    [Description("Returns application log entries from one or all Sorcha services, filtered by service name, log level, time window, or free-text search, ordered by timestamp. Call this when diagnosing a specific error or tracing a request through the platform after sorcha_health_check has flagged a degraded service; prefer this over sorcha_audit_query when the question is about service-level diagnostic output (exceptions, warnings, request traces) rather than user or admin behaviour, and prefer it over sorcha_metrics when you need the textual evidence behind an anomalous metric rather than the metric itself.")]
    public async Task<LogQueryResult> QueryLogsAsync(
        [Description("Filter by service name (e.g., Blueprint, Register, Wallet)")] string? service = null,
        [Description("Filter by log level: Debug, Info, Warning, Error")] string? level = null,
        [Description("Search text in log messages")] string? search = null,
        [Description("Start time for log range (ISO 8601 format)")] string? startTime = null,
        [Description("End time for log range (ISO 8601 format)")] string? endTime = null,
        [Description("Maximum number of log entries (default: 100, max: 1000)")] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        // Authorization check
        if (!_authService.CanInvokeTool("sorcha_log_query"))
        {
            return new LogQueryResult
            {
                Status = "Unauthorized",
                Message = "Access denied. This tool requires the sorcha:admin role.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // Validate limit
        if (limit < 1) limit = 100;
        if (limit > 1000) limit = 1000;

        // Validate log level if provided
        if (!string.IsNullOrWhiteSpace(level))
        {
            var validLevels = new[] { "Debug", "Info", "Warning", "Error" };
            if (!validLevels.Contains(level, StringComparer.OrdinalIgnoreCase))
            {
                return new LogQueryResult
                {
                    Status = "Error",
                    Message = "Invalid log level. Must be Debug, Info, Warning, or Error.",
                    CheckedAt = DateTimeOffset.UtcNow
                };
            }
        }

        // Check service availability
        if (!_availabilityTracker.IsServiceAvailable("ApiGateway"))
        {
            return new LogQueryResult
            {
                Status = "Unavailable",
                Message = "API Gateway is currently unavailable. Please try again later.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        _logger.LogInformation(
            "Querying logs. Service: {Service}, Level: {Level}, Search: {Search}",
            service ?? "all", level ?? "all", search ?? "none");

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(30);

            // Build query string
            var queryParams = new List<string> { $"limit={limit}" };

            if (!string.IsNullOrWhiteSpace(service))
                queryParams.Add($"service={Uri.EscapeDataString(service)}");

            if (!string.IsNullOrWhiteSpace(level))
                queryParams.Add($"level={Uri.EscapeDataString(level)}");

            if (!string.IsNullOrWhiteSpace(search))
                queryParams.Add($"search={Uri.EscapeDataString(search)}");

            if (!string.IsNullOrWhiteSpace(startTime))
                queryParams.Add($"startTime={Uri.EscapeDataString(startTime)}");

            if (!string.IsNullOrWhiteSpace(endTime))
                queryParams.Add($"endTime={Uri.EscapeDataString(endTime)}");

            var url = $"{_apiGatewayEndpoint.TrimEnd('/')}/api/admin/logs?{string.Join("&", queryParams)}";

            var response = await client.GetAsync(url, cancellationToken);

            stopwatch.Stop();

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Log query failed: HTTP {StatusCode} - {Error}", response.StatusCode, errorContent);

                _availabilityTracker.RecordSuccess("ApiGateway");

                return new LogQueryResult
                {
                    Status = "Error",
                    Message = $"Log query failed with status {(int)response.StatusCode}.",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

            _availabilityTracker.RecordSuccess("ApiGateway");

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<LogQueryResponse>(responseContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (result == null)
            {
                return new LogQueryResult
                {
                    Status = "Error",
                    Message = "Failed to parse log query response.",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

            _logger.LogInformation(
                "Retrieved {Count} log entries in {ElapsedMs}ms",
                result.Entries?.Count ?? 0, stopwatch.ElapsedMilliseconds);

            return new LogQueryResult
            {
                Status = "Success",
                Message = $"Retrieved {result.Entries?.Count ?? 0} log entries.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                Entries = result.Entries?.Select(e => new LogEntry
                {
                    Timestamp = e.Timestamp,
                    Service = e.Service ?? "Unknown",
                    Level = e.Level ?? "Info",
                    Message = e.Message ?? "",
                    Exception = e.Exception,
                    CorrelationId = e.CorrelationId
                }).ToList() ?? [],
                TotalCount = result.TotalCount
            };
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("ApiGateway");

            return new LogQueryResult
            {
                Status = "Timeout",
                Message = "Log query request timed out.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("ApiGateway", ex);

            return new LogQueryResult
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

            _logger.LogError(ex, "Unexpected error querying logs");

            return new LogQueryResult
            {
                Status = "Error",
                Message = "An unexpected error occurred while querying logs.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
    }

    // Internal response models
    private sealed class LogQueryResponse
    {
        public List<LogEntryDto>? Entries { get; set; }
        public int TotalCount { get; set; }
    }

    private sealed class LogEntryDto
    {
        public DateTimeOffset? Timestamp { get; set; }
        public string? Service { get; set; }
        public string? Level { get; set; }
        public string? Message { get; set; }
        public string? Exception { get; set; }
        public string? CorrelationId { get; set; }
    }
}

/// <summary>
/// Result of querying logs.
/// </summary>
public sealed record LogQueryResult
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
    /// List of log entries.
    /// </summary>
    public IReadOnlyList<LogEntry> Entries { get; init; } = [];

    /// <summary>
    /// Total number of matching log entries.
    /// </summary>
    public int TotalCount { get; init; }
}

/// <summary>
/// A log entry.
/// </summary>
public sealed record LogEntry
{
    /// <summary>
    /// Timestamp of the log entry.
    /// </summary>
    public DateTimeOffset? Timestamp { get; init; }

    /// <summary>
    /// Service that generated the log.
    /// </summary>
    public required string Service { get; init; }

    /// <summary>
    /// Log level: Debug, Info, Warning, Error.
    /// </summary>
    public required string Level { get; init; }

    /// <summary>
    /// Log message.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Exception details if present.
    /// </summary>
    public string? Exception { get; init; }

    /// <summary>
    /// Correlation ID for request tracing.
    /// </summary>
    public string? CorrelationId { get; init; }
}
