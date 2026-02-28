// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;

namespace Sorcha.McpServer.Tools.Participant;

/// <summary>
/// Participant tool for submitting action data.
/// </summary>
[McpServerToolType]
public sealed class ActionSubmitTool
{
    private readonly IMcpSessionService _sessionService;
    private readonly IMcpAuthorizationService _authService;
    private readonly IMcpErrorHandler _errorHandler;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ActionSubmitTool> _logger;
    private readonly string _blueprintServiceEndpoint;

    public ActionSubmitTool(
        IMcpSessionService sessionService,
        IMcpAuthorizationService authService,
        IMcpErrorHandler errorHandler,
        IServiceAvailabilityTracker availabilityTracker,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<ActionSubmitTool> logger)
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
    /// Submits data for an action, completing it and advancing the workflow.
    /// </summary>
    /// <param name="actionInstanceId">The action instance ID.</param>
    /// <param name="dataJson">The action data in JSON format.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result of the submission.</returns>
    [McpServerTool(Name = "sorcha_action_submit")]
    [Description("Submit data for an action to complete it and advance the workflow. The data must conform to the action's input schema.")]
    public async Task<ActionSubmitResult> SubmitActionAsync(
        [Description("The action instance ID")] string actionInstanceId,
        [Description("The action data in JSON format")] string dataJson,
        CancellationToken cancellationToken = default)
    {
        // Authorization check
        if (!_authService.CanInvokeTool("sorcha_action_submit"))
        {
            return new ActionSubmitResult
            {
                Status = "Unauthorized",
                Message = "Access denied. This tool requires the sorcha:participant role.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // Validate inputs
        if (string.IsNullOrWhiteSpace(actionInstanceId))
        {
            return new ActionSubmitResult
            {
                Status = "Error",
                Message = "Action instance ID is required.",
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

        _logger.LogInformation("Submitting action {ActionInstanceId}", actionInstanceId);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(60);

            var url = $"{_blueprintServiceEndpoint.TrimEnd('/')}/api/actions/{actionInstanceId}/submit";

            var content = new StringContent(dataJson, Encoding.UTF8, "application/json");
            var response = await client.PostAsync(url, content, cancellationToken);

            stopwatch.Stop();

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Action submit failed: HTTP {StatusCode} - {Error}", response.StatusCode, errorContent);

                _availabilityTracker.RecordSuccess("Blueprint");

                try
                {
                    var errorResponse = JsonSerializer.Deserialize<ErrorResponse>(errorContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    return new ActionSubmitResult
                    {
                        Status = "Error",
                        Message = errorResponse?.Error ?? "Action submission failed.",
                        CheckedAt = DateTimeOffset.UtcNow,
                        ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                        ValidationErrors = errorResponse?.ValidationErrors ?? []
                    };
                }
                catch (JsonException)
                {
                    return new ActionSubmitResult
                    {
                        Status = "Error",
                        Message = $"Action submission failed with status {(int)response.StatusCode}.",
                        CheckedAt = DateTimeOffset.UtcNow,
                        ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                    };
                }
            }

            // Record success
            _availabilityTracker.RecordSuccess("Blueprint");

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<SubmitResponse>(responseContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            _logger.LogInformation(
                "Action {ActionInstanceId} submitted successfully in {ElapsedMs}ms",
                actionInstanceId, stopwatch.ElapsedMilliseconds);

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

    private sealed class ErrorResponse
    {
        public string? Error { get; set; }
        public List<string>? ValidationErrors { get; set; }
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
