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
/// Designer tool for updating existing blueprints.
/// </summary>
[McpServerToolType]
public sealed class BlueprintUpdateTool
{
    private readonly IMcpSessionService _sessionService;
    private readonly IMcpAuthorizationService _authService;
    private readonly IMcpErrorHandler _errorHandler;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<BlueprintUpdateTool> _logger;
    private readonly string _blueprintServiceEndpoint;

    public BlueprintUpdateTool(
        IMcpSessionService sessionService,
        IMcpAuthorizationService authService,
        IMcpErrorHandler errorHandler,
        IServiceAvailabilityTracker availabilityTracker,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<BlueprintUpdateTool> logger)
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
    /// Updates an existing blueprint with new definition.
    /// </summary>
    /// <param name="blueprintId">The ID of the blueprint to update.</param>
    /// <param name="blueprintJson">The updated blueprint definition in JSON format.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result of the update operation.</returns>
    [McpServerTool(Name = "sorcha_blueprint_update")]
    [Description("Update an existing blueprint. Provide the blueprint ID and the complete updated definition in JSON format. The blueprint must include title, description, at least 2 participants, and at least 1 action.")]
    public async Task<BlueprintUpdateResult> UpdateBlueprintAsync(
        [Description("The ID of the blueprint to update")] string blueprintId,
        [Description("Updated blueprint definition in JSON format")] string blueprintJson,
        CancellationToken cancellationToken = default)
    {
        // Authorization check
        if (!_authService.CanInvokeTool("sorcha_blueprint_update"))
        {
            return new BlueprintUpdateResult
            {
                Status = "Unauthorized",
                Message = "Access denied. This tool requires the sorcha:designer role.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // Validate blueprint ID
        if (string.IsNullOrWhiteSpace(blueprintId))
        {
            return new BlueprintUpdateResult
            {
                Status = "Error",
                Message = "Blueprint ID is required.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // Validate JSON
        if (string.IsNullOrWhiteSpace(blueprintJson))
        {
            return new BlueprintUpdateResult
            {
                Status = "Error",
                Message = "Blueprint JSON is required.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // Parse and validate blueprint structure
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(blueprintJson);
        }
        catch (JsonException ex)
        {
            return new BlueprintUpdateResult
            {
                Status = "Error",
                Message = $"Invalid JSON format: {ex.Message}",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        using (doc)
        {
            var root = doc.RootElement;

            // Validate required fields
            if (!root.TryGetProperty("title", out var titleProp) || string.IsNullOrWhiteSpace(titleProp.GetString()))
            {
                return new BlueprintUpdateResult
                {
                    Status = "Error",
                    Message = "Blueprint must have a title.",
                    CheckedAt = DateTimeOffset.UtcNow
                };
            }

            if (!root.TryGetProperty("participants", out var participantsProp) ||
                participantsProp.ValueKind != JsonValueKind.Array ||
                participantsProp.GetArrayLength() < 2)
            {
                return new BlueprintUpdateResult
                {
                    Status = "Error",
                    Message = "Blueprint must have at least 2 participants.",
                    CheckedAt = DateTimeOffset.UtcNow
                };
            }

            if (!root.TryGetProperty("actions", out var actionsProp) ||
                actionsProp.ValueKind != JsonValueKind.Array ||
                actionsProp.GetArrayLength() < 1)
            {
                return new BlueprintUpdateResult
                {
                    Status = "Error",
                    Message = "Blueprint must have at least 1 action.",
                    CheckedAt = DateTimeOffset.UtcNow
                };
            }
        }

        // Check service availability
        if (!_availabilityTracker.IsServiceAvailable("Blueprint"))
        {
            return new BlueprintUpdateResult
            {
                Status = "Unavailable",
                Message = "Blueprint service is currently unavailable. Please try again later.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        _logger.LogInformation("Updating blueprint {BlueprintId}", blueprintId);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(30);

            var url = $"{_blueprintServiceEndpoint.TrimEnd('/')}/api/blueprints/{blueprintId}";

            var content = new StringContent(blueprintJson, Encoding.UTF8, "application/json");
            var response = await client.PutAsync(url, content, cancellationToken);

            stopwatch.Stop();

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Blueprint update failed: HTTP {StatusCode} - {Error}", response.StatusCode, errorContent);

                _availabilityTracker.RecordSuccess("Blueprint"); // Service responded, just with an error

                try
                {
                    var errorResponse = JsonSerializer.Deserialize<ErrorResponse>(errorContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    return new BlueprintUpdateResult
                    {
                        Status = "Error",
                        Message = errorResponse?.Error ?? "Blueprint update failed.",
                        CheckedAt = DateTimeOffset.UtcNow,
                        ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                    };
                }
                catch (JsonException)
                {
                    return new BlueprintUpdateResult
                    {
                        Status = "Error",
                        Message = $"Blueprint update failed with status {(int)response.StatusCode}.",
                        CheckedAt = DateTimeOffset.UtcNow,
                        ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                    };
                }
            }

            // Record success
            _availabilityTracker.RecordSuccess("Blueprint");

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<BlueprintResponse>(responseContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            _logger.LogInformation(
                "Blueprint {BlueprintId} updated successfully in {ElapsedMs}ms",
                blueprintId, stopwatch.ElapsedMilliseconds);

            return new BlueprintUpdateResult
            {
                Status = "Success",
                Message = $"Blueprint '{result?.Title ?? blueprintId}' updated successfully.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                Blueprint = result != null ? new UpdatedBlueprintInfo
                {
                    Id = result.Id ?? blueprintId,
                    Title = result.Title ?? "",
                    Version = result.Version,
                    Status = result.Status,
                    ModifiedAt = result.ModifiedAt
                } : null
            };
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("Blueprint");

            _logger.LogWarning("Blueprint update request timed out");

            return new BlueprintUpdateResult
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

            _logger.LogWarning(ex, "Failed to update blueprint");

            return new BlueprintUpdateResult
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

            _logger.LogError(ex, "Unexpected error updating blueprint");

            return new BlueprintUpdateResult
            {
                Status = "Error",
                Message = "An unexpected error occurred while updating the blueprint.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
    }

    // Internal response models
    private sealed class BlueprintResponse
    {
        public string? Id { get; set; }
        public string? Title { get; set; }
        public int Version { get; set; }
        public string? Status { get; set; }
        public DateTimeOffset? ModifiedAt { get; set; }
    }

    private sealed class ErrorResponse
    {
        public string? Error { get; set; }
    }
}

/// <summary>
/// Result of a blueprint update operation.
/// </summary>
public sealed record BlueprintUpdateResult
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
    /// Information about the updated blueprint.
    /// </summary>
    public UpdatedBlueprintInfo? Blueprint { get; init; }
}

/// <summary>
/// Information about an updated blueprint.
/// </summary>
public sealed record UpdatedBlueprintInfo
{
    /// <summary>
    /// The blueprint ID.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// The blueprint title.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// The new version number.
    /// </summary>
    public int Version { get; init; }

    /// <summary>
    /// The blueprint status.
    /// </summary>
    public string? Status { get; init; }

    /// <summary>
    /// When the blueprint was modified.
    /// </summary>
    public DateTimeOffset? ModifiedAt { get; init; }
}
