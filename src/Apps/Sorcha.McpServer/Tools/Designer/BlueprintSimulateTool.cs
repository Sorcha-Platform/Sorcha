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

namespace Sorcha.McpServer.Tools.Designer;

/// <summary>
/// Designer tool for simulating blueprint action execution (dry-run).
/// </summary>
[McpServerToolType]
public sealed class BlueprintSimulateTool
{
    private readonly IMcpSessionService _sessionService;
    private readonly IMcpAuthorizationService _authService;
    private readonly IMcpErrorHandler _errorHandler;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<BlueprintSimulateTool> _logger;
    private readonly string _blueprintServiceEndpoint;

    public BlueprintSimulateTool(
        IMcpSessionService sessionService,
        IMcpAuthorizationService authService,
        IMcpErrorHandler errorHandler,
        IServiceAvailabilityTracker availabilityTracker,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<BlueprintSimulateTool> logger)
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
    /// Simulates action execution to determine routing and calculate results without committing.
    /// </summary>
    /// <param name="blueprintId">The blueprint ID containing the action.</param>
    /// <param name="actionId">The action ID (sequence number) to simulate.</param>
    /// <param name="dataJson">The action data in JSON format.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Simulation result including next actions, calculations, and any validation errors.</returns>
    [McpServerTool(Name = "sorcha_blueprint_simulate")]
    [Description("Simulate blueprint action execution (dry-run). Validates data, applies calculations, and determines routing to next action(s) without actually executing the workflow. Useful for testing blueprint logic and previewing workflow paths.")]
    public async Task<BlueprintSimulateResult> SimulateActionAsync(
        [Description("Blueprint ID containing the action")] string blueprintId,
        [Description("Action ID (sequence number) to simulate")] string actionId,
        [Description("Action data in JSON format")] string dataJson,
        CancellationToken cancellationToken = default)
    {
        // Authorization check
        if (!_authService.CanInvokeTool("sorcha_blueprint_simulate"))
        {
            return new BlueprintSimulateResult
            {
                Status = "Unauthorized",
                Message = "Access denied. This tool requires the sorcha:designer role.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // Validate inputs
        if (string.IsNullOrWhiteSpace(blueprintId))
        {
            return new BlueprintSimulateResult
            {
                Status = "Error",
                Message = "Blueprint ID is required.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        if (string.IsNullOrWhiteSpace(actionId))
        {
            return new BlueprintSimulateResult
            {
                Status = "Error",
                Message = "Action ID is required.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        if (string.IsNullOrWhiteSpace(dataJson))
        {
            return new BlueprintSimulateResult
            {
                Status = "Error",
                Message = "Data JSON is required.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // Parse data JSON
        Dictionary<string, object>? data;
        try
        {
            data = JsonSerializer.Deserialize<Dictionary<string, object>>(dataJson);
            if (data == null)
            {
                return new BlueprintSimulateResult
                {
                    Status = "Error",
                    Message = "Data JSON must be a valid object.",
                    CheckedAt = DateTimeOffset.UtcNow
                };
            }
        }
        catch (JsonException ex)
        {
            return new BlueprintSimulateResult
            {
                Status = "Error",
                Message = $"Invalid data JSON format: {ex.Message}",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // Check service availability
        if (!_availabilityTracker.IsServiceAvailable("Blueprint"))
        {
            return new BlueprintSimulateResult
            {
                Status = "Unavailable",
                Message = "Blueprint service is currently unavailable. Please try again later.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        _logger.LogInformation(
            "Simulating action for blueprint {BlueprintId}, action {ActionId}",
            blueprintId, actionId);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(15);

            // Execute simulation steps in parallel
            var routeTask = DetermineRoutingAsync(client, blueprintId, actionId, dataJson, cancellationToken);
            var calculateTask = ApplyCalculationsAsync(client, blueprintId, actionId, dataJson, cancellationToken);

            await Task.WhenAll(routeTask, calculateTask);

            var routeResult = await routeTask;
            var calculateResult = await calculateTask;

            stopwatch.Stop();

            // Record success
            _availabilityTracker.RecordSuccess("Blueprint");

            // Determine overall status
            string status;
            string message;

            if (routeResult == null && calculateResult == null)
            {
                status = "Error";
                message = "Failed to simulate action. The service returned unexpected responses.";
            }
            else if (routeResult?.Error != null)
            {
                status = "Error";
                message = routeResult.Error;
            }
            else
            {
                status = "Success";
                message = routeResult?.NextActions?.Count > 0
                    ? $"Simulation complete. Routes to {routeResult.NextActions.Count} next action(s)."
                    : "Simulation complete. No routing configured for this action.";
            }

            _logger.LogInformation(
                "Simulation completed in {ElapsedMs}ms. Status: {Status}",
                stopwatch.ElapsedMilliseconds, status);

            return new BlueprintSimulateResult
            {
                Status = status,
                Message = message,
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                Routing = routeResult != null ? new RoutingInfo
                {
                    NextActions = routeResult.NextActions ?? [],
                    MatchedRoute = routeResult.MatchedRoute,
                    RouteDescription = routeResult.RouteDescription
                } : null,
                Calculations = calculateResult != null ? new CalculationInfo
                {
                    ProcessedData = calculateResult.ProcessedData,
                    CalculatedFields = calculateResult.CalculatedFields ?? []
                } : null
            };
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("Blueprint");

            _logger.LogWarning("Simulation request timed out");

            return new BlueprintSimulateResult
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

            _logger.LogWarning(ex, "Failed to simulate action");

            return new BlueprintSimulateResult
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

            _logger.LogError(ex, "Unexpected error simulating action");

            return new BlueprintSimulateResult
            {
                Status = "Error",
                Message = "An unexpected error occurred while simulating the action.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
    }

    private async Task<RouteResultDto?> DetermineRoutingAsync(
        HttpClient client,
        string blueprintId,
        string actionId,
        string dataJson,
        CancellationToken cancellationToken)
    {
        try
        {
            var url = $"{_blueprintServiceEndpoint.TrimEnd('/')}/api/execution/route";

            var requestBody = new
            {
                blueprintId,
                actionId,
                data = JsonSerializer.Deserialize<Dictionary<string, object>>(dataJson)
            };

            var content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json");

            var response = await client.PostAsync(url, content, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Route request failed: HTTP {StatusCode} - {Error}", response.StatusCode, errorContent);

                try
                {
                    var errorResponse = JsonSerializer.Deserialize<ErrorResponse>(errorContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    return new RouteResultDto { Error = errorResponse?.Error ?? "Routing failed" };
                }
                catch (JsonException)
                {
                    return new RouteResultDto { Error = "Routing failed" };
                }
            }

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<RouteResponse>(responseContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (result == null) return null;

            return new RouteResultDto
            {
                NextActions = result.NextActions?.Select(a => new NextActionInfo
                {
                    ActionId = a.ActionId,
                    Title = a.Title,
                    IsTerminal = a.IsTerminal
                }).ToList() ?? [],
                MatchedRoute = result.MatchedRoute,
                RouteDescription = result.RouteDescription
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error determining routing");
            return null;
        }
    }

    private async Task<CalculateResultDto?> ApplyCalculationsAsync(
        HttpClient client,
        string blueprintId,
        string actionId,
        string dataJson,
        CancellationToken cancellationToken)
    {
        try
        {
            var url = $"{_blueprintServiceEndpoint.TrimEnd('/')}/api/execution/calculate";

            var requestBody = new
            {
                blueprintId,
                actionId,
                data = JsonSerializer.Deserialize<Dictionary<string, object>>(dataJson)
            };

            var content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json");

            var response = await client.PostAsync(url, content, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Calculate request failed: HTTP {StatusCode}", response.StatusCode);
                return null;
            }

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<CalculateResponse>(responseContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (result == null) return null;

            return new CalculateResultDto
            {
                ProcessedData = result.ProcessedData,
                CalculatedFields = result.CalculatedFields ?? []
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error applying calculations");
            return null;
        }
    }

    // Internal response models
    private sealed class RouteResponse
    {
        public List<NextActionDto>? NextActions { get; set; }
        public string? MatchedRoute { get; set; }
        public string? RouteDescription { get; set; }
    }

    private sealed class NextActionDto
    {
        public int ActionId { get; set; }
        public string? Title { get; set; }
        public bool IsTerminal { get; set; }
    }

    private sealed class CalculateResponse
    {
        public Dictionary<string, object>? ProcessedData { get; set; }
        public List<string>? CalculatedFields { get; set; }
    }

    private sealed class ErrorResponse
    {
        public string? Error { get; set; }
    }

    private sealed class RouteResultDto
    {
        public List<NextActionInfo>? NextActions { get; set; }
        public string? MatchedRoute { get; set; }
        public string? RouteDescription { get; set; }
        public string? Error { get; set; }
    }

    private sealed class CalculateResultDto
    {
        public Dictionary<string, object>? ProcessedData { get; set; }
        public List<string> CalculatedFields { get; set; } = [];
    }
}

/// <summary>
/// Result of a blueprint simulation.
/// </summary>
public sealed record BlueprintSimulateResult
{
    /// <summary>
    /// Operation status: Success, Error, Unavailable, Timeout, or Unauthorized.
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// Human-readable message about the simulation result.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// When the simulation was performed.
    /// </summary>
    public required DateTimeOffset CheckedAt { get; init; }

    /// <summary>
    /// Response time in milliseconds.
    /// </summary>
    public int ResponseTimeMs { get; init; }

    /// <summary>
    /// Routing information showing next action(s).
    /// </summary>
    public RoutingInfo? Routing { get; init; }

    /// <summary>
    /// Calculation results.
    /// </summary>
    public CalculationInfo? Calculations { get; init; }
}

/// <summary>
/// Routing information from simulation.
/// </summary>
public sealed record RoutingInfo
{
    /// <summary>
    /// List of next action(s) based on routing rules.
    /// </summary>
    public IReadOnlyList<NextActionInfo> NextActions { get; init; } = [];

    /// <summary>
    /// The route rule that matched (if any).
    /// </summary>
    public string? MatchedRoute { get; init; }

    /// <summary>
    /// Description of the routing decision.
    /// </summary>
    public string? RouteDescription { get; init; }
}

/// <summary>
/// Information about a next action.
/// </summary>
public sealed record NextActionInfo
{
    /// <summary>
    /// The action ID.
    /// </summary>
    public int ActionId { get; init; }

    /// <summary>
    /// The action title.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// Whether this is a terminal action (end of workflow).
    /// </summary>
    public bool IsTerminal { get; init; }
}

/// <summary>
/// Calculation results from simulation.
/// </summary>
public sealed record CalculationInfo
{
    /// <summary>
    /// The processed data after calculations.
    /// </summary>
    public Dictionary<string, object>? ProcessedData { get; init; }

    /// <summary>
    /// List of fields that were calculated/added.
    /// </summary>
    public IReadOnlyList<string> CalculatedFields { get; init; } = [];
}
