// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;

namespace Sorcha.McpServer.Tools.Designer;

/// <summary>
/// Designer tool for creating blueprints.
/// </summary>
[McpServerToolType]
public sealed class BlueprintCreateTool
{
    private readonly IMcpSessionService _sessionService;
    private readonly IMcpAuthorizationService _authService;
    private readonly IMcpErrorHandler _errorHandler;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<BlueprintCreateTool> _logger;
    private readonly string _blueprintServiceEndpoint;

    public BlueprintCreateTool(
        IMcpSessionService sessionService,
        IMcpAuthorizationService authService,
        IMcpErrorHandler errorHandler,
        IServiceAvailabilityTracker availabilityTracker,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<BlueprintCreateTool> logger)
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
    /// Creates a new blueprint from JSON content.
    /// </summary>
    /// <param name="blueprintJson">The blueprint definition in JSON format. Must include title, description, participants (min 2), and actions (min 1).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created blueprint details including the assigned ID.</returns>
    [McpServerTool(Name = "sorcha_blueprint_create")]
    [Description("Creates a new blueprint from a complete JSON definition and returns the assigned blueprint ID, version (starts at 1), and counts of participants and actions. Call this when you need to register a brand-new multi-party workflow definition; use sorcha_blueprint_update instead when revising an existing blueprint by ID, and prefer sorcha_schema_validate or sorcha_jsonlogic_test for checking individual action schemas or routing rules before assembling the full blueprint. Required fields: title (3-200 chars), description (5-2000 chars), participants (min 2 with id and name each), and actions (min 1 with title each).")]
    public async Task<BlueprintCreateResult> CreateBlueprintAsync(
        [Description("Blueprint definition in JSON format")] string blueprintJson,
        CancellationToken cancellationToken = default)
    {
        // Authorization check
        if (!_authService.CanInvokeTool("sorcha_blueprint_create"))
        {
            return new BlueprintCreateResult
            {
                Status = "Unauthorized",
                Message = "Access denied. This tool requires the sorcha:designer role.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // Validate input
        if (string.IsNullOrWhiteSpace(blueprintJson))
        {
            return new BlueprintCreateResult
            {
                Status = "Error",
                Message = "Blueprint JSON is required.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // Parse and validate JSON
        JsonElement blueprintElement;
        try
        {
            blueprintElement = JsonSerializer.Deserialize<JsonElement>(blueprintJson);
        }
        catch (JsonException ex)
        {
            return new BlueprintCreateResult
            {
                Status = "Error",
                Message = $"Invalid JSON format: {ex.Message}",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // Basic structure validation
        var validationErrors = ValidateBlueprintStructure(blueprintElement);
        if (validationErrors.Count > 0)
        {
            return new BlueprintCreateResult
            {
                Status = "ValidationError",
                Message = "Blueprint validation failed.",
                CheckedAt = DateTimeOffset.UtcNow,
                ValidationErrors = validationErrors
            };
        }

        // Check service availability
        if (!_availabilityTracker.IsServiceAvailable("Blueprint"))
        {
            return new BlueprintCreateResult
            {
                Status = "Unavailable",
                Message = "Blueprint service is currently unavailable. Please try again later.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        _logger.LogInformation("Creating new blueprint");

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(15);

            var createdBlueprint = await CreateBlueprintInServiceAsync(client, blueprintJson, cancellationToken);

            stopwatch.Stop();

            if (createdBlueprint == null)
            {
                _availabilityTracker.RecordFailure("Blueprint");

                return new BlueprintCreateResult
                {
                    Status = "Error",
                    Message = "Failed to create blueprint. The service returned an unexpected response.",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

            // Record success
            _availabilityTracker.RecordSuccess("Blueprint");

            _logger.LogInformation(
                "Blueprint '{BlueprintId}' created in {ElapsedMs}ms",
                createdBlueprint.Id, stopwatch.ElapsedMilliseconds);

            return new BlueprintCreateResult
            {
                Status = "Success",
                Message = $"Blueprint '{createdBlueprint.Title}' created successfully with ID '{createdBlueprint.Id}'.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                CreatedBlueprint = createdBlueprint
            };
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("Blueprint");

            _logger.LogWarning("Blueprint create request timed out");

            return new BlueprintCreateResult
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

            _logger.LogWarning(ex, "Failed to create blueprint");

            return new BlueprintCreateResult
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

            _logger.LogError(ex, "Unexpected error creating blueprint");

            return new BlueprintCreateResult
            {
                Status = "Error",
                Message = "An unexpected error occurred while creating the blueprint.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
    }

    private List<string> ValidateBlueprintStructure(JsonElement element)
    {
        var errors = new List<string>();

        // Check title
        if (!element.TryGetProperty("title", out var titleProp) ||
            titleProp.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(titleProp.GetString()))
        {
            errors.Add("title is required and must be a non-empty string");
        }
        else
        {
            var title = titleProp.GetString()!;
            if (title.Length < 3) errors.Add("title must be at least 3 characters");
            if (title.Length > 200) errors.Add("title must not exceed 200 characters");
        }

        // Check description
        if (!element.TryGetProperty("description", out var descProp) ||
            descProp.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(descProp.GetString()))
        {
            errors.Add("description is required and must be a non-empty string");
        }
        else
        {
            var desc = descProp.GetString()!;
            if (desc.Length < 5) errors.Add("description must be at least 5 characters");
            if (desc.Length > 2000) errors.Add("description must not exceed 2000 characters");
        }

        // Check participants
        if (!element.TryGetProperty("participants", out var participantsProp) ||
            participantsProp.ValueKind != JsonValueKind.Array)
        {
            errors.Add("participants is required and must be an array");
        }
        else
        {
            var participantsCount = participantsProp.GetArrayLength();
            if (participantsCount < 2)
            {
                errors.Add("participants must contain at least 2 participants");
            }

            var index = 0;
            foreach (var participant in participantsProp.EnumerateArray())
            {
                if (!participant.TryGetProperty("id", out _))
                    errors.Add($"participants[{index}].id is required");
                if (!participant.TryGetProperty("name", out _))
                    errors.Add($"participants[{index}].name is required");
                index++;
            }
        }

        // Check actions
        if (!element.TryGetProperty("actions", out var actionsProp) ||
            actionsProp.ValueKind != JsonValueKind.Array)
        {
            errors.Add("actions is required and must be an array");
        }
        else
        {
            var actionsCount = actionsProp.GetArrayLength();
            if (actionsCount < 1)
            {
                errors.Add("actions must contain at least 1 action");
            }

            var index = 0;
            foreach (var action in actionsProp.EnumerateArray())
            {
                if (!action.TryGetProperty("title", out _))
                    errors.Add($"actions[{index}].title is required");
                index++;
            }
        }

        return errors;
    }

    private async Task<CreatedBlueprintInfo?> CreateBlueprintInServiceAsync(
        HttpClient client,
        string blueprintJson,
        CancellationToken cancellationToken)
    {
        try
        {
            var url = $"{_blueprintServiceEndpoint.TrimEnd('/')}/api/blueprints/";
            var content = new StringContent(blueprintJson, Encoding.UTF8, "application/json");
            var response = await client.PostAsync(url, content, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Failed to create blueprint: HTTP {StatusCode} - {Error}",
                    response.StatusCode, errorContent);
                return null;
            }

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var dto = JsonSerializer.Deserialize<BlueprintResponseDto>(responseContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (dto == null) return null;

            return new CreatedBlueprintInfo
            {
                Id = dto.Id,
                Title = dto.Title,
                Description = dto.Description,
                Version = dto.Version,
                CreatedAt = dto.CreatedAt,
                ParticipantCount = dto.Participants?.Count ?? 0,
                ActionCount = dto.Actions?.Count ?? 0
            };
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Error parsing create blueprint response");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error creating blueprint");
            return null;
        }
    }

    // Internal response models for deserialization
    private sealed class BlueprintResponseDto
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int Version { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public List<object>? Participants { get; set; }
        public List<object>? Actions { get; set; }
    }
}

/// <summary>
/// Result of a blueprint create operation.
/// </summary>
public sealed record BlueprintCreateResult
{
    /// <summary>
    /// Operation status: Success, ValidationError, Error, Unavailable, Timeout, or Unauthorized.
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
    /// Validation errors (if Status is ValidationError).
    /// </summary>
    public IReadOnlyList<string> ValidationErrors { get; init; } = [];

    /// <summary>
    /// The created blueprint details (if successful).
    /// </summary>
    public CreatedBlueprintInfo? CreatedBlueprint { get; init; }
}

/// <summary>
/// Information about a created blueprint.
/// </summary>
public sealed record CreatedBlueprintInfo
{
    /// <summary>
    /// The assigned blueprint ID.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Blueprint title.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// Blueprint description.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Blueprint version (starts at 1).
    /// </summary>
    public int Version { get; init; }

    /// <summary>
    /// When the blueprint was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Number of participants in the blueprint.
    /// </summary>
    public int ParticipantCount { get; init; }

    /// <summary>
    /// Number of actions in the blueprint.
    /// </summary>
    public int ActionCount { get; init; }
}
