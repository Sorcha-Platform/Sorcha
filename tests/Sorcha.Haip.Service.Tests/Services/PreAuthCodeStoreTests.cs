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
/// Tests for PreAuthCodeStore — in-memory fallback (Redis not available in unit tests).
/// </summary>
public class PreAuthCodeStoreTests
{
    private readonly PreAuthCodeStore _store;

    public PreAuthCodeStoreTests()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Haip:PreAuthCodeLifetimeSeconds"] = "300"
            })
            .Build();

        _store = new PreAuthCodeStore(
            Mock.Of<ILogger<PreAuthCodeStore>>(),
            config);
    }

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
}
