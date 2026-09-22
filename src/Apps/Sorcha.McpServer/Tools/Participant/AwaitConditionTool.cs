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
using Sorcha.ServiceClients.Register;

namespace Sorcha.McpServer.Tools.Participant;

/// <summary>
/// #1707 — a bounded, server-side wait over the three ledger conditions cold-start run #8 needed a
/// human to relay: a participant role becoming <c>Active</c> on a register, a workflow instance
/// reaching a given action (or completing), and a transaction sealing into a docket. Modelled on
/// <see cref="Sorcha.Blueprint.Service.Services.Implementation.RehearsalOrchestrationService"/>'s own
/// wait-for-projection loop (poll the authoritative read once a second, stop on a terminal signal),
/// but capped far tighter: that wait is an internal service call with a 90s budget; this one is a
/// live inbound MCP tool call that an MCP client's own timeout races against, so it must always
/// return well inside that budget rather than depend on it.
/// </summary>
/// <remarks>
/// <para>
/// <b>One tool, not three.</b> The catalogue is already ~69 tools (#1685 is open about argument
/// discoverability across it), and these three conditions share one shape — poll, tri-state,
/// bounded — so a <c>condition</c> discriminator gives an agent one new verb ("you can wait") rather
/// than three more tool names to learn. The discoverability risk #1685 actually documents is
/// inconsistent PARAMETER NAMES across tools (<c>orgId</c> vs <c>organizationId</c>,
/// <c>instanceId</c> vs <c>workflowInstanceId</c>), not parameter COUNT on one tool — so this tool
/// reuses the exact names <see cref="TransactionHistoryTool"/> and
/// <see cref="WorkflowStatusTool"/> already use (<c>workflowInstanceId</c>,
/// <c>registerId</c>, <c>transactionId</c>) and documents per-condition applicability in each
/// parameter's own description rather than forcing every argument to be always-required.
/// </para>
/// <para>
/// <b>Tri-state, deliberately not a boolean.</b> <see cref="AwaitOutcome.Unreachable"/> exists
/// because a caller that cannot tell "not yet" from "never" ends up either polling forever or
/// asking a human — the exact defect #1707 exists to remove. Each condition has its own reachability
/// rule; see <see cref="EvaluateParticipantActiveAsync"/>, <see cref="EvaluateInstanceReachesActionAsync"/>
/// and <see cref="EvaluateTransactionSealsAsync"/> for what makes each one unreachable.
/// </para>
/// </remarks>
[McpServerToolType]
public sealed class AwaitConditionTool
{
    private const string ToolName = "sorcha_await_condition";

    /// <summary>
    /// Default wait when the caller does not specify one. Chosen as a multiple (~25x) of the poll
    /// interval — long enough to ride out a docket-seal or projection-fold cycle (RehearsalOrchestrationService
    /// observes those completing in 5-10s) — while leaving comfortable margin under the ~60s call
    /// timeout many MCP clients apply by default, so the SERVER always returns before the CLIENT
    /// gives up.
    /// </summary>
    private const int DefaultTimeoutSeconds = 25;

    /// <summary>
    /// Hard ceiling on the per-call <c>timeoutSeconds</c> argument. 55s, not 60s: this call is a
    /// live inbound MCP request, not an internal service call — it must return with margin under a
    /// 60s client timeout rather than race it, so the cap is set 5s inside that common default
    /// rather than at it.
    /// </summary>
    private const int HardMaxTimeoutSeconds = 55;

    /// <summary>Terminal instance states that mean an instance can never move further.</summary>
    private static readonly string[] AdverseTerminalStates = ["Rejected", "TimedOut", "Cancelled"];

    private readonly IMcpAuthorizationService _authService;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly IBlueprintServiceClient _blueprintClient;
    private readonly IRegisterServiceClient _registerClient;
    private readonly ILogger<AwaitConditionTool> _logger;
    private readonly TimeSpan _pollInterval;

    /// <summary>
    /// Creates the tool. <paramref name="pollInterval"/> is a test-only seam (mirrors
    /// <c>RehearsalOrchestrationService</c>'s own optional constructor timing parameters) — DI never
    /// registers a <see cref="TimeSpan"/>, so production always gets the ~1s default.
    /// </summary>
    public AwaitConditionTool(
        IMcpAuthorizationService authService,
        IServiceAvailabilityTracker availabilityTracker,
        IBlueprintServiceClient blueprintClient,
        IRegisterServiceClient registerClient,
        ILogger<AwaitConditionTool> logger,
        TimeSpan? pollInterval = null)
    {
        _authService = authService;
        _availabilityTracker = availabilityTracker;
        _blueprintClient = blueprintClient;
        _registerClient = registerClient;
        _logger = logger;
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(1);
    }

    /// <summary>
    /// Blocks the caller while polling for one of three ledger conditions, returning as soon as the
    /// condition is met, is found to be permanently unreachable, or the timeout elapses.
    /// </summary>
    [McpServerTool(Name = ToolName)]
    [Description("Blocks the calling MCP request for up to the given timeout — 25 seconds by default, 55 seconds maximum — while polling the ledger about once a second for one of three conditions selected by 'condition': a blueprint participant role becoming Active on a register (ParticipantActive), a workflow instance reaching a given action id as its current action or completing when no actionId is given (InstanceReachesAction), or a submitted transaction acquiring a docket number (TransactionSeals). It returns a three-way outcome rather than a boolean: Met when the condition has happened, NotYet when the timeout elapsed without it happening yet (call this tool again to keep waiting — this is not a failure), or Unreachable when the condition can now never be met, for example the instance was rejected, already completed without reaching the target action, or has already advanced past it. Call this when you already know what you are waiting for and would otherwise write your own poll loop over sorcha_participant_list, sorcha_workflow_status, or sorcha_transaction_status; use those tools instead when you want to inspect current state once without waiting, or once this tool reports Unreachable and you need to read the final state it settled into.")]
    public async Task<AwaitConditionResult> AwaitAsync(
        [Description("Which condition to wait for. One of 'ParticipantActive', 'InstanceReachesAction', or 'TransactionSeals' (case-insensitive).")]
        string condition,
        [Description("The register id. Required for ParticipantActive and TransactionSeals; ignored for InstanceReachesAction.")]
        string? registerId = null,
        [Description("The blueprint participant (role) name to wait for — matches the ParticipantName sorcha_participant_list reports. Required for ParticipantActive.")]
        string? participantName = null,
        [Description("The workflow instance id to watch. Required for InstanceReachesAction.")]
        string? workflowInstanceId = null,
        [Description("The action id to wait to become the instance's current action, for InstanceReachesAction. Omit to wait for the instance to complete instead of reaching a specific action.")]
        string? actionId = null,
        [Description("The transaction id to wait to seal (acquire a docket number). Required for TransactionSeals.")]
        string? transactionId = null,
        [Description("Maximum seconds to block before returning a NotYet result (default 25, hard maximum 55 — this call BLOCKS for up to this long).")]
        int timeoutSeconds = DefaultTimeoutSeconds,
        CancellationToken cancellationToken = default)
    {
        if (!_authService.CanInvokeTool(ToolName))
        {
            return Fail("Unauthorized", "Access denied. This tool requires an authenticated consumer- or platform-tier caller.");
        }

        if (!TryParseCondition(condition, out var kind))
        {
            return Fail("Error",
                $"condition '{condition}' is not recognised. Use one of 'ParticipantActive', "
                + "'InstanceReachesAction', or 'TransactionSeals'.");
        }

        int? parsedActionId = null;
        switch (kind)
        {
            case AwaitConditionKind.ParticipantActive:
                if (string.IsNullOrWhiteSpace(registerId))
                {
                    return Fail("Error", "registerId is required for condition=ParticipantActive.");
                }
                if (string.IsNullOrWhiteSpace(participantName))
                {
                    return Fail("Error", "participantName is required for condition=ParticipantActive.");
                }
                break;

            case AwaitConditionKind.InstanceReachesAction:
                if (string.IsNullOrWhiteSpace(workflowInstanceId))
                {
                    return Fail("Error", "workflowInstanceId is required for condition=InstanceReachesAction.");
                }
                if (!string.IsNullOrWhiteSpace(actionId))
                {
                    if (!int.TryParse(actionId, out var parsed))
                    {
                        return Fail("Error", $"actionId '{actionId}' is not a valid integer.");
                    }
                    parsedActionId = parsed;
                }
                break;

            case AwaitConditionKind.TransactionSeals:
                if (string.IsNullOrWhiteSpace(registerId))
                {
                    return Fail("Error", "registerId is required for condition=TransactionSeals.");
                }
                if (string.IsNullOrWhiteSpace(transactionId))
                {
                    return Fail("Error", "transactionId is required for condition=TransactionSeals.");
                }
                break;
        }

        var serviceName = kind == AwaitConditionKind.InstanceReachesAction ? "Blueprint" : "Register";
        if (!_availabilityTracker.IsServiceAvailable(serviceName))
        {
            return Fail("Unavailable", $"{serviceName} service is currently unavailable. Please try again later.");
        }

        var boundedTimeout = TimeSpan.FromSeconds(
            Math.Clamp(timeoutSeconds <= 0 ? DefaultTimeoutSeconds : timeoutSeconds, 1, HardMaxTimeoutSeconds));

        _logger.LogInformation(
            "Awaiting condition {Condition} (timeout {TimeoutSeconds}s)", kind, boundedTimeout.TotalSeconds);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            while (true)
            {
                var (outcome, message) = kind switch
                {
                    AwaitConditionKind.ParticipantActive =>
                        await EvaluateParticipantActiveAsync(registerId!, participantName!, cancellationToken),
                    AwaitConditionKind.InstanceReachesAction =>
                        await EvaluateInstanceReachesActionAsync(workflowInstanceId!, parsedActionId, cancellationToken),
                    AwaitConditionKind.TransactionSeals =>
                        await EvaluateTransactionSealsAsync(registerId!, transactionId!, cancellationToken),
                    _ => throw new InvalidOperationException($"Unhandled condition {kind}"),
                };

                _availabilityTracker.RecordSuccess(serviceName);

                if (outcome != AwaitOutcome.NotYet)
                {
                    return Success(outcome, message, stopwatch);
                }

                if (stopwatch.Elapsed >= boundedTimeout)
                {
                    return Success(AwaitOutcome.NotYet,
                        $"{message} Timed out after waiting {boundedTimeout.TotalSeconds:0}s — call "
                        + $"{ToolName} again to keep waiting.", stopwatch);
                }

                var remaining = boundedTimeout - stopwatch.Elapsed;
                var delay = remaining < _pollInterval ? remaining : _pollInterval;
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cancellationToken);
                }
            }
        }
        catch (AwaitEvaluationException ex)
        {
            _availabilityTracker.RecordSuccess(serviceName);
            return Fail(ex.IsForbidden() ? "Refused" : "Error", ex.Message, (int)stopwatch.ElapsedMilliseconds);
        }
        catch (TaskCanceledException)
        {
            _availabilityTracker.RecordFailure(serviceName);
            return Fail("Timeout", "Request to a backend service timed out while waiting.", (int)stopwatch.ElapsedMilliseconds);
        }
        catch (HttpRequestException ex)
        {
            _availabilityTracker.RecordFailure(serviceName, ex);
            return Fail("Error", $"Failed to connect to a backend service: {ex.Message}", (int)stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _availabilityTracker.RecordFailure(serviceName, ex);
            _logger.LogError(ex, "Unexpected error awaiting condition {Condition}", kind);
            return Fail("Error", "An unexpected error occurred while awaiting the condition.", (int)stopwatch.ElapsedMilliseconds);
        }
    }

    // -------------------------------------------------------------------------
    // Condition evaluators — each called once per poll, each returning the tri-state answer for
    // THIS poll only. The outer loop in AwaitAsync owns bounding/retrying/timing out.
    // -------------------------------------------------------------------------

    /// <summary>
    /// ParticipantActive: met when an Active record named <paramref name="participantName"/> is
    /// published on the register (the same join <see cref="ParticipantListTool"/> reads). Unreachable
    /// only when the register itself does not exist — a role binding can never appear on a register
    /// that was never created. A Revoked/Deprecated record for the same name is NOT treated as
    /// unreachable: a replacement record can still be published later, so that case stays NotYet.
    /// </summary>
    private async Task<(AwaitOutcome Outcome, string Message)> EvaluateParticipantActiveAsync(
        string registerId, string participantName, CancellationToken cancellationToken)
    {
        var register = await _registerClient.GetRegisterAsync(registerId, cancellationToken);
        if (register is null)
        {
            return (AwaitOutcome.Unreachable,
                $"Register '{registerId}' does not exist, so participant role '{participantName}' can "
                + "never become Active on it.");
        }

        var page = await _registerClient.GetPublishedParticipantsAsync(
            registerId, skip: 0, top: 200, statusFilter: "active", cancellationToken);

        var match = page.Participants.FirstOrDefault(
            p => string.Equals(p.ParticipantName, participantName, StringComparison.OrdinalIgnoreCase));

        if (match is not null)
        {
            var wallet = match.Addresses.FirstOrDefault()?.WalletAddress ?? "(no wallet address)";
            return (AwaitOutcome.Met,
                $"Participant role '{participantName}' is Active on register '{registerId}', bound to {wallet}.");
        }

        return (AwaitOutcome.NotYet,
            $"No Active participant record named '{participantName}' is published on register '{registerId}' yet.");
    }

    /// <summary>
    /// InstanceReachesAction: with an actionId, met exactly when that id is in the instance's
    /// CurrentActionIds right now (it is currently awaiting execution). Unreachable when: the
    /// instance does not exist; the instance reached an adverse terminal state (Rejected, TimedOut,
    /// Cancelled); the instance Completed (nothing can become "current" again); or the instance is
    /// still Active but the target action already appears in its transaction history while not
    /// currently pending — it already happened and the instance has moved on, so waiting for it to
    /// become current again is what #1707 calls "advanced past". Without an actionId, this instead
    /// waits for the instance to complete: met on Completed, unreachable on any adverse terminal
    /// state, not-yet while Active.
    /// </summary>
    private async Task<(AwaitOutcome Outcome, string Message)> EvaluateInstanceReachesActionAsync(
        string workflowInstanceId, int? targetActionId, CancellationToken cancellationToken)
    {
        var read = await _blueprintClient.GetWorkflowStatusAsync(workflowInstanceId, cancellationToken);

        if (read.IsNotFound)
        {
            var target = targetActionId is { } a ? $"action {a}" : "completion";
            return (AwaitOutcome.Unreachable,
                $"No workflow instance '{workflowInstanceId}' exists, so it can never reach {target}.");
        }

        if (!read.IsSuccess || string.IsNullOrWhiteSpace(read.Body))
        {
            throw new AwaitEvaluationException(
                BlueprintReadExplanation.ForInstance(read, workflowInstanceId), read.IsForbidden);
        }

        // Hand-parsed via JsonDocument rather than JsonSerializer.Deserialize<T> into a declared
        // DTO — deliberately: the mcp-response-shapes gate treats every JsonSerializer.Deserialize<T>
        // call as pinning T against a server response shape, and a class declared for that purpose
        // here would sit in the SAME FILE as this tool's Register-owned reads (ParticipantActive,
        // TransactionSeals), which pools candidate server types across both owners and risks pairing
        // it against the wrong one. A named-tuple/local-variable extraction carries no such risk.
        string? registerId;
        string state;
        var currentActionIds = new List<int>();
        try
        {
            using var doc = JsonDocument.Parse(read.Body);
            var root = doc.RootElement;
            registerId = root.TryGetProperty("registerId", out var registerIdEl)
                && registerIdEl.ValueKind == JsonValueKind.String
                ? registerIdEl.GetString()
                : null;
            state = InstanceStateResolver.Resolve(
                root.TryGetProperty("state", out var stateEl) ? stateEl : null);
            if (root.TryGetProperty("currentActionIds", out var idsEl) && idsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var idEl in idsEl.EnumerateArray())
                {
                    if (idEl.TryGetInt32(out var idValue))
                    {
                        currentActionIds.Add(idValue);
                    }
                }
            }
        }
        catch (JsonException)
        {
            throw new AwaitEvaluationException(
                $"Failed to parse the workflow instance '{workflowInstanceId}' response.", isForbidden: false);
        }

        if (targetActionId is null)
        {
            if (state == "Completed")
            {
                return (AwaitOutcome.Met, $"Workflow instance '{workflowInstanceId}' has completed.");
            }
            if (AdverseTerminalStates.Contains(state))
            {
                return (AwaitOutcome.Unreachable,
                    $"Workflow instance '{workflowInstanceId}' reached a terminal state of {state} without "
                    + "completing — it will never complete now.");
            }
            return (AwaitOutcome.NotYet, $"Workflow instance '{workflowInstanceId}' is still {state}.");
        }

        if (currentActionIds.Contains(targetActionId.Value))
        {
            return (AwaitOutcome.Met,
                $"Workflow instance '{workflowInstanceId}' is now awaiting action {targetActionId} — it is "
                + "the current action.");
        }

        if (AdverseTerminalStates.Contains(state))
        {
            return (AwaitOutcome.Unreachable,
                $"Workflow instance '{workflowInstanceId}' reached a terminal state of {state} without ever "
                + $"reaching action {targetActionId} — it will never become current now.");
        }

        if (state == "Completed")
        {
            return (AwaitOutcome.Unreachable,
                $"Workflow instance '{workflowInstanceId}' has completed, so action {targetActionId} can no "
                + "longer become the current action.");
        }

        // Active, but not currently awaiting the target action — has it already happened and the
        // instance moved past it? A transient failure reading history degrades to NotYet rather than
        // aborting the whole wait; it will be checked again on the next poll.
        if (!string.IsNullOrWhiteSpace(registerId))
        {
            try
            {
                var transactions = await _registerClient.GetTransactionsByInstanceIdAsync(
                    registerId, workflowInstanceId, cancellationToken);
                if (transactions.Any(t => t.MetaData?.ActionId is { } sealedActionId
                    && (int)sealedActionId == targetActionId.Value))
                {
                    return (AwaitOutcome.Unreachable,
                        $"Action {targetActionId} already occurred on workflow instance "
                        + $"'{workflowInstanceId}' and is no longer the current action — the instance has "
                        + "advanced past it.");
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                _logger.LogDebug(ex,
                    "Transient failure reading transaction history for instance {InstanceId} while awaiting "
                    + "action {ActionId}; treating this poll as NotYet", workflowInstanceId, targetActionId);
            }
        }

        return (AwaitOutcome.NotYet,
            $"Workflow instance '{workflowInstanceId}' has not yet reached action {targetActionId}; it is "
            + $"currently awaiting {(currentActionIds.Count > 0 ? string.Join(", ", currentActionIds) : "nothing")}.");
    }

    /// <summary>
    /// TransactionSeals: met when the transaction carries a non-null DocketNumber. Unreachable only
    /// when the transaction does not exist on the FIRST poll — by the time a caller has a
    /// transactionId to wait on, a prior submission already put it in the register's mempool, so an
    /// immediate not-found is treated as a bad id rather than eventual-consistency lag. A schema
    /// violation is silently refused by the validator and never seals (CLAUDE.md pattern —
    /// "What the validator actually enforces"), so there is deliberately NO other unreachable
    /// signal here: this platform has no positive "will never seal" event to observe, and a caller
    /// whose transaction is stuck this way sees NotYet at every poll, including after timeout.
    /// </summary>
    private async Task<(AwaitOutcome Outcome, string Message)> EvaluateTransactionSealsAsync(
        string registerId, string transactionId, CancellationToken cancellationToken)
    {
        var transaction = await _registerClient.GetTransactionAsync(registerId, transactionId, cancellationToken);

        if (transaction is null)
        {
            return (AwaitOutcome.Unreachable,
                $"Transaction '{transactionId}' was not found in register '{registerId}' — check the id; "
                + "it cannot seal if it was never submitted there.");
        }

        if (transaction.DocketNumber is { } docketNumber)
        {
            return (AwaitOutcome.Met,
                $"Transaction '{transactionId}' sealed into docket {docketNumber} on register '{registerId}'.");
        }

        return (AwaitOutcome.NotYet,
            $"Transaction '{transactionId}' has not sealed yet — it is still awaiting a docket on register "
            + $"'{registerId}'.");
    }

    private static bool TryParseCondition(string? value, out AwaitConditionKind kind)
    {
        kind = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        foreach (var candidate in Enum.GetValues<AwaitConditionKind>())
        {
            if (string.Equals(value, candidate.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                kind = candidate;
                return true;
            }
        }

        return false;
    }

    private static AwaitConditionResult Success(AwaitOutcome outcome, string message, Stopwatch stopwatch) => new()
    {
        Status = "Success",
        Message = message,
        CheckedAt = DateTimeOffset.UtcNow,
        ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
        Outcome = outcome.ToString(),
        WaitedSeconds = Math.Round(stopwatch.Elapsed.TotalSeconds, 1),
    };

    private static AwaitConditionResult Fail(string status, string message, int responseTimeMs = 0) => new()
    {
        Status = status,
        Message = message,
        CheckedAt = DateTimeOffset.UtcNow,
        ResponseTimeMs = responseTimeMs,
    };

    /// <summary>
    /// Breaks out of the poll loop for a genuine infrastructure refusal or error — NOT for an
    /// Unreachable determination, which each evaluator returns as a normal tri-state result so it
    /// reaches the caller as <c>Status=Success, Outcome=Unreachable</c> rather than a transport
    /// failure. This exception exists only for reads that failed for reasons unrelated to the
    /// condition itself (403, unparseable body, 5xx) and must not be silently retried as NotYet.
    /// </summary>
    /// <remarks>
    /// Deliberately NOT a primary-constructor class and <see cref="IsForbidden"/> deliberately NOT a
    /// public/internal property — the mcp-response-shapes gate reads a primary constructor's
    /// parameter list the same way it reads a record's positional parameters (both become candidate
    /// wire properties), and any public/internal property on a type declared inside a
    /// <c>[McpServerToolType]</c> file makes it a candidate response DTO. An exception carrying a
    /// stray bool is not one, so it uses an ordinary constructor body and a non-public accessor.
    /// </remarks>
    private sealed class AwaitEvaluationException : Exception
    {
        private readonly bool _isForbidden;

        public AwaitEvaluationException(string message, bool isForbidden) : base(message)
        {
            _isForbidden = isForbidden;
        }

        /// <summary>True when the underlying read was refused (403) rather than failed outright.</summary>
        internal bool IsForbidden() => _isForbidden;
    }
}

/// <summary>Which ledger condition <see cref="AwaitConditionTool"/> should poll for.</summary>
public enum AwaitConditionKind
{
    /// <summary>A blueprint participant role becomes Active on a register.</summary>
    ParticipantActive,

    /// <summary>A workflow instance reaches a given action id as current, or completes.</summary>
    InstanceReachesAction,

    /// <summary>A transaction acquires a docket number.</summary>
    TransactionSeals,
}

/// <summary>
/// The tri-state answer #1707 exists to make possible: not just whether the condition holds NOW,
/// but whether it is even still POSSIBLE.
/// </summary>
public enum AwaitOutcome
{
    /// <summary>The condition has happened.</summary>
    Met,

    /// <summary>The condition has not happened yet; call the tool again to keep waiting.</summary>
    NotYet,

    /// <summary>The condition can now never be met — waiting further would be futile.</summary>
    Unreachable,
}

/// <summary>Result of a bounded wait for a ledger condition.</summary>
public sealed record AwaitConditionResult
{
    /// <summary>Transport-level status: Success, Error, Unauthorized, Unavailable, Refused, or Timeout.</summary>
    public required string Status { get; init; }

    /// <summary>Human-readable message about the result, including WHY when Unreachable.</summary>
    public required string Message { get; init; }

    /// <summary>When the result was produced (i.e. when the wait ended).</summary>
    public required DateTimeOffset CheckedAt { get; init; }

    /// <summary>How long the call took to return, in milliseconds — up to the timeout.</summary>
    public int ResponseTimeMs { get; init; }

    /// <summary>
    /// The tri-state domain answer: Met, NotYet, or Unreachable. Present only when
    /// <see cref="Status"/> is Success — a transport-level failure carries no outcome.
    /// </summary>
    public string? Outcome { get; init; }

    /// <summary>How long this call actually waited, in seconds, rounded to one decimal place.</summary>
    public double WaitedSeconds { get; init; }
}
