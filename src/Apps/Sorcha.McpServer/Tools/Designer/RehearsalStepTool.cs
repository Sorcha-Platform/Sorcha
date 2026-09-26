// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.ServiceClients.Blueprint;
using Sorcha.ServiceClients.Blueprint.Models;

namespace Sorcha.McpServer.Tools.Designer;

/// <summary>
/// Designer tool that submits the current action of a rehearsal (Feature 142, #1691). Reaching a
/// terminal state records the <c>RehearsalPass</c> that clears <c>sorcha_blueprint_publish</c>'s
/// safety gate.
/// </summary>
/// <remarks>
/// <para>
/// <b>A step runs the real execution pipeline</b> — sign as the acting role's stand-in wallet,
/// validate against the action's data schema, route, seal, disclose, issue credentials — against
/// the sandbox register, and then waits (up to 90 s server-side) for the transaction to seal
/// before answering. That wait is why a timeout here must never be treated as a failed step: the
/// submission may still seal, so the tool points at <c>sorcha_rehearsal_get</c> before any retry.
/// </para>
/// <para>
/// <b>A step that ran and failed is a 200, not a refusal.</b> The server records the reason in the
/// rehearsal log and sets the outcome to Failed; the result surfaces the last error entry.
/// </para>
/// </remarks>
[McpServerToolType]
public sealed class RehearsalStepTool
{
    private const string ToolName = "sorcha_rehearsal_step";

    private readonly IMcpAuthorizationService _authService;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly IBlueprintServiceClient _blueprintClient;
    private readonly ILogger<RehearsalStepTool> _logger;

    /// <summary>Creates the tool.</summary>
    public RehearsalStepTool(
        IMcpAuthorizationService authService,
        IServiceAvailabilityTracker availabilityTracker,
        IBlueprintServiceClient blueprintClient,
        ILogger<RehearsalStepTool> logger)
    {
        _authService = authService;
        _availabilityTracker = availabilityTracker;
        _blueprintClient = blueprintClient;
        _logger = logger;
    }

    /// <summary>Submits the rehearsal's current action.</summary>
    /// <param name="blueprintId">The draft blueprint being rehearsed.</param>
    /// <param name="rehearsalId">The rehearsal id from sorcha_rehearsal_start.</param>
    /// <param name="actionId">The action to submit — must be the rehearsal's current step.</param>
    /// <param name="payloadJson">The action's data, as a JSON object.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The advanced rehearsal, and the next action to submit or the final outcome.</returns>
    [McpServerTool(Name = ToolName, Destructive = false, ReadOnly = false, Idempotent = false)]
    [Description("Submits the current action of a rehearsal started with sorcha_rehearsal_start, signed as that action's participant by a stand-in sandbox wallet, and returns the next action to submit or the final outcome. Call this once per step, with the actionId the previous result named as current and a payload satisfying that action's data schema; the payload's field values decide the route taken, so choose the values for the path you want proven. When the status comes back Passed, a rehearsal pass is recorded and sorcha_blueprint_publish will go live without asking a person. Use sorcha_blueprint_validate instead when you only want to check a payload, because a failed step ends the rehearsal and a new one must be started. A step waits for the sandbox to seal it, so it can take tens of seconds.")]
    public async Task<RehearsalToolResult> SubmitRehearsalStepAsync(
        [Description("The draft blueprint's ID")] string blueprintId,
        [Description("The rehearsal ID returned by sorcha_rehearsal_start")] string rehearsalId,
        [Description("The action ID to submit — the rehearsal's current step")] string actionId,
        [Description("The action's data as a JSON object, e.g. {\"decision\":\"approved\"}")] string payloadJson,
        CancellationToken cancellationToken = default)
    {
        if (!_authService.CanInvokeTool(ToolName))
        {
            return RehearsalResultMapper.Fail(
                "Unauthorized",
                "Access denied. Rehearsing a blueprint requires the sorcha:designer role — the same "
                + "authority that authors one.");
        }

        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(blueprintId))
        {
            errors.Add("blueprintId is required");
        }

        if (!Guid.TryParse(rehearsalId, out var rehearsalGuid))
        {
            errors.Add("rehearsalId must be the GUID returned by sorcha_rehearsal_start");
        }

        if (!int.TryParse(actionId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var actionNumber))
        {
            errors.Add("actionId must be the current step's integer action id");
        }

        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            errors.Add("payloadJson is required — pass {} for an action with no fields");
        }
        else
        {
            try
            {
                using var document = JsonDocument.Parse(payloadJson);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    errors.Add("payloadJson must be a JSON object of the action's fields");
                }
            }
            catch (JsonException ex)
            {
                errors.Add($"payloadJson is not valid JSON: {ex.Message}");
            }
        }

        if (errors.Count > 0)
        {
            return RehearsalResultMapper.Fail(
                "ValidationError", "The step was not submitted: " + string.Join("; ", errors) + ".", errors);
        }

        if (!_availabilityTracker.IsServiceAvailable("Blueprint"))
        {
            return RehearsalResultMapper.Fail(
                "Unavailable", "Blueprint service is currently unavailable. Please try again later. The step was not submitted.");
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await _blueprintClient.SubmitRehearsalStepAsync(
                blueprintId,
                rehearsalGuid,
                new SubmitRehearsalStepRequest { ActionId = actionNumber, PayloadJson = payloadJson },
                cancellationToken);
            stopwatch.Stop();
            _availabilityTracker.RecordSuccess("Blueprint");

            if (result.Rehearsal is { } rehearsal)
            {
                _logger.LogInformation(
                    "Rehearsal {RehearsalId} step {ActionId} submitted via MCP (outcome {Outcome})",
                    rehearsalGuid, actionNumber, rehearsal.Outcome);
                return RehearsalResultMapper.FromRehearsal(
                    rehearsal, (int)stopwatch.ElapsedMilliseconds, submittedActionId: actionNumber);
            }

            return RehearsalResultMapper.FromRefusal(
                result.Refusal!, $"submit action {actionNumber} of rehearsal {rehearsalGuid}", (int)stopwatch.ElapsedMilliseconds);
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("Blueprint");
            _logger.LogWarning("Rehearsal {RehearsalId} step {ActionId} timed out", rehearsalGuid, actionNumber);
            return RehearsalResultMapper.Fail(
                "Timeout",
                $"Submitting action {actionNumber} timed out, so it is not known whether the step was "
                + "applied — it may still seal. Call sorcha_rehearsal_get to see which step is current "
                + "before submitting anything again.",
                elapsedMs: (int)stopwatch.ElapsedMilliseconds);
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("Blueprint", ex);
            _logger.LogWarning(ex, "Rehearsal {RehearsalId} step {ActionId} failed", rehearsalGuid, actionNumber);
            return RehearsalResultMapper.Fail(
                "Error",
                $"The step request failed: {ex.Message}. Call sorcha_rehearsal_get to see whether it was applied.",
                elapsedMs: (int)stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("Blueprint", ex);
            _logger.LogError(ex, "Unexpected error submitting rehearsal {RehearsalId} step {ActionId}", rehearsalGuid, actionNumber);
            return RehearsalResultMapper.Fail(
                "Error", "An unexpected error occurred while submitting the step.",
                elapsedMs: (int)stopwatch.ElapsedMilliseconds);
        }
    }
}
