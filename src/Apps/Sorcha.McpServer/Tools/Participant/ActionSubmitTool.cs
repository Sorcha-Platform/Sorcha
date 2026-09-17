// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.ServiceClients.Blueprint;
using Sorcha.ServiceClients.Blueprint.Models;

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
    /// <remarks>
    /// #1658: the execute endpoint binds <c>ActionSubmissionRequest</c>, which needs the blueprint id, the
    /// register address and the sender wallet as well as the data. The tool reads those from the action's
    /// submission context (<c>GET /api/instances/{id}/actions/{actionId}</c>), where the Blueprint Service
    /// decides which of the caller's wallets may submit, rather than re-deriving participant bindings here.
    /// A refusal from either call is passed through with the service's own reason.
    /// </remarks>
    /// <param name="instanceId">The workflow instance ID the action belongs to.</param>
    /// <param name="actionId">The action ID within the instance.</param>
    /// <param name="dataJson">The action data as a JSON object.</param>
    /// <param name="senderWallet">
    /// Optional. The caller's wallet to submit with; needed only when the service reports several candidates.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The submission result, including the transaction id if accepted. Does not report whether the
    /// workflow has advanced — check <c>sorcha_workflow_status</c> for that.
    /// </returns>
    [McpServerTool(Name = "sorcha_action_submit")]
    [Description("Submit (execute) data for an action on a workflow instance, committing it to the register. Requires the workflow instanceId, the actionId within it (use sorcha_inbox_list or sorcha_workflow_status to discover them) and the action data as a JSON object matching the action's schema (sorcha_action_details shows it). The tool works out the blueprint, the register and which of your wallets signs: pass senderWallet only when a previous call said you hold several wallets and named the candidates, because submitting binds that wallet to your participant role for the life of the instance. Acceptance is asynchronous (HTTP 202): the response carries the transaction id, but the instance advances only once the transaction seals, so call sorcha_workflow_status afterwards to see whether and how it advanced. If the service refuses the submission, the message carries its reason (for example that the action is not current, or that a credential presentation is required). Call this when the participant's input is ready and you intend to commit it to the register; use sorcha_action_validate rather than this tool when you only want to dry-run the data against the schema.")]
    public async Task<ActionSubmitResult> SubmitActionAsync(
        [Description("The workflow instance ID the action belongs to")] string instanceId,
        [Description("The action ID within the instance")] string actionId,
        [Description("The action data as a JSON object")] string dataJson,
        [Description("Optional: which of your wallets submits. Only needed when the tool reports several candidates.")] string? senderWallet = null,
        CancellationToken cancellationToken = default)
    {
        if (!_authService.CanInvokeTool("sorcha_action_submit"))
        {
            return Result("Unauthorized", "Access denied. This tool requires an authenticated consumer- or platform-tier caller.");
        }

        if (string.IsNullOrWhiteSpace(instanceId))
        {
            return Result("Error", "Workflow instance ID is required.");
        }

        if (string.IsNullOrWhiteSpace(actionId))
        {
            return Result("Error", "Action ID is required.");
        }

        if (string.IsNullOrWhiteSpace(dataJson))
        {
            return Result("Error", "Data JSON is required.");
        }

        JsonElement payload;
        try
        {
            using var document = JsonDocument.Parse(dataJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Result("Error", "Data JSON must be a JSON object.");
            }

            payload = document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            return Result("Error", $"Invalid data JSON format: {ex.Message}");
        }

        if (!_availabilityTracker.IsServiceAvailable("Blueprint"))
        {
            return Result("Unavailable", "Blueprint service is currently unavailable. Please try again later.");
        }

        _logger.LogInformation("Executing action {ActionId} on instance {InstanceId}", actionId, instanceId);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // An HTTP response of any status means the service is up. Only transport failures (the catch
            // blocks below) count against availability: a deterministic refusal recorded as a failure trips
            // the breaker and disables every Blueprint tool behind a false "unavailable".
            var (contextStatus, contextBody) = await _blueprintClient.GetActionForSubmissionAsync(
                instanceId, actionId, cancellationToken);
            _availabilityTracker.RecordSuccess("Blueprint");

            if (!IsSuccess(contextStatus))
            {
                var (reason, _) = ExtractReason(contextBody);
                return Timed(Refusal(contextStatus, $"Could not read action {actionId} on instance {instanceId}", reason), stopwatch);
            }

            var context = ReadSubmissionContext(contextBody);
            if (context is null)
            {
                return Timed(Result("Error",
                    "The Blueprint Service does not report a submission context for this action, so the blueprint, "
                    + "register and sender wallet cannot be determined. The service predates #1658 and needs updating."), stopwatch);
            }

            var (blueprintId, registerId, walletStatus, resolvedWallet, candidates) = context.Value;
            var chosenWallet = string.IsNullOrWhiteSpace(senderWallet) ? null : senderWallet.Trim();

            if (chosenWallet is null)
            {
                if (walletStatus == "resolved" && !string.IsNullOrWhiteSpace(resolvedWallet))
                {
                    chosenWallet = resolvedWallet;
                }
                else
                {
                    return Timed(NoWalletChosen(walletStatus, candidates, instanceId, actionId), stopwatch);
                }
            }

            var request = new ExecuteActionRequest
            {
                BlueprintId = blueprintId,
                ActionId = actionId,
                InstanceId = instanceId,
                SenderWallet = chosenWallet,
                RegisterAddress = registerId,
                PayloadData = payload,
            };

            var (status, body) = await _blueprintClient.ExecuteActionAsync(instanceId, actionId, request, cancellationToken);
            _availabilityTracker.RecordSuccess("Blueprint");

            if (!IsSuccess(status))
            {
                var (reason, fieldErrors) = ExtractReason(body);
                return Timed(Refusal(status, "The Blueprint Service refused the submission", reason) with
                {
                    ValidationErrors = fieldErrors,
                }, stopwatch);
            }

            var result = string.IsNullOrWhiteSpace(body) ? null : ParseSubmitResponse(body);

            _logger.LogInformation(
                "Action {ActionId} on instance {InstanceId} submitted in {ElapsedMs}ms",
                actionId, instanceId, stopwatch.ElapsedMilliseconds);

            return Timed(new ActionSubmitResult
            {
                Status = "Success",
                Message = BuildSuccessMessage(result),
                CheckedAt = DateTimeOffset.UtcNow,
                TransactionId = string.IsNullOrWhiteSpace(result?.TransactionId) ? null : result.TransactionId,
                NextActions = result?.NextActions?.Select(a => new NextAction
                {
                    ActionId = a.ActionId,
                    Title = a.ActionTitle ?? "",
                    AssignedTo = a.ParticipantId
                }).ToList() ?? []
            }, stopwatch);
        }
        catch (TaskCanceledException)
        {
            _availabilityTracker.RecordFailure("Blueprint");
            return Timed(Result("Timeout", "Request to blueprint service timed out."), stopwatch);
        }
        catch (HttpRequestException ex)
        {
            _availabilityTracker.RecordFailure("Blueprint", ex);
            return Timed(Result("Error", $"Failed to connect to blueprint service: {ex.Message}"), stopwatch);
        }
        catch (Exception ex)
        {
            _availabilityTracker.RecordFailure("Blueprint", ex);
            _logger.LogError(ex, "Unexpected error submitting action");
            return Timed(Result("Error", "An unexpected error occurred while submitting the action."), stopwatch);
        }
    }

    /// <summary>
    /// The service could not choose a sender wallet and the agent did not name one. Nothing is submitted:
    /// guessing would bind the guessed wallet to the participant for the life of the instance.
    /// </summary>
    private static ActionSubmitResult NoWalletChosen(
        string walletStatus, IReadOnlyList<string> candidates, string instanceId, string actionId) => walletStatus switch
    {
        "ambiguous" => Result("Error",
            "You hold several wallets and this action's sender is not yet bound to one. Submitting binds the wallet "
            + "you choose to your participant role for the life of the instance, so call again with senderWallet "
            + $"set to one of: {string.Join(", ", candidates)}."),
        "notYours" => Result("Refused",
            $"Action {actionId} on instance {instanceId} is bound to a wallet you do not hold, so you cannot submit it. "
            + "Another participant is expected to act; sorcha_workflow_status shows the instance's current actions."),
        "noWallet" => Result("Error",
            "The Blueprint Service found no wallet for you, so there is nothing to sign this submission with."),
        _ => Result("Error",
            $"The Blueprint Service reported an unrecognised sender wallet status '{walletStatus}'."),
    };

    private static bool IsSuccess(HttpStatusCode status) => (int)status is >= 200 and < 300;

    private static ActionSubmitResult Result(string status, string message) => new()
    {
        Status = status,
        Message = message,
        CheckedAt = DateTimeOffset.UtcNow,
    };

    private static ActionSubmitResult Timed(ActionSubmitResult result, Stopwatch stopwatch) =>
        result with { ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds };

    /// <summary>
    /// A non-success response, carrying the service's own reason. 401 and 403 refuse the caller, which an
    /// agent must not retry unchanged; anything else is an error reported with its status code.
    /// </summary>
    private static ActionSubmitResult Refusal(HttpStatusCode status, string what, string? reason)
    {
        var code = (int)status;
        var message = string.IsNullOrWhiteSpace(reason)
            ? $"{what} (HTTP {code}) and gave no reason."
            : $"{what} (HTTP {code}): {reason}";
        return Result(status is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized ? "Refused" : "Error", message);
    }

    /// <summary>
    /// Reads the reason from a Blueprint Service error body: <c>{"error": …}</c> from the endpoint's own
    /// handlers, or problem+json <c>detail</c> / <c>title</c>, with per-field <c>errors</c> also returned
    /// separately. A body that is not a JSON object is returned as text, so a reason is never dropped.
    /// </summary>
    internal static (string? Reason, IReadOnlyList<string> FieldErrors) ExtractReason(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return (null, []);
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return (Truncate(body), []);
            }

            var fieldErrors = new List<string>();
            if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
            {
                foreach (var field in errors.EnumerateObject())
                {
                    var messages = field.Value.ValueKind == JsonValueKind.Array
                        ? field.Value.EnumerateArray().Select(m => m.ToString())
                        : [field.Value.ToString()];
                    fieldErrors.AddRange(messages.Select(m => $"{field.Name}: {m}"));
                }
            }

            var reason = Text(root, "error") ?? Text(root, "detail") ?? Text(root, "title");
            if (fieldErrors.Count > 0)
            {
                reason = $"{reason ?? "Validation failed."} {string.Join("; ", fieldErrors)}";
            }

            return (reason ?? Truncate(body), fieldErrors);
        }
        catch (JsonException)
        {
            return (Truncate(body), []);
        }

        static string? Text(JsonElement root, string name) =>
            root.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(value.GetString())
                ? value.GetString()
                : null;

        static string Truncate(string text) => text.Length <= 500 ? text.Trim() : text[..500].Trim() + "…";
    }

    /// <summary>
    /// Reads the <c>submission</c> block of the instance-action response. A tuple rather than a record on
    /// purpose: the MCP response-shape gate pairs every record declared in a tool file with the endpoint's
    /// response type, and this is a hand-parsed projection of one nested member.
    /// </summary>
    internal static (string BlueprintId, string RegisterId, string Status, string? SenderWallet, IReadOnlyList<string> Candidates)?
        ReadSubmissionContext(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("submission", out var submission)
                || submission.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var blueprintId = ReadString(submission, "blueprintId");
            var registerId = ReadString(submission, "registerId");
            var status = ReadString(submission, "senderWalletStatus");
            if (string.IsNullOrWhiteSpace(blueprintId) || string.IsNullOrWhiteSpace(registerId) || string.IsNullOrWhiteSpace(status))
            {
                return null;
            }

            IReadOnlyList<string> candidates =
                submission.TryGetProperty("candidateWallets", out var list) && list.ValueKind == JsonValueKind.Array
                    ? list.EnumerateArray().Where(c => c.ValueKind == JsonValueKind.String).Select(c => c.GetString()!).ToList()
                    : [];

            return (blueprintId, registerId, status, ReadString(submission, "senderWallet"), candidates);
        }
        catch (JsonException)
        {
            return null;
        }

        static string? ReadString(JsonElement element, string name) =>
            element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    /// <summary>
    /// Builds the human-readable success message from what the server actually sends.
    /// <c>ActionSubmissionResponse</c> (see
    /// <c>src/Services/Sorcha.Blueprint.Service/Models/Responses/ActionSubmissionResponse.cs</c>)
    /// has no <c>message</c> property at all, so this is composed from <see cref="SubmitResponse.IsComplete"/>,
    /// <see cref="SubmitResponse.AwaitingPresentation"/> and <see cref="SubmitResponse.NextActions"/>.
    /// </summary>
    /// <remarks>
    /// Feature 145 — <c>ActionExecutionService.ExecuteAsync</c> hard-codes <c>IsComplete = false</c>
    /// and <c>NextActions = []</c> on every return path. Submission is asynchronous: the real routing
    /// decision rides on the sealed transaction's metadata for the <c>InstanceProjector</c> to fold, not on
    /// this HTTP response. So the <see cref="SubmitResponse.IsComplete"/>/next-action branches below are
    /// live only for other response shapes, and the final branch — which names <c>sorcha_workflow_status</c>
    /// as how to observe the real outcome — is the one ordinary live calls take. An agent that never sees
    /// the enriched branches must still be told submission is asynchronous.
    /// </remarks>
    internal static string BuildSuccessMessage(SubmitResponse? result)
    {
        if (result is null)
        {
            return "Action submitted successfully.";
        }

        if (result.AwaitingPresentation)
        {
            // Feature 111: the action is gated on a credential presentation and is not complete yet.
            var requestId = result.PresentationRequest?.RequestId;
            return "Action accepted, but it is waiting for a credential presentation before it can complete"
                + (string.IsNullOrWhiteSpace(requestId) ? "." : $" (presentation request {requestId}).")
                + " The instance does not advance until the credential is presented; sorcha_workflow_status shows whether it has.";
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

        return "Action submitted successfully. Processing is asynchronous: the workflow instance "
            + "advances once the transaction seals on the register. Call sorcha_workflow_status "
            + "with this instance id to check whether it has advanced.";
    }

    /// <summary>Deserializes the Blueprint Service's action-submission response body. Returns null on unparseable input.</summary>
    internal static SubmitResponse? ParseSubmitResponse(string body)
    {
        try
        {
            return JsonSerializer.Deserialize<SubmitResponse>(body, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException)
        {
            return null;
        }
    }

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
        public bool AwaitingPresentation { get; set; }
        public PresentationRequestDto? PresentationRequest { get; set; }
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

    /// <summary>The part of the Feature 111 presentation request an agent needs: its id.</summary>
    internal sealed class PresentationRequestDto
    {
        public string? RequestId { get; set; }
    }
}

/// <summary>
/// Result of submitting an action.
/// </summary>
public sealed record ActionSubmitResult
{
    /// <summary>
    /// Operation status: Success, Error, Refused (the service refused this caller — do not retry unchanged),
    /// Unavailable, Timeout, or Unauthorized.
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// Human-readable message about the operation result. On a refusal it carries the service's own reason.
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
    /// Per-field validation errors the service reported, as <c>field: message</c>.
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
