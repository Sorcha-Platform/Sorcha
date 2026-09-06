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
/// Designer tool for comparing blueprint versions.
/// <para>
/// MCP P0 restore-surface Task 5: this tool is intentionally <b>not</b> decorated with
/// <c>[McpServerToolType]</c>, so the assembly tool scan does not discover or register it and
/// it is fully absent from the served MCP surface (and from the advertised manifest catalogue).
/// No <c>/diff</c> endpoint exists anywhere in <c>Sorcha.Blueprint.Service</c> (or any other
/// service) — <see cref="IBlueprintServiceClient.GetBlueprintDiffAsync"/> targets a route that
/// was never mapped, so an advertised <c>sorcha_blueprint_diff</c> tool would always fail. The
/// class is kept intact (compiles, unit-tested) so a future wave can re-enable it by restoring
/// the attribute once a real diff endpoint exists. See issue #1607 for removing the now-dead
/// client method and its mock-only tests, which are deliberately left in place here.
/// </para>
/// </summary>
public sealed class BlueprintDiffTool
{
    private readonly IMcpAuthorizationService _authService;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly IBlueprintServiceClient _blueprintClient;
    private readonly ILogger<BlueprintDiffTool> _logger;

    public BlueprintDiffTool(
        IMcpAuthorizationService authService,
        IServiceAvailabilityTracker availabilityTracker,
        IBlueprintServiceClient blueprintClient,
        ILogger<BlueprintDiffTool> logger)
    {
        _authService = authService;
        _availabilityTracker = availabilityTracker;
        _blueprintClient = blueprintClient;
        _logger = logger;
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
    [Description("Compares two numbered versions of the same blueprint and returns a structured delta covering title, description, participants, actions, schemas, and routing rules. Call this when reviewing what changed between revisions before publishing, or when reconstructing version history for an audit; use sorcha_blueprint_get instead when you only need a single version's full definition, and call this before sorcha_blueprint_update rather than after to confirm the proposed delta is intentional.")]
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
            // Typed client forwards the caller's bearer and pins the route (GET api/blueprints/{id}/diff).
            var responseContent = await _blueprintClient.GetBlueprintDiffAsync(
                blueprintId, fromVersion, toVersion > 0 ? toVersion : null, cancellationToken);

            stopwatch.Stop();

            if (string.IsNullOrWhiteSpace(responseContent))
            {
                _availabilityTracker.RecordSuccess("Blueprint");

                return new BlueprintDiffResult
                {
                    Status = "Error",
                    Message = "Blueprint comparison failed.",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

            // Record success
            _availabilityTracker.RecordSuccess("Blueprint");

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
