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
/// Participant tool for getting action details.
/// </summary>
[McpServerToolType]
public sealed class ActionDetailsTool
{
    private readonly IMcpSessionService _sessionService;
    private readonly IMcpAuthorizationService _authService;
    private readonly IMcpErrorHandler _errorHandler;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ActionDetailsTool> _logger;
    private readonly string _blueprintServiceEndpoint;

    public ActionDetailsTool(
        IMcpSessionService sessionService,
        IMcpAuthorizationService authService,
        IMcpErrorHandler errorHandler,
        IServiceAvailabilityTracker availabilityTracker,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<ActionDetailsTool> logger)
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
    /// Gets details of a specific action instance.
    /// </summary>
    /// <param name="actionInstanceId">The action instance ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Action details including schema and disclosed data.</returns>
    [McpServerTool(Name = "sorcha_action_details")]
    [Description("Get details of a specific action from your inbox. Returns action configuration, input schema, and any data disclosed to you.")]
    public async Task<ActionDetailsResult> GetActionDetailsAsync(
        [Description("The action instance ID")] string actionInstanceId,
        CancellationToken cancellationToken = default)
    {
        // Authorization check
        if (!_authService.CanInvokeTool("sorcha_action_details"))
        {
            return new ActionDetailsResult
            {
                Status = "Unauthorized",
                Message = "Access denied. This tool requires the sorcha:participant role.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // Validate input
        if (string.IsNullOrWhiteSpace(actionInstanceId))
        {
            return new ActionDetailsResult
            {
                Status = "Error",
                Message = "Action instance ID is required.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // Check service availability
        if (!_availabilityTracker.IsServiceAvailable("Blueprint"))
        {
            return new ActionDetailsResult
            {
                Status = "Unavailable",
                Message = "Blueprint service is currently unavailable. Please try again later.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        _logger.LogInformation("Getting action details for {ActionInstanceId}", actionInstanceId);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(30);

            var url = $"{_blueprintServiceEndpoint.TrimEnd('/')}/api/actions/{actionInstanceId}";

            var response = await client.GetAsync(url, cancellationToken);

            stopwatch.Stop();

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Action details request failed: HTTP {StatusCode} - {Error}", response.StatusCode, errorContent);

                _availabilityTracker.RecordSuccess("Blueprint");

                try
                {
                    var errorResponse = JsonSerializer.Deserialize<ErrorResponse>(errorContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    return new ActionDetailsResult
                    {
                        Status = "Error",
                        Message = errorResponse?.Error ?? "Action not found.",
                        CheckedAt = DateTimeOffset.UtcNow,
                        ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                    };
                }
                catch (JsonException)
                {
                    return new ActionDetailsResult
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
            var result = JsonSerializer.Deserialize<ActionDetailsResponse>(responseContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (result == null)
            {
                return new ActionDetailsResult
                {
                    Status = "Error",
                    Message = "Failed to parse action details response.",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

            _logger.LogInformation(
                "Retrieved action details in {ElapsedMs}ms",
                stopwatch.ElapsedMilliseconds);

            return new ActionDetailsResult
            {
                Status = "Success",
                Message = $"Retrieved details for action '{result.Title ?? actionInstanceId}'.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                Action = new ActionDetail
                {
                    ActionInstanceId = result.ActionInstanceId ?? actionInstanceId,
                    WorkflowInstanceId = result.WorkflowInstanceId ?? "",
                    BlueprintId = result.BlueprintId ?? "",
                    BlueprintTitle = result.BlueprintTitle,
                    ActionId = result.ActionId,
                    Title = result.Title ?? "",
                    Description = result.Description,
                    Status = result.Status ?? "Pending",
                    InputSchema = result.InputSchema,
                    DisclosedData = result.DisclosedData,
                    RequiredFields = result.RequiredFields ?? [],
                    AssignedAt = result.AssignedAt,
                    DueAt = result.DueAt
                }
            };
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("Blueprint");

            return new ActionDetailsResult
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

            return new ActionDetailsResult
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

            _logger.LogError(ex, "Unexpected error getting action details");

            return new ActionDetailsResult
            {
                Status = "Error",
                Message = "An unexpected error occurred while getting action details.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
    }

    // Internal response models
    private sealed class ActionDetailsResponse
    {
        public string? ActionInstanceId { get; set; }
        public string? WorkflowInstanceId { get; set; }
        public string? BlueprintId { get; set; }
        public string? BlueprintTitle { get; set; }
        public int ActionId { get; set; }
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? Status { get; set; }
        public string? InputSchema { get; set; }
        public Dictionary<string, object>? DisclosedData { get; set; }
        public List<string>? RequiredFields { get; set; }
        public DateTimeOffset? AssignedAt { get; set; }
        public DateTimeOffset? DueAt { get; set; }
    }

    private sealed class ErrorResponse
    {
        public string? Error { get; set; }
    }
}

/// <summary>
/// Result of getting action details.
/// </summary>
public sealed record ActionDetailsResult
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
    /// The action details.
    /// </summary>
    public ActionDetail? Action { get; init; }
}

/// <summary>
/// Detailed information about an action.
/// </summary>
public sealed record ActionDetail
{
    /// <summary>
    /// The unique action instance ID.
    /// </summary>
    public required string ActionInstanceId { get; init; }

    /// <summary>
    /// The workflow instance ID this action belongs to.
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
    /// The action ID (sequence number).
    /// </summary>
    public int ActionId { get; init; }

    /// <summary>
    /// The action title.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// The action description.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Current status: Pending or InProgress.
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// JSON Schema for the action input data.
    /// </summary>
    public string? InputSchema { get; init; }

    /// <summary>
    /// Data disclosed to the current participant.
    /// </summary>
    public Dictionary<string, object>? DisclosedData { get; init; }

    /// <summary>
    /// List of required field names.
    /// </summary>
    public IReadOnlyList<string> RequiredFields { get; init; } = [];

    /// <summary>
    /// When the action was assigned to the user.
    /// </summary>
    public DateTimeOffset? AssignedAt { get; init; }

    /// <summary>
    /// When the action is due if a deadline is set.
    /// </summary>
    public DateTimeOffset? DueAt { get; init; }
}
