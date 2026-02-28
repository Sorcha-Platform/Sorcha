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

namespace Sorcha.McpServer.Tools.Designer;

/// <summary>
/// Designer tool for listing workflow instances.
/// </summary>
[McpServerToolType]
public sealed class WorkflowInstancesTool
{
    private readonly IMcpSessionService _sessionService;
    private readonly IMcpAuthorizationService _authService;
    private readonly IMcpErrorHandler _errorHandler;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WorkflowInstancesTool> _logger;
    private readonly string _blueprintServiceEndpoint;

    public WorkflowInstancesTool(
        IMcpSessionService sessionService,
        IMcpAuthorizationService authService,
        IMcpErrorHandler errorHandler,
        IServiceAvailabilityTracker availabilityTracker,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<WorkflowInstancesTool> logger)
    {
        _sessionService = sessionService;
        _authService = authService;
        _errorHandler = errorHandler;
        _availabilityTracker = availabilityTracker;
        _httpClientFactory = httpClientFactory;
        _logger = logger;

        _blueprintServiceEndpoint = configuration["ServiceClients:BlueprintService:Address"] ?? "http://localhost:5000";
    }

    /// <summary>
    /// Lists workflow instances for a blueprint.
    /// </summary>
    /// <param name="blueprintId">The blueprint ID to list instances for (optional - lists all if not specified).</param>
    /// <param name="status">Filter by status: Active, Completed, or Suspended (optional).</param>
    /// <param name="page">Page number (1-based, default: 1).</param>
    /// <param name="pageSize">Items per page (default: 20, max: 100).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of workflow instances.</returns>
    [McpServerTool(Name = "sorcha_workflow_instances")]
    [Description("List workflow instances (running or completed workflows). Filter by blueprint ID and status. Useful for monitoring workflow execution and debugging issues.")]
    public async Task<WorkflowInstancesResult> ListWorkflowInstancesAsync(
        [Description("Blueprint ID to filter instances (optional)")] string? blueprintId = null,
        [Description("Status filter: Active, Completed, or Suspended (optional)")] string? status = null,
        [Description("Page number (1-based, default: 1)")] int page = 1,
        [Description("Items per page (default: 20, max: 100)")] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        // Authorization check
        if (!_authService.CanInvokeTool("sorcha_workflow_instances"))
        {
            return new WorkflowInstancesResult
            {
                Status = "Unauthorized",
                Message = "Access denied. This tool requires the sorcha:designer role.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // Validate pagination
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 100) pageSize = 100;

        // Validate status if provided
        if (!string.IsNullOrWhiteSpace(status))
        {
            var validStatuses = new[] { "Active", "Completed", "Suspended" };
            if (!validStatuses.Contains(status, StringComparer.OrdinalIgnoreCase))
            {
                return new WorkflowInstancesResult
                {
                    Status = "Error",
                    Message = "Invalid status. Must be Active, Completed, or Suspended.",
                    CheckedAt = DateTimeOffset.UtcNow
                };
            }
        }

        // Check service availability
        if (!_availabilityTracker.IsServiceAvailable("Blueprint"))
        {
            return new WorkflowInstancesResult
            {
                Status = "Unavailable",
                Message = "Blueprint service is currently unavailable. Please try again later.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        _logger.LogInformation(
            "Listing workflow instances. Blueprint: {BlueprintId}, Status: {Status}, Page: {Page}",
            blueprintId ?? "all", status ?? "all", page);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(30);

            // Build query string
            var queryParams = new List<string>
            {
                $"page={page}",
                $"pageSize={pageSize}"
            };

            if (!string.IsNullOrWhiteSpace(blueprintId))
            {
                queryParams.Add($"blueprintId={Uri.EscapeDataString(blueprintId)}");
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                queryParams.Add($"status={Uri.EscapeDataString(status)}");
            }

            var url = $"{_blueprintServiceEndpoint.TrimEnd('/')}/api/workflows?{string.Join("&", queryParams)}";

            var response = await client.GetAsync(url, cancellationToken);

            stopwatch.Stop();

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Workflow instances request failed: HTTP {StatusCode} - {Error}", response.StatusCode, errorContent);

                _availabilityTracker.RecordSuccess("Blueprint");

                try
                {
                    var errorResponse = JsonSerializer.Deserialize<ErrorResponse>(errorContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    return new WorkflowInstancesResult
                    {
                        Status = "Error",
                        Message = errorResponse?.Error ?? "Failed to list workflow instances.",
                        CheckedAt = DateTimeOffset.UtcNow,
                        ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                    };
                }
                catch (JsonException)
                {
                    return new WorkflowInstancesResult
                    {
                        Status = "Error",
                        Message = $"Request failed with status {(int)response.StatusCode}.",
                        CheckedAt = DateTimeOffset.UtcNow,
                        ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                    };
                }
            }

            // Record success
            _availabilityTracker.RecordSuccess("Blueprint");

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<WorkflowListResponse>(responseContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (result == null)
            {
                return new WorkflowInstancesResult
                {
                    Status = "Error",
                    Message = "Failed to parse workflow instances response.",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

            _logger.LogInformation(
                "Retrieved {Count} workflow instances in {ElapsedMs}ms",
                result.Items?.Count ?? 0, stopwatch.ElapsedMilliseconds);

            return new WorkflowInstancesResult
            {
                Status = "Success",
                Message = $"Retrieved {result.Items?.Count ?? 0} workflow instance(s).",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                Instances = result.Items?.Select(i => new WorkflowInstanceInfo
                {
                    InstanceId = i.InstanceId ?? "",
                    BlueprintId = i.BlueprintId ?? "",
                    BlueprintTitle = i.BlueprintTitle,
                    Status = i.Status ?? "Unknown",
                    CurrentActionId = i.CurrentActionId,
                    CurrentActionTitle = i.CurrentActionTitle,
                    StartedAt = i.StartedAt,
                    CompletedAt = i.CompletedAt,
                    LastActivityAt = i.LastActivityAt
                }).ToList() ?? [],
                TotalCount = result.TotalCount,
                Page = result.Page,
                PageSize = result.PageSize,
                TotalPages = result.TotalPages
            };
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("Blueprint");

            _logger.LogWarning("Workflow instances request timed out");

            return new WorkflowInstancesResult
            {
                Status = "Timeout",
                Message = "Request to blueprint service timed out.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("Blueprint", ex);

            _logger.LogWarning(ex, "Failed to list workflow instances");

            return new WorkflowInstancesResult
            {
                Status = "Error",
                Message = $"Failed to connect to blueprint service: {ex.Message}",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("Blueprint", ex);

            _logger.LogError(ex, "Unexpected error listing workflow instances");

            return new WorkflowInstancesResult
            {
                Status = "Error",
                Message = "An unexpected error occurred while listing workflow instances.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
    }

    // Internal response models
    private sealed class WorkflowListResponse
    {
        public List<WorkflowItemDto>? Items { get; set; }
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages { get; set; }
    }

    private sealed class WorkflowItemDto
    {
        public string? InstanceId { get; set; }
        public string? BlueprintId { get; set; }
        public string? BlueprintTitle { get; set; }
        public string? Status { get; set; }
        public int? CurrentActionId { get; set; }
        public string? CurrentActionTitle { get; set; }
        public DateTimeOffset? StartedAt { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }
        public DateTimeOffset? LastActivityAt { get; set; }
    }

    private sealed class ErrorResponse
    {
        public string? Error { get; set; }
    }
}

/// <summary>
/// Result of listing workflow instances.
/// </summary>
public sealed record WorkflowInstancesResult
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
    /// List of workflow instances.
    /// </summary>
    public IReadOnlyList<WorkflowInstanceInfo> Instances { get; init; } = [];

    /// <summary>
    /// Total number of instances matching the filter.
    /// </summary>
    public int TotalCount { get; init; }

    /// <summary>
    /// Current page number.
    /// </summary>
    public int Page { get; init; }

    /// <summary>
    /// Items per page.
    /// </summary>
    public int PageSize { get; init; }

    /// <summary>
    /// Total number of pages.
    /// </summary>
    public int TotalPages { get; init; }
}

/// <summary>
/// Information about a workflow instance.
/// </summary>
public sealed record WorkflowInstanceInfo
{
    /// <summary>
    /// The workflow instance ID.
    /// </summary>
    public required string InstanceId { get; init; }

    /// <summary>
    /// The blueprint ID this instance is based on.
    /// </summary>
    public required string BlueprintId { get; init; }

    /// <summary>
    /// The blueprint title.
    /// </summary>
    public string? BlueprintTitle { get; init; }

    /// <summary>
    /// Current status: Active, Completed, or Suspended.
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// The current action ID (sequence number).
    /// </summary>
    public int? CurrentActionId { get; init; }

    /// <summary>
    /// The current action title.
    /// </summary>
    public string? CurrentActionTitle { get; init; }

    /// <summary>
    /// When the workflow was started.
    /// </summary>
    public DateTimeOffset? StartedAt { get; init; }

    /// <summary>
    /// When the workflow was completed (if completed).
    /// </summary>
    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>
    /// When the last activity occurred.
    /// </summary>
    public DateTimeOffset? LastActivityAt { get; init; }
}
