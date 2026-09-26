// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.ServiceClients.Blueprint;

namespace Sorcha.McpServer.Tools.Designer;

/// <summary>
/// Designer tool that reads a rehearsal's current state and log (Feature 142, #1691) — chiefly for
/// recovering after a <c>sorcha_rehearsal_step</c> timeout, when whether the step was applied is
/// unknown.
/// </summary>
[McpServerToolType]
public sealed class RehearsalGetTool
{
    private const string ToolName = "sorcha_rehearsal_get";

    private readonly IMcpAuthorizationService _authService;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly IBlueprintServiceClient _blueprintClient;
    private readonly ILogger<RehearsalGetTool> _logger;

    /// <summary>Creates the tool.</summary>
    public RehearsalGetTool(
        IMcpAuthorizationService authService,
        IServiceAvailabilityTracker availabilityTracker,
        IBlueprintServiceClient blueprintClient,
        ILogger<RehearsalGetTool> logger)
    {
        _authService = authService;
        _availabilityTracker = availabilityTracker;
        _blueprintClient = blueprintClient;
        _logger = logger;
    }

    /// <summary>Reads a rehearsal's state and log.</summary>
    /// <param name="blueprintId">The draft blueprint being rehearsed.</param>
    /// <param name="rehearsalId">The rehearsal id from sorcha_rehearsal_start.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The rehearsal's outcome, steps, current action and log.</returns>
    [McpServerTool(Name = ToolName, Destructive = false, ReadOnly = true, Idempotent = true)]
    [Description("Reads a rehearsal's current outcome, its steps, the action to submit next, and its activity log, without changing anything. Call this when a sorcha_rehearsal_step call timed out or failed in transit, to learn whether the step was applied before submitting again, rather than resubmitting blind; the log also records why a failed rehearsal failed.")]
    public async Task<RehearsalToolResult> GetRehearsalAsync(
        [Description("The draft blueprint's ID")] string blueprintId,
        [Description("The rehearsal ID returned by sorcha_rehearsal_start")] string rehearsalId,
        CancellationToken cancellationToken = default)
    {
        if (!_authService.CanInvokeTool(ToolName))
        {
            return RehearsalResultMapper.Fail(
                "Unauthorized",
                "Access denied. Reading a rehearsal requires the sorcha:designer role.");
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

        if (errors.Count > 0)
        {
            return RehearsalResultMapper.Fail("ValidationError", string.Join("; ", errors) + ".", errors);
        }

        if (!_availabilityTracker.IsServiceAvailable("Blueprint"))
        {
            return RehearsalResultMapper.Fail(
                "Unavailable", "Blueprint service is currently unavailable. Please try again later.");
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await _blueprintClient.GetRehearsalAsync(blueprintId, rehearsalGuid, cancellationToken);
            stopwatch.Stop();
            _availabilityTracker.RecordSuccess("Blueprint");

            return result.Rehearsal is { } rehearsal
                ? RehearsalResultMapper.FromRehearsal(rehearsal, (int)stopwatch.ElapsedMilliseconds)
                : RehearsalResultMapper.FromRefusal(
                    result.Refusal!, $"read rehearsal {rehearsalGuid}", (int)stopwatch.ElapsedMilliseconds);
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("Blueprint");
            return RehearsalResultMapper.Fail(
                "Timeout", "Reading the rehearsal timed out. Try again.",
                elapsedMs: (int)stopwatch.ElapsedMilliseconds);
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("Blueprint", ex);
            _logger.LogWarning(ex, "Reading rehearsal {RehearsalId} failed", rehearsalGuid);
            return RehearsalResultMapper.Fail(
                "Error", $"Reading the rehearsal failed: {ex.Message}.",
                elapsedMs: (int)stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("Blueprint", ex);
            _logger.LogError(ex, "Unexpected error reading rehearsal {RehearsalId}", rehearsalGuid);
            return RehearsalResultMapper.Fail(
                "Error", "An unexpected error occurred while reading the rehearsal.",
                elapsedMs: (int)stopwatch.ElapsedMilliseconds);
        }
    }
}
