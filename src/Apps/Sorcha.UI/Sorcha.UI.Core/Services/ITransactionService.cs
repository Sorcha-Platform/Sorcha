// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.UI.Core.Models.Registers;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Service for interacting with the Transaction API.
/// </summary>
public interface ITransactionService
{
    /// <summary>
    /// Gets paginated transactions for a register.
    /// </summary>
    /// <param name="registerId">Register identifier.</param>
    /// <param name="page">Page number (1-based).</param>
    /// <param name="pageSize">Number of items per page (max 100).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Paginated transaction list.</returns>
    Task<TransactionListResponse> GetTransactionsAsync(
        string registerId,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a single transaction by ID.
    /// </summary>
    /// <param name="registerId">Register identifier.</param>
    /// <param name="txId">Transaction identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Transaction details or null if not found.</returns>
    Task<TransactionViewModel?> GetTransactionAsync(
        string registerId,
        string txId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Queries transactions across all accessible registers by wallet address.
    /// </summary>
    /// <param name="walletAddress">Wallet address to search for (sender or recipient).</param>
    /// <param name="page">Page number (1-based).</param>
    /// <param name="pageSize">Number of items per page (max 100).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Paginated transaction query results with register context.</returns>
    Task<TransactionQueryResponse> QueryByWalletAsync(
        string walletAddress,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets transaction graph data for DAG visualization.
    /// </summary>
    /// <param name="registerId">Register identifier.</param>
    /// <param name="limit">Maximum number of nodes to return (default 200).</param>
    /// <param name="before">Cursor for pagination — return transactions before this TxId.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Transaction graph response with nodes for DAG rendering.</returns>
    Task<TransactionGraphResponse> GetTransactionGraphAsync(
        string registerId, int limit = 200, string? before = null, CancellationToken ct = default);
}

/// <summary>
/// Response for cross-register transaction queries.
/// </summary>
public record TransactionQueryResponse
{
    /// <summary>
    /// List of transactions matching the query.
    /// </summary>
    public IReadOnlyList<TransactionQueryResultItem> Items { get; init; } = [];

    /// <summary>
    /// Current page number.
    /// </summary>
    public int Page { get; init; } = 1;

    /// <summary>
    /// Items per page.
    /// </summary>
    public int PageSize { get; init; } = 20;

    /// <summary>
    /// Total number of matching transactions.
    /// </summary>
    public int TotalCount { get; init; }

    /// <summary>
    /// Total number of pages.
    /// </summary>
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;

    /// <summary>
    /// Whether there are more pages.
    /// </summary>
    public bool HasMore => Page < TotalPages;
}

/// <summary>
/// A transaction result with register context.
/// </summary>
public record TransactionQueryResultItem
{
    /// <summary>
    /// The transaction details.
    /// </summary>
    public required TransactionViewModel Transaction { get; init; }

    /// <summary>
    /// Register ID containing this transaction.
    /// </summary>
    public required string RegisterId { get; init; }

    /// <summary>
    /// Register name for display.
    /// </summary>
    public required string RegisterName { get; init; }
}
