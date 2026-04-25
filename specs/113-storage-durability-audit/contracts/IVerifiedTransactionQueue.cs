// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
//
// Contract reference for feature 113-storage-durability-audit.
// This file is a planning artefact. The implementation lives in
// src/Services/Sorcha.Validator.Service/Storage/.

using Sorcha.Validator.Service.Models;

namespace Sorcha.Validator.Service.Storage;

/// <summary>
/// Mempool of verified-but-not-yet-sealed transactions, partitioned by
/// register. Backed by Redis Sorted Sets in production
/// (<c>RedisVerifiedTransactionQueue</c>) and by an in-process structure in
/// development (<c>InMemoryVerifiedTransactionQueue</c>). The in-memory
/// implementation is on the audited list — Production/Staging fails fast.
/// </summary>
/// <remarks>
/// Contract changes from previous Sorcha versions:
/// <list type="bullet">
/// <item><c>Dequeue</c> and <c>ReturnToQueue</c> are removed.</item>
/// <item><c>ClaimAsync</c> + <c>ConfirmAsync</c> + <c>ReleaseAsync</c> are added.</item>
/// </list>
/// The lease pattern lets HA-replica deployments share one mempool: the
/// active validator claims; if it crashes mid-seal, the lease auto-releases
/// on the next claim by any replica.
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
    /// transactions from the available pool, holding them under a lease that
    /// expires after <paramref name="leaseDuration"/>. Before claiming,
    /// any expired leases for this register are released back to the pool.
    /// </summary>
    /// <returns>The claimed leases, in priority order. Empty if the pool is empty.</returns>
    Task<IReadOnlyList<VerifiedTransactionLease>> ClaimAsync(
        string registerId,
        int maxCount,
        TimeSpan leaseDuration,
        CancellationToken ct);

    /// <summary>
    /// Confirms the seal of the given transactions. Removes them from the
    /// claimed set and the underlying payload store. Idempotent: confirming
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

    bool Contains(string registerId, string transactionId);
    int GetCount(string registerId);
    int GetTotalCount();

    bool Remove(string registerId, string transactionId);
    int Clear(string registerId);
    int ClearAll();

    /// <summary>
    /// Sweeps every register's expiry index, removing any transactions whose
    /// TTL has passed. Run on a 30s timer by a background hosted service.
    /// </summary>
    int CleanupExpired();

    VerifiedQueueStats GetStats();
    RegisterQueueStats GetRegisterStats(string registerId);
}

/// <summary>
/// Time-bounded hold on a verified transaction. See data-model.md §4.
/// </summary>
public sealed record VerifiedTransactionLease(
    string TransactionId,
    string RegisterId,
    VerifiedTransaction Transaction,
    DateTimeOffset LeaseExpiresAt);
