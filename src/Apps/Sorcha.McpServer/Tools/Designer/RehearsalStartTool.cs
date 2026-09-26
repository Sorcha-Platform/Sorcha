// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.ServiceClients.Blueprint;
using Sorcha.ServiceClients.Blueprint.Models;

namespace Sorcha.McpServer.Tools.Designer;

/// <summary>
/// Designer tool that starts a full rehearsal of a draft blueprint (Feature 142) — the only thing
/// that records the <c>RehearsalPass</c> <c>sorcha_blueprint_publish</c>'s safety gate checks for.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists (#1691).</b> <c>PublishGate</c> refuses a publish whose
/// executable-definition hash has no <c>RehearsalPass</c>, and only
/// <c>RehearsalOrchestrationService</c> writes one. Until these tools existed nothing on the MCP
/// surface could drive it, so EVERY MCP publish reached the human override — on cold-start runs #4,
/// #5 and #7 alike. The gate was a fixed toll rather than a gate, and each audited override looked
/// like a considered human decision when it was the only way through. Run #8's agent ran
/// <c>sorcha_blueprint_simulate</c> and <c>sorcha_disclosure_analysis</c> over every action
/// believing that was the rehearsal; neither writes a pass.
/// </para>
/// <para>
/// <b>What a start does, server-side:</b> provisions (or reuses) the organisation's devMode sandbox
/// register, mints one stand-in wallet per participant, publishes a sandbox copy of the draft,
/// and creates an instance. No real register is touched, which is why no person has to approve it.
/// </para>
/// </remarks>
[McpServerToolType]
public sealed class RehearsalStartTool
{
    private const string ToolName = "sorcha_rehearsal_start";

    private readonly IMcpAuthorizationService _authService;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly IBlueprintServiceClient _blueprintClient;
    private readonly ILogger<RehearsalStartTool> _logger;

    /// <summary>Creates the tool.</summary>
    public RehearsalStartTool(
        IMcpAuthorizationService authService,
        IServiceAvailabilityTracker availabilityTracker,
        IBlueprintServiceClient blueprintClient,
        ILogger<RehearsalStartTool> logger)
    {
        _authService = authService;
        _availabilityTracker = availabilityTracker;
        _blueprintClient = blueprintClient;
        _logger = logger;
    }

    /// <summary>Starts a full rehearsal of a draft blueprint on the organisation's sandbox register.</summary>
    /// <param name="blueprintId">The draft blueprint to rehearse.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The started rehearsal: its id, the steps, and the first action to submit.</returns>
    [McpServerTool(Name = ToolName, Destructive = false, ReadOnly = false, Idempotent = false)]
    [Description("Starts a full rehearsal of a draft blueprint: it runs on your organisation's sandbox register, never a real one, with a stand-in wallet for each participant, and returns a rehearsal id plus the first action to submit. Call this when a draft built with sorcha_blueprint_create is finished and you are about to publish it: a rehearsal driven to a pass is the ONLY thing that clears the safety gate on sorcha_blueprint_publish, so that publishing then needs no person to waive it. Dry runs do not count — sorcha_blueprint_simulate and sorcha_disclosure_analysis never record a rehearsal pass. After starting, submit each current action with sorcha_rehearsal_step until the status is Passed. No person is asked to approve a rehearsal, because nothing leaves the sandbox.")]
    public async Task<RehearsalToolResult> StartRehearsalAsync(
        [Description("The draft blueprint's ID")] string blueprintId,
        CancellationToken cancellationToken = default)
    {
        if (!_authService.CanInvokeTool(ToolName))
        {
            return RehearsalResultMapper.Fail(
                "Unauthorized",
                "Access denied. Rehearsing a blueprint requires the sorcha:designer role — the same "
                + "authority that authors one.");
        }

        if (string.IsNullOrWhiteSpace(blueprintId))
        {
            return RehearsalResultMapper.Fail(
                "ValidationError", "blueprintId is required.", ["blueprintId is required"]);
        }

        if (!_availabilityTracker.IsServiceAvailable("Blueprint"))
        {
            return RehearsalResultMapper.Fail(
                "Unavailable", "Blueprint service is currently unavailable. Please try again later. No rehearsal was started.");
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await _blueprintClient.StartRehearsalAsync(
                blueprintId, new StartRehearsalRequest { Mode = RehearsalMode.Full }, cancellationToken);
            stopwatch.Stop();

            // Any HTTP answer, refusal included, proves the service is up (the publish tool learned
            // this the hard way: counting deterministic refusals as outages disabled every
            // Blueprint tool behind a false "unavailable").
            _availabilityTracker.RecordSuccess("Blueprint");

            if (result.Rehearsal is { } rehearsal)
            {
                _logger.LogInformation(
                    "Started rehearsal {RehearsalId} of blueprint {BlueprintId} via MCP (outcome {Outcome})",
                    rehearsal.RehearsalId, blueprintId, rehearsal.Outcome);
                return RehearsalResultMapper.FromRehearsal(rehearsal, (int)stopwatch.ElapsedMilliseconds);
            }

            return RehearsalResultMapper.FromRefusal(
                result.Refusal!, $"start a rehearsal of blueprint '{blueprintId}'", (int)stopwatch.ElapsedMilliseconds);
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("Blueprint");
            _logger.LogWarning("Starting a rehearsal of blueprint {BlueprintId} timed out", blueprintId);
            return RehearsalResultMapper.Fail(
                "Timeout",
                $"Starting the rehearsal of blueprint '{blueprintId}' timed out. A first rehearsal in an "
                + "organisation also creates its sandbox register, which takes longer; starting again is "
                + "safe, because an unfinished rehearsal records nothing.",
                elapsedMs: (int)stopwatch.ElapsedMilliseconds);
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("Blueprint", ex);
            _logger.LogWarning(ex, "Starting a rehearsal of blueprint {BlueprintId} failed", blueprintId);
            return RehearsalResultMapper.Fail(
                "Error", $"The rehearsal request failed: {ex.Message}. No rehearsal was started.",
                elapsedMs: (int)stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("Blueprint", ex);
            _logger.LogError(ex, "Unexpected error starting a rehearsal of blueprint {BlueprintId}", blueprintId);
            return RehearsalResultMapper.Fail(
                "Error", "An unexpected error occurred while starting the rehearsal.",
                elapsedMs: (int)stopwatch.ElapsedMilliseconds);
        }
    }
}
