// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics.Metrics;
using System.Text.Json;
using FluentAssertions;
using Fido2NetLib;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Data.Repositories;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Services;
using Sorcha.Tenant.Service.Telemetry;

namespace Sorcha.Tenant.Service.Tests.Services;

/// <summary>
/// Tests for <see cref="AuthChallengeService"/>. SQLite in-memory because
/// <see cref="AuthChallengeRepository.TryConsumeAsync"/> uses
/// <c>ExecuteUpdateAsync</c>, which the EF Core in-memory provider does not
/// support. Same pattern as <see cref="EmailVerificationServiceTests"/>.
/// </summary>
public class AuthChallengeServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TenantDbContext _db;
    private readonly AuthChallengeRepository _repository;
    private readonly Mock<ITotpService> _totp = new();
    private readonly Mock<IPasskeyService> _passkeys = new();
    private readonly Mock<ISocialLoginService> _social = new();
    private readonly AuthMetrics _metrics;

    public AuthChallengeServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<TenantDbContext>().UseSqlite(_connection).Options;
        _db = new TenantDbContext(options);
        _db.Database.EnsureCreated();
        _repository = new AuthChallengeRepository(_db);
        _metrics = new AuthMetrics(new TestMeterFactory());
    }

    private AuthChallengeService CreateService() => new(
        _db, _repository, _totp.Object, _passkeys.Object, _social.Object, _metrics, NullLogger<AuthChallengeService>.Instance);

    private async Task<(PlatformUser, UserIdentity)> SeedUserAsync(
        bool withPassword = false,
        bool withTotp = false,
        bool withActivePasskey = false,
        bool withSocial = false,
        string password = "DevPassword123!")
    {
        var platformUser = new PlatformUser
        {
            Id = Guid.NewGuid(),
            Email = $"u-{Guid.NewGuid():N}@test.com",
            DisplayName = "Test User",
            PasswordHash = withPassword ? BCrypt.Net.BCrypt.HashPassword(password) : null,
        };
        var userIdentity = new UserIdentity
        {
            Id = Guid.NewGuid(),
            OrganizationId = Guid.NewGuid(),
            PlatformUserId = platformUser.Id,
            Email = platformUser.Email,
            DisplayName = platformUser.DisplayName,
            Status = IdentityStatus.Active,
        };
        _db.PlatformUsers.Add(platformUser);
        _db.UserIdentities.Add(userIdentity);

        if (withTotp)
        {
            _db.TotpConfigurations.Add(new TotpConfiguration
            {
                Id = Guid.NewGuid(),
                UserId = userIdentity.Id,
                EncryptedSecret = new byte[] { 1, 2, 3 },
                EncryptionKeyId = "stub",
                BackupCodes = "stub",
                IsEnabled = true,
            });
        }

        if (withActivePasskey)
        {
            _db.PasskeyCredentials.Add(new PasskeyCredential
            {
                Id = Guid.NewGuid(),
                PlatformUserId = platformUser.Id,
                CredentialId = new byte[] { 1 },
                PublicKeyCose = new byte[] { 2 },
                DisplayName = "Dev Passkey",
                AttestationType = "none",
                Status = CredentialStatus.Active,
            });
        }

        if (withSocial)
        {
            _db.PlatformSocialLogins.Add(new PlatformSocialLogin
            {
                Id = Guid.NewGuid(),
                PlatformUserId = platformUser.Id,
                Provider = "google",
                Subject = $"google-{Guid.NewGuid():N}",
                Email = platformUser.Email,
            });
        }

        await _db.SaveChangesAsync();
        return (platformUser, userIdentity);
    }

    [Fact]
    public async Task InitiateAsync_ReturnsNoMethodAvailable_WhenUserHasNoEnrolledMethod()
    {
        var (pu, ui) = await SeedUserAsync();
        var service = CreateService();

        var prep = await service.InitiateAsync(
            new ChallengeContext(pu.Id, ui.Id),
            ScopedOperation.RemoveAuthMethod,
            preferredMethod: null);

        prep.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task InitiateAsync_PicksTotp_WhenTotpEnrolledRegardlessOfOtherMethods()
    {
        var (pu, ui) = await SeedUserAsync(withPassword: true, withTotp: true, withActivePasskey: true);
        var service = CreateService();

        var prep = await service.InitiateAsync(
            new ChallengeContext(pu.Id, ui.Id),
            ScopedOperation.ChangePassword,
            preferredMethod: null);

        prep.IsAvailable.Should().BeTrue();
        prep.Method.Should().Be(ChallengeMethod.Totp);
    }

    [Fact]
    public async Task InitiateAsync_HonoursPreferredMethod_WhenEnrolled()
    {
        var (pu, ui) = await SeedUserAsync(withPassword: true, withTotp: true);
        var service = CreateService();

        var prep = await service.InitiateAsync(
            new ChallengeContext(pu.Id, ui.Id),
            ScopedOperation.ChangePassword,
            preferredMethod: ChallengeMethod.Password);

        prep.Method.Should().Be(ChallengeMethod.Password);
    }

    [Fact]
    public async Task InitiateAsync_FallsBackToLadder_WhenPreferredMethodNotEnrolled()
    {
        var (pu, ui) = await SeedUserAsync(withPassword: true);
        var service = CreateService();

        var prep = await service.InitiateAsync(
            new ChallengeContext(pu.Id, ui.Id),
            ScopedOperation.ChangePassword,
            preferredMethod: ChallengeMethod.Totp); // Totp not enrolled

        prep.Method.Should().Be(ChallengeMethod.Password);
    }

    [Fact]
    public async Task VerifyAsync_TotpProof_AcceptedAndIssuesToken()
    {
        var (pu, ui) = await SeedUserAsync(withTotp: true);
        _totp.Setup(t => t.ValidateCodeAsync(ui.Id, "123456", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var service = CreateService();

        var result = await service.VerifyAsync(
            new ChallengeContext(pu.Id, ui.Id),
            ChallengeMethod.Totp,
            ScopedOperation.RemoveAuthMethod,
            JsonDocument.Parse("""{"code":"123456"}""").RootElement);

        result.Succeeded.Should().BeTrue();
        result.Token.Should().StartWith("ch_").And.HaveLength(46); // "ch_" + 43 base64url chars (32 bytes)
        result.ExpiresAt.Should().NotBeNull()
            .And.BeCloseTo(DateTimeOffset.UtcNow.AddMinutes(5), TimeSpan.FromSeconds(5));

        var stored = await _db.AuthChallengeTokens.SingleAsync();
        stored.PlatformUserId.Should().Be(pu.Id);
        stored.Method.Should().Be(ChallengeMethod.Totp);
        stored.ScopedOperation.Should().Be(ScopedOperation.RemoveAuthMethod);
        stored.TokenHash.Should().HaveLength(64); // SHA-256 hex
        stored.TokenHash.Should().NotContain(result.Token!); // raw never persisted
        stored.ConsumedAt.Should().BeNull();
    }

    [Fact]
    public async Task VerifyAsync_TotpRejected_NoTokenIssued()
    {
        var (pu, ui) = await SeedUserAsync(withTotp: true);
        _totp.Setup(t => t.ValidateCodeAsync(ui.Id, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var service = CreateService();

        var result = await service.VerifyAsync(
            new ChallengeContext(pu.Id, ui.Id),
            ChallengeMethod.Totp,
            ScopedOperation.RemoveAuthMethod,
            JsonDocument.Parse("""{"code":"000000"}""").RootElement);

        result.Outcome.Should().Be(ChallengeVerificationOutcome.ProofRejected);
        result.Token.Should().BeNull();
        (await _db.AuthChallengeTokens.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task VerifyAsync_PasswordProof_AcceptedAgainstBCryptHash()
    {
        var (pu, ui) = await SeedUserAsync(withPassword: true, password: "CorrectHorseBattery1!");
        var service = CreateService();

        var result = await service.VerifyAsync(
            new ChallengeContext(pu.Id, ui.Id),
            ChallengeMethod.Password,
            ScopedOperation.ChangePassword,
            JsonDocument.Parse("""{"password":"CorrectHorseBattery1!"}""").RootElement);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task VerifyAsync_WrongPassword_Rejected()
    {
        var (pu, ui) = await SeedUserAsync(withPassword: true, password: "Right!");
        var service = CreateService();

        var result = await service.VerifyAsync(
            new ChallengeContext(pu.Id, ui.Id),
            ChallengeMethod.Password,
            ScopedOperation.ChangePassword,
            JsonDocument.Parse("""{"password":"Wrong!"}""").RootElement);

        result.Outcome.Should().Be(ChallengeVerificationOutcome.ProofRejected);
    }

    [Fact]
    public async Task VerifyAsync_InvalidProofShape_ReturnsInvalidProofShape()
    {
        var (pu, ui) = await SeedUserAsync(withTotp: true);
        var service = CreateService();

        var result = await service.VerifyAsync(
            new ChallengeContext(pu.Id, ui.Id),
            ChallengeMethod.Totp,
            ScopedOperation.RemoveAuthMethod,
            JsonDocument.Parse("""{"wrong":"shape"}""").RootElement);

        result.Outcome.Should().Be(ChallengeVerificationOutcome.InvalidProofShape);
    }

    [Fact]
    public async Task VerifyAsync_MethodNotEnrolled_ReturnsMethodNotAvailable()
    {
        var (pu, ui) = await SeedUserAsync(withPassword: true); // No TOTP
        var service = CreateService();

        var result = await service.VerifyAsync(
            new ChallengeContext(pu.Id, ui.Id),
            ChallengeMethod.Totp,
            ScopedOperation.ChangePassword,
            JsonDocument.Parse("""{"code":"123456"}""").RootElement);

        result.Outcome.Should().Be(ChallengeVerificationOutcome.MethodNotAvailable);
    }

    // ---- Feature 150 (T014/T015): Passkey + Re-OAuth step-up proofs ----

    [Fact]
    public async Task VerifyAsync_PasskeyProof_MissingAssertionResponse_InvalidShape()
    {
        var (pu, ui) = await SeedUserAsync(withActivePasskey: true);
        var service = CreateService();

        var result = await service.VerifyAsync(
            new ChallengeContext(pu.Id, ui.Id),
            ChallengeMethod.Passkey,
            ScopedOperation.RemoveAuthMethod,
            JsonDocument.Parse("""{"transactionId":"tx"}""").RootElement);

        result.Outcome.Should().Be(ChallengeVerificationOutcome.InvalidProofShape);
    }

    [Fact]
    public async Task VerifyAsync_PasskeyProof_MatchingUser_Accepted()
    {
        var (pu, ui) = await SeedUserAsync(withActivePasskey: true);
        _passkeys
            .Setup(p => p.VerifyAssertionAsync(It.IsAny<string>(), It.IsAny<AuthenticatorAssertionRawResponse>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AssertionVerificationResult(NewCredential(pu.Id), pu.Id));
        var service = CreateService();

        var result = await service.VerifyAsync(
            new ChallengeContext(pu.Id, ui.Id),
            ChallengeMethod.Passkey,
            ScopedOperation.RemoveAuthMethod,
            JsonDocument.Parse("""{"transactionId":"tx","assertionResponse":{}}""").RootElement);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task VerifyAsync_PasskeyProof_DifferentUser_Rejected()
    {
        var (pu, ui) = await SeedUserAsync(withActivePasskey: true);
        _passkeys
            .Setup(p => p.VerifyAssertionAsync(It.IsAny<string>(), It.IsAny<AuthenticatorAssertionRawResponse>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AssertionVerificationResult(NewCredential(Guid.NewGuid()), Guid.NewGuid()));
        var service = CreateService();

        var result = await service.VerifyAsync(
            new ChallengeContext(pu.Id, ui.Id),
            ChallengeMethod.Passkey,
            ScopedOperation.RemoveAuthMethod,
            JsonDocument.Parse("""{"transactionId":"tx","assertionResponse":{}}""").RootElement);

        result.Outcome.Should().Be(ChallengeVerificationOutcome.ProofRejected);
    }

    [Fact]
    public async Task VerifyAsync_ReOAuthProof_LinkedProvider_Accepted()
    {
        var (pu, ui) = await SeedUserAsync(withSocial: true); // google
        _social
            .Setup(s => s.ExchangeCodeAsync("google", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SocialAuthCallbackResult(
                Success: true, Error: null, Subject: "sub", Email: "e@test.com",
                DisplayName: "d", EmailVerified: true, Provider: "google"));
        var service = CreateService();

        var result = await service.VerifyAsync(
            new ChallengeContext(pu.Id, ui.Id),
            ChallengeMethod.ReOAuth,
            ScopedOperation.RemoveAuthMethod,
            JsonDocument.Parse("""{"provider":"google","code":"c","state":"s"}""").RootElement);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task VerifyAsync_ReOAuthProof_ExchangeFails_Rejected()
    {
        var (pu, ui) = await SeedUserAsync(withSocial: true);
        _social
            .Setup(s => s.ExchangeCodeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SocialAuthCallbackResult(
                Success: false, Error: "denied", Subject: null, Email: null,
                DisplayName: null, EmailVerified: false, Provider: "google"));
        var service = CreateService();

        var result = await service.VerifyAsync(
            new ChallengeContext(pu.Id, ui.Id),
            ChallengeMethod.ReOAuth,
            ScopedOperation.RemoveAuthMethod,
            JsonDocument.Parse("""{"provider":"google","code":"c","state":"s"}""").RootElement);

        result.Outcome.Should().Be(ChallengeVerificationOutcome.ProofRejected);
    }

    private static PasskeyCredential NewCredential(Guid platformUserId) => new()
    {
        Id = Guid.NewGuid(),
        PlatformUserId = platformUserId,
        CredentialId = new byte[] { 1 },
        PublicKeyCose = new byte[] { 2 },
        DisplayName = "Dev Passkey",
        AttestationType = "none",
        Status = CredentialStatus.Active,
    };

    [Fact]
    public async Task TryConsumeAsync_AtomicConsume_ExactlyOneWinnerOnConcurrentPresentation()
    {
        // Manually insert a token, then drive two concurrent consumes.
        // Exactly one must win (return true); the other must lose (return false).
        var (pu, _) = await SeedUserAsync();
        var token = new AuthChallengeToken
        {
            PlatformUserId = pu.Id,
            TokenHash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            Method = ChallengeMethod.Totp,
            ScopedOperation = ScopedOperation.RemoveAuthMethod,
            IssuedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5),
        };
        await _repository.InsertAsync(token);

        // Two parallel consume attempts. Each gets its own DbContext to
        // mimic two requests hitting the same row.
        async Task<bool> ConsumeOnce()
        {
            var opts = new DbContextOptionsBuilder<TenantDbContext>().UseSqlite(_connection).Options;
            await using var db = new TenantDbContext(opts);
            var repo = new AuthChallengeRepository(db);
            return await repo.TryConsumeAsync(token.Id, DateTimeOffset.UtcNow);
        }

        var results = await Task.WhenAll(ConsumeOnce(), ConsumeOnce());
        results.Count(r => r).Should().Be(1, "exactly one consume must win the atomic UPDATE");
    }

    [Fact]
    public async Task PruneExpiredOlderThanAsync_DeletesOldRowsOnly()
    {
        var (pu, _) = await SeedUserAsync();
        var fresh = new AuthChallengeToken
        {
            PlatformUserId = pu.Id,
            TokenHash = "f" + new string('0', 63),
            Method = ChallengeMethod.Totp,
            ScopedOperation = ScopedOperation.RemoveAuthMethod,
            IssuedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5),
        };
        var stale = new AuthChallengeToken
        {
            PlatformUserId = pu.Id,
            TokenHash = "0" + new string('0', 63),
            Method = ChallengeMethod.Totp,
            ScopedOperation = ScopedOperation.RemoveAuthMethod,
            IssuedAt = DateTimeOffset.UtcNow.AddDays(-30),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(-29),
        };
        await _repository.InsertAsync(fresh);
        await _repository.InsertAsync(stale);

        var deleted = await _repository.PruneExpiredOlderThanAsync(DateTimeOffset.UtcNow.AddDays(-7));

        deleted.Should().Be(1);
        var survivors = await _db.AuthChallengeTokens.AsNoTracking().ToListAsync();
        survivors.Should().ContainSingle(t => t.Id == fresh.Id);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        _metrics.Dispose();
    }

    /// <summary>
    /// Bare-bones <see cref="IMeterFactory"/> for tests so we don't need a full
    /// telemetry pipeline. The counters are write-only here; assertions stay on
    /// the service contract.
    /// </summary>
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
