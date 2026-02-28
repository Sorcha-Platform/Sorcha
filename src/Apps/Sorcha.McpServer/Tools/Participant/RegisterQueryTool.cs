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

namespace Sorcha.McpServer.Tools.Participant;

/// <summary>
/// Participant tool for querying register data.
/// </summary>
[McpServerToolType]
public sealed class RegisterQueryTool
{
    private readonly IMcpSessionService _sessionService;
    private readonly IMcpAuthorizationService _authService;
    private readonly IMcpErrorHandler _errorHandler;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<RegisterQueryTool> _logger;
    private readonly string _registerServiceEndpoint;

    public RegisterQueryTool(
        IMcpSessionService sessionService,
        IMcpAuthorizationService authService,
        IMcpErrorHandler errorHandler,
        IServiceAvailabilityTracker availabilityTracker,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<RegisterQueryTool> logger)
    {
        _sessionService = sessionService;
        _authService = authService;
        _errorHandler = errorHandler;
        _availabilityTracker = availabilityTracker;
        _httpClientFactory = httpClientFactory;
        _logger = logger;

        _registerServiceEndpoint = configuration["ServiceClients:RegisterService:Address"] ?? "http://localhost:5290";
    }

    /// <summary>
    /// Queries data from a register.
    /// </summary>
    /// <param name="registerId">The register ID to query.</param>
    /// <param name="docketId">Filter by docket ID (optional).</param>
    /// <param name="query">OData-style query filter (optional).</param>
    /// <param name="page">Page number (1-based, default: 1).</param>
    /// <param name="pageSize">Items per page (default: 20, max: 100).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Query results.</returns>
    [McpServerTool(Name = "sorcha_register_query")]
    [Description("Query data from a register. Search and filter records stored on the distributed ledger. Supports OData-style filtering.")]
    public async Task<RegisterQueryResult> QueryRegisterAsync(
        [Description("The register ID to query")] string registerId,
        [Description("Filter by docket ID (optional)")] string? docketId = null,
        [Description("OData-style query filter (optional)")] string? query = null,
        [Description("Page number (1-based, default: 1)")] int page = 1,
        [Description("Items per page (default: 20, max: 100)")] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        // Authorization check
        if (!_authService.CanInvokeTool("sorcha_register_query"))
        {
            return new RegisterQueryResult
            {
                Status = "Unauthorized",
                Message = "Access denied. This tool requires the sorcha:participant role.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // Validate input
        if (string.IsNullOrWhiteSpace(registerId))
        {
            return new RegisterQueryResult
            {
                Status = "Error",
                Message = "Register ID is required.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // Validate pagination
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 100) pageSize = 100;

        // Check service availability
        if (!_availabilityTracker.IsServiceAvailable("Register"))
        {
            return new RegisterQueryResult
            {
                Status = "Unavailable",
                Message = "Register service is currently unavailable. Please try again later.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        _logger.LogInformation(
            "Querying register {RegisterId}, docket {DocketId}, page {Page}",
            registerId, docketId ?? "all", page);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(30);

            // Build query string
            var queryParams = new List<string>
            {
                $"$top={pageSize}",
                $"$skip={(page - 1) * pageSize}"
            };

            if (!string.IsNullOrWhiteSpace(docketId))
            {
                queryParams.Add($"docketId={Uri.EscapeDataString(docketId)}");
            }

            if (!string.IsNullOrWhiteSpace(query))
            {
                queryParams.Add($"$filter={Uri.EscapeDataString(query)}");
            }

            var url = $"{_registerServiceEndpoint.TrimEnd('/')}/api/registers/{registerId}/data?{string.Join("&", queryParams)}";

            var response = await client.GetAsync(url, cancellationToken);

            stopwatch.Stop();

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Register query failed: HTTP {StatusCode} - {Error}", response.StatusCode, errorContent);

                _availabilityTracker.RecordSuccess("Register");

                try
                {
                    var errorResponse = JsonSerializer.Deserialize<ErrorResponse>(errorContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    return new RegisterQueryResult
                    {
                        Status = "Error",
                        Message = errorResponse?.Error ?? "Query failed.",
                        CheckedAt = DateTimeOffset.UtcNow,
                        ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                    };
                }
                catch (JsonException)
                {
                    return new RegisterQueryResult
                    {
                        Status = "Error",
                        Message = $"Query failed with status {(int)response.StatusCode}.",
                        CheckedAt = DateTimeOffset.UtcNow,
                        ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                    };
                }
            }

            // Record success
            _availabilityTracker.RecordSuccess("Register");

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<RegisterQueryResponse>(responseContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (result == null)
            {
                return new RegisterQueryResult
                {
                    Status = "Error",
                    Message = "Failed to parse query response.",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

            _logger.LogInformation(
                "Query returned {Count} records in {ElapsedMs}ms",
                result.Value?.Count ?? 0, stopwatch.ElapsedMilliseconds);

            return new RegisterQueryResult
            {
                Status = "Success",
                Message = $"Query returned {result.Value?.Count ?? 0} record(s).",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                Records = result.Value?.Select(r => new RegisterRecord
                {
                    RecordId = r.Id ?? "",
                    DocketId = r.DocketId ?? "",
                    TransactionId = r.TransactionId,
                    Data = r.Data ?? new Dictionary<string, object>(),
                    CreatedAt = r.CreatedAt,
                    UpdatedAt = r.UpdatedAt
                }).ToList() ?? [],
                TotalCount = result.Count ?? result.Value?.Count ?? 0,
                Page = page,
                PageSize = pageSize
            };
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("Register");

            return new RegisterQueryResult
            {
                Status = "Timeout",
                Message = "Request to register service timed out.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("Register", ex);

            return new RegisterQueryResult
            {
                Status = "Error",
                Message = $"Failed to connect to register service: {ex.Message}",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("Register", ex);

            _logger.LogError(ex, "Unexpected error querying register");

            return new RegisterQueryResult
            {
                Status = "Error",
                Message = "An unexpected error occurred while querying register.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
    }

    // Internal response models (OData-style)
    private sealed class RegisterQueryResponse
    {
        public List<RecordDto>? Value { get; set; }
        public int? Count { get; set; }
    }

    private sealed class RecordDto
    {
        public string? Id { get; set; }
        public string? DocketId { get; set; }
        public string? TransactionId { get; set; }
        public Dictionary<string, object>? Data { get; set; }
        public DateTimeOffset? CreatedAt { get; set; }
        public DateTimeOffset? UpdatedAt { get; set; }
    }

    private sealed class ErrorResponse
    {
        public string? Error { get; set; }
    }
}

/// <summary>
/// Result of querying a register.
/// </summary>
public sealed record RegisterQueryResult
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
    /// List of records matching the query.
    /// </summary>
    public IReadOnlyList<RegisterRecord> Records { get; init; } = [];

    /// <summary>
    /// Total number of records matching the filter.
    /// </summary>
    public int TotalCount { get; init; }

    /// <summary>
    /// Current page number.
    /// </summary>
    public int Page { get; init; }

    /// <summary>
    /// Items per page.
    /// </summary>
    public int PageSize { get; init; }
}

/// <summary>
/// A record from the register.
/// </summary>
public sealed record RegisterRecord
{
    /// <summary>
    /// The record ID.
    /// </summary>
    public required string RecordId { get; init; }

    /// <summary>
    /// The docket ID this record belongs to.
    /// </summary>
    public required string DocketId { get; init; }

    /// <summary>
    /// The transaction ID that created this record.
    /// </summary>
    public string? TransactionId { get; init; }

    /// <summary>
    /// The record data.
    /// </summary>
    public Dictionary<string, object> Data { get; init; } = new();

    /// <summary>
    /// When the record was created.
    /// </summary>
    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>
    /// When the record was last updated.
    /// </summary>
    public DateTimeOffset? UpdatedAt { get; init; }
}
