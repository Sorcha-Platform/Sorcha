// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;

namespace Sorcha.McpServer.Tools.Participant;

/// <summary>
/// Participant tool for checking workflow instance status.
/// </summary>
[McpServerToolType]
public sealed class WorkflowStatusTool
{
    private readonly IMcpSessionService _sessionService;
    private readonly IMcpAuthorizationService _authService;
    private readonly IMcpErrorHandler _errorHandler;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WorkflowStatusTool> _logger;
    private readonly string _blueprintServiceEndpoint;

    public WorkflowStatusTool(
        IMcpSessionService sessionService,
        IMcpAuthorizationService authService,
        IMcpErrorHandler errorHandler,
        IServiceAvailabilityTracker availabilityTracker,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<WorkflowStatusTool> logger)
    {
        _sessionService = sessionService;
        _authService = authService;
        _errorHandler = errorHandler;
        _availabilityTracker = availabilityTracker;
        _httpClientFactory = httpClientFactory;
        _logger = logger;

        _blueprintServiceEndpoint = configuration["ServiceClients:BlueprintService:Address"] ?? "http://localhost:5000";
    }

    /// <summary>
    /// Gets the status of a workflow instance.
    /// </summary>
    /// <param name="workflowInstanceId">The workflow instance ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Workflow status and progress.</returns>
    [McpServerTool(Name = "sorcha_workflow_status")]
    [Description("Check the status of a workflow instance. Shows current progress, completed actions, and pending actions.")]
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
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(30);

            var url = $"{_blueprintServiceEndpoint.TrimEnd('/')}/api/workflows/{workflowInstanceId}";

            var response = await client.GetAsync(url, cancellationToken);

            stopwatch.Stop();

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Workflow status request failed: HTTP {StatusCode} - {Error}", response.StatusCode, errorContent);

                _availabilityTracker.RecordSuccess("Blueprint");

                try
                {
                    var errorResponse = JsonSerializer.Deserialize<ErrorResponse>(errorContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    return new WorkflowStatusResult
                    {
                        Status = "Error",
                        Message = errorResponse?.Error ?? "Workflow not found.",
                        CheckedAt = DateTimeOffset.UtcNow,
                        ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                    };
                }
                catch (JsonException)
                {
                    return new WorkflowStatusResult
                    {
                        Status = "Error",
                        Message = $"Request failed with status {(int)response.StatusCode}.",
                        CheckedAt = DateTimeOffset.UtcNow,
                        ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                    };
                }
            }

            // Record success
            _availabilityTracker.RecordSuccess("Blueprint");

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
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

    private sealed class ErrorResponse
    {
        public string? Error { get; set; }
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
