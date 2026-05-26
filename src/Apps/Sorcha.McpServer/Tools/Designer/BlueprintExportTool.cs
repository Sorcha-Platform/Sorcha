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
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Sorcha.McpServer.Tools.Designer;

/// <summary>
/// Designer tool for exporting blueprints to JSON or YAML format. Reads via the typed
/// <see cref="IBlueprintServiceClient"/> (spec 139 US4) so the caller's bearer is forwarded
/// and the route is contract-pinned, not hand-rolled.
/// </summary>
[McpServerToolType]
public sealed class BlueprintExportTool
{
    private readonly IMcpAuthorizationService _authService;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly IBlueprintServiceClient _blueprintClient;
    private readonly ILogger<BlueprintExportTool> _logger;

    public BlueprintExportTool(
        IMcpAuthorizationService authService,
        IServiceAvailabilityTracker availabilityTracker,
        IBlueprintServiceClient blueprintClient,
        ILogger<BlueprintExportTool> logger)
    {
        _authService = authService;
        _availabilityTracker = availabilityTracker;
        _blueprintClient = blueprintClient;
        _logger = logger;
    }

    /// <summary>
    /// Exports a blueprint to JSON or YAML format.
    /// </summary>
    /// <param name="blueprintId">The ID of the blueprint to export.</param>
    /// <param name="format">Export format: json or yaml (default: json).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The exported blueprint content.</returns>
    [McpServerTool(Name = "sorcha_blueprint_export")]
    [Description("Exports a blueprint to a serialised JSON or YAML document suitable for committing to version control, backing up, or transferring to another environment. Call this when you need an offline, file-shaped representation of the blueprint; use sorcha_blueprint_get instead when you want the structured response object for programmatic inspection, and prefer this over manual reconstruction when round-tripping a blueprint between tenants or environments.")]
    public async Task<BlueprintExportResult> ExportBlueprintAsync(
        [Description("The ID of the blueprint to export")] string blueprintId,
        [Description("Export format: json or yaml (default: json)")] string format = "json",
        CancellationToken cancellationToken = default)
    {
        // Authorization check
        if (!_authService.CanInvokeTool("sorcha_blueprint_export"))
        {
            return new BlueprintExportResult
            {
                Status = "Unauthorized",
                Message = "Access denied. This tool requires the sorcha:designer role.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // Validate blueprint ID
        if (string.IsNullOrWhiteSpace(blueprintId))
        {
            return new BlueprintExportResult
            {
                Status = "Error",
                Message = "Blueprint ID is required.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // Validate format
        var normalizedFormat = format?.ToLowerInvariant() ?? "json";
        if (normalizedFormat != "json" && normalizedFormat != "yaml")
        {
            return new BlueprintExportResult
            {
                Status = "Error",
                Message = "Format must be 'json' or 'yaml'.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // Check service availability
        if (!_availabilityTracker.IsServiceAvailable("Blueprint"))
        {
            return new BlueprintExportResult
            {
                Status = "Unavailable",
                Message = "Blueprint service is currently unavailable. Please try again later.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        _logger.LogInformation("Exporting blueprint {BlueprintId} as {Format}", blueprintId, normalizedFormat);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Typed client forwards the caller's bearer and pins the route (GET api/blueprints/{id}).
            var jsonContent = await _blueprintClient.GetBlueprintAsync(blueprintId, cancellationToken);

            stopwatch.Stop();

            if (string.IsNullOrWhiteSpace(jsonContent))
            {
                _availabilityTracker.RecordSuccess("Blueprint");

                return new BlueprintExportResult
                {
                    Status = "Error",
                    Message = "Blueprint not found.",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

            // Record success
            _availabilityTracker.RecordSuccess("Blueprint");

            string exportedContent;
            if (normalizedFormat == "yaml")
            {
                // Convert JSON to YAML
                var jsonDoc = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                var serializer = new SerializerBuilder()
                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                    .Build();

                exportedContent = serializer.Serialize(jsonDoc);
            }
            else
            {
                // Pretty-print JSON
                var jsonDoc = JsonDocument.Parse(jsonContent);
                exportedContent = JsonSerializer.Serialize(jsonDoc, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
            }

            _logger.LogInformation(
                "Blueprint {BlueprintId} exported as {Format} in {ElapsedMs}ms",
                blueprintId, normalizedFormat, stopwatch.ElapsedMilliseconds);

            return new BlueprintExportResult
            {
                Status = "Success",
                Message = $"Blueprint exported successfully as {normalizedFormat.ToUpperInvariant()}.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                Format = normalizedFormat,
                Content = exportedContent,
                ContentLength = exportedContent.Length
            };
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("Blueprint");

            _logger.LogWarning("Blueprint export request timed out");

            return new BlueprintExportResult
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

            _logger.LogWarning(ex, "Failed to export blueprint");

            return new BlueprintExportResult
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

            _logger.LogError(ex, "Unexpected error exporting blueprint");

            return new BlueprintExportResult
            {
                Status = "Error",
                Message = "An unexpected error occurred while exporting the blueprint.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
    }

}

/// <summary>
/// Result of a blueprint export operation.
/// </summary>
public sealed record BlueprintExportResult
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
    /// The export format used.
    /// </summary>
    public string? Format { get; init; }

    /// <summary>
    /// The exported blueprint content.
    /// </summary>
    public string? Content { get; init; }

    /// <summary>
    /// Length of the exported content in characters.
    /// </summary>
    public int ContentLength { get; init; }
}
