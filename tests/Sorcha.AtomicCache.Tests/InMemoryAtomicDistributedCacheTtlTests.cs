// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.AtomicCache;

namespace Sorcha.AtomicCache.Tests;

/// <summary>
/// In-memory-specific TTL behaviour tests. The contract specifies expiry,
/// but Redis-backed expiry is delegated to Redis itself; the in-memory
/// implementation enforces it locally and these tests pin the local
/// behaviour without sleeping in real time.
/// </summary>
public class InMemoryAtomicDistributedCacheTtlTests
{
    // The InMemory implementation's expiry check uses DateTimeOffset.UtcNow internally,
    // so we use very short real TTLs and a short sleep instead of FakeTimeProvider.
    // Trade-off: ~50ms test runtime per case.

    [Fact]
    public async Task GetAsync_AfterTtl_ReturnsNull()
    {
        var cache = new InMemoryAtomicDistributedCache();
        await cache.SetAsync("k", "v", TimeSpan.FromMilliseconds(20), CancellationToken.None);

        await Task.Delay(50);

        (await cache.GetAsync("k", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task GetAndRemoveAsync_AfterTtl_ReturnsNull()
    {
        var cache = new InMemoryAtomicDistributedCache();
        await cache.SetAsync("k", "v", TimeSpan.FromMilliseconds(20), CancellationToken.None);

        await Task.Delay(50);

        (await cache.GetAndRemoveAsync("k", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task TryUpdateIfMatchAsync_AfterTtl_ReturnsFalse()
    {
        var cache = new InMemoryAtomicDistributedCache();
        await cache.SetAsync("k", "v", TimeSpan.FromMilliseconds(20), CancellationToken.None);

        await Task.Delay(50);

        var ok = await cache.TryUpdateIfMatchAsync("k", "v", "new", TimeSpan.FromMinutes(1), CancellationToken.None);
        ok.Should().BeFalse();
    }
}
