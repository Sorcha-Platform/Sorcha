// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Validator.Service.Models;

namespace Sorcha.Validator.Service.Services.Interfaces;

/// <summary>
/// Mempool of verified-but-not-yet-sealed transactions, partitioned by
/// register. Backed by an in-process structure today
/// (<see cref="Sorcha.Validator.Service.Services.InMemoryVerifiedTransactionQueue"/>);
/// a Redis-backed implementation will land in PR 8 to give the mempool
/// restart durability and unlock the HA-replica deployment shape.
/// </summary>
/// <remarks>
/// <para>
/// Contract change vs prior Sorcha versions: the atomic
/// <c>Dequeue</c>/<c>ReturnToQueue</c> pair is replaced by
/// <see cref="ClaimAsync"/>/<see cref="ConfirmAsync"/>/<see cref="ReleaseAsync"/>.
/// </para>
/// <para>
/// The lease pattern lets HA-replica deployments share one mempool: the
/// active validator claims; if it crashes mid-seal, the lease auto-releases
/// on the next claim by any replica. A single-validator deployment uses the
/// same API; the lease just expires unobserved.
/// </para>
/// </remarks>
public interface IVerifiedTransactionQueue
{
    /// <summary>
    /// Adds a verified transaction to the available pool for the given register.
    /// </summary>
    /// <returns>True if enqueued; false if rejected (queue full, duplicate, etc.).</returns>
    bool Enqueue(string registerId, Transaction transaction, int priority = 0);

    /// <summary>
    /// Atomically claims up to <paramref name="maxCount"/> highest-priority
    /// transactions from the available pool, holding them under a lease
    /// that expires after <paramref name="leaseDuration"/>. Before claiming,
    /// any expired leases for this register are released back to the pool.
    /// </summary>
    Task<IReadOnlyList<VerifiedTransactionLease>> ClaimAsync(
        string registerId,
        int maxCount,
        TimeSpan leaseDuration,
        CancellationToken ct);

    /// <summary>
    /// Confirms the seal of the given transactions. Removes them from the
    /// claimed set and the underlying payload store. Idempotent — confirming
    /// an already-confirmed (or never-claimed) transaction is a no-op.
    /// </summary>
    Task ConfirmAsync(
        string registerId,
        IEnumerable<string> transactionIds,
        CancellationToken ct);

    /// <summary>
    /// Releases the claim, returning the transactions to the available pool
    /// at their original priority. Idempotent.
    /// </summary>
    Task ReleaseAsync(
        string registerId,
        IEnumerable<string> transactionIds,
        CancellationToken ct);

    /// <summary>
    /// Returns up to <paramref name="maxCount"/> available transactions
    /// without consuming or claiming them. Used by HA standby replicas
    /// for cache-warming and by introspection tooling. Read-only.
    /// </summary>
    IReadOnlyList<VerifiedTransaction> Peek(string registerId, int maxCount);

    /// <summary>Remove a specific transaction from the queue (any state).</summary>
    bool Remove(string registerId, string transactionId);

    /// <summary>Check if a transaction is in the queue (available or claimed).</summary>
    bool Contains(string registerId, string transactionId);

    /// <summary>Get the count of queued transactions for a register (available + claimed).</summary>
    int GetCount(string registerId);

    /// <summary>Get total count of queued transactions across all registers.</summary>
    int GetTotalCount();

    /// <summary>Clear all transactions for a register.</summary>
    int Clear(string registerId);

    /// <summary>Clear all transactions across all registers.</summary>
    int ClearAll();

    /// <summary>
    /// Sweeps every register, removing TTL-expired transactions and releasing
    /// expired leases. Run on a 30s timer by a background hosted service.
    /// </summary>
    int CleanupExpired();

    /// <summary>Get queue statistics across all registers.</summary>
    VerifiedQueueStats GetStats();

    /// <summary>Get queue statistics for a specific register.</summary>
    RegisterQueueStats GetRegisterStats(string registerId);
}

/// <summary>
/// Time-bounded hold on a verified transaction returned by
/// <see cref="IVerifiedTransactionQueue.ClaimAsync"/>.
/// </summary>
public sealed record VerifiedTransactionLease
{
    /// <summary>Transaction id (convenience accessor).</summary>
    public string TransactionId => Transaction.TransactionId;

    /// <summary>Owning register.</summary>
    public required string RegisterId { get; init; }

    /// <summary>The verified transaction payload.</summary>
    public required VerifiedTransaction Transaction { get; init; }

    /// <summary>UTC time after which the lease auto-releases on next ClaimAsync.</summary>
    public required DateTimeOffset LeaseExpiresAt { get; init; }
}

/// <summary>
/// A verified transaction with metadata.
/// </summary>
public record VerifiedTransaction
{
    /// <summary>The validated transaction.</summary>
    public required Transaction Transaction { get; init; }

    /// <summary>When the transaction was validated and enqueued.</summary>
    public required DateTimeOffset EnqueuedAt { get; init; }

    /// <summary>Priority for docket ordering (higher = processed first).</summary>
    public required int Priority { get; init; }

    /// <summary>When this entry expires and should be removed.</summary>
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>Transaction ID (convenience accessor).</summary>
    public string TransactionId => Transaction.TransactionId;
}

/// <summary>
/// Queue statistics across all registers.
/// </summary>
public record VerifiedQueueStats
{
    /// <summary>Total transactions in queue (available + claimed).</summary>
    public int TotalTransactions { get; init; }

    /// <summary>Number of active registers with queued transactions.</summary>
    public int ActiveRegisters { get; init; }

    /// <summary>Average transactions per register.</summary>
    public double AverageTransactionsPerRegister { get; init; }

    /// <summary>Oldest transaction in queue (null if empty).</summary>
    public DateTimeOffset? OldestTransaction { get; init; }

    /// <summary>Newest transaction in queue (null if empty).</summary>
    public DateTimeOffset? NewestTransaction { get; init; }

    /// <summary>Total transactions enqueued since service start.</summary>
    public long TotalEnqueued { get; init; }

    /// <summary>Total transactions confirmed (sealed) since service start.</summary>
    public long TotalConfirmed { get; init; }

    /// <summary>Total transactions expired since service start.</summary>
    public long TotalExpired { get; init; }
}

/// <summary>
/// Queue statistics for a specific register.
/// </summary>
public record RegisterQueueStats
{
    /// <summary>Register ID.</summary>
    public required string RegisterId { get; init; }

    /// <summary>Current transaction count (available + claimed).</summary>
    public int TransactionCount { get; init; }

    /// <summary>Oldest transaction in queue (null if empty).</summary>
    public DateTimeOffset? OldestTransaction { get; init; }

    /// <summary>Newest transaction in queue (null if empty).</summary>
    public DateTimeOffset? NewestTransaction { get; init; }

    /// <summary>Average priority of queued transactions.</summary>
    public double AveragePriority { get; init; }
}
