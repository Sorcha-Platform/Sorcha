// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Tests.Services;

/// <summary>
/// Tests for <see cref="AuthMethodService"/> — single source of truth for the
/// last-method floor. Walks the seven shapes of <c>{password?, socials, passkeys}</c>
/// and confirms TOTP enrolment is intentionally NOT counted as a method.
/// SQLite in-memory mirrors the production EF setup.
/// </summary>
public class AuthMethodServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TenantDbContext _db;

    public AuthMethodServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<TenantDbContext>().UseSqlite(_connection).Options;
        _db = new TenantDbContext(options);
        _db.Database.EnsureCreated();
    }

    private async Task<PlatformUser> SeedAsync(
        bool withPassword = false,
        int socialCount = 0,
        int activePasskeyCount = 0,
        int disabledPasskeyCount = 0,
        bool withTotp = false)
    {
        var pu = new PlatformUser
        {
            Id = Guid.NewGuid(),
            Email = $"u-{Guid.NewGuid():N}@test.com",
            DisplayName = "User",
            PasswordHash = withPassword ? "hash" : null,
        };
        _db.PlatformUsers.Add(pu);

        for (var i = 0; i < socialCount; i++)
        {
            _db.PlatformSocialLogins.Add(new PlatformSocialLogin
            {
                Id = Guid.NewGuid(),
                PlatformUserId = pu.Id,
                Provider = $"google-{i}",
                Subject = $"sub-{Guid.NewGuid():N}",
            });
        }

        for (var i = 0; i < activePasskeyCount; i++)
        {
            _db.PasskeyCredentials.Add(new PasskeyCredential
            {
                Id = Guid.NewGuid(),
                PlatformUserId = pu.Id,
                CredentialId = new[] { (byte)(i + 1) },
                PublicKeyCose = new byte[] { 1 },
                DisplayName = $"PK-{i}",
                AttestationType = "none",
                Status = CredentialStatus.Active,
            });
        }

        for (var i = 0; i < disabledPasskeyCount; i++)
        {
            _db.PasskeyCredentials.Add(new PasskeyCredential
            {
                Id = Guid.NewGuid(),
                PlatformUserId = pu.Id,
                CredentialId = new[] { (byte)(100 + i) },
                PublicKeyCose = new byte[] { 1 },
                DisplayName = $"PK-Disabled-{i}",
                AttestationType = "none",
                Status = CredentialStatus.Disabled,
            });
        }

        if (withTotp)
        {
            var ui = new UserIdentity
            {
                Id = Guid.NewGuid(), OrganizationId = Guid.NewGuid(),
                PlatformUserId = pu.Id, Email = pu.Email, DisplayName = pu.DisplayName,
                Status = IdentityStatus.Active,
            };
            _db.UserIdentities.Add(ui);
            _db.TotpConfigurations.Add(new TotpConfiguration
            {
                Id = Guid.NewGuid(),
                UserId = ui.Id,
                EncryptedSecret = "stub",
                BackupCodes = "stub",
                IsEnabled = true,
            });
        }

        await _db.SaveChangesAsync();
        return pu;
    }

    [Theory]
    [InlineData(true, 0, 0, 1)]   // password only -> 1
    [InlineData(false, 1, 0, 1)]  // 1 social only -> 1
    [InlineData(false, 0, 1, 1)]  // 1 active passkey only -> 1
    [InlineData(true, 2, 3, 6)]   // password + 2 socials + 3 passkeys -> 6
    [InlineData(true, 0, 2, 3)]   // password + 2 active passkeys -> 3
    [InlineData(false, 3, 0, 3)]  // 3 socials -> 3
    [InlineData(false, 0, 0, 0)]  // empty -> 0
    public async Task GetCountsAsync_ReturnsExpectedTotal_AcrossAllShapes(
        bool withPassword, int socials, int activePasskeys, int expectedTotal)
    {
        var pu = await SeedAsync(withPassword: withPassword,
                                  socialCount: socials, activePasskeyCount: activePasskeys);
        var service = new AuthMethodService(_db);

        var counts = await service.GetCountsAsync(pu.Id);

        counts.Total.Should().Be(expectedTotal);
        counts.HasPassword.Should().Be(withPassword);
        counts.SocialCount.Should().Be(socials);
        counts.ActivePasskeyCount.Should().Be(activePasskeys);
    }

    [Fact]
    public async Task GetCountsAsync_DisabledPasskeysNotCounted()
    {
        var pu = await SeedAsync(withPassword: false, activePasskeyCount: 1, disabledPasskeyCount: 5);
        var service = new AuthMethodService(_db);

        var counts = await service.GetCountsAsync(pu.Id);

        counts.ActivePasskeyCount.Should().Be(1);
        counts.Total.Should().Be(1);
    }

    [Fact]
    public async Task GetCountsAsync_TotpEnrolmentDoesNotIncreaseMethodCount()
    {
        // TOTP is a second factor, not a method. A user with ONLY TOTP is
        // still locked out — they have no method to unlock with.
        var pu = await SeedAsync(withPassword: true, withTotp: true);
        var service = new AuthMethodService(_db);

        var counts = await service.GetCountsAsync(pu.Id);

        counts.Total.Should().Be(1, "TOTP must not count toward the floor");
    }

    [Fact]
    public async Task WouldRemovingLeaveZero_PasswordOnlyUser_RemovingPasswordTriggersFloor()
    {
        var pu = await SeedAsync(withPassword: true);
        var service = new AuthMethodService(_db);

        var blocked = await service.WouldRemovingLeaveZeroAsync(pu.Id, AuthMethodKind.Password, methodId: null);

        blocked.Should().BeTrue();
    }

    [Fact]
    public async Task WouldRemovingLeaveZero_PasswordPlusOnePasskey_RemovingPasskeyAllowed()
    {
        var pu = await SeedAsync(withPassword: true, activePasskeyCount: 1);
        var service = new AuthMethodService(_db);

        var passkey = await _db.PasskeyCredentials.FirstAsync(p => p.PlatformUserId == pu.Id);
        var blocked = await service.WouldRemovingLeaveZeroAsync(pu.Id, AuthMethodKind.Passkey, passkey.Id);

        blocked.Should().BeFalse("removing the passkey leaves the password");
    }

    [Fact]
    public async Task WouldRemovingLeaveZero_RemovingAlreadyDisabledPasskey_DoesNotConsumeFromCount()
    {
        // Floor logic only subtracts from the count if the targeted row is
        // currently active. Disabled passkeys are already off the active set;
        // their removal cannot trigger the floor.
        var pu = await SeedAsync(withPassword: false, activePasskeyCount: 1, disabledPasskeyCount: 1);
        var service = new AuthMethodService(_db);

        var disabled = await _db.PasskeyCredentials
            .FirstAsync(p => p.PlatformUserId == pu.Id && p.Status == CredentialStatus.Disabled);
        var blocked = await service.WouldRemovingLeaveZeroAsync(pu.Id, AuthMethodKind.Passkey, disabled.Id);

        blocked.Should().BeFalse("disabled passkey removal does not draw down the active set");
    }

    [Fact]
    public async Task WouldRemovingLeaveZero_RemovingNonExistentSocial_DoesNotConsumeFromCount()
    {
        // Optimistic UI race protection: if the row was already removed by a
        // concurrent request, we must not under-count and falsely trigger the
        // floor on the remaining methods.
        var pu = await SeedAsync(withPassword: true, socialCount: 1);
        var service = new AuthMethodService(_db);

        var blocked = await service.WouldRemovingLeaveZeroAsync(pu.Id, AuthMethodKind.Social, methodId: Guid.NewGuid());

        blocked.Should().BeFalse("removing a non-existent social does not lower the active count");
    }

    [Fact]
    public async Task WouldRemovingLeaveZero_UnknownUser_TreatedAsFloorAlreadyAtZero()
    {
        var service = new AuthMethodService(_db);
        var blocked = await service.WouldRemovingLeaveZeroAsync(
            Guid.NewGuid(), AuthMethodKind.Password, methodId: null);
        blocked.Should().BeTrue();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
