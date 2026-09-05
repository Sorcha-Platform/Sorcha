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
/// Participant tool for submitting (executing) action data. Writes via the typed
/// <see cref="IBlueprintServiceClient"/> (spec 139 US4) so the caller's bearer is forwarded
/// and the route is the real execute endpoint, not a hand-rolled (non-existent) submit path.
/// </summary>
[McpServerToolType]
public sealed class ActionSubmitTool
{
    private readonly IMcpAuthorizationService _authService;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly IBlueprintServiceClient _blueprintClient;
    private readonly ILogger<ActionSubmitTool> _logger;

    public ActionSubmitTool(
        IMcpAuthorizationService authService,
        IServiceAvailabilityTracker availabilityTracker,
        IBlueprintServiceClient blueprintClient,
        ILogger<ActionSubmitTool> logger)
    {
        _authService = authService;
        _availabilityTracker = availabilityTracker;
        _blueprintClient = blueprintClient;
        _logger = logger;
    }

    /// <summary>
    /// Submits (executes) data for an action on a workflow instance, completing it and advancing the workflow.
    /// </summary>
    /// <param name="instanceId">The workflow instance ID the action belongs to.</param>
    /// <param name="actionId">The action ID within the instance.</param>
    /// <param name="dataJson">The action data in JSON format.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result of the submission.</returns>
    [McpServerTool(Name = "sorcha_action_submit")]
    [Description("Submit (execute) signed data for an action on a workflow instance, completing that action and advancing the multi-party workflow to its next step. Returns the resulting transaction id and any next actions triggered. Requires both the workflow instanceId and the actionId within it (use sorcha_inbox_list or sorcha_workflow_status to discover them). Call this when the agent has the participant's input ready and intends to commit it to the register; call sorcha_action_validate first when you only want to dry-run the data against the input schema, and use sorcha_inbox_list rather than this tool when discovering which actions are pending.")]
    public async Task<ActionSubmitResult> SubmitActionAsync(
        [Description("The workflow instance ID the action belongs to")] string instanceId,
        [Description("The action ID within the instance")] string actionId,
        [Description("The action data in JSON format")] string dataJson,
        CancellationToken cancellationToken = default)
    {
        // Authorization check
        if (!_authService.CanInvokeTool("sorcha_action_submit"))
        {
            return new ActionSubmitResult
            {
                Status = "Unauthorized",
                Message = "Access denied. This tool requires an authenticated consumer- or platform-tier caller.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // Validate inputs
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            return new ActionSubmitResult
            {
                Status = "Error",
                Message = "Workflow instance ID is required.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        if (string.IsNullOrWhiteSpace(actionId))
        {
            return new ActionSubmitResult
            {
                Status = "Error",
                Message = "Action ID is required.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        if (string.IsNullOrWhiteSpace(dataJson))
        {
            return new ActionSubmitResult
            {
                Status = "Error",
                Message = "Data JSON is required.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // Parse data JSON
        try
        {
            var data = JsonSerializer.Deserialize<Dictionary<string, object>>(dataJson);
            if (data == null)
            {
                return new ActionSubmitResult
                {
                    Status = "Error",
                    Message = "Data JSON must be a valid object.",
                    CheckedAt = DateTimeOffset.UtcNow
                };
            }
        }
        catch (JsonException ex)
        {
            return new ActionSubmitResult
            {
                Status = "Error",
                Message = $"Invalid data JSON format: {ex.Message}",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // Check service availability
        if (!_availabilityTracker.IsServiceAvailable("Blueprint"))
        {
            return new ActionSubmitResult
            {
                Status = "Unavailable",
                Message = "Blueprint service is currently unavailable. Please try again later.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        _logger.LogInformation("Executing action {ActionId} on instance {InstanceId}", actionId, instanceId);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Typed client forwards the caller's bearer and pins the real route
            // (POST api/instances/{instanceId}/actions/{actionId}/execute).
            var responseContent = await _blueprintClient.ExecuteActionAsync(
                instanceId, actionId, dataJson, cancellationToken);

            stopwatch.Stop();

            if (string.IsNullOrWhiteSpace(responseContent))
            {
                _availabilityTracker.RecordSuccess("Blueprint");

                return new ActionSubmitResult
                {
                    Status = "Error",
                    Message = "Action submission failed.",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

            // Record success
            _availabilityTracker.RecordSuccess("Blueprint");

            var result = JsonSerializer.Deserialize<SubmitResponse>(responseContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            _logger.LogInformation(
                "Action {ActionId} on instance {InstanceId} executed successfully in {ElapsedMs}ms",
                actionId, instanceId, stopwatch.ElapsedMilliseconds);

            return new ActionSubmitResult
            {
                Status = "Success",
                Message = result?.Message ?? "Action submitted successfully.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                TransactionId = result?.TransactionId,
                NextActions = result?.NextActions?.Select(a => new NextAction
                {
                    ActionId = a.ActionId,
                    Title = a.Title ?? "",
                    AssignedTo = a.AssignedTo
                }).ToList() ?? []
            };
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("Blueprint");

            return new ActionSubmitResult
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

            return new ActionSubmitResult
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

            _logger.LogError(ex, "Unexpected error submitting action");

            return new ActionSubmitResult
            {
                Status = "Error",
                Message = "An unexpected error occurred while submitting the action.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
    }

    // Internal response models
    private sealed class SubmitResponse
    {
        public string? Message { get; set; }
        public string? TransactionId { get; set; }
        public List<NextActionDto>? NextActions { get; set; }
    }

    private sealed class NextActionDto
    {
        public int ActionId { get; set; }
        public string? Title { get; set; }
        public string? AssignedTo { get; set; }
    }
}

/// <summary>
/// Result of submitting an action.
/// </summary>
public sealed record ActionSubmitResult
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
    /// The transaction ID if the submission created a transaction.
    /// </summary>
    public string? TransactionId { get; init; }

    /// <summary>
    /// Next actions in the workflow that were triggered.
    /// </summary>
    public IReadOnlyList<NextAction> NextActions { get; init; } = [];

    /// <summary>
    /// Validation errors if the submission failed validation.
    /// </summary>
    public IReadOnlyList<string> ValidationErrors { get; init; } = [];
}

/// <summary>
/// Information about a next action triggered by submission.
/// </summary>
public sealed record NextAction
{
    /// <summary>
    /// The action ID.
    /// </summary>
    public int ActionId { get; init; }

    /// <summary>
    /// The action title.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// Who the action was assigned to.
    /// </summary>
    public string? AssignedTo { get; init; }
}
