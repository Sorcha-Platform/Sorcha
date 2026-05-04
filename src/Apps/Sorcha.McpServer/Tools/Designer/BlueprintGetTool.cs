// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;

namespace Sorcha.McpServer.Tools.Designer;

/// <summary>
/// Designer tool for retrieving a blueprint by ID.
/// </summary>
[McpServerToolType]
public sealed class BlueprintGetTool
{
    private readonly IMcpSessionService _sessionService;
    private readonly IMcpAuthorizationService _authService;
    private readonly IMcpErrorHandler _errorHandler;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<BlueprintGetTool> _logger;
    private readonly string _blueprintServiceEndpoint;

    public BlueprintGetTool(
        IMcpSessionService sessionService,
        IMcpAuthorizationService authService,
        IMcpErrorHandler errorHandler,
        IServiceAvailabilityTracker availabilityTracker,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<BlueprintGetTool> logger)
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
    /// Gets a blueprint by ID.
    /// </summary>
    /// <param name="blueprintId">The unique identifier of the blueprint to retrieve.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The full blueprint details including participants, actions, and metadata.</returns>
    [McpServerTool(Name = "sorcha_blueprint_get")]
    [Description("Retrieves a single blueprint by its ID and returns the full definition including title, description, participants, actions, action schemas, routing rules, and metadata. Call this when you have an exact blueprint ID and need the complete definition for inspection, simulation, or as input to a subsequent update; use sorcha_blueprint_list instead when you do not yet know the ID and need to search by title or status, and prefer sorcha_blueprint_diff over repeated gets when comparing two versions.")]
    public async Task<BlueprintGetResult> GetBlueprintAsync(
        [Description("The unique identifier of the blueprint to retrieve")] string blueprintId,
        CancellationToken cancellationToken = default)
    {
        // Authorization check
        if (!_authService.CanInvokeTool("sorcha_blueprint_get"))
        {
            return new BlueprintGetResult
            {
                Status = "Unauthorized",
                Message = "Access denied. This tool requires the sorcha:designer role.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // Validate input
        if (string.IsNullOrWhiteSpace(blueprintId))
        {
            return new BlueprintGetResult
            {
                Status = "Error",
                Message = "Blueprint ID is required.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // Check service availability
        if (!_availabilityTracker.IsServiceAvailable("Blueprint"))
        {
            return new BlueprintGetResult
            {
                Status = "Unavailable",
                Message = "Blueprint service is currently unavailable. Please try again later.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        _logger.LogInformation("Getting blueprint {BlueprintId}", blueprintId);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);

            var blueprint = await FetchBlueprintAsync(client, blueprintId, cancellationToken);

            stopwatch.Stop();

            // Record success (even if not found, the service responded)
            _availabilityTracker.RecordSuccess("Blueprint");

            if (blueprint == null)
            {
                _logger.LogInformation("Blueprint {BlueprintId} not found", blueprintId);

                return new BlueprintGetResult
                {
                    Status = "NotFound",
                    Message = $"Blueprint '{blueprintId}' was not found.",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

            _logger.LogInformation(
                "Blueprint {BlueprintId} retrieved in {ElapsedMs}ms",
                blueprintId, stopwatch.ElapsedMilliseconds);

            return new BlueprintGetResult
            {
                Status = "Success",
                Message = $"Retrieved blueprint '{blueprint.Title}'.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                Blueprint = blueprint
            };
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("Blueprint");

            _logger.LogWarning("Blueprint get query timed out for {BlueprintId}", blueprintId);

            return new BlueprintGetResult
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

            _logger.LogWarning(ex, "Failed to get blueprint {BlueprintId}", blueprintId);

            return new BlueprintGetResult
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

            _logger.LogError(ex, "Unexpected error getting blueprint {BlueprintId}", blueprintId);

            return new BlueprintGetResult
            {
                Status = "Error",
                Message = "An unexpected error occurred while retrieving the blueprint.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
    }

    private async Task<BlueprintDetail?> FetchBlueprintAsync(
        HttpClient client,
        string blueprintId,
        CancellationToken cancellationToken)
    {
        try
        {
            var url = $"{_blueprintServiceEndpoint.TrimEnd('/')}/api/blueprints/{Uri.EscapeDataString(blueprintId)}";
            var response = await client.GetAsync(url, cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch blueprint {BlueprintId}: HTTP {StatusCode}",
                    blueprintId, response.StatusCode);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var dto = JsonSerializer.Deserialize<BlueprintDto>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (dto == null) return null;

            return new BlueprintDetail
            {
                Id = dto.Id,
                Title = dto.Title,
                Description = dto.Description,
                Version = dto.Version,
                CreatedAt = dto.CreatedAt,
                UpdatedAt = dto.UpdatedAt,
                Metadata = dto.Metadata,
                Participants = dto.Participants?.Select(p => new ParticipantInfo
                {
                    Id = p.Id,
                    Name = p.Name,
                    Organisation = p.Organisation,
                    WalletAddress = p.WalletAddress,
                    DidUri = p.DidUri,
                    UseStealthAddress = p.UseStealthAddress
                }).ToList() ?? [],
                Actions = dto.Actions?.Select(a => new ActionInfo
                {
                    Id = a.Id,
                    Title = a.Title,
                    Description = a.Description,
                    Sender = a.Sender,
                    Target = a.Target,
                    IsStartingAction = a.IsStartingAction,
                    RequiredActionData = a.RequiredActionData?.ToList() ?? [],
                    AdditionalRecipients = a.AdditionalRecipients?.ToList() ?? [],
                    DisclosureCount = a.Disclosures?.Count() ?? 0
                }).ToList() ?? []
            };
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Error parsing blueprint {BlueprintId}", blueprintId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error fetching blueprint {BlueprintId}", blueprintId);
            return null;
        }
    }

    // Internal response models for deserialization
    private sealed class BlueprintDto
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int Version { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public Dictionary<string, string>? Metadata { get; set; }
        public List<ParticipantDto>? Participants { get; set; }
        public List<ActionDto>? Actions { get; set; }
    }

    private sealed class ParticipantDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Organisation { get; set; } = string.Empty;
        public string WalletAddress { get; set; } = string.Empty;
        public string? DidUri { get; set; }
        public bool UseStealthAddress { get; set; }
    }

    private sealed class ActionDto
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Sender { get; set; } = string.Empty;
        public string? Target { get; set; }
        public bool IsStartingAction { get; set; }
        public IEnumerable<string>? RequiredActionData { get; set; }
        public IEnumerable<string>? AdditionalRecipients { get; set; }
        public IEnumerable<object>? Disclosures { get; set; }
    }
}

/// <summary>
/// Result of a blueprint get query.
/// </summary>
public sealed record BlueprintGetResult
{
    /// <summary>
    /// Query status: Success, NotFound, Error, Unavailable, Timeout, or Unauthorized.
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// Human-readable message about the query result.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// When the query was performed.
    /// </summary>
    public required DateTimeOffset CheckedAt { get; init; }

    /// <summary>
    /// Response time in milliseconds.
    /// </summary>
    public int ResponseTimeMs { get; init; }

    /// <summary>
    /// The blueprint details (if found).
    /// </summary>
    public BlueprintDetail? Blueprint { get; init; }
}

/// <summary>
/// Full blueprint details.
/// </summary>
public sealed record BlueprintDetail
{
    /// <summary>
    /// Blueprint unique identifier.
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
    /// Blueprint version number.
    /// </summary>
    public int Version { get; init; }

    /// <summary>
    /// When the blueprint was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// When the blueprint was last updated.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>
    /// Blueprint metadata key-value pairs.
    /// </summary>
    public Dictionary<string, string>? Metadata { get; init; }

    /// <summary>
    /// List of participants in the blueprint.
    /// </summary>
    public IReadOnlyList<ParticipantInfo> Participants { get; init; } = [];

    /// <summary>
    /// List of actions in the blueprint.
    /// </summary>
    public IReadOnlyList<ActionInfo> Actions { get; init; } = [];
}

/// <summary>
/// Participant information.
/// </summary>
public sealed record ParticipantInfo
{
    /// <summary>
    /// Participant unique identifier.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Participant display name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Organization the participant belongs to.
    /// </summary>
    public string? Organisation { get; init; }

    /// <summary>
    /// Participant's wallet address.
    /// </summary>
    public string? WalletAddress { get; init; }

    /// <summary>
    /// Decentralized Identifier (DID) URI.
    /// </summary>
    public string? DidUri { get; init; }

    /// <summary>
    /// Whether the participant uses a stealth address.
    /// </summary>
    public bool UseStealthAddress { get; init; }
}

/// <summary>
/// Action information.
/// </summary>
public sealed record ActionInfo
{
    /// <summary>
    /// Action sequence ID.
    /// </summary>
    public int Id { get; init; }

    /// <summary>
    /// Action title.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// Action description.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Sender participant ID.
    /// </summary>
    public string? Sender { get; init; }

    /// <summary>
    /// Target participant ID.
    /// </summary>
    public string? Target { get; init; }

    /// <summary>
    /// Whether this is a starting action.
    /// </summary>
    public bool IsStartingAction { get; init; }

    /// <summary>
    /// List of required action data IDs.
    /// </summary>
    public IReadOnlyList<string> RequiredActionData { get; init; } = [];

    /// <summary>
    /// Additional recipient participant IDs.
    /// </summary>
    public IReadOnlyList<string> AdditionalRecipients { get; init; } = [];

    /// <summary>
    /// Number of disclosure rules defined for this action.
    /// </summary>
    public int DisclosureCount { get; init; }
}
