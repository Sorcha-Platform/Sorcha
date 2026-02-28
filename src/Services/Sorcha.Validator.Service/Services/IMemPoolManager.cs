// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Validator.Service.Models;

namespace Sorcha.Validator.Service.Services;

/// <summary>
/// Manages memory pools for pending transactions per register with FIFO + priority queuing
/// </summary>
public interface IMemPoolManager
{
    /// <summary>
    /// Adds a transaction to the memory pool for the specified register
    /// </summary>
    /// <param name="registerId">Target register ID</param>
    /// <param name="transaction">Transaction to add</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if added successfully, false if pool is full or transaction already exists</returns>
    Task<bool> AddTransactionAsync(string registerId, Transaction transaction, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a transaction from the memory pool
    /// </summary>
    Task<bool> RemoveTransactionAsync(string registerId, string transactionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets pending transactions from the memory pool for docket building
    /// </summary>
    /// <param name="registerId">Register ID</param>
    /// <param name="maxCount">Maximum number of transactions to retrieve</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of transactions ordered by priority (high first) then FIFO</returns>
    Task<List<Transaction>> GetPendingTransactionsAsync(string registerId, int maxCount, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the count of transactions in a memory pool
    /// </summary>
    Task<int> GetTransactionCountAsync(string registerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets memory pool statistics for a register
    /// </summary>
    Task<MemPoolStats> GetStatsAsync(string registerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a transaction with the given payload hash exists in any memory pool
    /// </summary>
    /// <param name="payloadHash">SHA-256 hash of the transaction payload</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the transaction exists in the memory pool</returns>
    Task<bool> TransactionExistsAsync(string payloadHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes expired transactions from all memory pools
    /// </summary>
    Task CleanupExpiredTransactionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns transactions to the memory pool (e.g., after failed consensus)
    /// </summary>
    Task ReturnTransactionsAsync(string registerId, List<Transaction> transactions, CancellationToken cancellationToken = default);
}

/// <summary>
/// Memory pool statistics
/// </summary>
public record MemPoolStats
{
    public required string RegisterId { get; init; }
    public int TotalTransactions { get; init; }
    public int HighPriorityCount { get; init; }
    public int NormalPriorityCount { get; init; }
    public int LowPriorityCount { get; init; }
    public int MaxSize { get; init; }
    public double FillPercentage => MaxSize > 0 ? (double)TotalTransactions / MaxSize * 100 : 0;
    public int TotalEvictions { get; init; }
    public int TotalExpired { get; init; }
    public DateTimeOffset? OldestTransactionTime { get; init; }
}
