// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Register.Models;

namespace Sorcha.Validator.Service.Services.Interfaces;

/// <summary>
/// L1+L2 cache for <c>IRegisterServiceClient.GetTransactionAsync</c>, used by the
/// chain-section predecessor lookup hot path (<c>VAL_CHAIN_PREDECESSOR_LOOKUP</c>).
/// Follows the <see cref="IBlueprintCache"/> pattern.
/// </summary>
public interface IChainTransactionCache
{
    /// <summary>Get a cached transaction, or null when neither L1 nor L2 has it.</summary>
    Task<TransactionModel?> GetAsync(
        string registerId,
        string txId,
        CancellationToken ct = default);

    /// <summary>
    /// Get from cache or fetch via <paramref name="factory"/> and cache the result.
    /// Null fetch results are NOT cached — a missing predecessor today may be a
    /// not-yet-replicated tx tomorrow.
    /// </summary>
    Task<TransactionModel?> GetOrFetchAsync(
        string registerId,
        string txId,
        Func<string, string, CancellationToken, Task<TransactionModel?>> factory,
        CancellationToken ct = default);

    /// <summary>Current cache hit / miss statistics.</summary>
    ChainTransactionCacheStats GetStats();
}

/// <summary>Counter snapshot for <see cref="IChainTransactionCache"/>.</summary>
public sealed class ChainTransactionCacheStats
{
    public long TotalHits { get; init; }
    public long TotalMisses { get; init; }
    public long LocalCacheHits { get; init; }
    public long RedisCacheHits { get; init; }
    public long LocalCacheEntries { get; init; }
}
