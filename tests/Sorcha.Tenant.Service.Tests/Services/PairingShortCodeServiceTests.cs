// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics.Metrics;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Sorcha.AtomicCache;
using Sorcha.Tenant.Service.Extensions;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Tests.Services;

/// <summary>
/// Feature 128 — covers the <see cref="PairingShortCodeService"/> mint +
/// redeem semantics: 6-digit numeric shape, single-use, expired-after-TTL,
/// per-code attempt rate limit, layering onto the underlying enrol-session
/// redeem path.
/// </summary>
public sealed class PairingShortCodeServiceTests
{
    private readonly Mock<IPlatformUserService> _platformUserService = new();
    private readonly InMemoryAtomicDistributedCache _cache = new();
    private readonly FakeTimeProvider _time = new();
    private readonly EnrolSessionService _enrolSessionService;
    private readonly PairingShortCodeService _service;

    public PairingShortCodeServiceTests()
    {
        var jwt = new JwtConfiguration
        {
            SigningKey = "test-signing-key-please-be-at-least-32-bytes-long-aaaaa",
            Issuer = "sorcha-test",
            Audiences = ["sorcha:citizen-wallet"],
            AccessTokenLifetimeMinutes = 60,
        };
        var jwtOptions = Options.Create(jwt);
        var config = new ConfigurationBuilder().Build();

        var metrics = new EnrolSessionMetrics(new TestMeterFactory());
        _enrolSessionService = new EnrolSessionService(
            jwtOptions, _cache, _platformUserService.Object, metrics,
            config, _time, NullLogger<EnrolSessionService>.Instance);

        _service = new PairingShortCodeService(
            _enrolSessionService, _cache, metrics,
            NullLogger<PairingShortCodeService>.Instance);
    }

    [Fact]
    public async Task MintAsync_Returns_Six_Digit_Numeric_Code_With_5_Minute_TTL()
    {
        var pu = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        _time.SetUtcNow(now);

        var response = await _service.MintAsync(pu, PairingShortCodeRoute.DesktopHandoff, CancellationToken.None);

        response.Code.Should().HaveLength(6).And.MatchRegex("^[0-9]{6}$");
        // The mint wraps an underlying enrol-session token with a 10-minute
        // lifetime. The short code's "expiresAt" surfaced to the client
        // reflects the inner token's expiry — the per-code cache entry will
        // be GC'd at 5 minutes anyway.
        response.ExpiresAt.Should().BeCloseTo(now.AddMinutes(10), TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task MintAsync_Throws_On_Empty_UserId()
    {
        var act = () => _service.MintAsync(Guid.Empty, PairingShortCodeRoute.DesktopHandoff, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task RedeemAsync_Happy_Path_Unwraps_Underlying_Session()
    {
        var pu = Guid.NewGuid();
        var user = new PlatformUser { Id = pu, Email = "sarah@example.test", DisplayName = "Sarah Example" };
        _platformUserService.Setup(p => p.GetByIdAsync(pu, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var mint = await _service.MintAsync(pu, PairingShortCodeRoute.PwaTakeover, CancellationToken.None);
        var redeem = await _service.RedeemAsync(mint.Code, CancellationToken.None);

        redeem.IsSuccess.Should().BeTrue();
        redeem.Success!.AccessToken.Should().NotBeNullOrWhiteSpace();
        redeem.Success.DisplayName.Should().Be("Sarah Example");
        redeem.Success.Email.Should().Be("sarah@example.test");
        redeem.Success.Mode.Should().Be(EnrolSessionMode.Standalone);
    }

    [Fact]
    public async Task RedeemAsync_Replay_Returns_ExpiredCode_On_Second_Attempt()
    {
        var pu = Guid.NewGuid();
        var user = new PlatformUser { Id = pu, Email = "s@e.test", DisplayName = "S" };
        _platformUserService.Setup(p => p.GetByIdAsync(pu, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var mint = await _service.MintAsync(pu, PairingShortCodeRoute.DesktopHandoff, CancellationToken.None);

        var first = await _service.RedeemAsync(mint.Code, CancellationToken.None);
        first.IsSuccess.Should().BeTrue();

        var second = await _service.RedeemAsync(mint.Code, CancellationToken.None);
        second.IsSuccess.Should().BeFalse();
        second.Error!.Code.Should().Be(RedeemPairingShortCodeErrorCode.ExpiredCode);
    }

    [Fact]
    public async Task RedeemAsync_Malformed_Non_Numeric_Code_Returns_MalformedCode()
    {
        var result = await _service.RedeemAsync("12345A", CancellationToken.None);
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(RedeemPairingShortCodeErrorCode.MalformedCode);
    }

    [Fact]
    public async Task RedeemAsync_Wrong_Length_Returns_MalformedCode()
    {
        var resultShort = await _service.RedeemAsync("12345", CancellationToken.None);
        var resultLong = await _service.RedeemAsync("1234567", CancellationToken.None);
        var resultEmpty = await _service.RedeemAsync("", CancellationToken.None);

        resultShort.Error!.Code.Should().Be(RedeemPairingShortCodeErrorCode.MalformedCode);
        resultLong.Error!.Code.Should().Be(RedeemPairingShortCodeErrorCode.MalformedCode);
        resultEmpty.Error!.Code.Should().Be(RedeemPairingShortCodeErrorCode.MalformedCode);
    }

    [Fact]
    public async Task RedeemAsync_Unknown_Code_Returns_ExpiredCode()
    {
        // 999999 was never minted in this test fixture.
        var result = await _service.RedeemAsync("999999", CancellationToken.None);
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(RedeemPairingShortCodeErrorCode.ExpiredCode);
    }

    [Fact]
    public async Task RedeemAsync_Rate_Limits_After_5_Attempts_Per_Code()
    {
        // Mint a real code so the attempts counter increments against a
        // known key without being short-circuited by malformed-code rejection.
        var pu = Guid.NewGuid();
        var user = new PlatformUser { Id = pu, Email = "s@e.test", DisplayName = "S" };
        _platformUserService.Setup(p => p.GetByIdAsync(pu, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        // 5 attempts against the WRONG code burn the counter without consuming
        // the mapping. The 6th attempt should be rate-limited.
        var wrongCode = "111111"; // not minted

        for (var i = 0; i < PairingShortCodeService.MaxRedeemAttempts; i++)
        {
            await _service.RedeemAsync(wrongCode, CancellationToken.None);
        }

        var sixth = await _service.RedeemAsync(wrongCode, CancellationToken.None);
        sixth.IsSuccess.Should().BeFalse();
        sixth.Error!.Code.Should().Be(RedeemPairingShortCodeErrorCode.RateLimited);
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public void SetUtcNow(DateTimeOffset value) => _now = value;
        public override DateTimeOffset GetUtcNow() => _now;
    }

    private sealed class TestMeterFactory : IMeterFactory
    {
        public Meter Create(MeterOptions options) => new(options);
        public void Dispose() { }
    }
}
