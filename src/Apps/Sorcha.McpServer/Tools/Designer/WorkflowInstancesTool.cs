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
    /// Lists workflow instances the caller participates in.
    /// </summary>
    /// <param name="status">Filter by lifecycle status (optional).</param>
    /// <param name="page">Page number (1-based, default: 1).</param>
    /// <param name="pageSize">Items per page (default: 20, max: 100).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of workflow instances.</returns>
    [McpServerTool(Name = "sorcha_workflow_instances")]
    [Description("Returns a paginated list of workflow instances — concrete executions of a blueprint, each with their own lifecycle state and current action(s) — for every wallet the caller controls, optionally filtered by lifecycle status (Active, Completed, Rejected, TimedOut, Cancelled). Call this when monitoring running workflows or locating a specific instance for debugging; use sorcha_blueprint_list instead when you need blueprint definitions rather than running executions, and prefer this over sorcha_blueprint_get when the question is about runtime activity rather than design-time structure. NOTE: the underlying endpoint (GET /api/instances/) has no blueprintId filter — a caller only ever sees instances they participate in, spanning every blueprint, and results cannot be narrowed to one blueprint server-side.")]
    public async Task<WorkflowInstancesResult> ListWorkflowInstancesAsync(
        [Description("Status filter: Active, Completed, Rejected, TimedOut, or Cancelled (optional)")] string? status = null,
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

        // Validate status if provided — these are Sorcha.Blueprint.Service.Models.InstanceState's
        // actual members, not the "Suspended" placeholder this tool previously (and incorrectly)
        // advertised.
        if (!string.IsNullOrWhiteSpace(status))
        {
            var validStatuses = new[] { "Active", "Completed", "Rejected", "TimedOut", "Cancelled" };
            if (!validStatuses.Contains(status, StringComparer.OrdinalIgnoreCase))
            {
                return new WorkflowInstancesResult
                {
                    Status = "Error",
                    Message = "Invalid status. Must be Active, Completed, Rejected, TimedOut, or Cancelled.",
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
            "Listing workflow instances. Status: {Status}, Page: {Page}",
            status ?? "all", page);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Build query string. No blueprintId param — the endpoint does not support one.
            var queryParams = new List<string>
            {
                $"page={page}",
                $"pageSize={pageSize}"
            };

            if (!string.IsNullOrWhiteSpace(status))
            {
                queryParams.Add($"status={Uri.EscapeDataString(status)}");
            }

            // Typed client forwards the caller's bearer and pins the route (GET api/instances/).
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
                    InstanceId = i.Id ?? "",
                    BlueprintId = i.BlueprintId ?? "",
                    Status = ResolveState(i.State),
                    CurrentActionId = i.CurrentActionIds?.Count > 0 ? i.CurrentActionIds[0] : null,
                    StartedAt = i.CreatedAt,
                    CompletedAt = i.CompletedAt,
                    LastActivityAt = i.UpdatedAt
                }).ToList() ?? [],
                TotalCount = result.TotalCount,
                Page = result.PageNumber,
                PageSize = result.PageSize,
                // The endpoint returns no totalPages field — derive it rather than report a
                // permanently-zero value the wire body never actually carries.
                TotalPages = result.PageSize > 0
                    ? (int)Math.Ceiling(result.TotalCount / (double)result.PageSize)
                    : 0
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

    // Sorcha.Blueprint.Service.Models.InstanceState's ordinal order — mirrored here because
    // enums serialize as their underlying int by default (no JsonStringEnumConverter is registered
    // for this type in Blueprint Service) and McpServer cannot reference the service's own model
    // project. Index MUST track that enum's declaration order.
    private static readonly string[] InstanceStateNames =
        ["Active", "Completed", "Rejected", "TimedOut", "Cancelled"];

    private static string ResolveState(JsonElement? state)
    {
        if (state is not { } value)
        {
            return "Unknown";
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? "Unknown",
            JsonValueKind.Number when value.TryGetInt32(out var ordinal)
                && ordinal >= 0 && ordinal < InstanceStateNames.Length => InstanceStateNames[ordinal],
            _ => "Unknown"
        };
    }

    // Internal response models — mirror GET /api/instances/ (Sorcha.Blueprint.Service.Models.Instance
    // items, "pageNumber" not "page", no totalPages, no blueprint/action title).
    private sealed class WorkflowListResponse
    {
        public List<WorkflowItemDto>? Items { get; set; }
        public int TotalCount { get; set; }
        public int PageNumber { get; set; }
        public int PageSize { get; set; }
    }

    private sealed class WorkflowItemDto
    {
        public string? Id { get; set; }
        public string? BlueprintId { get; set; }
        public JsonElement? State { get; set; }
        public List<int>? CurrentActionIds { get; set; }
        public DateTimeOffset? CreatedAt { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }
        public DateTimeOffset? UpdatedAt { get; set; }
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
/// Information about a workflow instance, mirroring the fields actually present on
/// <c>GET /api/instances/</c> items (no blueprint/action title — see sorcha_blueprint_get for that).
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
    /// Current lifecycle status: Active, Completed, Rejected, TimedOut, or Cancelled.
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// The current action ID (sequence number) awaiting execution, if any. Only the first of a
    /// possible parallel-branch set is surfaced here.
    /// </summary>
    public int? CurrentActionId { get; init; }

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
