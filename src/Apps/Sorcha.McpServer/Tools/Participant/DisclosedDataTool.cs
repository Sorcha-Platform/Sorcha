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
/// Participant tool for viewing data disclosed to the user.
/// </summary>
[McpServerToolType]
public sealed class DisclosedDataTool
{
    private readonly IMcpSessionService _sessionService;
    private readonly IMcpAuthorizationService _authService;
    private readonly IMcpErrorHandler _errorHandler;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<DisclosedDataTool> _logger;
    private readonly string _blueprintServiceEndpoint;

    public DisclosedDataTool(
        IMcpSessionService sessionService,
        IMcpAuthorizationService authService,
        IMcpErrorHandler errorHandler,
        IServiceAvailabilityTracker availabilityTracker,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<DisclosedDataTool> logger)
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
    /// Gets data disclosed to the current user for a workflow or action.
    /// </summary>
    /// <param name="workflowInstanceId">The workflow instance ID.</param>
    /// <param name="actionInstanceId">The action instance ID (optional - returns all workflow disclosures if not specified).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Data disclosed to the user.</returns>
    [McpServerTool(Name = "sorcha_disclosed_data")]
    [Description("View data that has been disclosed to you in a workflow. Shows data from previous actions that you are permitted to see based on disclosure rules.")]
    public async Task<DisclosedDataResult> GetDisclosedDataAsync(
        [Description("The workflow instance ID")] string workflowInstanceId,
        [Description("The action instance ID (optional - returns all workflow disclosures if not specified)")] string? actionInstanceId = null,
        CancellationToken cancellationToken = default)
    {
        // Authorization check
        if (!_authService.CanInvokeTool("sorcha_disclosed_data"))
        {
            return new DisclosedDataResult
            {
                Status = "Unauthorized",
                Message = "Access denied. This tool requires the sorcha:participant role.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // Validate input
        if (string.IsNullOrWhiteSpace(workflowInstanceId))
        {
            return new DisclosedDataResult
            {
                Status = "Error",
                Message = "Workflow instance ID is required.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // Check service availability
        if (!_availabilityTracker.IsServiceAvailable("Blueprint"))
        {
            return new DisclosedDataResult
            {
                Status = "Unavailable",
                Message = "Blueprint service is currently unavailable. Please try again later.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        _logger.LogInformation(
            "Getting disclosed data for workflow {WorkflowInstanceId}, action {ActionInstanceId}",
            workflowInstanceId, actionInstanceId ?? "all");

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(30);

            var url = string.IsNullOrWhiteSpace(actionInstanceId)
                ? $"{_blueprintServiceEndpoint.TrimEnd('/')}/api/workflows/{workflowInstanceId}/disclosures"
                : $"{_blueprintServiceEndpoint.TrimEnd('/')}/api/workflows/{workflowInstanceId}/actions/{actionInstanceId}/disclosures";

            var response = await client.GetAsync(url, cancellationToken);

            stopwatch.Stop();

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Disclosed data request failed: HTTP {StatusCode} - {Error}", response.StatusCode, errorContent);

                _availabilityTracker.RecordSuccess("Blueprint");

                try
                {
                    var errorResponse = JsonSerializer.Deserialize<ErrorResponse>(errorContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    return new DisclosedDataResult
                    {
                        Status = "Error",
                        Message = errorResponse?.Error ?? "Failed to retrieve disclosed data.",
                        CheckedAt = DateTimeOffset.UtcNow,
                        ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                    };
                }
                catch (JsonException)
                {
                    return new DisclosedDataResult
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
            var result = JsonSerializer.Deserialize<DisclosedDataResponse>(responseContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (result == null)
            {
                return new DisclosedDataResult
                {
                    Status = "Error",
                    Message = "Failed to parse disclosed data response.",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

            var disclosureCount = result.Disclosures?.Count ?? 0;

            _logger.LogInformation(
                "Retrieved {Count} disclosure(s) in {ElapsedMs}ms",
                disclosureCount, stopwatch.ElapsedMilliseconds);

            return new DisclosedDataResult
            {
                Status = "Success",
                Message = disclosureCount > 0
                    ? $"Retrieved {disclosureCount} disclosure(s)."
                    : "No data has been disclosed to you for this workflow.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                Disclosures = result.Disclosures?.Select(d => new DisclosureItem
                {
                    ActionId = d.ActionId,
                    ActionTitle = d.ActionTitle ?? "",
                    DisclosedAt = d.DisclosedAt,
                    Data = d.Data ?? new Dictionary<string, object>()
                }).ToList() ?? [],
                TotalFields = result.Disclosures?.Sum(d => d.Data?.Count ?? 0) ?? 0
            };
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("Blueprint");

            return new DisclosedDataResult
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

            return new DisclosedDataResult
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

            _logger.LogError(ex, "Unexpected error getting disclosed data");

            return new DisclosedDataResult
            {
                Status = "Error",
                Message = "An unexpected error occurred while getting disclosed data.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
    }

    // Internal response models
    private sealed class DisclosedDataResponse
    {
        public List<DisclosureDto>? Disclosures { get; set; }
    }

    private sealed class DisclosureDto
    {
        public int ActionId { get; set; }
        public string? ActionTitle { get; set; }
        public DateTimeOffset? DisclosedAt { get; set; }
        public Dictionary<string, object>? Data { get; set; }
    }

    private sealed class ErrorResponse
    {
        public string? Error { get; set; }
    }
}

/// <summary>
/// Result of getting disclosed data.
/// </summary>
public sealed record DisclosedDataResult
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
    /// List of disclosures from different actions.
    /// </summary>
    public IReadOnlyList<DisclosureItem> Disclosures { get; init; } = [];

    /// <summary>
    /// Total number of fields disclosed across all disclosures.
    /// </summary>
    public int TotalFields { get; init; }
}

/// <summary>
/// Data disclosed from a specific action.
/// </summary>
public sealed record DisclosureItem
{
    /// <summary>
    /// The action ID that disclosed the data.
    /// </summary>
    public int ActionId { get; init; }

    /// <summary>
    /// The action title.
    /// </summary>
    public required string ActionTitle { get; init; }

    /// <summary>
    /// When the data was disclosed.
    /// </summary>
    public DateTimeOffset? DisclosedAt { get; init; }

    /// <summary>
    /// The disclosed data fields.
    /// </summary>
    public Dictionary<string, object> Data { get; init; } = new();
}
