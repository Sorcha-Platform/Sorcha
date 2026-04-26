// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.AtomicCache;

namespace Sorcha.AtomicCache.Tests.Contracts;

/// <summary>
/// Behavioural contract for <see cref="IAtomicDistributedCache"/>.
/// Subclasses provide a fresh implementation per test and may add
/// implementation-specific tests on top.
/// </summary>
public abstract class IAtomicDistributedCacheContractTests
{
    /// <summary>Provides a fresh implementation instance.</summary>
    protected abstract IAtomicDistributedCache CreateCache();

    [Fact]
    public async Task SetAsync_ThenGetAsync_ReturnsValue()
    {
        var cache = CreateCache();
        await cache.SetAsync("k", "v", TimeSpan.FromMinutes(1), CancellationToken.None);

        var value = await cache.GetAsync("k", CancellationToken.None);

        value.Should().Be("v");
    }

    [Fact]
    public async Task GetAsync_AbsentKey_ReturnsNull()
    {
        var cache = CreateCache();
        var value = await cache.GetAsync("missing", CancellationToken.None);

        value.Should().BeNull();
    }

    [Fact]
    public async Task SetAsync_OverwritesExistingValue()
    {
        var cache = CreateCache();
        await cache.SetAsync("k", "first", TimeSpan.FromMinutes(1), CancellationToken.None);
        await cache.SetAsync("k", "second", TimeSpan.FromMinutes(1), CancellationToken.None);

        (await cache.GetAsync("k", CancellationToken.None)).Should().Be("second");
    }

    [Fact]
    public async Task RemoveAsync_PresentKey_ReturnsTrue_AndDeletes()
    {
        var cache = CreateCache();
        await cache.SetAsync("k", "v", TimeSpan.FromMinutes(1), CancellationToken.None);

        var removed = await cache.RemoveAsync("k", CancellationToken.None);

        removed.Should().BeTrue();
        (await cache.GetAsync("k", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task RemoveAsync_AbsentKey_ReturnsFalse()
    {
        var cache = CreateCache();

        var removed = await cache.RemoveAsync("missing", CancellationToken.None);

        removed.Should().BeFalse();
    }

    [Fact]
    public async Task GetAndRemoveAsync_PresentKey_ReturnsValue_AndDeletes()
    {
        var cache = CreateCache();
        await cache.SetAsync("k", "v", TimeSpan.FromMinutes(1), CancellationToken.None);

        var first = await cache.GetAndRemoveAsync("k", CancellationToken.None);
        var second = await cache.GetAndRemoveAsync("k", CancellationToken.None);

        first.Should().Be("v");
        second.Should().BeNull();
    }

    [Fact]
    public async Task GetAndRemoveAsync_AbsentKey_ReturnsNull()
    {
        var cache = CreateCache();
        var value = await cache.GetAndRemoveAsync("missing", CancellationToken.None);
        value.Should().BeNull();
    }

    [Fact]
    public async Task TryUpdateIfMatchAsync_ExpectedMatches_ReplacesAndReturnsTrue()
    {
        var cache = CreateCache();
        await cache.SetAsync("k", "old", TimeSpan.FromMinutes(1), CancellationToken.None);

        var ok = await cache.TryUpdateIfMatchAsync("k", "old", "new", TimeSpan.FromMinutes(1), CancellationToken.None);

        ok.Should().BeTrue();
        (await cache.GetAsync("k", CancellationToken.None)).Should().Be("new");
    }

    [Fact]
    public async Task TryUpdateIfMatchAsync_ExpectedMismatches_DoesNotReplace_AndReturnsFalse()
    {
        var cache = CreateCache();
        await cache.SetAsync("k", "current", TimeSpan.FromMinutes(1), CancellationToken.None);

        var ok = await cache.TryUpdateIfMatchAsync("k", "wrong-expected", "new", TimeSpan.FromMinutes(1), CancellationToken.None);

        ok.Should().BeFalse();
        (await cache.GetAsync("k", CancellationToken.None)).Should().Be("current");
    }

    [Fact]
    public async Task TryUpdateIfMatchAsync_AbsentKey_ReturnsFalse()
    {
        var cache = CreateCache();

        var ok = await cache.TryUpdateIfMatchAsync("missing", "anything", "new", TimeSpan.FromMinutes(1), CancellationToken.None);

        ok.Should().BeFalse();
    }

    [Fact]
    public async Task ConcurrentGetAndRemove_OnSingleKey_ExactlyOneSucceeds()
    {
        // The headline US3 contract: 100 concurrent consumers race on the same nonce-style
        // key — exactly one wins, the rest see null. Today's IDistributedCache pattern
        // (Get + Remove) fails this; GetAndRemoveAsync passes.
        var cache = CreateCache();
        await cache.SetAsync("nonce", "secret", TimeSpan.FromMinutes(1), CancellationToken.None);

        var tasks = Enumerable.Range(0, 100)
            .Select(_ => Task.Run(() => cache.GetAndRemoveAsync("nonce", CancellationToken.None)))
            .ToArray();
        var results = await Task.WhenAll(tasks);

        results.Count(r => r == "secret").Should().Be(1);
        results.Count(r => r is null).Should().Be(99);
    }

    [Fact]
    public async Task ConcurrentTryUpdateIfMatch_OnSingleKey_OneWinnerThenAllOthersFail()
    {
        // Two callbacks racing to transition presentation state — exactly one wins,
        // others see the new value and report mismatch.
        var cache = CreateCache();
        await cache.SetAsync("state", "pending", TimeSpan.FromMinutes(1), CancellationToken.None);

        var tasks = Enumerable.Range(0, 50)
            .Select(i => Task.Run(() =>
                cache.TryUpdateIfMatchAsync("state", "pending", $"terminal-{i}", TimeSpan.FromMinutes(1), CancellationToken.None)))
            .ToArray();
        var results = await Task.WhenAll(tasks);

        results.Count(r => r).Should().Be(1);
        results.Count(r => !r).Should().Be(49);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task GetAsync_BlankKey_Throws(string? key)
    {
        var cache = CreateCache();
        await cache.Invoking(c => c.GetAsync(key!, CancellationToken.None))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SetAsync_NonPositiveTtl_Throws()
    {
        var cache = CreateCache();
        await cache.Invoking(c => c.SetAsync("k", "v", TimeSpan.Zero, CancellationToken.None))
            .Should().ThrowAsync<ArgumentOutOfRangeException>();
    }
}
