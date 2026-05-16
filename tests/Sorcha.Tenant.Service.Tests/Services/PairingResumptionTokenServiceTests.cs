// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sorcha.AtomicCache;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Tests.Services;

/// <summary>
/// Feature 128 US2 — covers the resumption-token mint+redeem round-trip,
/// single-use enforcement, and rejection of empty/malformed tokens.
/// </summary>
public sealed class PairingResumptionTokenServiceTests
{
    private readonly InMemoryAtomicDistributedCache _cache = new();
    private readonly FakeTimeProvider _time = new();
    private readonly PairingResumptionTokenService _service;

    public PairingResumptionTokenServiceTests()
    {
        _service = new PairingResumptionTokenService(
            _cache, _time, NullLogger<PairingResumptionTokenService>.Instance);
    }

    [Fact]
    public async Task MintAsync_Returns_Url_Safe_Token_With_24h_Expiry()
    {
        var pu = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        _time.SetUtcNow(now);

        var minted = await _service.MintAsync(pu, CancellationToken.None);

        minted.Token.Should().NotBeNullOrWhiteSpace();
        minted.Token.Should().MatchRegex(@"^[A-Za-z0-9_\-]+$",
            "the token must be url-safe base64 — no '+', '/', or '=' chars");
        minted.ExpiresAt.Should().BeCloseTo(now.Add(PairingResumptionTokenService.Lifetime), TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task MintAsync_Throws_On_Empty_UserId()
    {
        var act = () => _service.MintAsync(Guid.Empty, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task RedeemAsync_Happy_Path_Returns_Bound_PlatformUserId()
    {
        var pu = Guid.NewGuid();
        var minted = await _service.MintAsync(pu, CancellationToken.None);

        var result = await _service.RedeemAsync(minted.Token, CancellationToken.None);

        result.Should().Be(pu);
    }

    [Fact]
    public async Task RedeemAsync_Second_Attempt_Returns_Null()
    {
        var pu = Guid.NewGuid();
        var minted = await _service.MintAsync(pu, CancellationToken.None);

        var first = await _service.RedeemAsync(minted.Token, CancellationToken.None);
        var second = await _service.RedeemAsync(minted.Token, CancellationToken.None);

        first.Should().Be(pu);
        second.Should().BeNull();
    }

    [Fact]
    public async Task RedeemAsync_Unknown_Token_Returns_Null()
    {
        var result = await _service.RedeemAsync("not-a-minted-token", CancellationToken.None);
        result.Should().BeNull();
    }

    [Fact]
    public async Task RedeemAsync_Empty_Token_Returns_Null()
    {
        var result = await _service.RedeemAsync("", CancellationToken.None);
        result.Should().BeNull();
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public void SetUtcNow(DateTimeOffset value) => _now = value;
        public override DateTimeOffset GetUtcNow() => _now;
    }
}
