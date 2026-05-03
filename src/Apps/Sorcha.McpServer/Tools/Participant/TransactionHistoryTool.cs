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
/// Participant tool for viewing transaction history.
/// </summary>
[McpServerToolType]
public sealed class TransactionHistoryTool
{
    private readonly IMcpSessionService _sessionService;
    private readonly IMcpAuthorizationService _authService;
    private readonly IMcpErrorHandler _errorHandler;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<TransactionHistoryTool> _logger;
    private readonly string _registerServiceEndpoint;

    public TransactionHistoryTool(
        IMcpSessionService sessionService,
        IMcpAuthorizationService authService,
        IMcpErrorHandler errorHandler,
        IServiceAvailabilityTracker availabilityTracker,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<TransactionHistoryTool> logger)
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
    /// Lists transactions for a workflow or register.
    /// </summary>
    /// <param name="workflowInstanceId">Filter by workflow instance ID (optional).</param>
    /// <param name="registerId">Filter by register ID (optional).</param>
    /// <param name="page">Page number (1-based, default: 1).</param>
    /// <param name="pageSize">Items per page (default: 20, max: 100).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of transactions.</returns>
    [McpServerTool(Name = "sorcha_transaction_history")]
    [Description("Return the immutable, signed transaction log for a given workflow instance or register, in submission order. Each row carries the originating wallet signature, transaction id, and the action or register-mutation it recorded — enough for an agent to reconstruct the complete audit trail without trusting the platform. Call this when the agent needs to answer who-did-what-when questions or assemble provenance for downstream AI decisions; use sorcha_workflow_status instead when you want the current state of an in-flight workflow rather than its history, and prefer sorcha_register_query when querying current record values rather than the sequence of changes that produced them.")]
    public async Task<TransactionHistoryResult> GetTransactionHistoryAsync(
        [Description("Filter by workflow instance ID (optional)")] string? workflowInstanceId = null,
        [Description("Filter by register ID (optional)")] string? registerId = null,
        [Description("Page number (1-based, default: 1)")] int page = 1,
        [Description("Items per page (default: 20, max: 100)")] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        // Authorization check
        if (!_authService.CanInvokeTool("sorcha_transaction_history"))
        {
            return new TransactionHistoryResult
            {
                Status = "Unauthorized",
                Message = "Access denied. This tool requires the sorcha:participant role.",
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
            return new TransactionHistoryResult
            {
                Status = "Unavailable",
                Message = "Register service is currently unavailable. Please try again later.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        _logger.LogInformation(
            "Getting transaction history. Workflow: {WorkflowId}, Register: {RegisterId}, Page: {Page}",
            workflowInstanceId ?? "all", registerId ?? "all", page);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(30);

            // Build query string
            var queryParams = new List<string>
            {
                $"page={page}",
                $"pageSize={pageSize}"
            };

            if (!string.IsNullOrWhiteSpace(workflowInstanceId))
            {
                queryParams.Add($"workflowInstanceId={Uri.EscapeDataString(workflowInstanceId)}");
            }

            if (!string.IsNullOrWhiteSpace(registerId))
            {
                queryParams.Add($"registerId={Uri.EscapeDataString(registerId)}");
            }

            var url = $"{_registerServiceEndpoint.TrimEnd('/')}/api/transactions?{string.Join("&", queryParams)}";

            var response = await client.GetAsync(url, cancellationToken);

            stopwatch.Stop();

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Transaction history request failed: HTTP {StatusCode} - {Error}", response.StatusCode, errorContent);

                _availabilityTracker.RecordSuccess("Register");

                return new TransactionHistoryResult
                {
                    Status = "Error",
                    Message = "Failed to retrieve transaction history.",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

            // Record success
            _availabilityTracker.RecordSuccess("Register");

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<TransactionHistoryResponse>(responseContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (result == null)
            {
                return new TransactionHistoryResult
                {
                    Status = "Error",
                    Message = "Failed to parse transaction history response.",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

            _logger.LogInformation(
                "Retrieved {Count} transactions in {ElapsedMs}ms",
                result.Items?.Count ?? 0, stopwatch.ElapsedMilliseconds);

            return new TransactionHistoryResult
            {
                Status = "Success",
                Message = $"Retrieved {result.Items?.Count ?? 0} transaction(s).",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                Transactions = result.Items?.Select(t => new TransactionInfo
                {
                    TransactionId = t.TransactionId ?? "",
                    RegisterId = t.RegisterId ?? "",
                    WorkflowInstanceId = t.WorkflowInstanceId,
                    ActionId = t.ActionId,
                    TransactionType = t.TransactionType ?? "Action",
                    Submitter = t.Submitter ?? "",
                    Timestamp = t.Timestamp,
                    Signature = t.Signature
                }).ToList() ?? [],
                TotalCount = result.TotalCount,
                Page = result.Page,
                PageSize = result.PageSize,
                TotalPages = result.TotalPages
            };
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("Register");

            return new TransactionHistoryResult
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

            return new TransactionHistoryResult
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

            _logger.LogError(ex, "Unexpected error getting transaction history");

            return new TransactionHistoryResult
            {
                Status = "Error",
                Message = "An unexpected error occurred while getting transaction history.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
    }

    // Internal response models
    private sealed class TransactionHistoryResponse
    {
        public List<TransactionDto>? Items { get; set; }
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages { get; set; }
    }

    private sealed class TransactionDto
    {
        public string? TransactionId { get; set; }
        public string? RegisterId { get; set; }
        public string? WorkflowInstanceId { get; set; }
        public int? ActionId { get; set; }
        public string? TransactionType { get; set; }
        public string? Submitter { get; set; }
        public DateTimeOffset? Timestamp { get; set; }
        public string? Signature { get; set; }
    }
}

/// <summary>
/// Result of getting transaction history.
/// </summary>
public sealed record TransactionHistoryResult
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
    /// List of transactions.
    /// </summary>
    public IReadOnlyList<TransactionInfo> Transactions { get; init; } = [];

    /// <summary>
    /// Total number of transactions matching the filter.
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

    /// <summary>
    /// Total number of pages.
    /// </summary>
    public int TotalPages { get; init; }
}

/// <summary>
/// Information about a transaction.
/// </summary>
public sealed record TransactionInfo
{
    /// <summary>
    /// The transaction ID.
    /// </summary>
    public required string TransactionId { get; init; }

    /// <summary>
    /// The register ID.
    /// </summary>
    public required string RegisterId { get; init; }

    /// <summary>
    /// The workflow instance ID if this transaction is part of a workflow.
    /// </summary>
    public string? WorkflowInstanceId { get; init; }

    /// <summary>
    /// The action ID if this transaction is an action submission.
    /// </summary>
    public int? ActionId { get; init; }

    /// <summary>
    /// Transaction type (Action, Data, etc.).
    /// </summary>
    public required string TransactionType { get; init; }

    /// <summary>
    /// Who submitted the transaction.
    /// </summary>
    public required string Submitter { get; init; }

    /// <summary>
    /// When the transaction was recorded.
    /// </summary>
    public DateTimeOffset? Timestamp { get; init; }

    /// <summary>
    /// Transaction signature (truncated for display).
    /// </summary>
    public string? Signature { get; init; }
}
