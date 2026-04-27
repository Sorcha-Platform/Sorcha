// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Fido2NetLib;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Tests.Services;

/// <summary>
/// Tests for the Feature 116 US2 passkey lifecycle changes in
/// <see cref="PasskeyService"/>: soft-revocation reason transitions, rename
/// blocking on Disabled/Revoked, and last-method floor enforcement during
/// Active revocation.
/// </summary>
/// <remarks>
/// Uses SQLite in-memory rather than EF InMemory because
/// <see cref="AuthMethodService.WouldRemovingLeaveZeroAsync"/> joins three
/// tables in a single projection — InMemory does not produce realistic LINQ
/// translation for that shape, while SQLite does.
/// </remarks>
public class PasskeyRevocationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TenantDbContext _db;
    private readonly AuthMethodService _floor;
    private readonly PasskeyService _service;

    public PasskeyRevocationTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<TenantDbContext>().UseSqlite(_connection).Options;
        _db = new TenantDbContext(options);
        _db.Database.EnsureCreated();
        _floor = new AuthMethodService(_db);

        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var fido2 = new Mock<IFido2>(MockBehavior.Strict);
        _service = new PasskeyService(fido2.Object, cache, _db, _floor, NullLogger<PasskeyService>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private async Task<PlatformUser> SeedUserAsync(
        bool withPassword = true,
        int activePasskeys = 0,
        int disabledPasskeys = 0,
        int socialLinks = 0)
    {
        var pu = new PlatformUser
        {
            Id = Guid.NewGuid(),
            Email = $"u-{Guid.NewGuid():N}@test.com",
            DisplayName = "Test User",
            PasswordHash = withPassword ? "hash" : null,
        };
        _db.PlatformUsers.Add(pu);

        for (var i = 0; i < activePasskeys; i++)
        {
            _db.PasskeyCredentials.Add(NewPasskey(pu.Id, CredentialStatus.Active, $"Active-{i}"));
        }
        for (var i = 0; i < disabledPasskeys; i++)
        {
            _db.PasskeyCredentials.Add(NewPasskey(pu.Id, CredentialStatus.Disabled, $"Disabled-{i}",
                disabledReason: "signature-counter-regression"));
        }
        for (var i = 0; i < socialLinks; i++)
        {
            _db.PlatformSocialLogins.Add(new PlatformSocialLogin
            {
                Id = Guid.NewGuid(),
                PlatformUserId = pu.Id,
                Provider = "google",
                Subject = $"sub-{Guid.NewGuid():N}",
                Email = $"u-{i}@gmail.com",
                LinkedAt = DateTimeOffset.UtcNow,
            });
        }

        await _db.SaveChangesAsync();
        return pu;
    }

    private static PasskeyCredential NewPasskey(
        Guid platformUserId,
        CredentialStatus status,
        string displayName,
        string? disabledReason = null) => new()
    {
        Id = Guid.NewGuid(),
        PlatformUserId = platformUserId,
        CredentialId = Guid.NewGuid().ToByteArray(),
        PublicKeyCose = new byte[] { 1, 2, 3 },
        DisplayName = displayName,
        AttestationType = "none",
        Status = status,
        DisabledAt = status != CredentialStatus.Active ? DateTimeOffset.UtcNow : null,
        DisabledReason = disabledReason,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    // -------- Revocation: reason string transitions ----------

    [Fact]
    public async Task RevokeCredentialAsync_ActiveWithOtherMethod_FlipsToRevokedWithUserRemovedReason()
    {
        var user = await SeedUserAsync(withPassword: true, activePasskeys: 1);
        var passkey = _db.PasskeyCredentials.Single();

        var outcome = await _service.RevokeCredentialAsync(passkey.Id, user.Id);

        outcome.Should().Be(PasskeyRevocationOutcome.RevokedFromActive);
        var stored = await _db.PasskeyCredentials.AsNoTracking().FirstAsync(c => c.Id == passkey.Id);
        stored.Status.Should().Be(CredentialStatus.Revoked);
        stored.DisabledReason.Should().Be("user-removed");
        stored.DisabledAt.Should().NotBeNull();
    }

    [Fact]
    public async Task RevokeCredentialAsync_Disabled_FlipsToRevokedWithUserRemovedAfterDisableReason()
    {
        // Disabled passkeys don't count toward the active method total; the
        // user has no other methods, but the floor still permits removal.
        var user = await SeedUserAsync(withPassword: false, activePasskeys: 0, disabledPasskeys: 1);
        var disabled = _db.PasskeyCredentials.Single();

        var outcome = await _service.RevokeCredentialAsync(disabled.Id, user.Id);

        outcome.Should().Be(PasskeyRevocationOutcome.RevokedFromDisabled);
        var stored = await _db.PasskeyCredentials.AsNoTracking().FirstAsync(c => c.Id == disabled.Id);
        stored.Status.Should().Be(CredentialStatus.Revoked);
        stored.DisabledReason.Should().Be("user-removed-after-disable");
    }

    [Fact]
    public async Task RevokeCredentialAsync_AlreadyRevoked_ReturnsAlreadyRevoked_DoesNotMutate()
    {
        var user = await SeedUserAsync(withPassword: true);
        var revoked = NewPasskey(user.Id, CredentialStatus.Revoked, "Old", disabledReason: "user-removed");
        revoked.DisabledAt = DateTimeOffset.UtcNow.AddDays(-1);
        _db.PasskeyCredentials.Add(revoked);
        await _db.SaveChangesAsync();
        var originalDisabledAt = revoked.DisabledAt;
        var originalReason = revoked.DisabledReason;

        var outcome = await _service.RevokeCredentialAsync(revoked.Id, user.Id);

        outcome.Should().Be(PasskeyRevocationOutcome.AlreadyRevoked);
        var stored = await _db.PasskeyCredentials.AsNoTracking().FirstAsync(c => c.Id == revoked.Id);
        stored.DisabledAt.Should().Be(originalDisabledAt);
        stored.DisabledReason.Should().Be(originalReason);
    }

    [Fact]
    public async Task RevokeCredentialAsync_NotFound_ReturnsNotFound()
    {
        var user = await SeedUserAsync();

        var outcome = await _service.RevokeCredentialAsync(Guid.NewGuid(), user.Id);

        outcome.Should().Be(PasskeyRevocationOutcome.NotFound);
    }

    [Fact]
    public async Task RevokeCredentialAsync_WrongOwner_ReturnsNotFound()
    {
        var ownerA = await SeedUserAsync(withPassword: true, activePasskeys: 1);
        var ownerB = await SeedUserAsync(withPassword: true);
        var aPasskey = _db.PasskeyCredentials.Single(p => p.PlatformUserId == ownerA.Id);

        var outcome = await _service.RevokeCredentialAsync(aPasskey.Id, ownerB.Id);

        outcome.Should().Be(PasskeyRevocationOutcome.NotFound);
        // Untouched
        var stored = await _db.PasskeyCredentials.AsNoTracking().FirstAsync(c => c.Id == aPasskey.Id);
        stored.Status.Should().Be(CredentialStatus.Active);
    }

    // -------- Floor enforcement on Active revocation ----------

    [Fact]
    public async Task RevokeCredentialAsync_LastActivePasskey_NoOtherMethods_BlockedByFloor()
    {
        var user = await SeedUserAsync(withPassword: false, activePasskeys: 1);
        var only = _db.PasskeyCredentials.Single();

        var outcome = await _service.RevokeCredentialAsync(only.Id, user.Id);

        outcome.Should().Be(PasskeyRevocationOutcome.BlockedByFloor);
        var stored = await _db.PasskeyCredentials.AsNoTracking().FirstAsync(c => c.Id == only.Id);
        stored.Status.Should().Be(CredentialStatus.Active);
    }

    [Fact]
    public async Task RevokeCredentialAsync_LastActivePasskey_WithPassword_AllowsRevoke()
    {
        var user = await SeedUserAsync(withPassword: true, activePasskeys: 1);
        var only = _db.PasskeyCredentials.Single();

        var outcome = await _service.RevokeCredentialAsync(only.Id, user.Id);

        outcome.Should().Be(PasskeyRevocationOutcome.RevokedFromActive);
    }

    [Fact]
    public async Task RevokeCredentialAsync_LastActivePasskey_WithSocial_AllowsRevoke()
    {
        var user = await SeedUserAsync(withPassword: false, activePasskeys: 1, socialLinks: 1);
        var only = _db.PasskeyCredentials.Single();

        var outcome = await _service.RevokeCredentialAsync(only.Id, user.Id);

        outcome.Should().Be(PasskeyRevocationOutcome.RevokedFromActive);
    }

    [Fact]
    public async Task RevokeCredentialAsync_TwoActivePasskeys_FloorPermitsRemovingOne()
    {
        var user = await SeedUserAsync(withPassword: false, activePasskeys: 2);
        var first = _db.PasskeyCredentials.First();

        var outcome = await _service.RevokeCredentialAsync(first.Id, user.Id);

        outcome.Should().Be(PasskeyRevocationOutcome.RevokedFromActive);
        var remaining = _db.PasskeyCredentials
            .AsNoTracking()
            .Count(c => c.Status == CredentialStatus.Active && c.PlatformUserId == user.Id);
        remaining.Should().Be(1);
    }

    [Fact]
    public async Task RevokeCredentialAsync_DisabledPasskeyDoesNotCountAsActive_FloorBlocksLastActive()
    {
        // 1 active + 1 disabled, no other methods. Removing the active one
        // would leave only a disabled (non-functional) row → blocked.
        var user = await SeedUserAsync(withPassword: false, activePasskeys: 1, disabledPasskeys: 1);
        var active = _db.PasskeyCredentials.Single(p => p.Status == CredentialStatus.Active);

        var outcome = await _service.RevokeCredentialAsync(active.Id, user.Id);

        outcome.Should().Be(PasskeyRevocationOutcome.BlockedByFloor);
    }

    [Fact]
    public async Task RevokeCredentialAsync_RemovingDisabled_NeverHitsFloor()
    {
        // Critical: even a user with zero remaining methods after a Disabled
        // removal is allowed — Disabled rows aren't counted, so the floor
        // never fires. The hold against self-lockout is on Active removal.
        var user = await SeedUserAsync(withPassword: false, activePasskeys: 0, disabledPasskeys: 1);
        var disabled = _db.PasskeyCredentials.Single();

        var outcome = await _service.RevokeCredentialAsync(disabled.Id, user.Id);

        outcome.Should().Be(PasskeyRevocationOutcome.RevokedFromDisabled);
    }

    // -------- Rename ----------

    [Fact]
    public async Task RenameCredentialAsync_ActiveCredential_UpdatesDisplayName()
    {
        var user = await SeedUserAsync(activePasskeys: 1);
        var passkey = _db.PasskeyCredentials.Single();

        var outcome = await _service.RenameCredentialAsync(passkey.Id, user.Id, "  My YubiKey  ");

        outcome.Should().Be(PasskeyRenameOutcome.Renamed);
        var stored = await _db.PasskeyCredentials.AsNoTracking().FirstAsync(c => c.Id == passkey.Id);
        stored.DisplayName.Should().Be("My YubiKey");
    }

    [Fact]
    public async Task RenameCredentialAsync_DisabledCredential_BlockedByDisabled()
    {
        var user = await SeedUserAsync(disabledPasskeys: 1);
        var passkey = _db.PasskeyCredentials.Single();
        var originalName = passkey.DisplayName;

        var outcome = await _service.RenameCredentialAsync(passkey.Id, user.Id, "Renamed");

        outcome.Should().Be(PasskeyRenameOutcome.BlockedByDisabled);
        var stored = await _db.PasskeyCredentials.AsNoTracking().FirstAsync(c => c.Id == passkey.Id);
        stored.DisplayName.Should().Be(originalName);
    }

    [Fact]
    public async Task RenameCredentialAsync_RevokedCredential_BlockedByRevoked()
    {
        var user = await SeedUserAsync(withPassword: true);
        var revoked = NewPasskey(user.Id, CredentialStatus.Revoked, "Old", disabledReason: "user-removed");
        _db.PasskeyCredentials.Add(revoked);
        await _db.SaveChangesAsync();

        var outcome = await _service.RenameCredentialAsync(revoked.Id, user.Id, "Renamed");

        outcome.Should().Be(PasskeyRenameOutcome.BlockedByRevoked);
    }

    [Fact]
    public async Task RenameCredentialAsync_NotFound_ReturnsNotFound()
    {
        var user = await SeedUserAsync();

        var outcome = await _service.RenameCredentialAsync(Guid.NewGuid(), user.Id, "Anything");

        outcome.Should().Be(PasskeyRenameOutcome.NotFound);
    }

    [Fact]
    public async Task RenameCredentialAsync_WrongOwner_ReturnsNotFound()
    {
        var ownerA = await SeedUserAsync(activePasskeys: 1);
        var ownerB = await SeedUserAsync();
        var aPasskey = _db.PasskeyCredentials.Single(p => p.PlatformUserId == ownerA.Id);

        var outcome = await _service.RenameCredentialAsync(aPasskey.Id, ownerB.Id, "Stolen");

        outcome.Should().Be(PasskeyRenameOutcome.NotFound);
    }

    [Fact]
    public async Task RenameCredentialAsync_EmptyName_ThrowsArgumentException()
    {
        var user = await SeedUserAsync(activePasskeys: 1);
        var passkey = _db.PasskeyCredentials.Single();

        var act = async () => await _service.RenameCredentialAsync(passkey.Id, user.Id, "   ");

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
