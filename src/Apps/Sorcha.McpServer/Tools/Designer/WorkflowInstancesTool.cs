// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.ServiceClients.Blueprint;

namespace Sorcha.McpServer.Tools.Designer;

/// <summary>
/// Designer tool for listing workflow instances. Reads via the typed
/// <see cref="IBlueprintServiceClient"/> (spec 139 US4) so the caller's bearer is forwarded
/// and the route is contract-pinned, not hand-rolled.
/// </summary>
[McpServerToolType]
public sealed class WorkflowInstancesTool
{
    private readonly IMcpAuthorizationService _authService;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly IBlueprintServiceClient _blueprintClient;
    private readonly ILogger<WorkflowInstancesTool> _logger;

    public WorkflowInstancesTool(
        IMcpAuthorizationService authService,
        IServiceAvailabilityTracker availabilityTracker,
        IBlueprintServiceClient blueprintClient,
        ILogger<WorkflowInstancesTool> logger)
    {
        _authService = authService;
        _availabilityTracker = availabilityTracker;
        _blueprintClient = blueprintClient;
        _logger = logger;
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
    [Description("Returns a paginated list of workflow instances — concrete executions of a blueprint, each with their own state, current action, and participants — filterable by blueprint ID and lifecycle status (Active, Completed, Suspended). Call this when monitoring running workflows, locating a specific instance for debugging, or auditing recently completed workflows for a given blueprint; use sorcha_blueprint_list instead when you need blueprint definitions rather than running executions, and prefer this over sorcha_blueprint_get when the question is about runtime activity rather than design-time structure.")]
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

            // Typed client forwards the caller's bearer and pins the route (GET api/workflows).
            var responseContent = await _blueprintClient.GetWorkflowInstancesAsync(
                string.Join("&", queryParams), cancellationToken);

            stopwatch.Stop();

            if (string.IsNullOrWhiteSpace(responseContent))
            {
                _availabilityTracker.RecordSuccess("Blueprint");

                return new WorkflowInstancesResult
                {
                    Status = "Error",
                    Message = "Failed to list workflow instances.",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

            // Record success
            _availabilityTracker.RecordSuccess("Blueprint");

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
