// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Tenant.Models.Auth;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Services;
using Xunit;

namespace Sorcha.Tenant.Service.Tests.Services;

/// <summary>
/// Tests for <see cref="VerificationChannelRegistry"/> (Feature 150). The registry is built from the
/// channels registered in DI, so the EmailOtp channel resolves at Tier=Basic, and SMS is reported
/// available only when an SMS channel is present (US3's config gate).
/// </summary>
public sealed class VerificationChannelRegistryTests
{
    private sealed class FakeChannel(ChallengeMethod kind, AuthAssuranceTier tier) : IVerificationChannel
    {
        public ChallengeMethod Kind => kind;
        public AuthAssuranceTier Tier => tier;
        public Task<ChannelSendOutcome> SendAsync(Guid platformUserId, OtpPurpose purpose, CancellationToken ct = default)
            => Task.FromResult(ChannelSendOutcome.Sent);
        public Task<OtpVerifyOutcome> VerifyAsync(Guid platformUserId, OtpPurpose purpose, string code, CancellationToken ct = default)
            => Task.FromResult(OtpVerifyOutcome.Verified);
    }

    [Fact]
    public void Resolve_EmailOtp_ReturnsBasicTierChannel()
    {
        var registry = new VerificationChannelRegistry(
            [new FakeChannel(ChallengeMethod.EmailOtp, AuthAssuranceTier.Basic)]);

        var channel = registry.Resolve(ChallengeMethod.EmailOtp);

        channel.Should().NotBeNull();
        channel!.Tier.Should().Be(AuthAssuranceTier.Basic);
    }

    [Fact]
    public void SmsAvailable_FalseWhenOnlyEmailRegistered_TrueWhenSmsRegistered()
    {
        var emailOnly = new VerificationChannelRegistry(
            [new FakeChannel(ChallengeMethod.EmailOtp, AuthAssuranceTier.Basic)]);
        emailOnly.SmsAvailable.Should().BeFalse();
        emailOnly.Resolve(ChallengeMethod.SmsOtp).Should().BeNull();

        var withSms = new VerificationChannelRegistry(
        [
            new FakeChannel(ChallengeMethod.EmailOtp, AuthAssuranceTier.Basic),
            new FakeChannel(ChallengeMethod.SmsOtp, AuthAssuranceTier.Basic),
        ]);
        withSms.SmsAvailable.Should().BeTrue();
        withSms.Resolve(ChallengeMethod.SmsOtp).Should().NotBeNull();
    }
}
