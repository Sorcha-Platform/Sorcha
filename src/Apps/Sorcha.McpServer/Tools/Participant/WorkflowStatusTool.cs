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
/// Participant tool for checking workflow instance status. Reads via the typed
/// <see cref="IBlueprintServiceClient"/> (spec 139 US4) so the caller's bearer is forwarded
/// and the route is contract-pinned, not hand-rolled.
/// </summary>
[McpServerToolType]
public sealed class WorkflowStatusTool
{
    private readonly IMcpAuthorizationService _authService;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly IBlueprintServiceClient _blueprintClient;
    private readonly ILogger<WorkflowStatusTool> _logger;

    public WorkflowStatusTool(
        IMcpAuthorizationService authService,
        IServiceAvailabilityTracker availabilityTracker,
        IBlueprintServiceClient blueprintClient,
        ILogger<WorkflowStatusTool> logger)
    {
        _authService = authService;
        _availabilityTracker = availabilityTracker;
        _blueprintClient = blueprintClient;
        _logger = logger;
    }

    /// <summary>
    /// Gets the status of a workflow instance.
    /// </summary>
    /// <param name="workflowInstanceId">The workflow instance ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Workflow status and progress.</returns>
    [McpServerTool(Name = "sorcha_workflow_status")]
    [Description("Return the current state of a single workflow instance: which actions have completed, which are pending, who they are assigned to, and where the instance sits in its blueprint. Gives an agent a real-time snapshot of progress across all participants in the workflow, not just the caller. Call this when investigating one specific workflow instance the agent already knows the id of; use sorcha_inbox_list rather than this tool when you want the participant's pending work across all workflows, and prefer sorcha_transaction_history when you need the immutable signed audit log instead of the live status view.")]
    public async Task<WorkflowStatusResult> GetWorkflowStatusAsync(
        [Description("The workflow instance ID")] string workflowInstanceId,
        CancellationToken cancellationToken = default)
    {
        // Authorization check
        if (!_authService.CanInvokeTool("sorcha_workflow_status"))
        {
            return new WorkflowStatusResult
            {
                Status = "Unauthorized",
                Message = "Access denied. This tool requires the sorcha:participant role.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // Validate input
        if (string.IsNullOrWhiteSpace(workflowInstanceId))
        {
            return new WorkflowStatusResult
            {
                Status = "Error",
                Message = "Workflow instance ID is required.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // Check service availability
        if (!_availabilityTracker.IsServiceAvailable("Blueprint"))
        {
            return new WorkflowStatusResult
            {
                Status = "Unavailable",
                Message = "Blueprint service is currently unavailable. Please try again later.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        _logger.LogInformation("Getting workflow status for {WorkflowInstanceId}", workflowInstanceId);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Typed client forwards the caller's bearer and pins the route (GET api/workflows/{id}).
            var responseContent = await _blueprintClient.GetWorkflowStatusAsync(workflowInstanceId, cancellationToken);

            stopwatch.Stop();

            if (string.IsNullOrWhiteSpace(responseContent))
            {
                _availabilityTracker.RecordSuccess("Blueprint");

                return new WorkflowStatusResult
                {
                    Status = "Error",
                    Message = "Workflow not found.",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

            // Record success
            _availabilityTracker.RecordSuccess("Blueprint");

            var result = JsonSerializer.Deserialize<WorkflowStatusResponse>(responseContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (result == null)
            {
                return new WorkflowStatusResult
                {
                    Status = "Error",
                    Message = "Failed to parse workflow status response.",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

            _logger.LogInformation(
                "Retrieved workflow status in {ElapsedMs}ms. Status: {WorkflowStatus}",
                stopwatch.ElapsedMilliseconds, result.Status);

            return new WorkflowStatusResult
            {
                Status = "Success",
                Message = $"Workflow is {result.Status}.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                Workflow = new WorkflowStatus
                {
                    WorkflowInstanceId = result.WorkflowInstanceId ?? workflowInstanceId,
                    BlueprintId = result.BlueprintId ?? "",
                    BlueprintTitle = result.BlueprintTitle,
                    CurrentStatus = result.Status ?? "Unknown",
                    CurrentActionId = result.CurrentActionId,
                    CurrentActionTitle = result.CurrentActionTitle,
                    CompletedActions = result.CompletedActions ?? 0,
                    TotalActions = result.TotalActions ?? 0,
                    Progress = result.TotalActions > 0
                        ? (int)((result.CompletedActions ?? 0) * 100.0 / result.TotalActions)
                        : 0,
                    StartedAt = result.StartedAt,
                    CompletedAt = result.CompletedAt,
                    LastActivityAt = result.LastActivityAt
                }
            };
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("Blueprint");

            return new WorkflowStatusResult
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

            return new WorkflowStatusResult
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

            _logger.LogError(ex, "Unexpected error getting workflow status");

            return new WorkflowStatusResult
            {
                Status = "Error",
                Message = "An unexpected error occurred while getting workflow status.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
    }

    // Internal response models
    private sealed class WorkflowStatusResponse
    {
        public string? WorkflowInstanceId { get; set; }
        public string? BlueprintId { get; set; }
        public string? BlueprintTitle { get; set; }
        public string? Status { get; set; }
        public int? CurrentActionId { get; set; }
        public string? CurrentActionTitle { get; set; }
        public int? CompletedActions { get; set; }
        public int? TotalActions { get; set; }
        public DateTimeOffset? StartedAt { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }
        public DateTimeOffset? LastActivityAt { get; set; }
    }

}

/// <summary>
/// Result of getting workflow status.
/// </summary>
public sealed record WorkflowStatusResult
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
    /// The workflow status details.
    /// </summary>
    public WorkflowStatus? Workflow { get; init; }
}

/// <summary>
/// Workflow status details.
/// </summary>
public sealed record WorkflowStatus
{
    /// <summary>
    /// The workflow instance ID.
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
    /// Current workflow status: Active, Completed, or Suspended.
    /// </summary>
    public required string CurrentStatus { get; init; }

    /// <summary>
    /// The current action ID (sequence number).
    /// </summary>
    public int? CurrentActionId { get; init; }

    /// <summary>
    /// The current action title.
    /// </summary>
    public string? CurrentActionTitle { get; init; }

    /// <summary>
    /// Number of completed actions.
    /// </summary>
    public int CompletedActions { get; init; }

    /// <summary>
    /// Total number of actions in the workflow.
    /// </summary>
    public int TotalActions { get; init; }

    /// <summary>
    /// Progress percentage (0-100).
    /// </summary>
    public int Progress { get; init; }

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
