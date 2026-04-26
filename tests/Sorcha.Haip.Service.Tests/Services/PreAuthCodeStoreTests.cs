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
/// Tests for PreAuthCodeStore — backed by InMemoryAtomicDistributedCache.
/// </summary>
public class PreAuthCodeStoreTests : IDisposable
{
    private readonly PreAuthCodeStore _store;
    private readonly ServiceProvider _sp;

    public PreAuthCodeStoreTests()
    {
        // Build the metrics class through DI so it shares the host lifecycle and
        // is disposed cleanly with the ServiceProvider.
        _sp = new ServiceCollection()
            .AddMetrics()
            .AddSingleton<HaipNonceMetrics>()
            .BuildServiceProvider();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Haip:PreAuthCodeLifetimeSeconds"] = "300"
            })
            .Build();

        _store = new PreAuthCodeStore(
            new InMemoryAtomicDistributedCache(),
            _sp.GetRequiredService<HaipNonceMetrics>(),
            NullLogger<PreAuthCodeStore>.Instance,
            config);
    }

    public void Dispose() => _sp.Dispose();

    [Fact]
    public async Task CreateAsync_ReturnsNonEmptyCode()
    {
        var offerId = Guid.NewGuid();
        var code = await _store.CreateAsync(offerId);

        code.Should().NotBeNullOrWhiteSpace();
        code.Length.Should().BeGreaterThan(20);
    }

    [Fact]
    public async Task RedeemAsync_ValidCode_ReturnsOfferId()
    {
        var offerId = Guid.NewGuid();
        var code = await _store.CreateAsync(offerId);

        var result = await _store.RedeemAsync(code);

        result.Should().Be(offerId);
    }

    [Fact]
    public async Task RedeemAsync_InvalidCode_ReturnsNull()
    {
        var result = await _store.RedeemAsync("nonexistent-code");

        result.Should().BeNull();
    }

    [Fact]
    public async Task RedeemAsync_CodeReusedTwice_SecondRedemptionFails()
    {
        var offerId = Guid.NewGuid();
        var code = await _store.CreateAsync(offerId);

        var first = await _store.RedeemAsync(code);
        var second = await _store.RedeemAsync(code);

        first.Should().Be(offerId);
        second.Should().BeNull("pre-auth codes are one-time-use");
    }

    [Fact]
    public async Task RedeemAsync_ConcurrentRedeemsOfSameCode_ExactlyOneSucceeds()
    {
        // Two relying-party callbacks racing to redeem the same pre-auth code:
        // exactly one wins, the rest see null. Old Get+Remove pattern could allow
        // multiple successes; atomic GetAndRemoveAsync resolves to one winner.
        var offerId = Guid.NewGuid();
        var code = await _store.CreateAsync(offerId);

        var tasks = Enumerable.Range(0, 100)
            .Select(_ => Task.Run(() => _store.RedeemAsync(code)))
            .ToArray();
        var results = await Task.WhenAll(tasks);

        results.Count(r => r == offerId).Should().Be(1, "exactly one concurrent redeem must succeed");
        results.Count(r => r is null).Should().Be(99);
    }
}
