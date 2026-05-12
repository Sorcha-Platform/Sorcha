// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models.Registers;

/// <summary>
/// State for cross-register transaction query form.
/// </summary>
public record TransactionQueryState
{
    /// <summary>
    /// Wallet address to search for.
    /// </summary>
    public string WalletAddress { get; init; } = string.Empty;

    /// <summary>
    /// Whether a query is currently executing.
    /// </summary>
    public bool IsLoading { get; init; }

    /// <summary>
    /// Error message if query failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Query results. Null if query not yet executed.
    /// </summary>
    public IReadOnlyList<TransactionQueryResult>? Results { get; init; }

    /// <summary>
    /// Current page number (1-based).
    /// </summary>
    public int Page { get; init; } = 1;

    /// <summary>
    /// Total number of results across all pages.
    /// </summary>
    public int Total { get; init; }

    /// <summary>
    /// Returns true if wallet address is valid for query.
    /// </summary>
    public bool IsValid =>
        !string.IsNullOrWhiteSpace(WalletAddress) &&
        WalletAddress.Length >= 26 &&
        WalletAddress.Length <= 58;

    /// <summary>
    /// Returns true if can submit query.
    /// </summary>
    public bool CanSubmit => IsValid && !IsLoading;

    /// <summary>
    /// Returns true if results are empty.
    /// </summary>
    public bool HasNoResults => Results is { Count: 0 };

    /// <summary>
    /// Returns true if has results to display.
    /// </summary>
    public bool HasResults => Results is { Count: > 0 };

    /// <summary>
    /// Returns true if more results are available.
    /// </summary>
    public bool HasMoreResults => Results != null && (Page * 20) < Total;

    /// <summary>
    /// Returns a new state with updated wallet address.
    /// </summary>
    public TransactionQueryState WithWalletAddress(string address) =>
        this with { WalletAddress = address, ErrorMessage = null };

    /// <summary>
    /// Returns a new state indicating loading started.
    /// </summary>
    public TransactionQueryState StartLoading() =>
        this with { IsLoading = true, ErrorMessage = null };

    /// <summary>
    /// Returns a new state indicating loading started with new wallet address.
    /// </summary>
    public TransactionQueryState StartLoading(string walletAddress) =>
        this with { IsLoading = true, ErrorMessage = null, WalletAddress = walletAddress, Page = 1, Results = null };

    /// <summary>
    /// Returns a new state with query results.
    /// </summary>
    public TransactionQueryState WithResults(IReadOnlyList<TransactionQueryResult> results, int total, int page) =>
        this with { IsLoading = false, Results = results, Total = total, Page = page, ErrorMessage = null };

    /// <summary>
    /// Returns a new state with query results (for initial page 1).
    /// </summary>
    public TransactionQueryState WithResults(IReadOnlyList<TransactionQueryResult> results, int total) =>
        WithResults(results, total, 1);

    /// <summary>
    /// Returns a new state with appended results (for pagination).
    /// </summary>
    public TransactionQueryState AppendResults(IReadOnlyList<TransactionQueryResult> newResults, int page)
    {
        var combined = Results?.Concat(newResults).ToList() ?? newResults.ToList();
        return this with { IsLoading = false, Results = combined, Page = page, ErrorMessage = null };
    }

    /// <summary>
    /// Returns a new state with error.
    /// </summary>
    public TransactionQueryState WithError(string error) =>
        this with { IsLoading = false, ErrorMessage = error };

    /// <summary>
    /// Returns a new state with error cleared.
    /// </summary>
    public TransactionQueryState ClearError() =>
        this with { ErrorMessage = null };

    /// <summary>
    /// Returns a new state with updated page number.
    /// </summary>
    public TransactionQueryState WithPage(int page) =>
        this with { Page = page };

    /// <summary>
    /// Returns a new state cleared for new query.
    /// </summary>
    public TransactionQueryState Clear() => new();
}

/// <summary>
/// Single result from cross-register transaction query.
/// </summary>
public record TransactionQueryResult
{
    /// <summary>
    /// The transaction data.
    /// </summary>
    public required TransactionViewModel Transaction { get; init; }

    /// <summary>
    /// Register name for context (not just ID).
    /// </summary>
    public required string RegisterName { get; init; }

    /// <summary>
    /// Register ID for navigation.
    /// </summary>
    public required string RegisterId { get; init; }
}
