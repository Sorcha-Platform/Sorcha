// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.ServiceClients.Blueprint;

namespace Sorcha.McpServer.Tools.Admin;

/// <summary>
/// Administrator tool that polls the lifecycle state of a presentation attempt (Feature 111).
/// Routes through the typed <see cref="IBlueprintServiceClient"/>.
/// </summary>
[McpServerToolType]
public sealed class PresentationStatusTool
{
    private const string ToolName = "sorcha_presentation_status";
    private const string ServiceName = "Blueprint";

    private readonly IMcpAuthorizationService _authService;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly IBlueprintServiceClient _blueprintClient;
    private readonly ILogger<PresentationStatusTool> _logger;

    public PresentationStatusTool(
        IMcpAuthorizationService authService,
        IServiceAvailabilityTracker availabilityTracker,
        IBlueprintServiceClient blueprintClient,
        ILogger<PresentationStatusTool> logger)
    {
        _authService = authService;
        _availabilityTracker = availabilityTracker;
        _blueprintClient = blueprintClient;
        _logger = logger;
    }

    /// <summary>
    /// Gets the current lifecycle state of a presentation attempt.
    /// </summary>
    /// <param name="presentationRequestId">The presentation request ID to poll.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The lifecycle state + expiry JSON, or NotFound if unknown/expired.</returns>
    [McpServerTool(Name = ToolName)]
    [Description("Polls the lifecycle state of a Feature-111 presentation attempt to its terminal outcome — awaiting-presentation, success, decline, abandoned, abandoned-with-late-outcome, or expired — returning only the state and attempt expiry (no register, instance, or consumer metadata is exposed). Call this after sorcha_presentation_request to learn whether the citizen's wallet presented the requested credential and whether verification succeeded. Use this for verification (presentation) flows; use sorcha_credential_offer with an offerId instead when polling an issuance offer, and read the register transaction stream when you need the authoritative full history rather than just the current state.")]
    public async Task<PresentationStatusToolResult> GetStatusAsync(
        [Description("The presentation request ID to poll")] string presentationRequestId,
        CancellationToken cancellationToken = default)
    {
        if (!_authService.CanInvokeTool(ToolName))
        {
            return new PresentationStatusToolResult
            {
                Status = "Unauthorized",
                Message = "Access denied. This tool requires the sorcha:admin role.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        if (string.IsNullOrWhiteSpace(presentationRequestId))
        {
            return new PresentationStatusToolResult
            {
                Status = "Error",
                Message = "presentationRequestId is required.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        if (!_availabilityTracker.IsServiceAvailable(ServiceName))
        {
            return new PresentationStatusToolResult
            {
                Status = "Unavailable",
                Message = "Blueprint service is currently unavailable. Please try again later.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        _logger.LogInformation("Getting presentation status for request {PresentationRequestId}", presentationRequestId);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var body = await _blueprintClient.GetPresentationStatusAsync(presentationRequestId, cancellationToken);
            stopwatch.Stop();
            _availabilityTracker.RecordSuccess(ServiceName);

            if (string.IsNullOrWhiteSpace(body))
            {
                return new PresentationStatusToolResult
                {
                    Status = "NotFound",
                    Message = $"Presentation request '{presentationRequestId}' was not found or has expired.",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

            return new PresentationStatusToolResult
            {
                Status = "Success",
                Message = $"Presentation status retrieved for request '{presentationRequestId}'.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                StatusJson = body
            };
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure(ServiceName);
            _logger.LogWarning("Presentation-status query timed out for request {PresentationRequestId}", presentationRequestId);
            return new PresentationStatusToolResult
            {
                Status = "Timeout",
                Message = "Request to blueprint service timed out.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure(ServiceName, ex);
            _logger.LogError(ex, "Failed to get presentation status for request {PresentationRequestId}", presentationRequestId);
            return new PresentationStatusToolResult
            {
                Status = "Error",
                Message = $"Failed to get presentation status: {ex.Message}",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
    }
}

/// <summary>
/// Result of a presentation-status query.
/// </summary>
public sealed record PresentationStatusToolResult
{
    /// <summary>Status: Success, NotFound, Error, Unavailable, Timeout, or Unauthorized.</summary>
    public required string Status { get; init; }

    /// <summary>Human-readable message about the result.</summary>
    public required string Message { get; init; }

    /// <summary>When the query was performed.</summary>
    public required DateTimeOffset CheckedAt { get; init; }

    /// <summary>Response time in milliseconds.</summary>
    public int ResponseTimeMs { get; init; }

    /// <summary>The raw presentation-status JSON body (state + expiry) on success.</summary>
    public string? StatusJson { get; init; }
}
