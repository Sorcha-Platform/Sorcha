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

namespace Sorcha.McpServer.Tools.Designer;

/// <summary>
/// Designer tool for simulating blueprint action execution (dry-run). Reads via the typed
/// <see cref="IBlueprintServiceClient"/> (spec 139 US4) so the caller's bearer is forwarded
/// and the routes are contract-pinned, not hand-rolled.
/// </summary>
[McpServerToolType]
public sealed class BlueprintSimulateTool
{
    private readonly IMcpAuthorizationService _authService;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly IBlueprintServiceClient _blueprintClient;
    private readonly ILogger<BlueprintSimulateTool> _logger;

    public BlueprintSimulateTool(
        IMcpAuthorizationService authService,
        IServiceAvailabilityTracker availabilityTracker,
        IBlueprintServiceClient blueprintClient,
        ILogger<BlueprintSimulateTool> logger)
    {
        _authService = authService;
        _availabilityTracker = availabilityTracker;
        _blueprintClient = blueprintClient;
        _logger = logger;
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
    [Description("Performs a dry-run of a single blueprint action against supplied JSON data and returns the resolved next-action routing, computed calculation values, and any validation errors, without committing anything to the register or signing a transaction. Call this when you need to preview which downstream actions a payload would trigger or to debug routing JsonLogic in context of a real action; use sorcha_blueprint_validate instead when you only need schema conformance without routing evaluation, and prefer sorcha_jsonlogic_test rather than this when testing a standalone rule outside any blueprint.")]
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
            // Execute simulation steps in parallel via the typed client (bearer forwarded, routes pinned).
            var routeTask = DetermineRoutingAsync(blueprintId, actionId, dataJson, cancellationToken);
            var calculateTask = ApplyCalculationsAsync(blueprintId, actionId, dataJson, cancellationToken);

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

    private static string BuildSimulationRequest(string blueprintId, string actionId, string dataJson) =>
        JsonSerializer.Serialize(new
        {
            blueprintId,
            actionId,
            data = JsonSerializer.Deserialize<Dictionary<string, object>>(dataJson)
        });

    private async Task<RouteResultDto?> DetermineRoutingAsync(
        string blueprintId,
        string actionId,
        string dataJson,
        CancellationToken cancellationToken)
    {
        try
        {
            var requestJson = BuildSimulationRequest(blueprintId, actionId, dataJson);
            var responseContent = await _blueprintClient.SimulateRouteAsync(requestJson, cancellationToken);

            if (string.IsNullOrWhiteSpace(responseContent))
            {
                return new RouteResultDto { Error = "Routing failed" };
            }

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
        string blueprintId,
        string actionId,
        string dataJson,
        CancellationToken cancellationToken)
    {
        try
        {
            var requestJson = BuildSimulationRequest(blueprintId, actionId, dataJson);
            var responseContent = await _blueprintClient.SimulateCalculateAsync(requestJson, cancellationToken);

            if (string.IsNullOrWhiteSpace(responseContent))
            {
                return null;
            }

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
