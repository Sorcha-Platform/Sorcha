// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using FluentAssertions;
using Sorcha.Wallet.Core.Domain;
using Sorcha.Wallet.Core.Domain.Entities;
using Xunit;

namespace Sorcha.Wallet.Core.Tests.Domain.Entities;

public class WalletAccessTests
{
    private static WalletAccess CreateAccess(
        DateTime? expiresAt = null,
        DateTime? revokedAt = null)
    {
        return new WalletAccess
        {
            ParentWalletAddress = "test-address",
            Subject = "test-subject",
            GrantedBy = "test-granter",
            AccessRight = AccessRight.ReadWrite,
            ExpiresAt = expiresAt,
            RevokedAt = revokedAt
        };
    }

    [Fact]
    public void IsActive_NotRevokedNoExpiry_ReturnsTrue()
    {
        var access = CreateAccess();

        access.IsActive.Should().BeTrue();
    }

    [Fact]
    public void IsActive_Revoked_ReturnsFalse()
    {
        var access = CreateAccess(revokedAt: DateTime.UtcNow.AddMinutes(-1));

        access.IsActive.Should().BeFalse();
    }

    [Fact]
    public void IsActive_Expired_ReturnsFalse()
    {
        var access = CreateAccess(expiresAt: DateTime.UtcNow.AddMinutes(-1));

        access.IsActive.Should().BeFalse();
    }

    [Fact]
    public void IsActive_ExpiresInFuture_ReturnsTrue()
    {
        var access = CreateAccess(expiresAt: DateTime.UtcNow.AddHours(1));

        access.IsActive.Should().BeTrue();
    }

    [Fact]
    public void IsActive_NullExpiresAt_ReturnsTrue()
    {
        var access = CreateAccess(expiresAt: null);

        access.IsActive.Should().BeTrue();
    }

    [Fact]
    public void IsActive_RevokedAndExpired_ReturnsFalse()
    {
        var access = CreateAccess(
            expiresAt: DateTime.UtcNow.AddMinutes(-1),
            revokedAt: DateTime.UtcNow.AddMinutes(-5));

        access.IsActive.Should().BeFalse();
    }

    [Fact]
    public void IsActive_RevokedButNotExpired_ReturnsFalse()
    {
        var access = CreateAccess(
            expiresAt: DateTime.UtcNow.AddHours(1),
            revokedAt: DateTime.UtcNow.AddMinutes(-1));

        access.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Constructor_DefaultValues_SetsExpectedDefaults()
    {
        var access = CreateAccess();

        access.Id.Should().NotBe(Guid.Empty);
        access.GrantedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        access.RevokedAt.Should().BeNull();
        access.RevokedBy.Should().BeNull();
        access.Reason.Should().BeNull();
    }
}
