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

namespace Sorcha.McpServer.Tools.Designer;

/// <summary>
/// Designer tool for comparing blueprint versions.
/// </summary>
[McpServerToolType]
public sealed class BlueprintDiffTool
{
    private readonly IMcpSessionService _sessionService;
    private readonly IMcpAuthorizationService _authService;
    private readonly IMcpErrorHandler _errorHandler;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<BlueprintDiffTool> _logger;
    private readonly string _blueprintServiceEndpoint;

    public BlueprintDiffTool(
        IMcpSessionService sessionService,
        IMcpAuthorizationService authService,
        IMcpErrorHandler errorHandler,
        IServiceAvailabilityTracker availabilityTracker,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<BlueprintDiffTool> logger)
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
    /// Compares two blueprint versions to show differences.
    /// </summary>
    /// <param name="blueprintId">The ID of the blueprint to compare.</param>
    /// <param name="fromVersion">The source version number to compare from.</param>
    /// <param name="toVersion">The target version number to compare to (default: latest).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Differences between the two versions.</returns>
    [McpServerTool(Name = "sorcha_blueprint_diff")]
    [Description("Compare two versions of a blueprint to see what changed. Useful for reviewing changes before publishing or understanding version history.")]
    public async Task<BlueprintDiffResult> CompareBlueprintVersionsAsync(
        [Description("The ID of the blueprint to compare")] string blueprintId,
        [Description("Source version number to compare from")] int fromVersion,
        [Description("Target version number to compare to (0 for latest)")] int toVersion = 0,
        CancellationToken cancellationToken = default)
    {
        // Authorization check
        if (!_authService.CanInvokeTool("sorcha_blueprint_diff"))
        {
            return new BlueprintDiffResult
            {
                Status = "Unauthorized",
                Message = "Access denied. This tool requires the sorcha:designer role.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // Validate blueprint ID
        if (string.IsNullOrWhiteSpace(blueprintId))
        {
            return new BlueprintDiffResult
            {
                Status = "Error",
                Message = "Blueprint ID is required.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // Validate version
        if (fromVersion < 1)
        {
            return new BlueprintDiffResult
            {
                Status = "Error",
                Message = "From version must be at least 1.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // Check service availability
        if (!_availabilityTracker.IsServiceAvailable("Blueprint"))
        {
            return new BlueprintDiffResult
            {
                Status = "Unavailable",
                Message = "Blueprint service is currently unavailable. Please try again later.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        _logger.LogInformation(
            "Comparing blueprint {BlueprintId} versions {FromVersion} to {ToVersion}",
            blueprintId, fromVersion, toVersion == 0 ? "latest" : toVersion);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(30);

            var url = toVersion > 0
                ? $"{_blueprintServiceEndpoint.TrimEnd('/')}/api/blueprints/{blueprintId}/diff?from={fromVersion}&to={toVersion}"
                : $"{_blueprintServiceEndpoint.TrimEnd('/')}/api/blueprints/{blueprintId}/diff?from={fromVersion}";

            var response = await client.GetAsync(url, cancellationToken);

            stopwatch.Stop();

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Blueprint diff failed: HTTP {StatusCode} - {Error}", response.StatusCode, errorContent);

                _availabilityTracker.RecordSuccess("Blueprint");

                try
                {
                    var errorResponse = JsonSerializer.Deserialize<ErrorResponse>(errorContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    return new BlueprintDiffResult
                    {
                        Status = "Error",
                        Message = errorResponse?.Error ?? "Blueprint comparison failed.",
                        CheckedAt = DateTimeOffset.UtcNow,
                        ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                    };
                }
                catch (JsonException)
                {
                    return new BlueprintDiffResult
                    {
                        Status = "Error",
                        Message = $"Blueprint comparison failed with status {(int)response.StatusCode}.",
                        CheckedAt = DateTimeOffset.UtcNow,
                        ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                    };
                }
            }

            // Record success
            _availabilityTracker.RecordSuccess("Blueprint");

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<DiffResponse>(responseContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (result == null)
            {
                return new BlueprintDiffResult
                {
                    Status = "Error",
                    Message = "Failed to parse diff response.",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

            var changeCount = (result.Changes?.Count ?? 0);
            var message = changeCount > 0
                ? $"Found {changeCount} change(s) between version {result.FromVersion} and {result.ToVersion}."
                : $"No changes between version {result.FromVersion} and {result.ToVersion}.";

            _logger.LogInformation(
                "Blueprint diff completed in {ElapsedMs}ms. {ChangeCount} changes found.",
                stopwatch.ElapsedMilliseconds, changeCount);

            return new BlueprintDiffResult
            {
                Status = "Success",
                Message = message,
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                FromVersion = result.FromVersion,
                ToVersion = result.ToVersion,
                Changes = result.Changes?.Select(c => new BlueprintChange
                {
                    Path = c.Path ?? "",
                    ChangeType = c.ChangeType ?? "Modified",
                    OldValue = c.OldValue,
                    NewValue = c.NewValue
                }).ToList() ?? [],
                TotalChanges = changeCount
            };
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("Blueprint");

            _logger.LogWarning("Blueprint diff request timed out");

            return new BlueprintDiffResult
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

            _logger.LogWarning(ex, "Failed to compare blueprint versions");

            return new BlueprintDiffResult
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

            _logger.LogError(ex, "Unexpected error comparing blueprint versions");

            return new BlueprintDiffResult
            {
                Status = "Error",
                Message = "An unexpected error occurred while comparing blueprint versions.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
    }

    // Internal response models
    private sealed class DiffResponse
    {
        public int FromVersion { get; set; }
        public int ToVersion { get; set; }
        public List<ChangeDto>? Changes { get; set; }
    }

    private sealed class ChangeDto
    {
        public string? Path { get; set; }
        public string? ChangeType { get; set; }
        public string? OldValue { get; set; }
        public string? NewValue { get; set; }
    }

    private sealed class ErrorResponse
    {
        public string? Error { get; set; }
    }
}

/// <summary>
/// Result of a blueprint diff operation.
/// </summary>
public sealed record BlueprintDiffResult
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
    /// The source version that was compared.
    /// </summary>
    public int FromVersion { get; init; }

    /// <summary>
    /// The target version that was compared.
    /// </summary>
    public int ToVersion { get; init; }

    /// <summary>
    /// List of changes between the versions.
    /// </summary>
    public IReadOnlyList<BlueprintChange> Changes { get; init; } = [];

    /// <summary>
    /// Total number of changes found.
    /// </summary>
    public int TotalChanges { get; init; }
}

/// <summary>
/// A single change between blueprint versions.
/// </summary>
public sealed record BlueprintChange
{
    /// <summary>
    /// JSON path of the changed element.
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// Type of change: Added, Removed, or Modified.
    /// </summary>
    public required string ChangeType { get; init; }

    /// <summary>
    /// The old value (for Removed and Modified changes).
    /// </summary>
    public string? OldValue { get; init; }

    /// <summary>
    /// The new value (for Added and Modified changes).
    /// </summary>
    public string? NewValue { get; init; }
}
