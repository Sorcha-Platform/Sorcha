// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Register.Service.Services.Interfaces;

/// <summary>
/// Probabilistic index of local wallet addresses using a bloom filter.
/// Supports fast membership testing with no false negatives and configurable false positive rate.
/// </summary>
public interface ILocalAddressIndex
{
    /// <summary>Add a wallet address to the bloom filter index.</summary>
    /// <param name="registerId">Register this address belongs to.</param>
    /// <param name="address">The wallet address to add.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the address was added (bits changed); false if already present.</returns>
    Task<bool> AddAsync(string registerId, string address, CancellationToken cancellationToken = default);

    /// <summary>Check if an address may be in the bloom filter (probabilistic).</summary>
    /// <param name="registerId">Register to check against.</param>
    /// <param name="address">The wallet address to test.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the address may be present (could be false positive); false means definitely not present.</returns>
    Task<bool> MayContainAsync(string registerId, string address, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rebuild the bloom filter from a complete set of addresses.
    /// Uses atomic swap (build under temp key, RENAME) so concurrent MayContain calls see either
    /// the old or new filter, never an empty one.
    /// </summary>
    /// <param name="registerId">Register to rebuild the index for.</param>
    /// <param name="addresses">Complete set of addresses to populate the filter with.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Statistics about the rebuilt index.</returns>
    Task<BloomFilterStats> RebuildAsync(string registerId, IAsyncEnumerable<string> addresses, CancellationToken cancellationToken = default);

    /// <summary>Get current bloom filter statistics for a register.</summary>
    /// <param name="registerId">Register to query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Current index statistics, or null if no index exists.</returns>
    Task<BloomFilterStats?> GetStatsAsync(string registerId, CancellationToken cancellationToken = default);
}

/// <summary>Statistics about a bloom filter index.</summary>
/// <param name="AddressCount">Number of addresses in the filter.</param>
/// <param name="BitArraySize">Size of the bit array in bits.</param>
/// <param name="HashFunctionCount">Number of hash functions used.</param>
/// <param name="LastRebuiltAt">When the filter was last fully rebuilt.</param>
public record BloomFilterStats(
    int AddressCount,
    long BitArraySize,
    int HashFunctionCount,
    DateTimeOffset? LastRebuiltAt);
