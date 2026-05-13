// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models.Registers;

/// <summary>
/// Paginated response for transaction list queries.
/// </summary>
public record TransactionListResponse
{
    /// <summary>
    /// Current page number (1-based)
    /// </summary>
    public int Page { get; init; } = 1;

    /// <summary>
    /// Number of items per page
    /// </summary>
    public int PageSize { get; init; } = 20;

    /// <summary>
    /// Total count of transactions
    /// </summary>
    public int Total { get; init; }

    /// <summary>
    /// List of transactions for the current page
    /// </summary>
    public IReadOnlyList<TransactionViewModel> Transactions { get; init; } = [];

    /// <summary>
    /// Computed: Whether there are more transactions to load
    /// </summary>
    public bool HasMore => Page * PageSize < Total;

    /// <summary>
    /// Computed: Total number of pages
    /// </summary>
    public int TotalPages => (int)Math.Ceiling((double)Total / PageSize);
}
