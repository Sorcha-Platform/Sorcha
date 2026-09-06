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
    /// Submits (executes) data for an action on a workflow instance, committing it to the register.
    /// Acceptance is asynchronous (Feature 145): the workflow instance advances only once the
    /// resulting transaction seals and is folded by the projector, not synchronously within this
    /// call. Poll <c>sorcha_workflow_status</c> with the instance id to observe the outcome.
    /// </summary>
    /// <param name="instanceId">The workflow instance ID the action belongs to.</param>
    /// <param name="actionId">The action ID within the instance.</param>
    /// <param name="dataJson">The action data in JSON format.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The submission result, including the transaction id if accepted. Does not report whether the
    /// workflow has advanced — check <c>sorcha_workflow_status</c> for that.
    /// </returns>
    [McpServerTool(Name = "sorcha_action_submit")]
    [Description("Submit (execute) signed data for an action on a workflow instance, committing it to the register. This call is accepted asynchronously (HTTP 202): it returns the resulting transaction id, but the workflow instance advances only later, once that transaction seals and is folded by the instance projector — not synchronously in this response, and the response never carries next-action data. After calling this, use sorcha_workflow_status with the instance id to observe whether and how the workflow advanced. Requires both the workflow instanceId and the actionId within it (use sorcha_inbox_list or sorcha_workflow_status to discover them). Call this when the agent has the participant's input ready and intends to commit it to the register; call sorcha_action_validate first when you only want to dry-run the data against the input schema, and use sorcha_inbox_list rather than this tool when discovering which actions are pending.")]
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

            var result = ParseSubmitResponse(responseContent);

            _logger.LogInformation(
                "Action {ActionId} on instance {InstanceId} executed successfully in {ElapsedMs}ms",
                actionId, instanceId, stopwatch.ElapsedMilliseconds);

            return new ActionSubmitResult
            {
                Status = "Success",
                Message = BuildSuccessMessage(result),
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                TransactionId = result?.TransactionId,
                NextActions = result?.NextActions?.Select(a => new NextAction
                {
                    ActionId = a.ActionId,
                    Title = a.ActionTitle ?? "",
                    AssignedTo = a.ParticipantId
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

    /// <summary>
    /// Builds the human-readable success message from what the server actually sends.
    /// <c>ActionSubmissionResponse</c> (see
    /// <c>src/Services/Sorcha.Blueprint.Service/Models/Responses/ActionSubmissionResponse.cs</c>)
    /// has no <c>message</c> property at all, so this is composed from <see cref="SubmitResponse.IsComplete"/>
    /// and <see cref="SubmitResponse.NextActions"/> instead of reading a field that does not exist.
    /// </summary>
    /// <remarks>
    /// Feature 145 — <c>ActionExecutionService.ExecuteAsync</c> hard-codes <c>IsComplete = false</c>
    /// and <c>NextActions = []</c> on every one of its three return paths (see
    /// <c>src/Services/Sorcha.Blueprint.Service/Services/Implementation/ActionExecutionService.cs</c>,
    /// return sites around lines 358/1211/1411, one of which logs "returning 202 — instance advances
    /// on projection of the sealed docket"). Submission is asynchronous: the real routing decision
    /// rides on the sealed transaction's metadata for the <c>InstanceProjector</c> to fold, not on
    /// this HTTP response (also documented in <c>RehearsalOrchestrationService.cs</c>'s "F145
    /// reconciliation" comment block). So the <see cref="IsComplete"/>/next-action branches below are
    /// live for other future or historical response shapes, but the ELSE branch — which names
    /// <c>sorcha_workflow_status</c> as how to observe the real outcome — is the one every live call
    /// through this tool actually takes today. Do not let the enriched branches be the only ones that
    /// say something meaningful; an agent that never sees them must still be told submission is
    /// asynchronous.
    /// </remarks>
    internal static string BuildSuccessMessage(SubmitResponse? result)
    {
        if (result is null)
        {
            return "Action submitted successfully.";
        }

        if (result.IsComplete)
        {
            return "Action submitted successfully. Workflow is complete.";
        }

        var next = result.NextActions?.FirstOrDefault();
        if (next is not null)
        {
            return $"Action submitted successfully. Next action: '{next.ActionTitle}'.";
        }

        // The path every live call takes today (see remarks above): the server accepted the
        // submission but processes it asynchronously, so there is nothing further to report from
        // this response. Tell the agent plainly rather than letting a bare "submitted successfully"
        // imply the workflow already advanced.
        return "Action submitted successfully. Processing is asynchronous: the workflow instance " +
               "advances once the transaction seals on the register. Call sorcha_workflow_status " +
               "with this instance id to check whether it has advanced.";
    }

    /// <summary>Deserializes the Blueprint Service's action-submission response body. Returns null on unparseable input.</summary>
    internal static SubmitResponse? ParseSubmitResponse(string body) =>
        JsonSerializer.Deserialize<SubmitResponse>(body, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

    /// <summary>
    /// Mirrors the Blueprint Service's <c>ActionSubmissionResponse</c>. It has no <c>message</c>
    /// property at all — reading one always deserialized to null — so <see cref="SubmitResponse"/>
    /// deliberately carries none; <see cref="ActionSubmitResult.Message"/> is composed instead by
    /// <see cref="BuildSuccessMessage"/> from fields the server does send.
    /// </summary>
    internal sealed class SubmitResponse
    {
        public string? TransactionId { get; set; }
        public List<NextActionDto>? NextActions { get; set; }
        public bool IsComplete { get; set; }
    }

    /// <summary>
    /// Mirrors the Blueprint Service's <c>NextActionResponse</c>. The wire properties are
    /// <c>actionTitle</c> and <c>participantId</c> — NOT <c>title</c>/<c>assignedTo</c>, which
    /// silently deserialized to null against every live response.
    /// </summary>
    internal sealed class NextActionDto
    {
        public int ActionId { get; set; }
        public string? ActionTitle { get; set; }
        public string? ParticipantId { get; set; }
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
    /// Next actions the server reported as triggered by this submission, when present. Feature
    /// 145's execute endpoint currently hard-codes this empty on every live call — the real
    /// routing decision rides on the sealed transaction's metadata for the instance projector to
    /// fold, not on this HTTP response (see <c>ActionExecutionService.ExecuteAsync</c>). Modelled
    /// for shape-parity with the shared wire contract and in case a future response populates it;
    /// do not expect entries here from a live <c>sorcha_action_submit</c> call today — use
    /// <c>sorcha_workflow_status</c> to see what actually advanced.
    /// </summary>
    public IReadOnlyList<NextAction> NextActions { get; init; } = [];

    /// <summary>
    /// Validation errors if the submission failed validation.
    /// </summary>
    public IReadOnlyList<string> ValidationErrors { get; init; } = [];
}

/// <summary>
/// A next-action entry as the Blueprint Service's wire shape describes it. NOTE: a live
/// <c>sorcha_action_submit</c> response never populates this today (Feature 145 — see
/// <see cref="ActionSubmitResult.NextActions"/> for why); this type exists for shape-parity with
/// the same <c>NextActionResponse</c> contract other consumers deserialize.
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
