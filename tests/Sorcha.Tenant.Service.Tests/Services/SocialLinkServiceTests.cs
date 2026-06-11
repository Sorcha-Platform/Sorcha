// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics.Metrics;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Services;
using Sorcha.Tenant.Service.Telemetry;

namespace Sorcha.Tenant.Service.Tests.Services;

/// <summary>
/// Tests for <see cref="SocialLinkService"/> (Feature 116 US1). SQLite
/// in-memory provider so the floor helper's joined LINQ runs against a
/// real query plan, matching the AuthChallengeServiceTests pattern.
/// </summary>
public class SocialLinkServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TenantDbContext _db;
    private readonly AuthMetrics _metrics;
    private readonly AuthMethodService _floor;

    public SocialLinkServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<TenantDbContext>().UseSqlite(_connection).Options;
        _db = new TenantDbContext(options);
        _db.Database.EnsureCreated();
        _metrics = new AuthMetrics(new TestMeterFactory());
        _floor = new AuthMethodService(_db, new VerificationChannelRegistry([]));
    }

    private SocialLinkService CreateService() => new(
        _db, _floor, Mock.Of<ISecurityChangeNotifier>(), _metrics, NullLogger<SocialLinkService>.Instance);

    private async Task<PlatformUser> SeedUserAsync(
        string email = "alice@test.com",
        bool withPassword = true,
        bool withActivePasskey = false)
    {
        var pu = new PlatformUser
        {
            Id = Guid.NewGuid(),
            Email = email,
            DisplayName = "Test User",
            PasswordHash = withPassword ? "hash" : null,
        };
        _db.PlatformUsers.Add(pu);

        if (withActivePasskey)
        {
            _db.PasskeyCredentials.Add(new PasskeyCredential
            {
                Id = Guid.NewGuid(),
                PlatformUserId = pu.Id,
                CredentialId = new byte[] { 1 },
                PublicKeyCose = new byte[] { 1 },
                DisplayName = "Active",
                AttestationType = "none",
                Status = CredentialStatus.Active,
            });
        }

        await _db.SaveChangesAsync();
        return pu;
    }

    private async Task<PlatformSocialLogin> SeedLinkAsync(
        Guid platformUserId,
        string provider = "google",
        string? subject = null,
        string? email = "user@gmail.com")
    {
        var link = new PlatformSocialLogin
        {
            Id = Guid.NewGuid(),
            PlatformUserId = platformUserId,
            Provider = provider,
            Subject = subject ?? $"sub-{Guid.NewGuid():N}",
            Email = email,
            LinkedAt = DateTimeOffset.UtcNow,
        };
        _db.PlatformSocialLogins.Add(link);
        await _db.SaveChangesAsync();
        return link;
    }

    [Fact]
    public async Task LinkAsync_FreshLink_Inserted()
    {
        var pu = await SeedUserAsync();
        var service = CreateService();

        var outcome = await service.LinkAsync(
            pu.Id, "google", "google-sub-1", "alice@gmail.com", "Alice");

        outcome.Should().Be(SocialLinkOutcome.Linked);
        var stored = await _db.PlatformSocialLogins.SingleAsync();
        stored.Provider.Should().Be("google");
        stored.Subject.Should().Be("google-sub-1");
        stored.PlatformUserId.Should().Be(pu.Id);
    }

    [Fact]
    public async Task LinkAsync_AlreadyLinkedToCaller_IdempotentNoOp()
    {
        var pu = await SeedUserAsync();
        await SeedLinkAsync(pu.Id, "google", "sub-X");
        var service = CreateService();

        var outcome = await service.LinkAsync(
            pu.Id, "google", "sub-X", "alice@gmail.com", "Alice");

        outcome.Should().Be(SocialLinkOutcome.AlreadyLinkedToCaller);
        (await _db.PlatformSocialLogins.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task LinkAsync_AlreadyLinkedToOtherUser_RejectedWithCollision()
    {
        var alice = await SeedUserAsync("alice@test.com");
        var bob = await SeedUserAsync("bob@test.com");
        await SeedLinkAsync(bob.Id, "google", "shared-sub");
        var service = CreateService();

        var outcome = await service.LinkAsync(
            alice.Id, "google", "shared-sub", "bob@gmail.com", "Bob");

        outcome.Should().Be(SocialLinkOutcome.AlreadyLinkedToDifferentUser);
        // No additional row inserted.
        (await _db.PlatformSocialLogins.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task LinkAsync_EmailBelongsToDifferentPlatformUser_RejectedWithCollision()
    {
        // Q1: provider returns an email that belongs to another Sorcha account
        // (no (Provider, Subject) collision because that subject is not yet
        // linked anywhere — but the email matches Bob's primary email).
        await SeedUserAsync("bob@test.com");
        var alice = await SeedUserAsync("alice@test.com");
        var service = CreateService();

        var outcome = await service.LinkAsync(
            alice.Id, "google", "fresh-sub", "bob@test.com", "Bob");

        outcome.Should().Be(SocialLinkOutcome.EmailCollision);
        (await _db.PlatformSocialLogins.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task LinkAsync_NoEmailFromProvider_AllowedWhenSubjectIsFree()
    {
        // Apple "Hide my email" / private GitHub email — collision check is
        // skipped (no email to collide). The (Provider, Subject) unique
        // index still prevents duplicate links.
        var alice = await SeedUserAsync("alice@test.com");
        var service = CreateService();

        var outcome = await service.LinkAsync(
            alice.Id, "apple", "apple-private-sub", providerEmail: null, providerDisplayName: null);

        outcome.Should().Be(SocialLinkOutcome.Linked);
    }

    [Fact]
    public async Task LinkAsync_EmailMatchesCallersOwnEmail_NotTreatedAsCollision()
    {
        // The caller themselves should be allowed to link a social provider
        // that returns their own primary email.
        var alice = await SeedUserAsync("alice@test.com");
        var service = CreateService();

        var outcome = await service.LinkAsync(
            alice.Id, "google", "fresh-sub", "alice@test.com", "Alice");

        outcome.Should().Be(SocialLinkOutcome.Linked);
    }

    [Fact]
    public async Task UnlinkAsync_ExistingLinkWithFloorRoom_HardDeleted()
    {
        var pu = await SeedUserAsync(withPassword: true);
        var link = await SeedLinkAsync(pu.Id, "google");
        var service = CreateService();

        var outcome = await service.UnlinkAsync(pu.Id, link.Id);

        outcome.Should().Be(SocialUnlinkOutcome.Unlinked);
        (await _db.PlatformSocialLogins.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task UnlinkAsync_NotFound_ReturnsNotFound()
    {
        var pu = await SeedUserAsync();
        var service = CreateService();

        var outcome = await service.UnlinkAsync(pu.Id, Guid.NewGuid());

        outcome.Should().Be(SocialUnlinkOutcome.NotFound);
    }

    [Fact]
    public async Task UnlinkAsync_LinkBelongsToDifferentUser_NotFound()
    {
        // Defence: even if the caller knows another user's linkId, the lookup
        // is scoped by PlatformUserId and returns NotFound rather than 403 —
        // matches the existing pattern for most Tenant endpoints.
        var alice = await SeedUserAsync("alice@test.com");
        var bob = await SeedUserAsync("bob@test.com");
        var bobLink = await SeedLinkAsync(bob.Id, "google");
        var service = CreateService();

        var outcome = await service.UnlinkAsync(alice.Id, bobLink.Id);

        outcome.Should().Be(SocialUnlinkOutcome.NotFound);
        (await _db.PlatformSocialLogins.AnyAsync(s => s.Id == bobLink.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task UnlinkAsync_OnlyMethod_BlockedByFloor()
    {
        // Social-only user: unlinking the only link triggers the floor.
        var pu = await SeedUserAsync(withPassword: false);
        var link = await SeedLinkAsync(pu.Id, "google");
        var service = CreateService();

        var outcome = await service.UnlinkAsync(pu.Id, link.Id);

        outcome.Should().Be(SocialUnlinkOutcome.FloorViolation);
        (await _db.PlatformSocialLogins.AnyAsync()).Should().BeTrue("row remains");
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        _metrics.Dispose();
    }

    private sealed class TestMeterFactory : IMeterFactory
    {
        private readonly List<Meter> _meters = new();

        public Meter Create(MeterOptions options)
        {
            var meter = new Meter(options);
            _meters.Add(meter);
            return meter;
        }

        public void Dispose()
        {
            foreach (var m in _meters) m.Dispose();
            _meters.Clear();
        }
    }
}
