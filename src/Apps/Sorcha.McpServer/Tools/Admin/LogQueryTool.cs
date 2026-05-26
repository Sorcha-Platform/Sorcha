// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Sorcha.McpServer.Services;

namespace Sorcha.McpServer.Tools.Admin;

/// <summary>
/// Admin tool for querying application logs.
/// </summary>
/// <remarks>
/// Spec 139 US4 (LOCKED DECISION): the platform exposes no log-query API yet, so this tool
/// is marked <c>NotSupported</c>. It keeps its admin authorization gate and stays advertised so
/// the catalogue/manifest is stable, but it fails honestly instead of calling a phantom endpoint.
/// Wire it up when an observability/log surface lands.
/// </remarks>
[McpServerToolType]
public sealed class LogQueryTool
{
    private readonly IMcpAuthorizationService _authService;
    private readonly ILogger<LogQueryTool> _logger;

    public LogQueryTool(
        IMcpAuthorizationService authService,
        ILogger<LogQueryTool> logger)
    {
        _authService = authService;
        _logger = logger;
    }

    /// <summary>
    /// Queries application logs with filtering options. Currently not supported (no backend API).
    /// </summary>
    /// <param name="service">Filter by service name (optional).</param>
    /// <param name="level">Filter by log level: Debug, Info, Warning, Error (optional).</param>
    /// <param name="search">Search text in log messages (optional).</param>
    /// <param name="startTime">Start time for log range (ISO 8601, optional).</param>
    /// <param name="endTime">End time for log range (ISO 8601, optional).</param>
    /// <param name="limit">Maximum number of log entries (default: 100, max: 1000).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A NotSupported result until a log-query API exists.</returns>
    [McpServerTool(Name = "sorcha_log_query")]
    [Description("Returns application log entries from one or all Sorcha services, filtered by service name, log level, time window, or free-text search. NOTE: the platform exposes no log-query API yet, so this tool currently returns a NotSupported result; it will be wired up when an observability/log surface lands. Call this when diagnosing a specific error or tracing a request through the platform; prefer this over sorcha_audit_query when the question is about service-level diagnostic output rather than user or admin behaviour.")]
    public Task<LogQueryResult> QueryLogsAsync(
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
            return Task.FromResult(new LogQueryResult
            {
                Status = "Unauthorized",
                Message = "Access denied. This tool requires the sorcha:admin role.",
                CheckedAt = DateTimeOffset.UtcNow
            });
        }

        _logger.LogInformation("sorcha_log_query invoked but no log-query API is available (NotSupported).");

        return Task.FromResult(new LogQueryResult
        {
            Status = "NotSupported",
            Message = "The platform exposes no log-query API yet; this tool will be wired up when an observability/log surface lands.",
            CheckedAt = DateTimeOffset.UtcNow
        });
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
