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
/// Tests for <see cref="PasswordManagementService"/> (Feature 116 US3).
/// SQLite in-memory provider so the floor service's joined LINQ runs against
/// a real query plan, matching the AuthChallengeServiceTests / SocialLinkServiceTests
/// pattern. Uses the real <see cref="PasswordPolicyService"/> via
/// <see cref="StubPasswordPolicy"/> for tests that don't care about policy
/// detail; policy-violation paths inject a deny-everything stub directly.
/// </summary>
public class PasswordManagementServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TenantDbContext _db;
    private readonly AuthMethodService _floor;
    private readonly AuthMetrics _metrics;
    private readonly StubPasswordPolicy _policy;
    private readonly PasswordManagementService _service;

    public PasswordManagementServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<TenantDbContext>().UseSqlite(_connection).Options;
        _db = new TenantDbContext(options);
        _db.Database.EnsureCreated();
        _floor = new AuthMethodService(_db);
        _metrics = new AuthMetrics(new TestMeterFactory());
        _policy = new StubPasswordPolicy(allowAll: true);
        _service = new PasswordManagementService(_db, _floor, _policy, Mock.Of<ISecurityChangeNotifier>(), _metrics, NullLogger<PasswordManagementService>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        _metrics.Dispose();
    }

    private async Task<PlatformUser> SeedUserAsync(
        bool withPassword = false,
        int activePasskeys = 0,
        int socials = 0)
    {
        var pu = new PlatformUser
        {
            Id = Guid.NewGuid(),
            Email = $"u-{Guid.NewGuid():N}@test.com",
            DisplayName = "Test User",
            PasswordHash = withPassword ? BCrypt.Net.BCrypt.HashPassword("OriginalP4ssw0rd!!") : null,
        };
        _db.PlatformUsers.Add(pu);

        for (var i = 0; i < activePasskeys; i++)
        {
            _db.PasskeyCredentials.Add(new PasskeyCredential
            {
                Id = Guid.NewGuid(),
                PlatformUserId = pu.Id,
                CredentialId = Guid.NewGuid().ToByteArray(),
                PublicKeyCose = new byte[] { 1, 2, 3 },
                DisplayName = $"Active-{i}",
                AttestationType = "none",
                Status = CredentialStatus.Active,
            });
        }
        for (var i = 0; i < socials; i++)
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

    // -------- SetAsync --------

    [Fact]
    public async Task SetAsync_NoCurrentPassword_PolicyPasses_StoresHash()
    {
        var user = await SeedUserAsync(withPassword: false);

        var outcome = await _service.SetAsync(user.Id, "Brand-New-P4ssw0rd!!");

        outcome.Should().Be(PasswordSetOutcome.Set);
        var stored = await _db.PlatformUsers.AsNoTracking().FirstAsync(u => u.Id == user.Id);
        stored.PasswordHash.Should().NotBeNullOrEmpty();
        BCrypt.Net.BCrypt.Verify("Brand-New-P4ssw0rd!!", stored.PasswordHash).Should().BeTrue();
    }

    [Fact]
    public async Task SetAsync_AlreadyHasPassword_ReturnsAlreadySet_DoesNotMutate()
    {
        var user = await SeedUserAsync(withPassword: true);
        var originalHash = user.PasswordHash;

        var outcome = await _service.SetAsync(user.Id, "Brand-New-P4ssw0rd!!");

        outcome.Should().Be(PasswordSetOutcome.AlreadySet);
        var stored = await _db.PlatformUsers.AsNoTracking().FirstAsync(u => u.Id == user.Id);
        stored.PasswordHash.Should().Be(originalHash);
    }

    [Fact]
    public async Task SetAsync_PolicyViolation_ReturnsPolicyViolation_DoesNotMutate()
    {
        var user = await SeedUserAsync(withPassword: false);
        _policy.AllowAll = false;

        var outcome = await _service.SetAsync(user.Id, "anything");

        outcome.Should().Be(PasswordSetOutcome.PolicyViolation);
        var stored = await _db.PlatformUsers.AsNoTracking().FirstAsync(u => u.Id == user.Id);
        stored.PasswordHash.Should().BeNull();
    }

    [Fact]
    public async Task SetAsync_UserMissing_ReturnsNotFound()
    {
        var outcome = await _service.SetAsync(Guid.NewGuid(), "Brand-New-P4ssw0rd!!");

        outcome.Should().Be(PasswordSetOutcome.NotFound);
    }

    // -------- ChangeAsync --------

    [Fact]
    public async Task ChangeAsync_HasCurrentPassword_RotatesHash()
    {
        var user = await SeedUserAsync(withPassword: true);
        var originalHash = user.PasswordHash;

        var outcome = await _service.ChangeAsync(user.Id, "Rotated-P4ssw0rd!!");

        outcome.Should().Be(PasswordChangeOutcome.Changed);
        var stored = await _db.PlatformUsers.AsNoTracking().FirstAsync(u => u.Id == user.Id);
        stored.PasswordHash.Should().NotBe(originalHash);
        BCrypt.Net.BCrypt.Verify("Rotated-P4ssw0rd!!", stored.PasswordHash).Should().BeTrue();
    }

    [Fact]
    public async Task ChangeAsync_NoCurrentPassword_ReturnsNoCurrentPassword_DoesNotMutate()
    {
        var user = await SeedUserAsync(withPassword: false);

        var outcome = await _service.ChangeAsync(user.Id, "Anything-V4lid!!");

        outcome.Should().Be(PasswordChangeOutcome.NoCurrentPassword);
        var stored = await _db.PlatformUsers.AsNoTracking().FirstAsync(u => u.Id == user.Id);
        stored.PasswordHash.Should().BeNull();
    }

    [Fact]
    public async Task ChangeAsync_PolicyViolation_ReturnsPolicyViolation()
    {
        var user = await SeedUserAsync(withPassword: true);
        _policy.AllowAll = false;

        var outcome = await _service.ChangeAsync(user.Id, "weak");

        outcome.Should().Be(PasswordChangeOutcome.PolicyViolation);
    }

    // -------- RemoveAsync --------

    [Fact]
    public async Task RemoveAsync_HasPasswordAndPasskey_AllowsRemove()
    {
        var user = await SeedUserAsync(withPassword: true, activePasskeys: 1);

        var outcome = await _service.RemoveAsync(user.Id);

        outcome.Should().Be(PasswordRemoveOutcome.Removed);
        var stored = await _db.PlatformUsers.AsNoTracking().FirstAsync(u => u.Id == user.Id);
        stored.PasswordHash.Should().BeNull();
    }

    [Fact]
    public async Task RemoveAsync_HasPasswordAndSocial_AllowsRemove()
    {
        var user = await SeedUserAsync(withPassword: true, socials: 1);

        var outcome = await _service.RemoveAsync(user.Id);

        outcome.Should().Be(PasswordRemoveOutcome.Removed);
    }

    [Fact]
    public async Task RemoveAsync_PasswordOnly_BlockedByFloor_DoesNotMutate()
    {
        var user = await SeedUserAsync(withPassword: true);

        var outcome = await _service.RemoveAsync(user.Id);

        outcome.Should().Be(PasswordRemoveOutcome.BlockedByFloor);
        var stored = await _db.PlatformUsers.AsNoTracking().FirstAsync(u => u.Id == user.Id);
        stored.PasswordHash.Should().NotBeNull();
    }

    [Fact]
    public async Task RemoveAsync_NoCurrentPassword_ReturnsNoCurrentPassword()
    {
        var user = await SeedUserAsync(withPassword: false, activePasskeys: 1);

        var outcome = await _service.RemoveAsync(user.Id);

        outcome.Should().Be(PasswordRemoveOutcome.NoCurrentPassword);
    }

    [Fact]
    public async Task RemoveAsync_UserMissing_ReturnsNotFound()
    {
        var outcome = await _service.RemoveAsync(Guid.NewGuid());

        outcome.Should().Be(PasswordRemoveOutcome.NotFound);
    }

    [Fact]
    public async Task RemoveAsync_DisabledPasskeyDoesNotCount_FloorBlocks()
    {
        // password + 1 disabled passkey → only 1 active method (the password).
        // Removing the password leaves the user with 0 active methods → blocked.
        var user = await SeedUserAsync(withPassword: true);
        _db.PasskeyCredentials.Add(new PasskeyCredential
        {
            Id = Guid.NewGuid(),
            PlatformUserId = user.Id,
            CredentialId = Guid.NewGuid().ToByteArray(),
            PublicKeyCose = new byte[] { 1 },
            DisplayName = "Disabled",
            AttestationType = "none",
            Status = CredentialStatus.Disabled,
            DisabledAt = DateTimeOffset.UtcNow,
            DisabledReason = "signature-counter-regression",
        });
        await _db.SaveChangesAsync();

        var outcome = await _service.RemoveAsync(user.Id);

        outcome.Should().Be(PasswordRemoveOutcome.BlockedByFloor);
    }

    // -------- IsBootstrapModeAsync --------

    [Fact]
    public async Task IsBootstrapMode_NoMethodsAtAll_True()
    {
        var user = await SeedUserAsync(withPassword: false);

        var bootstrap = await _service.IsBootstrapModeAsync(user.Id);

        bootstrap.Should().BeTrue();
    }

    [Fact]
    public async Task IsBootstrapMode_HasPassword_False()
    {
        var user = await SeedUserAsync(withPassword: true);

        var bootstrap = await _service.IsBootstrapModeAsync(user.Id);

        bootstrap.Should().BeFalse();
    }

    [Fact]
    public async Task IsBootstrapMode_HasOnlyDisabledPasskey_True()
    {
        // Disabled passkeys aren't counted as active methods, so a user who
        // has only a Disabled passkey is technically in bootstrap mode — they
        // can't authenticate with anything. The /set endpoint will accept their
        // password without challenge in this case (correctly).
        var user = await SeedUserAsync(withPassword: false);
        _db.PasskeyCredentials.Add(new PasskeyCredential
        {
            Id = Guid.NewGuid(),
            PlatformUserId = user.Id,
            CredentialId = Guid.NewGuid().ToByteArray(),
            PublicKeyCose = new byte[] { 1 },
            DisplayName = "Disabled",
            AttestationType = "none",
            Status = CredentialStatus.Disabled,
            DisabledAt = DateTimeOffset.UtcNow,
            DisabledReason = "signature-counter-regression",
        });
        await _db.SaveChangesAsync();

        var bootstrap = await _service.IsBootstrapModeAsync(user.Id);

        bootstrap.Should().BeTrue();
    }

    [Fact]
    public async Task IsBootstrapMode_HasSocial_False()
    {
        var user = await SeedUserAsync(withPassword: false, socials: 1);

        var bootstrap = await _service.IsBootstrapModeAsync(user.Id);

        bootstrap.Should().BeFalse();
    }

    private sealed class StubPasswordPolicy : IPasswordPolicyService
    {
        public bool AllowAll { get; set; }
        public StubPasswordPolicy(bool allowAll) { AllowAll = allowAll; }
        public Task<PasswordValidationResult> ValidateAsync(string password, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new PasswordValidationResult
            {
                IsValid = AllowAll,
                Errors = AllowAll ? Array.Empty<string>() : new[] { "stubbed deny" },
            });
        }
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
