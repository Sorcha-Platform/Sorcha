// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Tests.Services;

/// <summary>
/// Unit tests for <see cref="LinkPendingTokenService"/> — mint/verify round-trip,
/// tamper detection, expiry, and absent-input handling. Feature 168, T011.
/// </summary>
public class LinkPendingTokenServiceTests
{
    private static readonly byte[] TestKey = new byte[32];

    static LinkPendingTokenServiceTests()
    {
        // Deterministic 32-byte test key (not random — tests must be repeatable).
        for (int i = 0; i < 32; i++) TestKey[i] = (byte)(i + 1);
    }

    private LinkPendingTokenService CreateService() =>
        new(new LinkPendingTokenKey(TestKey));

    private static LinkPendingToken ValidToken(DateTimeOffset? expiresAt = null) =>
        new(
            Provider: "google",
            Subject: "sub-123",
            SocialEmail: "alice@example.com",
            DisplayName: "Alice",
            TargetAccountId: new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            ExpiresAt: expiresAt ?? DateTimeOffset.UtcNow.AddMinutes(5));

    [Fact]
    public void Mint_ThenTryVerify_RoundTrips()
    {
        var svc = CreateService();
        var original = ValidToken();

        var raw = svc.Mint(original);
        var ok = svc.TryVerify(raw, out var decoded, out var err);

        ok.Should().BeTrue();
        err.Should().Be(LinkPendingTokenError.None);
        decoded.Provider.Should().Be(original.Provider);
        decoded.Subject.Should().Be(original.Subject);
        decoded.SocialEmail.Should().Be(original.SocialEmail);
        decoded.DisplayName.Should().Be(original.DisplayName);
        decoded.TargetAccountId.Should().Be(original.TargetAccountId);
    }

    [Fact]
    public void TryVerify_TamperedPayload_ReturnsInvalid()
    {
        var svc = CreateService();
        var raw = svc.Mint(ValidToken());

        // Flip one character in the payload (first segment).
        var parts = raw.Split('|');
        parts[0] = parts[0][..^1] + (parts[0][^1] == 'A' ? 'B' : 'A');
        var tampered = string.Join('|', parts);

        var ok = svc.TryVerify(tampered, out _, out var err);
        ok.Should().BeFalse();
        err.Should().Be(LinkPendingTokenError.Invalid);
    }

    [Fact]
    public void TryVerify_TamperedExpiry_ReturnsInvalid()
    {
        var svc = CreateService();
        var raw = svc.Mint(ValidToken());

        // Increment the expiry by 1 second (second segment).
        var parts = raw.Split('|');
        parts[1] = (long.Parse(parts[1]) + 1).ToString();
        var tampered = string.Join('|', parts);

        var ok = svc.TryVerify(tampered, out _, out var err);
        ok.Should().BeFalse();
        err.Should().Be(LinkPendingTokenError.Invalid);
    }

    [Fact]
    public void TryVerify_ExpiredToken_ReturnsExpired()
    {
        var svc = CreateService();
        var raw = svc.Mint(ValidToken(DateTimeOffset.UtcNow.AddMinutes(-1)));

        var ok = svc.TryVerify(raw, out _, out var err);
        ok.Should().BeFalse();
        err.Should().Be(LinkPendingTokenError.Expired);
    }

    [Fact]
    public void TryVerify_AbsentInput_ReturnsInvalid()
    {
        var svc = CreateService();

        var ok = svc.TryVerify(string.Empty, out _, out var err);
        ok.Should().BeFalse();
        err.Should().Be(LinkPendingTokenError.Invalid);
    }

    [Fact]
    public void TryVerify_NullDisplayName_RoundTrips()
    {
        var svc = CreateService();
        var token = new LinkPendingToken(
            Provider: "github",
            Subject: "sub-456",
            SocialEmail: "bob@example.com",
            DisplayName: null,
            TargetAccountId: Guid.NewGuid(),
            ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(5));

        var raw = svc.Mint(token);
        var ok = svc.TryVerify(raw, out var decoded, out _);

        ok.Should().BeTrue();
        decoded.DisplayName.Should().BeNull();
    }

    [Fact]
    public void TryVerify_WrongNumberOfParts_ReturnsInvalid()
    {
        var svc = CreateService();

        var ok = svc.TryVerify("only-one-part", out _, out var err);
        ok.Should().BeFalse();
        err.Should().Be(LinkPendingTokenError.Invalid);
    }
}
