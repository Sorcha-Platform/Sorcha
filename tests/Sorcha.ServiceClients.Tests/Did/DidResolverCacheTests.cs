// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Sorcha.ServiceClients.Did;

namespace Sorcha.ServiceClients.Tests.Did;

public class DidResolverCacheTests
{
    private static DidResolverCache CreateCache(
        FakeTimeProvider clock,
        int webTtlMinutes = 60,
        int negativeTtlSeconds = 60)
    {
        var options = Options.Create(new DidResolverCacheOptions
        {
            WebTtlMinutes = webTtlMinutes,
            NegativeTtlSeconds = negativeTtlSeconds
        });
        return new DidResolverCache(options, clock);
    }

    private static DidDocument Doc(string id) => new() { Id = id };

    [Fact]
    public async Task GetOrAddAsync_FirstCall_InvokesFactory()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var cache = CreateCache(clock);
        var calls = 0;

        var doc = await cache.GetOrAddAsync("did:web:example.com", () =>
        {
            calls++;
            return Task.FromResult<DidDocument?>(Doc("did:web:example.com"));
        });

        doc.Should().NotBeNull();
        calls.Should().Be(1);
    }

    [Fact]
    public async Task GetOrAddAsync_WithinPositiveTtl_DoesNotInvokeFactory()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var cache = CreateCache(clock, webTtlMinutes: 60);
        var calls = 0;
        Task<DidDocument?> Factory() { calls++; return Task.FromResult<DidDocument?>(Doc("did:web:example.com")); }

        await cache.GetOrAddAsync("did:web:example.com", Factory);
        clock.Advance(TimeSpan.FromMinutes(30));
        await cache.GetOrAddAsync("did:web:example.com", Factory);

        calls.Should().Be(1);
    }

    [Fact]
    public async Task GetOrAddAsync_AfterPositiveTtlExpires_InvokesFactoryAgain()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var cache = CreateCache(clock, webTtlMinutes: 60);
        var calls = 0;
        Task<DidDocument?> Factory() { calls++; return Task.FromResult<DidDocument?>(Doc("did:web:example.com")); }

        await cache.GetOrAddAsync("did:web:example.com", Factory);
        clock.Advance(TimeSpan.FromMinutes(61));
        await cache.GetOrAddAsync("did:web:example.com", Factory);

        calls.Should().Be(2);
    }

    [Fact]
    public async Task GetOrAddAsync_DidSorcha_PositiveTtlInfinite()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var cache = CreateCache(clock);
        var calls = 0;
        Task<DidDocument?> Factory() { calls++; return Task.FromResult<DidDocument?>(Doc("did:sorcha:org:abc")); }

        await cache.GetOrAddAsync("did:sorcha:org:abc", Factory);
        clock.Advance(TimeSpan.FromDays(7));
        await cache.GetOrAddAsync("did:sorcha:org:abc", Factory);

        calls.Should().Be(1);
    }

    [Fact]
    public async Task GetOrAddAsync_DidKey_PositiveTtlInfinite()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var cache = CreateCache(clock);
        var calls = 0;
        Task<DidDocument?> Factory() { calls++; return Task.FromResult<DidDocument?>(Doc("did:key:z6Mk")); }

        await cache.GetOrAddAsync("did:key:z6Mk", Factory);
        clock.Advance(TimeSpan.FromDays(30));
        await cache.GetOrAddAsync("did:key:z6Mk", Factory);

        calls.Should().Be(1);
    }

    [Fact]
    public async Task GetOrAddAsync_NegativeResultCachedForShortTtl()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var cache = CreateCache(clock, negativeTtlSeconds: 60);
        var calls = 0;
        Task<DidDocument?> Factory() { calls++; return Task.FromResult<DidDocument?>(null); }

        var first = await cache.GetOrAddAsync("did:web:example.com", Factory);
        clock.Advance(TimeSpan.FromSeconds(30));
        var second = await cache.GetOrAddAsync("did:web:example.com", Factory);

        first.Should().BeNull();
        second.Should().BeNull();
        calls.Should().Be(1);
    }

    [Fact]
    public async Task GetOrAddAsync_NegativeResult_RetriesAfterNegativeTtl()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var cache = CreateCache(clock, negativeTtlSeconds: 60);
        var calls = 0;
        Task<DidDocument?> Factory() { calls++; return Task.FromResult<DidDocument?>(null); }

        await cache.GetOrAddAsync("did:web:example.com", Factory);
        clock.Advance(TimeSpan.FromSeconds(61));
        await cache.GetOrAddAsync("did:web:example.com", Factory);

        calls.Should().Be(2);
    }

    [Fact]
    public async Task Invalidate_RemovesEntry_NextCallResolvesAgain()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var cache = CreateCache(clock);
        var calls = 0;
        Task<DidDocument?> Factory() { calls++; return Task.FromResult<DidDocument?>(Doc("did:sorcha:org:abc")); }

        await cache.GetOrAddAsync("did:sorcha:org:abc", Factory);
        cache.Invalidate("did:sorcha:org:abc");
        await cache.GetOrAddAsync("did:sorcha:org:abc", Factory);

        calls.Should().Be(2);
    }

    [Fact]
    public async Task GetOrAddAsync_ConcurrentCalls_CoalesceToSingleFactoryInvocation()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var cache = CreateCache(clock);
        var calls = 0;
        var gate = new TaskCompletionSource();

        Task<DidDocument?> Factory()
        {
            Interlocked.Increment(ref calls);
            return SlowFactory();
        }

        async Task<DidDocument?> SlowFactory()
        {
            await gate.Task;
            return Doc("did:web:example.com");
        }

        var t1 = cache.GetOrAddAsync("did:web:example.com", Factory);
        var t2 = cache.GetOrAddAsync("did:web:example.com", Factory);
        var t3 = cache.GetOrAddAsync("did:web:example.com", Factory);

        gate.SetResult();
        await Task.WhenAll(t1, t2, t3);

        calls.Should().Be(1);
        (await t1).Should().NotBeNull();
        (await t2).Should().NotBeNull();
        (await t3).Should().NotBeNull();
    }

    [Fact]
    public void Invalidate_NullOrEmpty_NoThrow()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var cache = CreateCache(clock);

        var act = () => { cache.Invalidate(""); cache.Invalidate(null!); };
        act.Should().NotThrow();
    }
}
