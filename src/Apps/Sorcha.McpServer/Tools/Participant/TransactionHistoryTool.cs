// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.Register.Models;
using Sorcha.ServiceClients.Register;

namespace Sorcha.McpServer.Tools.Participant;

/// <summary>
/// Participant tool for viewing transaction history. Reads via the typed
/// <see cref="IRegisterServiceClient"/> (spec 139 US4) so the caller's bearer is forwarded
/// and the routes are contract-pinned, not hand-rolled.
/// </summary>
[McpServerToolType]
public sealed class TransactionHistoryTool
{
    private readonly IMcpAuthorizationService _authService;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly IRegisterServiceClient _registerClient;
    private readonly ILogger<TransactionHistoryTool> _logger;

    public TransactionHistoryTool(
        IMcpAuthorizationService authService,
        IServiceAvailabilityTracker availabilityTracker,
        IRegisterServiceClient registerClient,
        ILogger<TransactionHistoryTool> logger)
    {
        _authService = authService;
        _availabilityTracker = availabilityTracker;
        _registerClient = registerClient;
        _logger = logger;
    }

    /// <summary>
    /// Lists transactions for a register, optionally scoped to a single workflow instance.
    /// </summary>
    /// <param name="registerId">The register ID to read the transaction log from (required).</param>
    /// <param name="workflowInstanceId">Scope to a single workflow instance (optional).</param>
    /// <param name="page">Page number (1-based, default: 1) — applies to register-wide reads only.</param>
    /// <param name="pageSize">Items per page (default: 20, max: 100) — applies to register-wide reads only.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of transactions.</returns>
    [McpServerTool(Name = "sorcha_transaction_history")]
    [Description("Return the immutable, signed transaction log for a given register (or a single workflow instance within it), in submission order. Each row carries the originating wallet signature, transaction id, and the action or register-mutation it recorded — enough for an agent to reconstruct the complete audit trail without trusting the platform. A registerId is required; supply workflowInstanceId to scope the log to one workflow instance (pagination then does not apply). Call this when the agent needs to answer who-did-what-when questions or assemble provenance for downstream AI decisions; use sorcha_workflow_status instead when you want the current state of an in-flight workflow rather than its history, and prefer sorcha_register_query when querying current record values rather than the sequence of changes that produced them.")]
    public async Task<TransactionHistoryResult> GetTransactionHistoryAsync(
        [Description("The register ID to read transactions from (required)")] string registerId,
        [Description("Scope to a single workflow instance ID (optional)")] string? workflowInstanceId = null,
        [Description("Page number (1-based, default: 1) — register-wide reads only")] int page = 1,
        [Description("Items per page (default: 20, max: 100) — register-wide reads only")] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        // Authorization check
        if (!_authService.CanInvokeTool("sorcha_transaction_history"))
        {
            return new TransactionHistoryResult
            {
                Status = "Unauthorized",
                Message = "Access denied. This tool requires an authenticated consumer- or platform-tier caller.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // Validate input
        if (string.IsNullOrWhiteSpace(registerId))
        {
            return new TransactionHistoryResult
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
            return new TransactionHistoryResult
            {
                Status = "Unavailable",
                Message = "Register service is currently unavailable. Please try again later.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        _logger.LogInformation(
            "Getting transaction history. Register: {RegisterId}, Workflow: {WorkflowId}, Page: {Page}",
            registerId, workflowInstanceId ?? "all", page);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            List<TransactionModel> transactions;
            int totalCount;
            int totalPages;
            int effectivePage = page;
            int effectivePageSize = pageSize;

            if (!string.IsNullOrWhiteSpace(workflowInstanceId))
            {
                // Instance-scoped read returns the complete ordered instance log (no pagination).
                transactions = await _registerClient.GetTransactionsByInstanceIdAsync(
                    registerId, workflowInstanceId, cancellationToken);
                totalCount = transactions.Count;
                totalPages = totalCount > 0 ? 1 : 0;
                effectivePage = 1;
                effectivePageSize = totalCount;
            }
            else
            {
                var pageResult = await _registerClient.GetTransactionsAsync(
                    registerId, page, pageSize, cancellationToken);
                transactions = pageResult.Transactions;
                totalCount = pageResult.Total;
                totalPages = pageResult.TotalPages;
                effectivePage = pageResult.Page;
                effectivePageSize = pageResult.PageSize;
            }

            stopwatch.Stop();
            _availabilityTracker.RecordSuccess("Register");

            _logger.LogInformation(
                "Retrieved {Count} transactions in {ElapsedMs}ms",
                transactions.Count, stopwatch.ElapsedMilliseconds);

            return new TransactionHistoryResult
            {
                Status = "Success",
                Message = $"Retrieved {transactions.Count} transaction(s).",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                Transactions = transactions.Select(MapTransaction).ToList(),
                TotalCount = totalCount,
                Page = effectivePage,
                PageSize = effectivePageSize,
                TotalPages = totalPages
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

    private static TransactionInfo MapTransaction(TransactionModel t) => new()
    {
        TransactionId = t.TxId,
        RegisterId = t.RegisterId,
        WorkflowInstanceId = t.MetaData?.InstanceId,
        ActionId = t.MetaData?.ActionId is { } actionId ? (int)actionId : null,
        TransactionType = t.MetaData?.TransactionType.ToString() ?? "Action",
        Submitter = t.SenderWallet,
        Timestamp = t.TimeStamp,
        Signature = string.IsNullOrEmpty(t.Signature) ? null : t.Signature
    };
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
