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
/// Designer tool for listing blueprints with filtering and pagination.
/// </summary>
[McpServerToolType]
public sealed class BlueprintListTool
{
    private readonly IMcpSessionService _sessionService;
    private readonly IMcpAuthorizationService _authService;
    private readonly IMcpErrorHandler _errorHandler;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<BlueprintListTool> _logger;
    private readonly string _blueprintServiceEndpoint;

    public BlueprintListTool(
        IMcpSessionService sessionService,
        IMcpAuthorizationService authService,
        IMcpErrorHandler errorHandler,
        IServiceAvailabilityTracker availabilityTracker,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<BlueprintListTool> logger)
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
    /// Lists blueprints with optional filtering and pagination.
    /// </summary>
    /// <param name="page">Page number (1-based). Default is 1.</param>
    /// <param name="pageSize">Number of items per page. Default is 20, max 100.</param>
    /// <param name="search">Optional search term to filter by title or description.</param>
    /// <param name="status">Optional status filter (Draft, Published, Archived).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Paginated list of blueprint summaries.</returns>
    [McpServerTool(Name = "sorcha_blueprint_list")]
    [Description("Returns a paginated list of blueprint summaries (ID, title, description, participant count, action count) with optional filtering by status (Draft, Published, Archived) and free-text search across title and description. Call this when you need to discover blueprints by name or status without yet knowing their IDs; use sorcha_blueprint_get instead once you have a specific ID and want the full definition, and prefer this over sorcha_workflow_instances when you are looking for blueprint definitions rather than running workflow executions.")]
    public async Task<BlueprintListResult> ListBlueprintsAsync(
        [Description("Page number (1-based, default: 1)")] int page = 1,
        [Description("Items per page (default: 20, max: 100)")] int pageSize = 20,
        [Description("Search term for title/description filtering")] string? search = null,
        [Description("Status filter: Draft, Published, or Archived")] string? status = null,
        CancellationToken cancellationToken = default)
    {
        // Authorization check
        if (!_authService.CanInvokeTool("sorcha_blueprint_list"))
        {
            return new BlueprintListResult
            {
                Status = "Unauthorized",
                Message = "Access denied. This tool requires the sorcha:designer role.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // Check service availability
        if (!_availabilityTracker.IsServiceAvailable("Blueprint"))
        {
            return new BlueprintListResult
            {
                Status = "Unavailable",
                Message = "Blueprint service is currently unavailable. Please try again later.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // Validate parameters
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        _logger.LogInformation(
            "Listing blueprints: page={Page}, pageSize={PageSize}, search={Search}, status={Status}",
            page, pageSize, search ?? "(none)", status ?? "(none)");

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);

            var pagedResult = await FetchBlueprintsAsync(client, page, pageSize, search, status, cancellationToken);

            stopwatch.Stop();

            // Record success
            _availabilityTracker.RecordSuccess("Blueprint");

            // Determine status
            string resultStatus;
            string message;

            if (pagedResult == null)
            {
                resultStatus = "Error";
                message = "Failed to retrieve blueprints from the service.";
            }
            else
            {
                resultStatus = "Success";
                message = pagedResult.TotalCount == 0
                    ? "No blueprints found matching the specified criteria."
                    : $"Retrieved {pagedResult.Items.Count()} of {pagedResult.TotalCount} blueprints (page {pagedResult.Page}/{pagedResult.TotalPages}).";
            }

            _logger.LogInformation(
                "Blueprint list query completed in {ElapsedMs}ms. Status: {Status}, Count: {Count}",
                stopwatch.ElapsedMilliseconds, resultStatus, pagedResult?.Items.Count() ?? 0);

            return new BlueprintListResult
            {
                Status = resultStatus,
                Message = message,
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                Page = pagedResult?.Page ?? page,
                PageSize = pagedResult?.PageSize ?? pageSize,
                TotalCount = pagedResult?.TotalCount ?? 0,
                TotalPages = pagedResult?.TotalPages ?? 0,
                Blueprints = pagedResult?.Items.Select(b => new BlueprintSummaryItem
                {
                    Id = b.Id,
                    Title = b.Title,
                    Description = b.Description,
                    CreatedAt = b.CreatedAt,
                    UpdatedAt = b.UpdatedAt,
                    ParticipantCount = b.ParticipantCount,
                    ActionCount = b.ActionCount
                }).ToList() ?? []
            };
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("Blueprint");

            _logger.LogWarning("Blueprint list query timed out");

            return new BlueprintListResult
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

            _logger.LogWarning(ex, "Failed to query blueprint list");

            return new BlueprintListResult
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

            _logger.LogError(ex, "Unexpected error querying blueprint list");

            return new BlueprintListResult
            {
                Status = "Error",
                Message = "An unexpected error occurred while listing blueprints.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
    }

    private async Task<PagedBlueprintResponse?> FetchBlueprintsAsync(
        HttpClient client,
        int page,
        int pageSize,
        string? search,
        string? status,
        CancellationToken cancellationToken)
    {
        try
        {
            var queryParams = new List<string>
            {
                $"page={page}",
                $"pageSize={pageSize}"
            };

            if (!string.IsNullOrWhiteSpace(search))
            {
                queryParams.Add($"search={Uri.EscapeDataString(search)}");
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                queryParams.Add($"status={Uri.EscapeDataString(status)}");
            }

            var url = $"{_blueprintServiceEndpoint.TrimEnd('/')}/api/blueprints/?{string.Join("&", queryParams)}";
            var response = await client.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch blueprints: HTTP {StatusCode}", response.StatusCode);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<PagedBlueprintResponse>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error fetching blueprints");
            return null;
        }
    }

    // Internal response models for deserialization
    private sealed class PagedBlueprintResponse
    {
        public IEnumerable<BlueprintSummaryDto> Items { get; set; } = [];
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalCount { get; set; }
        public int TotalPages { get; set; }
    }

    private sealed class BlueprintSummaryDto
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public int ParticipantCount { get; set; }
        public int ActionCount { get; set; }
    }
}

/// <summary>
/// Result of a blueprint list query.
/// </summary>
public sealed record BlueprintListResult
{
    /// <summary>
    /// Query status: Success, Error, Unavailable, Timeout, or Unauthorized.
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
    /// Current page number.
    /// </summary>
    public int Page { get; init; }

    /// <summary>
    /// Number of items per page.
    /// </summary>
    public int PageSize { get; init; }

    /// <summary>
    /// Total number of blueprints matching the criteria.
    /// </summary>
    public int TotalCount { get; init; }

    /// <summary>
    /// Total number of pages.
    /// </summary>
    public int TotalPages { get; init; }

    /// <summary>
    /// List of blueprint summaries.
    /// </summary>
    public IReadOnlyList<BlueprintSummaryItem> Blueprints { get; init; } = [];
}

/// <summary>
/// Summary information about a blueprint.
/// </summary>
public sealed record BlueprintSummaryItem
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
    public string? Description { get; init; }

    /// <summary>
    /// When the blueprint was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// When the blueprint was last updated.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>
    /// Number of participants defined in the blueprint.
    /// </summary>
    public int ParticipantCount { get; init; }

    /// <summary>
    /// Number of actions defined in the blueprint.
    /// </summary>
    public int ActionCount { get; init; }
}
