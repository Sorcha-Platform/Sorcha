// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Sorcha.Wallet.Service.Services.Implementation;
using Sorcha.Wallet.Service.Services.Interfaces;

namespace Sorcha.Wallet.Service.Tests.Services;

/// <summary>
/// Tests for <see cref="RedisPendingApplicationStore"/> (Feature 124, T015).
/// Backed by <see cref="MemoryDistributedCache"/> so the suite is deterministic
/// and free of Redis dependencies — the contract is the
/// <see cref="IDistributedCache"/> interface, not the Redis client.
/// </summary>
public sealed class PendingApplicationStoreTests
{
    private static IPendingApplicationStore NewStore(out IDistributedCache cache)
    {
        cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        return new RedisPendingApplicationStore(cache);
    }

    [Fact]
    public async Task GetAsync_NoNoticeSet_ReturnsNull()
    {
        var store = NewStore(out _);
        var notice = await store.GetAsync(Guid.NewGuid());
        notice.Should().BeNull();
    }

    [Fact]
    public async Task SetAsync_ThenGetAsync_RoundTripsLabel()
    {
        var store = NewStore(out _);
        var platformUserId = Guid.NewGuid();

        var set = await store.SetAsync(platformUserId, "Assured Identity");

        set.Label.Should().Be("Assured Identity");
        set.SetAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));

        var read = await store.GetAsync(platformUserId);
        read.Should().NotBeNull();
        read!.Label.Should().Be("Assured Identity");
    }

    [Fact]
    public async Task SetAsync_ReplaceLabel_OverwritesPriorValue()
    {
        var store = NewStore(out _);
        var platformUserId = Guid.NewGuid();

        await store.SetAsync(platformUserId, "Assured Identity");
        await store.SetAsync(platformUserId, "Driving Licence");

        var read = await store.GetAsync(platformUserId);
        read!.Label.Should().Be("Driving Licence");
    }

    [Fact]
    public async Task ClearAsync_RemovesNotice_AndIsIdempotent()
    {
        var store = NewStore(out _);
        var platformUserId = Guid.NewGuid();

        await store.SetAsync(platformUserId, "Assured Identity");
        await store.ClearAsync(platformUserId);

        (await store.GetAsync(platformUserId)).Should().BeNull();

        // Idempotent — clearing again does not throw.
        var act = () => store.ClearAsync(platformUserId);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SetAsync_DistinctPlatformUsers_IsolatedKeys()
    {
        var store = NewStore(out _);
        var u1 = Guid.NewGuid();
        var u2 = Guid.NewGuid();

        await store.SetAsync(u1, "Assured Identity");
        await store.SetAsync(u2, "Driving Licence");

        (await store.GetAsync(u1))!.Label.Should().Be("Assured Identity");
        (await store.GetAsync(u2))!.Label.Should().Be("Driving Licence");
    }

    [Fact]
    public async Task SetAsync_WhitespaceLabel_Throws()
    {
        var store = NewStore(out _);
        var act = () => store.SetAsync(Guid.NewGuid(), "   ");
        await act.Should().ThrowAsync<ArgumentException>();
    }
}
