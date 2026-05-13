// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics.Metrics;

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Moq;

using StackExchange.Redis;

using Sorcha.Register.Models;
using Sorcha.Validator.Service.Configuration;
using Sorcha.Validator.Service.Services;

namespace Sorcha.Validator.Service.Tests.Services;

/// <summary>
/// Unit tests for <see cref="ChainTransactionCache"/>. Focuses on L1 (local) cache
/// behaviour — Redis L2 is stubbed to a miss-only path so the tests exercise the
/// in-memory side. L2 integration is covered separately by the integration suite.
/// </summary>
public class ChainTransactionCacheTests
{
    private const string RegisterId = "reg-1";
    private const string TxId = "tx-1";

    private static ChainTransactionCache BuildCache(
        ChainTransactionCacheConfiguration? config = null)
    {
        var redis = new Mock<IConnectionMultiplexer>();
        var db = new Mock<IDatabase>();
        db.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);
        db.Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(), It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        redis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(db.Object);

        return new ChainTransactionCache(
            redis.Object,
            Options.Create(config ?? new ChainTransactionCacheConfiguration()),
            new DummyMeterFactory(),
            NullLogger<ChainTransactionCache>.Instance);
    }

    [Fact]
    public async Task GetOrFetch_FirstCall_InvokesFactory()
    {
        var cache = BuildCache();
        var invocations = 0;
        var expected = new TransactionModel { TxId = TxId, RegisterId = RegisterId };

        var result = await cache.GetOrFetchAsync(RegisterId, TxId, (_, _, _) =>
        {
            invocations++;
            return Task.FromResult<TransactionModel?>(expected);
        }, CancellationToken.None);

        result.Should().BeSameAs(expected);
        invocations.Should().Be(1);
    }

    [Fact]
    public async Task GetOrFetch_SecondCallSameKey_UsesCache()
    {
        var cache = BuildCache();
        var invocations = 0;
        var tx = new TransactionModel { TxId = TxId, RegisterId = RegisterId };

        await cache.GetOrFetchAsync(RegisterId, TxId, (_, _, _) =>
        {
            invocations++;
            return Task.FromResult<TransactionModel?>(tx);
        }, CancellationToken.None);

        await cache.GetOrFetchAsync(RegisterId, TxId, (_, _, _) =>
        {
            invocations++;
            return Task.FromResult<TransactionModel?>(tx);
        }, CancellationToken.None);

        invocations.Should().Be(1);
        var stats = cache.GetStats();
        stats.LocalCacheHits.Should().Be(1);
        stats.TotalMisses.Should().Be(1);
    }

    [Fact]
    public async Task GetOrFetch_NullResult_NotCached()
    {
        var cache = BuildCache();
        var invocations = 0;

        await cache.GetOrFetchAsync(RegisterId, TxId, (_, _, _) =>
        {
            invocations++;
            return Task.FromResult<TransactionModel?>(null);
        }, CancellationToken.None);

        await cache.GetOrFetchAsync(RegisterId, TxId, (_, _, _) =>
        {
            invocations++;
            return Task.FromResult<TransactionModel?>(null);
        }, CancellationToken.None);

        invocations.Should().Be(2);
    }

    [Fact]
    public async Task GetOrFetch_ConcurrentColdMisses_SingleFetch()
    {
        var cache = BuildCache();
        var invocations = 0;
        var gate = new TaskCompletionSource();

        async Task<TransactionModel?> Fetch(string _, string __, CancellationToken ___)
        {
            Interlocked.Increment(ref invocations);
            await gate.Task;
            return new TransactionModel { TxId = TxId, RegisterId = RegisterId };
        }

        var t1 = cache.GetOrFetchAsync(RegisterId, TxId, Fetch, CancellationToken.None);
        var t2 = cache.GetOrFetchAsync(RegisterId, TxId, Fetch, CancellationToken.None);
        var t3 = cache.GetOrFetchAsync(RegisterId, TxId, Fetch, CancellationToken.None);

        await Task.Delay(50);
        gate.SetResult();
        await Task.WhenAll(t1, t2, t3);

        invocations.Should().Be(1, "per-key lock collapses concurrent cold-cache fetches");
    }

    [Fact]
    public async Task GetOrFetch_DifferentKeys_IndependentFetches()
    {
        var cache = BuildCache();
        var invocations = 0;

        await cache.GetOrFetchAsync(RegisterId, "tx-a", (_, _, _) =>
        {
            invocations++;
            return Task.FromResult<TransactionModel?>(new TransactionModel { TxId = "tx-a", RegisterId = RegisterId });
        }, CancellationToken.None);

        await cache.GetOrFetchAsync(RegisterId, "tx-b", (_, _, _) =>
        {
            invocations++;
            return Task.FromResult<TransactionModel?>(new TransactionModel { TxId = "tx-b", RegisterId = RegisterId });
        }, CancellationToken.None);

        invocations.Should().Be(2);
    }

    [Fact]
    public async Task GetOrFetch_DisabledViaConfig_AlwaysFetches()
    {
        var cache = BuildCache(new ChainTransactionCacheConfiguration { Enabled = false });
        var invocations = 0;
        var tx = new TransactionModel { TxId = TxId, RegisterId = RegisterId };

        await cache.GetOrFetchAsync(RegisterId, TxId, (_, _, _) =>
        {
            invocations++;
            return Task.FromResult<TransactionModel?>(tx);
        }, CancellationToken.None);

        await cache.GetOrFetchAsync(RegisterId, TxId, (_, _, _) =>
        {
            invocations++;
            return Task.FromResult<TransactionModel?>(tx);
        }, CancellationToken.None);

        invocations.Should().Be(2);
    }

    [Fact]
    public async Task GetOrFetch_LocalCacheBoundedBySize()
    {
        var cache = BuildCache(new ChainTransactionCacheConfiguration
        {
            LocalCacheMaxEntries = 3,
            LocalCacheTtl = TimeSpan.FromMinutes(30),
        });

        // Fill past capacity.
        for (var i = 0; i < 10; i++)
        {
            var id = $"tx-{i}";
            await cache.GetOrFetchAsync(RegisterId, id, (_, txId, _) =>
                Task.FromResult<TransactionModel?>(new TransactionModel
                {
                    TxId = txId,
                    RegisterId = RegisterId,
                }),
                CancellationToken.None);
        }

        var stats = cache.GetStats();
        stats.LocalCacheEntries.Should().BeLessThanOrEqualTo(3);
    }

    private sealed class DummyMeterFactory : IMeterFactory
    {
        public Meter Create(MeterOptions options) => new(options);
        public void Dispose() { }
    }
}
