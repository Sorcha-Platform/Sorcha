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
/// Participant tool for querying register data. Reads via the typed
/// <see cref="IRegisterServiceClient"/> (spec 139 US4) so the caller's bearer is forwarded
/// and the route is contract-pinned, not hand-rolled.
/// </summary>
[McpServerToolType]
public sealed class RegisterQueryTool
{
    private readonly IMcpAuthorizationService _authService;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly IRegisterServiceClient _registerClient;
    private readonly ILogger<RegisterQueryTool> _logger;

    public RegisterQueryTool(
        IMcpAuthorizationService authService,
        IServiceAvailabilityTracker availabilityTracker,
        IRegisterServiceClient registerClient,
        ILogger<RegisterQueryTool> logger)
    {
        _authService = authService;
        _availabilityTracker = availabilityTracker;
        _registerClient = registerClient;
        _logger = logger;
    }

    /// <summary>
    /// Queries the raw transaction ledger of a register, paginated.
    /// </summary>
    /// <param name="registerId">The register ID to query.</param>
    /// <param name="page">Page number (1-based, default: 1).</param>
    /// <param name="pageSize">Items per page (default: 20, max: 100).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Query results.</returns>
    [McpServerTool(Name = "sorcha_register_query")]
    [Description("Returns the raw signed transactions recorded on a register's ledger, newest first, paginated. Call this when you need direct paginated ledger access; prefer sorcha_transaction_history instead when the goal is reconstructing an audit trail or scoping to one workflow instance via workflowInstanceId — both tools read the same underlying data for a register-wide query. Payload contents are encrypted and not decrypted here — only transaction envelope fields (sender, recipients, docket, blueprint/instance/action linkage, payload count) are returned. NOTE: the underlying endpoint (GET /api/registers/{registerId}/transactions) has no docket filter and no OData $filter — those parameters were removed because the server never bound them.")]
    public async Task<RegisterQueryResult> QueryRegisterAsync(
        [Description("The register ID to query")] string registerId,
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
                Message = "Access denied. This tool requires an authenticated consumer- or platform-tier caller.",
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
            "Querying register {RegisterId}, page {Page}",
            registerId, page);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Typed client forwards the caller's bearer and pins the route
            // (GET api/registers/{registerId}/transactions).
            var pageResult = await _registerClient.GetTransactionsAsync(
                registerId, page, pageSize, cancellationToken);

            stopwatch.Stop();
            _availabilityTracker.RecordSuccess("Register");

            _logger.LogInformation(
                "Query returned {Count} record(s) in {ElapsedMs}ms",
                pageResult.Transactions.Count, stopwatch.ElapsedMilliseconds);

            return new RegisterQueryResult
            {
                Status = "Success",
                Message = $"Query returned {pageResult.Transactions.Count} record(s).",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                Records = pageResult.Transactions.Select(MapRecord).ToList(),
                TotalCount = pageResult.Total,
                Page = pageResult.Page,
                PageSize = pageResult.PageSize
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

    // Maps the actual wire shape (Sorcha.Register.Models.TransactionModel — TxId, PrevTxId,
    // DocketNumber, SenderWallet, RecipientsWallets, TimeStamp, MetaData, PayloadCount, encrypted
    // Payloads) rather than the fictional OData-record shape (Id/DocketId/Data dictionary) this
    // tool previously assumed, which no endpoint has ever returned.
    private static RegisterRecord MapRecord(TransactionModel t) => new()
    {
        TransactionId = t.TxId,
        PreviousTransactionId = string.IsNullOrEmpty(t.PrevTxId) ? null : t.PrevTxId,
        DocketNumber = t.DocketNumber,
        SenderWallet = t.SenderWallet,
        RecipientWallets = t.RecipientsWallets?.ToList() ?? [],
        TimeStamp = new DateTimeOffset(DateTime.SpecifyKind(t.TimeStamp, DateTimeKind.Utc)),
        BlueprintId = t.MetaData?.BlueprintId,
        WorkflowInstanceId = t.MetaData?.InstanceId,
        ActionId = t.MetaData?.ActionId is { } actionId ? (int)actionId : null,
        PayloadCount = (int)t.PayloadCount
    };
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
    /// List of transactions matching the query.
    /// </summary>
    public IReadOnlyList<RegisterRecord> Records { get; init; } = [];

    /// <summary>
    /// Total number of transactions on the register.
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
/// A raw transaction record from the register's ledger. Payload contents are encrypted and not
/// exposed here — only envelope fields are.
/// </summary>
public sealed record RegisterRecord
{
    /// <summary>
    /// The transaction ID — the record's unique identifier on the ledger.
    /// </summary>
    public required string TransactionId { get; init; }

    /// <summary>
    /// The previous transaction ID in the sender's chain, if any.
    /// </summary>
    public string? PreviousTransactionId { get; init; }

    /// <summary>
    /// Docket number this transaction was sealed in, or null if still pending.
    /// </summary>
    public ulong? DocketNumber { get; init; }

    /// <summary>
    /// Wallet address that submitted the transaction.
    /// </summary>
    public required string SenderWallet { get; init; }

    /// <summary>
    /// Wallet addresses authorised to decrypt this transaction's payloads.
    /// </summary>
    public IReadOnlyList<string> RecipientWallets { get; init; } = [];

    /// <summary>
    /// When the transaction was recorded.
    /// </summary>
    public DateTimeOffset TimeStamp { get; init; }

    /// <summary>
    /// Blueprint ID this transaction belongs to, if it is a workflow action.
    /// </summary>
    public string? BlueprintId { get; init; }

    /// <summary>
    /// Workflow instance ID this transaction belongs to, if it is a workflow action.
    /// </summary>
    public string? WorkflowInstanceId { get; init; }

    /// <summary>
    /// Action sequence number within the blueprint, if this is a workflow action.
    /// </summary>
    public int? ActionId { get; init; }

    /// <summary>
    /// Number of encrypted payloads on this transaction. Contents are not decrypted here.
    /// </summary>
    public int PayloadCount { get; init; }
}
