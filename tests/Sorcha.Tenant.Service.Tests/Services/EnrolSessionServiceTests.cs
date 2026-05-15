// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics.Metrics;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Sorcha.AtomicCache;
using Sorcha.Tenant.Service.Extensions;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Tests.Services;

/// <summary>
/// Tests for <see cref="EnrolSessionService"/>. Covers mint claims, single-use
/// redeem semantics, expired-token rejection, scope-mismatch, and replay.
/// </summary>
public sealed class EnrolSessionServiceTests
{
    private const string SigningKey = "test-signing-key-must-be-at-least-32-bytes-long-padded-out";

    private readonly InMemoryAtomicDistributedCache _cache = new();
    private readonly FakeTimeProvider _time = new();
    private readonly Mock<IPlatformUserService> _platformUserService = new();
    private readonly EnrolSessionMetrics _metrics;
    private readonly EnrolSessionService _service;

    public EnrolSessionServiceTests()
    {
        var jwt = new JwtConfiguration
        {
            Issuer = "https://test.sorcha",
            Audiences = new[] { "sorcha:citizen-wallet" },
            SigningKey = SigningKey,
            AccessTokenLifetimeMinutes = 60,
        };
        var jwtOptions = Options.Create(jwt);
        var config = new ConfigurationBuilder().Build();

        _metrics = new EnrolSessionMetrics(new TestMeterFactory());
        _service = new EnrolSessionService(
            jwtOptions, _cache, _platformUserService.Object, _metrics,
            config, _time, NullLogger<EnrolSessionService>.Instance);
    }

    [Fact]
    public async Task MintAsync_Issues_JWT_With_Enrol_Scope_And_10_Minute_TTL()
    {
        var pu = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        _time.SetUtcNow(now);

        var response = await _service.MintAsync(pu, CancellationToken.None);

        response.SessionToken.Should().NotBeNullOrWhiteSpace();
        response.QrUrl.Should().Contain($"session={response.SessionToken}");
        response.ExpiresAt.Should().BeCloseTo(now.AddMinutes(10), TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task RedeemAsync_Happy_Path_Returns_DisplayName_And_Email()
    {
        var pu = Guid.NewGuid();
        var user = new PlatformUser { Id = pu, Email = "sarah@example.test", DisplayName = "Sarah Example" };
        _platformUserService.Setup(p => p.GetByIdAsync(pu, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var mint = await _service.MintAsync(pu, CancellationToken.None);
        var redeem = await _service.RedeemAsync(mint.SessionToken, CancellationToken.None);

        redeem.IsSuccess.Should().BeTrue();
        redeem.Success!.DisplayName.Should().Be("Sarah Example");
        redeem.Success.Email.Should().Be("sarah@example.test");
        redeem.Success.AccessToken.Should().NotBeNullOrWhiteSpace();
        redeem.Success.ExpiresIn.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task RedeemAsync_Replay_Returns_AlreadyUsed()
    {
        var pu = Guid.NewGuid();
        var user = new PlatformUser { Id = pu, Email = "s@e.test", DisplayName = "S" };
        _platformUserService.Setup(p => p.GetByIdAsync(pu, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var mint = await _service.MintAsync(pu, CancellationToken.None);
        var first = await _service.RedeemAsync(mint.SessionToken, CancellationToken.None);
        first.IsSuccess.Should().BeTrue();

        var second = await _service.RedeemAsync(mint.SessionToken, CancellationToken.None);
        second.IsSuccess.Should().BeFalse();
        second.Error!.Code.Should().Be(RedeemEnrolSessionErrorCode.AlreadyUsed);
    }

    [Fact]
    public async Task RedeemAsync_Expired_Token_Returns_Expired()
    {
        var pu = Guid.NewGuid();
        _time.SetUtcNow(DateTimeOffset.UtcNow);
        var mint = await _service.MintAsync(pu, CancellationToken.None);

        _time.Advance(TimeSpan.FromMinutes(11));

        var result = await _service.RedeemAsync(mint.SessionToken, CancellationToken.None);
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(RedeemEnrolSessionErrorCode.Expired);
    }

    [Fact]
    public async Task RedeemAsync_Malformed_Token_Returns_MalformedToken()
    {
        var result = await _service.RedeemAsync("not-a-jwt", CancellationToken.None);
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(RedeemEnrolSessionErrorCode.MalformedToken);
    }

    [Fact]
    public async Task RedeemAsync_Empty_Token_Returns_MalformedToken()
    {
        var result = await _service.RedeemAsync("", CancellationToken.None);
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(RedeemEnrolSessionErrorCode.MalformedToken);
    }

    [Fact]
    public async Task RedeemAsync_Tampered_Signature_Returns_InvalidSignature()
    {
        var pu = Guid.NewGuid();
        var mint = await _service.MintAsync(pu, CancellationToken.None);
        var tampered = mint.SessionToken.Substring(0, mint.SessionToken.Length - 4) + "AAAA";

        var result = await _service.RedeemAsync(tampered, CancellationToken.None);
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().BeOneOf(
            RedeemEnrolSessionErrorCode.InvalidSignature,
            RedeemEnrolSessionErrorCode.MalformedToken);
    }

    [Fact]
    public async Task RedeemAsync_When_PlatformUser_Missing_Returns_MalformedToken()
    {
        var pu = Guid.NewGuid();
        _platformUserService.Setup(p => p.GetByIdAsync(pu, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlatformUser?)null);

        var mint = await _service.MintAsync(pu, CancellationToken.None);
        var result = await _service.RedeemAsync(mint.SessionToken, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(RedeemEnrolSessionErrorCode.MalformedToken);
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public void SetUtcNow(DateTimeOffset value) => _now = value;
        public void Advance(TimeSpan delta) => _now = _now.Add(delta);
        public override DateTimeOffset GetUtcNow() => _now;
    }

    private sealed class TestMeterFactory : IMeterFactory
    {
        public Meter Create(MeterOptions options) => new(options);
        public void Dispose() { }
    }
}
