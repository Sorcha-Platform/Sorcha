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
    /// <param name="page">Page number (1-based, default: 1).</param>
    /// <param name="pageSize">Items per page (default: 20, max: 100).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of pending actions.</returns>
    [McpServerTool(Name = "sorcha_inbox_list")]
    [Description("List the workflow actions currently assigned to the authenticated participant, paginated. Returns one row per outstanding action with its instance id, action id, blueprint context, and urgency — the entry point an agent should use to discover what work is waiting. Call this when an agent first wakes up on behalf of a participant and needs to know what to act on; use sorcha_action_details rather than this tool when you already have an instanceId and actionId and need the action's schema, and prefer sorcha_workflow_status when investigating a specific workflow instance rather than a participant's whole queue. NOTE: the underlying endpoint (GET /api/actions/pending) has no server-side status filter — every item returned is pending by definition, so a status parameter is not offered.")]
    public async Task<InboxListResult> ListInboxAsync(
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

        _logger.LogInformation("Listing inbox items. Page: {Page}", page);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Build query string
            var queryString = $"page={page}&pageSize={pageSize}";

            // Typed client forwards the caller's bearer and pins the route (GET api/actions/pending).
            var responseContent = await _blueprintClient.GetInboxAsync(queryString, cancellationToken);

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
                    InstanceId = i.InstanceId ?? "",
                    BlueprintId = i.BlueprintId ?? "",
                    BlueprintTitle = i.BlueprintTitle,
                    ActionId = i.ActionId,
                    ActionTitle = i.ActionTitle ?? "",
                    Urgency = i.Urgency ?? "normal",
                    ReceivedAt = i.ReceivedAt,
                    DueAt = i.Deadline
                }).ToList() ?? [],
                TotalCount = result.TotalCount,
                Page = result.Page,
                PageSize = result.PageSize,
                // The endpoint does not return a totalPages field — derive it rather than report a
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

    // Internal response models — mirror Sorcha.Blueprint.Service.Models.PendingActionSummary, the
    // actual wire shape of GET /api/actions/pending (no totalPages, no status/priority/actionInstanceId —
    // those fields never existed on this endpoint; see report for what was dropped/renamed).
    private sealed class InboxResponse
    {
        public List<InboxItemDto>? Items { get; set; }
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
    }

    private sealed class InboxItemDto
    {
        public string? InstanceId { get; set; }
        public int ActionId { get; set; }
        public string? ActionTitle { get; set; }
        public string? BlueprintId { get; set; }
        public string? BlueprintTitle { get; set; }
        public string? Urgency { get; set; }
        public DateTimeOffset? Deadline { get; set; }
        public DateTimeOffset ReceivedAt { get; set; }
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
/// An item in the user's inbox. Every item is a pending action — there is no separate
/// "in progress" state on this endpoint.
/// </summary>
public sealed record InboxItem
{
    /// <summary>
    /// The workflow instance ID this action belongs to. Pass this and <see cref="ActionId"/> to
    /// sorcha_action_details or sorcha_action_validate.
    /// </summary>
    public required string InstanceId { get; init; }

    /// <summary>
    /// The blueprint ID.
    /// </summary>
    public required string BlueprintId { get; init; }

    /// <summary>
    /// The blueprint title.
    /// </summary>
    public string? BlueprintTitle { get; init; }

    /// <summary>
    /// The action ID (sequence number) within the blueprint.
    /// </summary>
    public int ActionId { get; init; }

    /// <summary>
    /// The action title.
    /// </summary>
    public required string ActionTitle { get; init; }

    /// <summary>
    /// Urgency level: normal, warning, or urgent.
    /// </summary>
    public required string Urgency { get; init; }

    /// <summary>
    /// When the action arrived in the participant's queue.
    /// </summary>
    public DateTimeOffset ReceivedAt { get; init; }

    /// <summary>
    /// When the action is due if a deadline is configured.
    /// </summary>
    public DateTimeOffset? DueAt { get; init; }
}
