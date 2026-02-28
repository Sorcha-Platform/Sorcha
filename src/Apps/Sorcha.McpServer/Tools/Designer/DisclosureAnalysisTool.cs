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
/// Designer tool for analyzing disclosure rules to see what data each participant will receive.
/// </summary>
[McpServerToolType]
public sealed class DisclosureAnalysisTool
{
    private readonly IMcpSessionService _sessionService;
    private readonly IMcpAuthorizationService _authService;
    private readonly IMcpErrorHandler _errorHandler;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<DisclosureAnalysisTool> _logger;
    private readonly string _blueprintServiceEndpoint;

    public DisclosureAnalysisTool(
        IMcpSessionService sessionService,
        IMcpAuthorizationService authService,
        IMcpErrorHandler errorHandler,
        IServiceAvailabilityTracker availabilityTracker,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<DisclosureAnalysisTool> logger)
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
    /// Analyzes disclosure rules to show what data each participant will receive.
    /// </summary>
    /// <param name="blueprintId">The blueprint ID containing the action.</param>
    /// <param name="actionId">The action ID (sequence number) to analyze.</param>
    /// <param name="dataJson">The action data in JSON format to analyze.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Analysis showing what data each participant would receive based on disclosure rules.</returns>
    [McpServerTool(Name = "sorcha_disclosure_analysis")]
    [Description("Analyze disclosure rules for an action. Shows what data each participant will receive based on the action's disclosure configuration. Useful for verifying privacy settings and data visibility rules in blueprints.")]
    public async Task<DisclosureAnalysisResult> AnalyzeDisclosuresAsync(
        [Description("Blueprint ID containing the action")] string blueprintId,
        [Description("Action ID (sequence number) to analyze")] string actionId,
        [Description("Action data in JSON format to analyze")] string dataJson,
        CancellationToken cancellationToken = default)
    {
        // Authorization check
        if (!_authService.CanInvokeTool("sorcha_disclosure_analysis"))
        {
            return new DisclosureAnalysisResult
            {
                Status = "Unauthorized",
                Message = "Access denied. This tool requires the sorcha:designer role.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // Validate inputs
        if (string.IsNullOrWhiteSpace(blueprintId))
        {
            return new DisclosureAnalysisResult
            {
                Status = "Error",
                Message = "Blueprint ID is required.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        if (string.IsNullOrWhiteSpace(actionId))
        {
            return new DisclosureAnalysisResult
            {
                Status = "Error",
                Message = "Action ID is required.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        if (string.IsNullOrWhiteSpace(dataJson))
        {
            return new DisclosureAnalysisResult
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
                return new DisclosureAnalysisResult
                {
                    Status = "Error",
                    Message = "Data JSON must be a valid object.",
                    CheckedAt = DateTimeOffset.UtcNow
                };
            }
        }
        catch (JsonException ex)
        {
            return new DisclosureAnalysisResult
            {
                Status = "Error",
                Message = $"Invalid data JSON format: {ex.Message}",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // Check service availability
        if (!_availabilityTracker.IsServiceAvailable("Blueprint"))
        {
            return new DisclosureAnalysisResult
            {
                Status = "Unavailable",
                Message = "Blueprint service is currently unavailable. Please try again later.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        _logger.LogInformation(
            "Analyzing disclosures for blueprint {BlueprintId}, action {ActionId}",
            blueprintId, actionId);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);

            var disclosureResult = await GetDisclosuresAsync(client, blueprintId, actionId, dataJson, cancellationToken);

            stopwatch.Stop();

            // Record success
            _availabilityTracker.RecordSuccess("Blueprint");

            if (disclosureResult == null)
            {
                return new DisclosureAnalysisResult
                {
                    Status = "Error",
                    Message = "Failed to analyze disclosures. The service returned an unexpected response.",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

            if (disclosureResult.Error != null)
            {
                return new DisclosureAnalysisResult
                {
                    Status = "Error",
                    Message = disclosureResult.Error,
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

            var participantCount = disclosureResult.Disclosures?.Count ?? 0;
            var totalFields = disclosureResult.Disclosures?.Sum(d => d.FieldCount) ?? 0;

            _logger.LogInformation(
                "Disclosure analysis completed in {ElapsedMs}ms. Participants: {Count}, Total fields: {Fields}",
                stopwatch.ElapsedMilliseconds, participantCount, totalFields);

            return new DisclosureAnalysisResult
            {
                Status = "Success",
                Message = participantCount > 0
                    ? $"Analyzed disclosures for {participantCount} participant(s), {totalFields} field(s) total."
                    : "No disclosure rules defined for this action. All data will be visible to all participants.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                Disclosures = disclosureResult.Disclosures?.Select(d => new ParticipantDisclosure
                {
                    ParticipantId = d.ParticipantId,
                    DisclosureId = d.DisclosureId,
                    DisclosedFields = d.DisclosedFields,
                    FieldCount = d.FieldCount
                }).ToList() ?? [],
                TotalParticipants = participantCount,
                TotalDisclosedFields = totalFields
            };
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("Blueprint");

            _logger.LogWarning("Disclosure analysis request timed out");

            return new DisclosureAnalysisResult
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

            _logger.LogWarning(ex, "Failed to analyze disclosures");

            return new DisclosureAnalysisResult
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

            _logger.LogError(ex, "Unexpected error analyzing disclosures");

            return new DisclosureAnalysisResult
            {
                Status = "Error",
                Message = "An unexpected error occurred while analyzing disclosures.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
    }

    private async Task<DisclosureResultDto?> GetDisclosuresAsync(
        HttpClient client,
        string blueprintId,
        string actionId,
        string dataJson,
        CancellationToken cancellationToken)
    {
        try
        {
            var url = $"{_blueprintServiceEndpoint.TrimEnd('/')}/api/execution/disclose";

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
                _logger.LogWarning("Disclose request failed: HTTP {StatusCode} - {Error}", response.StatusCode, errorContent);

                try
                {
                    var errorResponse = JsonSerializer.Deserialize<ErrorResponse>(errorContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    return new DisclosureResultDto { Error = errorResponse?.Error ?? "Disclosure analysis failed" };
                }
                catch (JsonException)
                {
                    return new DisclosureResultDto { Error = "Disclosure analysis failed" };
                }
            }

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<DisclosureResponse>(responseContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (result == null) return null;

            return new DisclosureResultDto
            {
                Disclosures = result.Disclosures?.Select(d => new DisclosureDto
                {
                    ParticipantId = d.ParticipantId ?? "",
                    DisclosureId = d.DisclosureId,
                    DisclosedFields = d.DisclosedData?.Keys.ToList() ?? [],
                    FieldCount = d.FieldCount
                }).ToList() ?? []
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error analyzing disclosures");
            return null;
        }
    }

    // Internal response models
    private sealed class DisclosureResponse
    {
        public List<DisclosureItemDto>? Disclosures { get; set; }
    }

    private sealed class DisclosureItemDto
    {
        public string? ParticipantId { get; set; }
        public string? DisclosureId { get; set; }
        public Dictionary<string, object>? DisclosedData { get; set; }
        public int FieldCount { get; set; }
    }

    private sealed class ErrorResponse
    {
        public string? Error { get; set; }
    }

    private sealed class DisclosureResultDto
    {
        public List<DisclosureDto>? Disclosures { get; set; }
        public string? Error { get; set; }
    }

    private sealed class DisclosureDto
    {
        public string ParticipantId { get; set; } = "";
        public string? DisclosureId { get; set; }
        public List<string> DisclosedFields { get; set; } = [];
        public int FieldCount { get; set; }
    }
}

/// <summary>
/// Result of a disclosure analysis.
/// </summary>
public sealed record DisclosureAnalysisResult
{
    /// <summary>
    /// Operation status: Success, Error, Unavailable, Timeout, or Unauthorized.
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// Human-readable message about the analysis result.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// When the analysis was performed.
    /// </summary>
    public required DateTimeOffset CheckedAt { get; init; }

    /// <summary>
    /// Response time in milliseconds.
    /// </summary>
    public int ResponseTimeMs { get; init; }

    /// <summary>
    /// List of disclosures for each participant.
    /// </summary>
    public IReadOnlyList<ParticipantDisclosure> Disclosures { get; init; } = [];

    /// <summary>
    /// Total number of participants receiving data.
    /// </summary>
    public int TotalParticipants { get; init; }

    /// <summary>
    /// Total number of fields being disclosed across all participants.
    /// </summary>
    public int TotalDisclosedFields { get; init; }
}

/// <summary>
/// Disclosure information for a specific participant.
/// </summary>
public sealed record ParticipantDisclosure
{
    /// <summary>
    /// The participant ID receiving the disclosed data.
    /// </summary>
    public required string ParticipantId { get; init; }

    /// <summary>
    /// The disclosure rule ID that matched.
    /// </summary>
    public string? DisclosureId { get; init; }

    /// <summary>
    /// List of field names being disclosed to this participant.
    /// </summary>
    public IReadOnlyList<string> DisclosedFields { get; init; } = [];

    /// <summary>
    /// Number of fields being disclosed.
    /// </summary>
    public int FieldCount { get; init; }
}
