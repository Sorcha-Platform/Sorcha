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

namespace Sorcha.McpServer.Tools.Participant;

/// <summary>
/// Participant tool for listing pending actions in the user's inbox. Reads via the typed
/// <see cref="IBlueprintServiceClient"/> (spec 139 US4) so the caller's bearer is forwarded
/// and the route is contract-pinned, not hand-rolled.
/// </summary>
[McpServerToolType]
public sealed class InboxListTool
{
    private readonly IMcpAuthorizationService _authService;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly IBlueprintServiceClient _blueprintClient;
    private readonly ILogger<InboxListTool> _logger;

    public InboxListTool(
        IMcpAuthorizationService authService,
        IServiceAvailabilityTracker availabilityTracker,
        IBlueprintServiceClient blueprintClient,
        ILogger<InboxListTool> logger)
    {
        _authService = authService;
        _availabilityTracker = availabilityTracker;
        _blueprintClient = blueprintClient;
        _logger = logger;
    }

    /// <summary>
    /// Lists pending actions waiting for the current user.
    /// </summary>
    /// <param name="status">Filter by status: Pending, InProgress, or all (optional).</param>
    /// <param name="page">Page number (1-based, default: 1).</param>
    /// <param name="pageSize">Items per page (default: 20, max: 100).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of pending actions.</returns>
    [McpServerTool(Name = "sorcha_inbox_list")]
    [Description("List the workflow actions currently assigned to the authenticated participant, optionally filtered by status and paginated. Returns one row per outstanding action with its instance id, workflow context, and assignment state — the entry point an agent should use to discover what work is waiting. Call this when an agent first wakes up on behalf of a participant and needs to know what to act on; use sorcha_action_details rather than this tool when you already have an actionInstanceId and need its schema, and prefer sorcha_workflow_status when investigating a specific workflow instance rather than a participant's whole queue.")]
    public async Task<InboxListResult> ListInboxAsync(
        [Description("Status filter: Pending, InProgress, or leave empty for all")] string? status = null,
        [Description("Page number (1-based, default: 1)")] int page = 1,
        [Description("Items per page (default: 20, max: 100)")] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        // Authorization check
        if (!_authService.CanInvokeTool("sorcha_inbox_list"))
        {
            return new InboxListResult
            {
                Status = "Unauthorized",
                Message = "Access denied. This tool requires the sorcha:participant role.",
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
            var validStatuses = new[] { "Pending", "InProgress" };
            if (!validStatuses.Contains(status, StringComparer.OrdinalIgnoreCase))
            {
                return new InboxListResult
                {
                    Status = "Error",
                    Message = "Invalid status. Must be Pending or InProgress.",
                    CheckedAt = DateTimeOffset.UtcNow
                };
            }
        }

        // Check service availability
        if (!_availabilityTracker.IsServiceAvailable("Blueprint"))
        {
            return new InboxListResult
            {
                Status = "Unavailable",
                Message = "Blueprint service is currently unavailable. Please try again later.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        _logger.LogInformation("Listing inbox items. Status: {Status}, Page: {Page}", status ?? "all", page);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Build query string
            var queryParams = new List<string>
            {
                $"page={page}",
                $"pageSize={pageSize}"
            };

            if (!string.IsNullOrWhiteSpace(status))
            {
                queryParams.Add($"status={Uri.EscapeDataString(status)}");
            }

            // Typed client forwards the caller's bearer and pins the route (GET api/inbox).
            var responseContent = await _blueprintClient.GetInboxAsync(
                string.Join("&", queryParams), cancellationToken);

            stopwatch.Stop();

            if (string.IsNullOrWhiteSpace(responseContent))
            {
                _availabilityTracker.RecordSuccess("Blueprint");

                return new InboxListResult
                {
                    Status = "Error",
                    Message = "Failed to retrieve inbox items.",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

            // Record success
            _availabilityTracker.RecordSuccess("Blueprint");

            var result = JsonSerializer.Deserialize<InboxResponse>(responseContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (result == null)
            {
                return new InboxListResult
                {
                    Status = "Error",
                    Message = "Failed to parse inbox response.",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

            _logger.LogInformation(
                "Retrieved {Count} inbox items in {ElapsedMs}ms",
                result.Items?.Count ?? 0, stopwatch.ElapsedMilliseconds);

            return new InboxListResult
            {
                Status = "Success",
                Message = $"Retrieved {result.Items?.Count ?? 0} inbox item(s).",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                Items = result.Items?.Select(i => new InboxItem
                {
                    ActionInstanceId = i.ActionInstanceId ?? "",
                    WorkflowInstanceId = i.WorkflowInstanceId ?? "",
                    BlueprintId = i.BlueprintId ?? "",
                    BlueprintTitle = i.BlueprintTitle,
                    ActionId = i.ActionId,
                    ActionTitle = i.ActionTitle ?? "",
                    Status = i.Status ?? "Pending",
                    Priority = i.Priority,
                    AssignedAt = i.AssignedAt,
                    DueAt = i.DueAt
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

            return new InboxListResult
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

            return new InboxListResult
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

            _logger.LogError(ex, "Unexpected error listing inbox");

            return new InboxListResult
            {
                Status = "Error",
                Message = "An unexpected error occurred while listing inbox items.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
    }

    // Internal response models
    private sealed class InboxResponse
    {
        public List<InboxItemDto>? Items { get; set; }
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages { get; set; }
    }

    private sealed class InboxItemDto
    {
        public string? ActionInstanceId { get; set; }
        public string? WorkflowInstanceId { get; set; }
        public string? BlueprintId { get; set; }
        public string? BlueprintTitle { get; set; }
        public int ActionId { get; set; }
        public string? ActionTitle { get; set; }
        public string? Status { get; set; }
        public string? Priority { get; set; }
        public DateTimeOffset? AssignedAt { get; set; }
        public DateTimeOffset? DueAt { get; set; }
    }
}

/// <summary>
/// Result of listing inbox items.
/// </summary>
public sealed record InboxListResult
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
    /// List of inbox items.
    /// </summary>
    public IReadOnlyList<InboxItem> Items { get; init; } = [];

    /// <summary>
    /// Total number of items matching the filter.
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
/// An item in the user's inbox.
/// </summary>
public sealed record InboxItem
{
    /// <summary>
    /// The unique action instance ID.
    /// </summary>
    public required string ActionInstanceId { get; init; }

    /// <summary>
    /// The workflow instance ID this action belongs to.
    /// </summary>
    public required string WorkflowInstanceId { get; init; }

    /// <summary>
    /// The blueprint ID.
    /// </summary>
    public required string BlueprintId { get; init; }

    /// <summary>
    /// The blueprint title.
    /// </summary>
    public string? BlueprintTitle { get; init; }

    /// <summary>
    /// The action ID (sequence number).
    /// </summary>
    public int ActionId { get; init; }

    /// <summary>
    /// The action title.
    /// </summary>
    public required string ActionTitle { get; init; }

    /// <summary>
    /// Current status: Pending or InProgress.
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// Priority level if set.
    /// </summary>
    public string? Priority { get; init; }

    /// <summary>
    /// When the action was assigned to the user.
    /// </summary>
    public DateTimeOffset? AssignedAt { get; init; }

    /// <summary>
    /// When the action is due if a deadline is set.
    /// </summary>
    public DateTimeOffset? DueAt { get; init; }
}
