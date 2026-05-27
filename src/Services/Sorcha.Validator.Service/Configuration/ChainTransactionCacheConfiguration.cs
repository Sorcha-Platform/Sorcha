// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Validator.Service.Configuration;

/// <summary>
/// Configuration for <see cref="Services.IChainTransactionCache"/> — the
/// L1+L2 cache that fronts <c>VAL_CHAIN_PREDECESSOR_LOOKUP</c>.
/// </summary>
/// <remarks>
/// Mirrors <see cref="BlueprintCacheConfiguration"/>'s shape since this cache
/// follows the same pattern (Redis L2 + local L1, Polly resilience). Defaults
/// differ because transactions are immutable: TTL can be long, no
/// invalidation channel, no warmup.
/// </remarks>
public class ChainTransactionCacheConfiguration
{
    public const string SectionName = "ChainTransactionCache";

    /// <summary>Redis key prefix.</summary>
    public string KeyPrefix { get; set; } = "sorcha:validator:chain-tx:";

    /// <summary>
    /// Redis TTL. Transactions are immutable so the TTL is a memory-pressure
    /// guard, not a freshness one. 1h covers typical docket-batch validation
    /// windows comfortably.
    /// </summary>
    public TimeSpan DefaultTtl { get; set; } = TimeSpan.FromHours(1);

    public int MaxRetries { get; set; } = 3;
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>Enable the in-memory L1 cache (default true).</summary>
    public bool EnableLocalCache { get; set; } = true;

    public TimeSpan LocalCacheTtl { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Max L1 entries. Each entry is a TransactionModel (~1 KB envelope, payload
    /// sizes vary). 10k caps L1 at roughly 10–50 MB.
    /// </summary>
    public int LocalCacheMaxEntries { get; set; } = 10_000;

    /// <summary>Master switch for the cache. Disable for benchmarking comparisons.</summary>
    public bool Enabled { get; set; } = true;
}
