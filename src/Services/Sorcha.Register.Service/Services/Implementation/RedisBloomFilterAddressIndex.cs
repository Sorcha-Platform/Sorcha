// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sorcha.Register.Service.Services.Interfaces;
using StackExchange.Redis;

namespace Sorcha.Register.Service.Services.Implementation;

/// <summary>
/// Redis-backed bloom filter for local wallet address detection.
/// Uses SETBIT/GETBIT with MurmurHash3-derived hash functions.
/// Atomic rebuild via RENAME ensures concurrent MayContain calls
/// see either the old or new filter, never an empty one.
/// </summary>
public class RedisBloomFilterAddressIndex : ILocalAddressIndex
{
    private const string BloomKeyPrefix = "register:bloom:";
    private const string ParamsKeyPrefix = "register:bloom:params:";
    private const string RebuildSuffix = ":rebuild";

    private readonly IDatabase _database;
    private readonly ILogger<RedisBloomFilterAddressIndex> _logger;
    private readonly int _expectedAddressCount;
    private readonly double _falsePositiveRate;

    public RedisBloomFilterAddressIndex(
        IConnectionMultiplexer redis,
        IConfiguration configuration,
        ILogger<RedisBloomFilterAddressIndex> logger)
    {
        ArgumentNullException.ThrowIfNull(redis);
        ArgumentNullException.ThrowIfNull(configuration);

        _database = redis.GetDatabase();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _expectedAddressCount = configuration.GetValue("BloomFilter:ExpectedAddressCount", 100_000);
        _falsePositiveRate = configuration.GetValue("BloomFilter:FalsePositiveRate", 0.001);
    }

    /// <inheritdoc />
    public async Task<bool> AddAsync(string registerId, string address, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(registerId);
        ArgumentException.ThrowIfNullOrEmpty(address);

        var (bitArraySize, hashCount) = CalculateOptimalParameters(_expectedAddressCount, _falsePositiveRate);
        var bloomKey = BloomKeyPrefix + registerId;
        var paramsKey = ParamsKeyPrefix + registerId;

        var positions = ComputeHashPositions(address, hashCount, bitArraySize);
        var allAlreadySet = true;

        foreach (var position in positions)
        {
            var wasSet = await _database.StringGetBitAsync(bloomKey, position);
            if (!wasSet)
            {
                allAlreadySet = false;
                await _database.StringSetBitAsync(bloomKey, position, true);
            }
        }

        if (!allAlreadySet)
        {
            await _database.HashIncrementAsync(paramsKey, "address_count", 1);
            await EnsureParamsSetAsync(paramsKey, bitArraySize, hashCount);

            _logger.LogDebug("Added address to bloom filter for register {RegisterId}", registerId);
        }

        return !allAlreadySet;
    }

    /// <inheritdoc />
    public async Task<bool> MayContainAsync(string registerId, string address, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(registerId);
        ArgumentException.ThrowIfNullOrEmpty(address);

        var (bitArraySize, hashCount) = CalculateOptimalParameters(_expectedAddressCount, _falsePositiveRate);
        var bloomKey = BloomKeyPrefix + registerId;

        var positions = ComputeHashPositions(address, hashCount, bitArraySize);

        foreach (var position in positions)
        {
            if (!await _database.StringGetBitAsync(bloomKey, position))
                return false;
        }

        return true;
    }

    /// <inheritdoc />
    public async Task<BloomFilterStats> RebuildAsync(
        string registerId,
        IAsyncEnumerable<string> addresses,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(registerId);
        ArgumentNullException.ThrowIfNull(addresses);

        var sw = Stopwatch.StartNew();
        var (bitArraySize, hashCount) = CalculateOptimalParameters(_expectedAddressCount, _falsePositiveRate);

        var bloomKey = BloomKeyPrefix + registerId;
        var rebuildKey = bloomKey + RebuildSuffix;
        var paramsKey = ParamsKeyPrefix + registerId;

        // Delete the temp key if leftover from a previous failed rebuild
        await _database.KeyDeleteAsync(rebuildKey);

        var addressCount = 0;

        await foreach (var address in addresses.WithCancellation(cancellationToken))
        {
            if (string.IsNullOrEmpty(address))
                continue;

            var positions = ComputeHashPositions(address, hashCount, bitArraySize);
            foreach (var position in positions)
            {
                await _database.StringSetBitAsync(rebuildKey, position, true);
            }

            addressCount++;

            if (addressCount % 10_000 == 0)
            {
                _logger.LogInformation(
                    "Bloom filter rebuild progress for register {RegisterId}: {Count} addresses processed",
                    registerId, addressCount);
            }
        }

        // Atomic swap — concurrent MayContain calls see either old or new, never empty
        await _database.KeyRenameAsync(rebuildKey, bloomKey);

        // Update params
        var batch = _database.CreateBatch();
        batch.HashSetAsync(paramsKey, [
            new HashEntry("bit_array_size", bitArraySize),
            new HashEntry("hash_function_count", hashCount),
            new HashEntry("address_count", addressCount),
            new HashEntry("last_rebuilt_at", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
        ]);
        batch.Execute();

        sw.Stop();
        _logger.LogInformation(
            "Bloom filter rebuilt for register {RegisterId}: {AddressCount} addresses, {Duration}ms, {BitArraySize} bits, {HashCount} hash functions",
            registerId, addressCount, sw.ElapsedMilliseconds, bitArraySize, hashCount);

        return new BloomFilterStats(addressCount, bitArraySize, hashCount, DateTimeOffset.UtcNow);
    }

    /// <inheritdoc />
    public async Task<BloomFilterStats?> GetStatsAsync(string registerId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(registerId);

        var paramsKey = ParamsKeyPrefix + registerId;
        var entries = await _database.HashGetAllAsync(paramsKey);

        if (entries.Length == 0)
            return null;

        var dict = entries.ToDictionary(
            e => (string)e.Name!,
            e => (string)e.Value!);

        var addressCount = dict.TryGetValue("address_count", out var ac) ? int.Parse(ac) : 0;
        var bitArraySize = dict.TryGetValue("bit_array_size", out var bas) ? long.Parse(bas) : 0;
        var hashFunctionCount = dict.TryGetValue("hash_function_count", out var hfc) ? int.Parse(hfc) : 0;
        DateTimeOffset? lastRebuilt = dict.TryGetValue("last_rebuilt_at", out var lra)
            ? DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(lra))
            : null;

        return new BloomFilterStats(addressCount, bitArraySize, hashFunctionCount, lastRebuilt);
    }

    /// <summary>
    /// Calculate optimal bloom filter parameters for the given capacity and FP rate.
    /// m = -(n * ln(p)) / (ln(2)^2), k = (m/n) * ln(2)
    /// </summary>
    internal static (long BitArraySize, int HashCount) CalculateOptimalParameters(
        int expectedCount, double falsePositiveRate)
    {
        if (expectedCount <= 0) expectedCount = 1;
        if (falsePositiveRate <= 0 || falsePositiveRate >= 1) falsePositiveRate = 0.001;

        var m = (long)Math.Ceiling(-(expectedCount * Math.Log(falsePositiveRate)) / (Math.Log(2) * Math.Log(2)));
        var k = (int)Math.Round((double)m / expectedCount * Math.Log(2));

        return (m, Math.Max(k, 1));
    }

    /// <summary>
    /// Compute k hash positions for an address using double hashing.
    /// Uses SHA-256 to produce two 64-bit base hashes, then derives k positions
    /// via h_i(x) = (h1 + i * h2) mod m (Kirsch-Mitzenmacher optimization).
    /// </summary>
    internal static long[] ComputeHashPositions(string address, int hashCount, long bitArraySize)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(address));

        var h1 = BitConverter.ToUInt64(hash, 0);
        var h2 = BitConverter.ToUInt64(hash, 8);

        var positions = new long[hashCount];
        for (var i = 0; i < hashCount; i++)
        {
            var combined = h1 + (ulong)i * h2;
            positions[i] = (long)(combined % (ulong)bitArraySize);
        }

        return positions;
    }

    private async Task EnsureParamsSetAsync(string paramsKey, long bitArraySize, int hashCount)
    {
        var exists = await _database.KeyExistsAsync(paramsKey);
        if (!exists || !await _database.HashExistsAsync(paramsKey, "bit_array_size"))
        {
            await _database.HashSetAsync(paramsKey, [
                new HashEntry("bit_array_size", bitArraySize),
                new HashEntry("hash_function_count", hashCount)
            ]);
        }
    }
}
