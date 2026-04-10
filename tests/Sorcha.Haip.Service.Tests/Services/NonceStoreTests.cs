// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Haip.Service.Services;
using Xunit;

namespace Sorcha.Haip.Service.Tests.Services;

/// <summary>
/// Tests for NonceStore — in-memory fallback.
/// </summary>
public class NonceStoreTests
{
    private readonly NonceStore _store;

    public NonceStoreTests()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Haip:NonceLifetimeSeconds"] = "300"
            })
            .Build();

        _store = new NonceStore(
            Mock.Of<ILogger<NonceStore>>(),
            config);
    }

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
}
