// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Sorcha.AtomicCache;
using Sorcha.Tenant.Service.Services;
using Xunit;

namespace Sorcha.Tenant.Service.Tests.Services;

/// <summary>
/// Tests for <see cref="ServerSentOtpService"/> (Feature 150 US2) using the real
/// <see cref="InMemoryAtomicDistributedCache"/> and a <see cref="FakeTimeProvider"/> so expiry and
/// attempt logic are deterministic. Covers single-use GETDEL, hashed-not-plaintext storage, the
/// 10-minute expiry, the 5-attempt cap, and the send cooldown.
/// </summary>
public sealed class ServerSentOtpServiceTests
{
    private readonly InMemoryAtomicDistributedCache _cache = new();
    private readonly FakeTimeProvider _time = new();

    private ServerSentOtpService Create() => new(_cache, _time);

    private const string Subject = "user-123";

    [Fact]
    public async Task GenerateThenVerify_CorrectCode_Verified_AndSingleUse()
    {
        var sut = Create();
        var gen = await sut.GenerateAsync(Subject, OtpPurpose.Login2Fa);
        gen.Outcome.Should().Be(OtpSendOutcome.Generated);
        gen.Code.Should().NotBeNullOrEmpty();

        (await sut.VerifyAsync(Subject, OtpPurpose.Login2Fa, gen.Code!))
            .Should().Be(OtpVerifyOutcome.Verified);

        // Single-use: the code is consumed, so a second verify finds nothing live.
        (await sut.VerifyAsync(Subject, OtpPurpose.Login2Fa, gen.Code!))
            .Should().Be(OtpVerifyOutcome.Expired);
    }

    [Fact]
    public async Task StoredValue_ContainsHashNotPlaintext()
    {
        var sut = Create();
        var gen = await sut.GenerateAsync(Subject, OtpPurpose.Login2Fa);

        var raw = await _cache.GetAsync($"otp:{OtpPurpose.Login2Fa}:{Subject}", CancellationToken.None);
        var expectedHash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(gen.Code!)));

        raw.Should().NotBeNull();
        raw!.Should().NotContain(gen.Code!);   // plaintext never stored
        raw.Should().Contain(expectedHash);    // the SHA-256 hex is
    }

    [Fact]
    public async Task Verify_AfterExpiry_ReturnsExpired()
    {
        var sut = Create();
        var gen = await sut.GenerateAsync(Subject, OtpPurpose.Login2Fa);

        _time.Advance(ServerSentOtpService.CodeLifetime + TimeSpan.FromSeconds(1));

        (await sut.VerifyAsync(Subject, OtpPurpose.Login2Fa, gen.Code!))
            .Should().Be(OtpVerifyOutcome.Expired);
    }

    [Fact]
    public async Task Verify_WrongCode_FiveTimes_VoidsCode()
    {
        var sut = Create();
        var gen = await sut.GenerateAsync(Subject, OtpPurpose.Login2Fa);

        // Attempts 1–4 are Invalid; the 5th hits the cap and voids the code.
        for (var i = 0; i < ServerSentOtpService.MaxAttempts - 1; i++)
        {
            (await sut.VerifyAsync(Subject, OtpPurpose.Login2Fa, "000000"))
                .Should().Be(OtpVerifyOutcome.Invalid, "attempt {0} should burn, not void", i + 1);
        }

        (await sut.VerifyAsync(Subject, OtpPurpose.Login2Fa, "000000"))
            .Should().Be(OtpVerifyOutcome.TooManyAttempts);

        // Even the correct code no longer works — the code was voided.
        (await sut.VerifyAsync(Subject, OtpPurpose.Login2Fa, gen.Code!))
            .Should().Be(OtpVerifyOutcome.Expired);
    }

    [Fact]
    public async Task Generate_TwiceWithinCooldown_SecondIsRateLimited()
    {
        var sut = Create();
        (await sut.GenerateAsync(Subject, OtpPurpose.Login2Fa)).Outcome.Should().Be(OtpSendOutcome.Generated);
        (await sut.GenerateAsync(Subject, OtpPurpose.Login2Fa)).Outcome.Should().Be(OtpSendOutcome.RateLimited);
    }

    [Fact]
    public async Task Purpose_ScopesTheCode_NoCrossReplay()
    {
        var sut = Create();
        var login = await sut.GenerateAsync(Subject, OtpPurpose.Login2Fa);

        // The login code must not satisfy a step-up challenge (different key, no code stored there).
        (await sut.VerifyAsync(Subject, OtpPurpose.StepUp, login.Code!))
            .Should().Be(OtpVerifyOutcome.Expired);
    }
}
