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
    [Description("Return the current state of a single workflow instance: its lifecycle state (Active, Completed, Rejected, TimedOut, or Cancelled), which action(s) are currently awaiting execution, and how many actions have completed. Gives an agent a real-time snapshot of instance progress. Readable only by a caller controlling a wallet recorded as a participant on the instance. Call this when investigating one specific workflow instance the agent already knows the id of; use sorcha_inbox_list rather than this tool when you want the participant's pending work across all workflows, and prefer sorcha_transaction_history when you need the immutable signed audit log instead of the live status view. NOTE: the blueprint title, the current action's title, and the workflow's total action count are not carried on the instance record this reads and are always null/zero here — call sorcha_blueprint_get with the blueprint ID for that context.")]
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
                Message = "Access denied. This tool requires an authenticated consumer- or platform-tier caller.",
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
            // Typed client forwards the caller's bearer and pins the route (GET api/instances/{id}).
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

            var currentStatus = InstanceStateResolver.Resolve(result.State);

            _logger.LogInformation(
                "Retrieved workflow status in {ElapsedMs}ms. Status: {WorkflowStatus}",
                stopwatch.ElapsedMilliseconds, currentStatus);

            return new WorkflowStatusResult
            {
                Status = "Success",
                Message = $"Workflow is {currentStatus}.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                Workflow = new WorkflowStatus
                {
                    WorkflowInstanceId = result.Id ?? workflowInstanceId,
                    BlueprintId = result.BlueprintId ?? "",
                    CurrentStatus = currentStatus,
                    CurrentActionId = result.CurrentActionIds?.Count > 0 ? result.CurrentActionIds[0] : null,
                    CompletedActions = result.CompletedActionCount,
                    StartedAt = result.CreatedAt,
                    CompletedAt = result.CompletedAt,
                    LastActivityAt = result.UpdatedAt
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

    // Internal response model — mirrors Sorcha.Blueprint.Service.Models.Instance, the actual wire
    // body of GET /api/instances/{id} (the whole instance, returned verbatim). There is no
    // "workflow status" projection: no blueprint/action title, no total-action count.
    private sealed class WorkflowStatusResponse
    {
        public string? Id { get; set; }
        public string? BlueprintId { get; set; }
        public JsonElement? State { get; set; }
        public List<int>? CurrentActionIds { get; set; }
        public int CompletedActionCount { get; set; }
        public DateTimeOffset? CreatedAt { get; set; }
        public DateTimeOffset? UpdatedAt { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }
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
/// Workflow status details, mirroring the fields actually present on
/// <c>GET /api/instances/{instanceId}</c> (the raw instance record — there is no separate
/// "workflow status" projection carrying blueprint/action titles or a total-action count).
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
    /// Current instance lifecycle state: Active, Completed, Rejected, TimedOut, or Cancelled.
    /// </summary>
    public required string CurrentStatus { get; init; }

    /// <summary>
    /// The current action ID (sequence number) awaiting execution, if any. Multiple current
    /// actions (parallel branches) are possible on the instance; only the first is surfaced here —
    /// call sorcha_action_details per action ID for the full set.
    /// </summary>
    public int? CurrentActionId { get; init; }

    /// <summary>
    /// Number of completed actions.
    /// </summary>
    public int CompletedActions { get; init; }

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
