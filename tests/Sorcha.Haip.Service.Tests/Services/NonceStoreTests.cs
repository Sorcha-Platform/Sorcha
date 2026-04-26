// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics.Metrics;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sorcha.AtomicCache;
using Sorcha.Haip.Service.Services;
using Xunit;

namespace Sorcha.Haip.Service.Tests.Services;

/// <summary>
/// Tests for NonceStore — backed by InMemoryAtomicDistributedCache.
/// </summary>
public class NonceStoreTests : IDisposable
{
    private readonly NonceStore _store;
    private readonly ServiceProvider _sp;

    public NonceStoreTests()
    {
        _sp = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var meterFactory = _sp.GetRequiredService<IMeterFactory>();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Haip:NonceLifetimeSeconds"] = "300"
            })
            .Build();

        _store = new NonceStore(
            new InMemoryAtomicDistributedCache(),
            new HaipNonceMetrics(meterFactory),
            NullLogger<NonceStore>.Instance,
            config);
    }

    public void Dispose() => _sp.Dispose();

    [Fact]
    public async Task CreateAsync_ReturnsFreshNonce()
    {
        var (nonce, expiresIn) = await _store.CreateAsync();

        nonce.Should().NotBeNullOrWhiteSpace();
        expiresIn.Should().Be(300);
    }

    [Fact]
    public async Task ConsumeAsync_ValidNonce_ReturnsTrue()
    {
        var (nonce, _) = await _store.CreateAsync();

        var result = await _store.ConsumeAsync(nonce);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task ConsumeAsync_InvalidNonce_ReturnsFalse()
    {
        var result = await _store.ConsumeAsync("nonexistent-nonce");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ConsumeAsync_NonceReusedTwice_SecondConsumptionFails()
    {
        var (nonce, _) = await _store.CreateAsync();

        var first = await _store.ConsumeAsync(nonce);
        var second = await _store.ConsumeAsync(nonce);

        first.Should().BeTrue();
        second.Should().BeFalse("nonces are single-use");
    }

    [Fact]
    public async Task ConsumeAsync_ConcurrentConsumesOfSameNonce_ExactlyOneSucceeds()
    {
        // The headline US3 test: 100 concurrent consumers race on the same c_nonce.
        // Old IDistributedCache Get+Remove pattern would fail this — multiple succeed.
        // New IAtomicDistributedCache.GetAndRemoveAsync makes it impossible.
        var (nonce, _) = await _store.CreateAsync();

        var tasks = Enumerable.Range(0, 100)
            .Select(_ => Task.Run(() => _store.ConsumeAsync(nonce)))
            .ToArray();
        var results = await Task.WhenAll(tasks);

        results.Count(r => r).Should().Be(1, "exactly one concurrent consume of a single nonce must succeed");
        results.Count(r => !r).Should().Be(99);
    }
}
