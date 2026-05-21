// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.ServiceDefaults.Auth;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Data.Repositories;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Models.Dtos;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Tests.Services;

/// <summary>
/// Unit tests for <see cref="LoginService"/>: platform-first authentication,
/// org selection, progressive lockout, 2FA detection, rate limiting, and token issuance.
/// </summary>
public class LoginServiceTests : IDisposable
{
    private readonly Mock<IIdentityRepository> _identityRepo = new();
    private readonly Mock<IOrganizationRepository> _orgRepo = new();
    private readonly Mock<ITokenService> _tokenService = new();
    private readonly Mock<ITotpService> _totpService = new();
    private readonly Mock<IPasskeyService> _passkeyService = new();
    private readonly Mock<ITokenRevocationService> _revocationService = new();
    private readonly Mock<IPlatformUserService> _platformUserService = new();
    private readonly Mock<ITransactionalEmailService> _transactionalEmail = new();
    private readonly ILogger<LoginService> _logger = NullLogger<LoginService>.Instance;
    private readonly Microsoft.Data.Sqlite.SqliteConnection _connection;
    private readonly TenantDbContext _dbContext;

    public LoginServiceTests()
    {
        // SQLite in-memory — the WelcomeEmailDispatcher uses ExecuteUpdateAsync which
        // the EF Core in-memory provider does not support.
        _connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlite(_connection)
            .Options;
        _dbContext = new TenantDbContext(options);
        _dbContext.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }

    private LoginService CreateService()
    {
        var dispatcher = new WelcomeEmailDispatcher(
            _dbContext, _transactionalEmail.Object, NullLogger<WelcomeEmailDispatcher>.Instance);
        return new(
            _dbContext,
            _identityRepo.Object,
            _orgRepo.Object,
            _tokenService.Object,
            _totpService.Object,
            _passkeyService.Object,
            _revocationService.Object,
            _platformUserService.Object,
            dispatcher,
            _logger);
    }

    private PlatformUser SeedPlatformUser(Guid platformUserId, string email = "user@test.com")
    {
        var platformUser = new PlatformUser
        {
            Id = platformUserId,
            Email = email,
            DisplayName = "Test User",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("correct-password"),
            Status = PlatformUserStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.PlatformUsers.Add(platformUser);
        _dbContext.SaveChanges();
        return platformUser;
    }

    /// <summary>
    /// Seeds the full user graph: PlatformUser, Organization, UserIdentity, OrgMembership.
    /// Returns (platformUser, organization, userIdentity).
    /// </summary>
    private (PlatformUser platformUser, Organization org, UserIdentity identity) SeedFullUser(
        string email = "user@test.com")
    {
        var platformUserId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var identityId = Guid.NewGuid();

        var platformUser = SeedPlatformUser(platformUserId, email);

        var org = new Organization
        {
            Id = orgId,
            Name = "Test Organization",
            Subdomain = "testorg",
            Status = OrganizationStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.Organizations.Add(org);

        var identity = new UserIdentity
        {
            Id = identityId,
            Email = email,
            DisplayName = "Test User",
            OrganizationId = orgId,
            PlatformUserId = platformUserId,
            Status = IdentityStatus.Active
        };
        _dbContext.UserIdentities.Add(identity);

        var membership = new PlatformUserOrgMembership
        {
            PlatformUserId = platformUserId,
            OrganizationId = orgId,
            Role = "Consumer",
            JoinedAt = DateTimeOffset.UtcNow
        };
        _dbContext.PlatformUserOrgMemberships.Add(membership);

        _dbContext.SaveChanges();
        return (platformUser, org, identity);
    }

    private void SetupNoRateLimit()
    {
        _revocationService.Setup(r => r.IsRateLimitedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
    }

    private void SetupNo2Fa()
    {
        _totpService.Setup(t => t.GetStatusAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TotpStatusResult { IsEnabled = false });
        _passkeyService.Setup(p => p.GetCredentialsByOwnerAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PasskeyCredential>());
    }

    private void SetupPasswordSuccess()
    {
        _platformUserService.Setup(p => p.ValidatePasswordAsync(
                It.IsAny<PlatformUser>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PasswordAuthResult(true));
    }

    private void SetupPasswordFailure()
    {
        _platformUserService.Setup(p => p.ValidatePasswordAsync(
                It.IsAny<PlatformUser>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PasswordAuthResult(false));
    }

    private void SetupPlatformUserLookup(PlatformUser platformUser, string email = "user@test.com")
    {
        _platformUserService.Setup(p => p.GetByEmailAsync(email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(platformUser);
    }

    private void SetupOrgMemberships(Guid platformUserId, params PlatformUserOrgMembership[] memberships)
    {
        _platformUserService.Setup(p => p.GetOrgMembershipsAsync(platformUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(memberships);
    }

    [Fact]
    public async Task LoginAsync_ValidCredentials_SingleOrg_ReturnsTokens()
    {
        // Arrange
        var (platformUser, org, identity) = SeedFullUser();
        var expectedTokens = new TokenResponse
        {
            AccessToken = "access-token-123",
            RefreshToken = "refresh-token-456"
        };

        SetupNoRateLimit();
        SetupNo2Fa();
        SetupPasswordSuccess();
        SetupPlatformUserLookup(platformUser);
        SetupOrgMemberships(platformUser.Id, new PlatformUserOrgMembership
        {
            PlatformUserId = platformUser.Id,
            OrganizationId = org.Id,
            Role = "Consumer",
            JoinedAt = DateTimeOffset.UtcNow
        });
        _tokenService.Setup(t => t.GenerateUserTokenAsync(
                It.IsAny<UserIdentity>(), It.IsAny<Organization>(), It.IsAny<Guid>(), It.IsAny<Tier>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedTokens);

        var service = CreateService();

        // Act
        var result = await service.LoginAsync("user@test.com", "correct-password");

        // Assert
        result.Success.Should().BeTrue();
        result.Tokens.Should().NotBeNull();
        result.Tokens!.AccessToken.Should().Be("access-token-123");
        result.Tokens.RefreshToken.Should().Be("refresh-token-456");
        result.TwoFactorRequired.Should().BeFalse();
        result.OrgSelectionRequired.Should().BeFalse();

        _revocationService.Verify(r => r.ResetFailedAuthAttemptsAsync("user@test.com", It.IsAny<CancellationToken>()), Times.Once);
        _identityRepo.Verify(r => r.UpdateUserAsync(It.Is<UserIdentity>(u => u.LastLoginAt != null), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LoginAsync_VerifiedUserFirstSuccessfulLogin_FiresWelcomeDispatcher()
    {
        // Arrange — user has verified email but hasn't had a welcome yet. This is the
        // social/passkey-equivalent case on the email-password path: verification was
        // completed previously, and the first successful login is now the welcome moment.
        var (platformUser, org, identity) = SeedFullUser();
        platformUser.EmailVerified = true;
        platformUser.EmailVerifiedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        platformUser.WelcomeSentAt = null;
        _dbContext.SaveChanges();

        SetupNoRateLimit();
        SetupNo2Fa();
        SetupPasswordSuccess();
        SetupPlatformUserLookup(platformUser);
        SetupOrgMemberships(platformUser.Id, new PlatformUserOrgMembership
        {
            PlatformUserId = platformUser.Id,
            OrganizationId = org.Id,
            Role = "Consumer",
            JoinedAt = DateTimeOffset.UtcNow,
        });
        _tokenService
            .Setup(t => t.GenerateUserTokenAsync(
                It.IsAny<UserIdentity>(), It.IsAny<Organization>(), It.IsAny<Guid>(), It.IsAny<Tier>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TokenResponse { AccessToken = "a", RefreshToken = "r" });

        var service = CreateService();

        // Act
        var result = await service.LoginAsync("user@test.com", "correct-password");

        // Assert
        result.Success.Should().BeTrue();
        _transactionalEmail.Verify(t => t.SendWelcomeAsync(
            It.Is<WelcomeDispatchContext>(c => c.User.Id == platformUser.Id),
            It.IsAny<CancellationToken>()), Times.Once);

        // WelcomeSentAt should have been persisted on success.
        var reloaded = _dbContext.PlatformUsers.AsNoTracking().First(u => u.Id == platformUser.Id);
        reloaded.WelcomeSentAt.Should().NotBeNull();
    }

    [Fact]
    public async Task LoginAsync_AlreadyWelcomedUser_DoesNotFireWelcomeAgain()
    {
        // Arrange — user already received a welcome on a prior login/verification.
        var (platformUser, org, identity) = SeedFullUser();
        var priorWelcome = DateTimeOffset.UtcNow.AddDays(-7);
        platformUser.EmailVerified = true;
        platformUser.WelcomeSentAt = priorWelcome;
        _dbContext.SaveChanges();

        SetupNoRateLimit();
        SetupNo2Fa();
        SetupPasswordSuccess();
        SetupPlatformUserLookup(platformUser);
        SetupOrgMemberships(platformUser.Id, new PlatformUserOrgMembership
        {
            PlatformUserId = platformUser.Id,
            OrganizationId = org.Id,
            Role = "Consumer",
            JoinedAt = DateTimeOffset.UtcNow,
        });
        _tokenService
            .Setup(t => t.GenerateUserTokenAsync(
                It.IsAny<UserIdentity>(), It.IsAny<Organization>(), It.IsAny<Guid>(), It.IsAny<Tier>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TokenResponse { AccessToken = "a", RefreshToken = "r" });

        var service = CreateService();

        // Act
        var result = await service.LoginAsync("user@test.com", "correct-password");

        // Assert
        result.Success.Should().BeTrue();
        _transactionalEmail.Verify(t => t.SendWelcomeAsync(
            It.IsAny<WelcomeDispatchContext>(), It.IsAny<CancellationToken>()), Times.Never);

        var reloaded = _dbContext.PlatformUsers.AsNoTracking().First(u => u.Id == platformUser.Id);
        reloaded.WelcomeSentAt.Should().Be(priorWelcome);
    }

    [Fact]
    public async Task LoginAsync_ValidCredentials_MultipleOrgs_ReturnsOrgSelection()
    {
        // Arrange
        var platformUserId = Guid.NewGuid();
        var platformUser = SeedPlatformUser(platformUserId);

        var org1 = new Organization
        {
            Id = Guid.NewGuid(), Name = "Org Alpha", Subdomain = "alpha",
            Status = OrganizationStatus.Active, CreatedAt = DateTimeOffset.UtcNow
        };
        var org2 = new Organization
        {
            Id = Guid.NewGuid(), Name = "Org Beta", Subdomain = "beta",
            Status = OrganizationStatus.Active, CreatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.Organizations.AddRange(org1, org2);

        var identity1 = new UserIdentity
        {
            Id = Guid.NewGuid(), Email = "user@test.com", DisplayName = "Test",
            OrganizationId = org1.Id, PlatformUserId = platformUserId, Status = IdentityStatus.Active
        };
        var identity2 = new UserIdentity
        {
            Id = Guid.NewGuid(), Email = "user@test.com", DisplayName = "Test",
            OrganizationId = org2.Id, PlatformUserId = platformUserId, Status = IdentityStatus.Active
        };
        _dbContext.UserIdentities.AddRange(identity1, identity2);
        _dbContext.SaveChanges();

        var memberships = new[]
        {
            new PlatformUserOrgMembership { PlatformUserId = platformUserId, OrganizationId = org1.Id, Role = "Admin", JoinedAt = DateTimeOffset.UtcNow },
            new PlatformUserOrgMembership { PlatformUserId = platformUserId, OrganizationId = org2.Id, Role = "Consumer", JoinedAt = DateTimeOffset.UtcNow }
        };

        SetupNoRateLimit();
        SetupPasswordSuccess();
        SetupPlatformUserLookup(platformUser);
        SetupOrgMemberships(platformUserId, memberships);
        _totpService.Setup(t => t.GenerateLoginTokenAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("platform-login-token");

        var service = CreateService();

        // Act
        var result = await service.LoginAsync("user@test.com", "correct-password");

        // Assert
        result.Success.Should().BeTrue();
        result.OrgSelectionRequired.Should().BeTrue();
        result.PlatformLoginToken.Should().Be("platform-login-token");
        result.AvailableOrganizations.Should().HaveCount(2);
        result.AvailableOrganizations!.Select(o => o.OrganizationName).Should().Contain("Org Alpha");
        result.AvailableOrganizations!.Select(o => o.OrganizationName).Should().Contain("Org Beta");
        result.Tokens.Should().BeNull();
    }

    [Fact]
    public async Task LoginAsync_InvalidPassword_ReturnsError()
    {
        // Arrange
        var (platformUser, _, _) = SeedFullUser();
        SetupNoRateLimit();
        SetupPasswordFailure();
        SetupPlatformUserLookup(platformUser);

        var service = CreateService();

        // Act
        var result = await service.LoginAsync("user@test.com", "wrong-password");

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Be("Invalid email or password.");
        result.Tokens.Should().BeNull();

        _revocationService.Verify(r => r.IncrementFailedAuthAttemptsAsync("user@test.com", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LoginAsync_AccountLocked_ReturnsLockedError()
    {
        // Arrange
        var (platformUser, _, _) = SeedFullUser();
        SetupNoRateLimit();
        SetupPlatformUserLookup(platformUser);
        _platformUserService.Setup(p => p.ValidatePasswordAsync(
                It.IsAny<PlatformUser>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PasswordAuthResult(false, IsLocked: true, LockedUntil: DateTimeOffset.UtcNow.AddMinutes(15)));

        var service = CreateService();

        // Act
        var result = await service.LoginAsync("user@test.com", "wrong-password");

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(LoginErrorCode.AccountLocked);
        result.Error.Should().Contain("Too many failed login attempts");
    }

    [Fact]
    public async Task LoginAsync_PermanentlyLocked_ReturnsLockedError()
    {
        // Arrange
        var (platformUser, _, _) = SeedFullUser();
        SetupNoRateLimit();
        SetupPlatformUserLookup(platformUser);
        _platformUserService.Setup(p => p.ValidatePasswordAsync(
                It.IsAny<PlatformUser>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PasswordAuthResult(false, IsLocked: true, IsPermanentlyLocked: true));

        var service = CreateService();

        // Act
        var result = await service.LoginAsync("user@test.com", "wrong-password");

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(LoginErrorCode.AccountLocked);
        result.Error.Should().Contain("contact an administrator");
    }

    [Fact]
    public async Task LoginAsync_UserNotFound_ReturnsError()
    {
        // Arrange
        SetupNoRateLimit();
        _platformUserService.Setup(p => p.GetByEmailAsync("nobody@test.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlatformUser?)null);

        var service = CreateService();

        // Act
        var result = await service.LoginAsync("nobody@test.com", "any-password");

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Be("Invalid email or password.");

        _revocationService.Verify(r => r.IncrementFailedAuthAttemptsAsync("nobody@test.com", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LoginAsync_TwoFactorEnabled_ReturnsTwoFactorChallenge()
    {
        // Arrange
        var (platformUser, org, identity) = SeedFullUser();

        SetupNoRateLimit();
        SetupPasswordSuccess();
        SetupPlatformUserLookup(platformUser);
        SetupOrgMemberships(platformUser.Id, new PlatformUserOrgMembership
        {
            PlatformUserId = platformUser.Id,
            OrganizationId = org.Id,
            Role = "Consumer",
            JoinedAt = DateTimeOffset.UtcNow
        });

        _totpService.Setup(t => t.GetStatusAsync(identity.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TotpStatusResult { IsEnabled = true });
        _passkeyService.Setup(p => p.GetCredentialsByOwnerAsync(
                platformUser.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PasskeyCredential>());
        _totpService.Setup(t => t.GenerateLoginTokenAsync(identity.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync("login-token-abc");

        var service = CreateService();

        // Act
        var result = await service.LoginAsync("user@test.com", "correct-password");

        // Assert
        result.Success.Should().BeTrue();
        result.TwoFactorRequired.Should().BeTrue();
        result.LoginToken.Should().Be("login-token-abc");
        result.AvailableMethods.Should().Contain("totp");
        result.Tokens.Should().BeNull();

        _tokenService.Verify(t => t.GenerateUserTokenAsync(
            It.IsAny<UserIdentity>(), It.IsAny<Organization>(), It.IsAny<Guid>(), It.IsAny<Tier>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LoginAsync_RateLimited_ReturnsError()
    {
        // Arrange
        _revocationService.Setup(r => r.IsRateLimitedAsync("blocked@test.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var service = CreateService();

        // Act
        var result = await service.LoginAsync("blocked@test.com", "any-password");

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Too many login attempts");
        result.ErrorCode.Should().Be(LoginErrorCode.RateLimited);

        // Should not even attempt user lookup
        _platformUserService.Verify(p => p.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LoginAsync_InactiveUser_ReturnsError()
    {
        // Arrange — platform user exists but is suspended
        var platformUser = SeedPlatformUser(Guid.NewGuid());
        platformUser.Status = PlatformUserStatus.Suspended;
        _dbContext.PlatformUsers.Update(platformUser);
        _dbContext.SaveChanges();

        SetupNoRateLimit();
        _platformUserService.Setup(p => p.GetByEmailAsync("user@test.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(platformUser);

        var service = CreateService();

        // Act
        var result = await service.LoginAsync("user@test.com", "correct-password");

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Be("Invalid email or password.");
    }

    [Fact]
    public async Task LoginAsync_PasskeyEnabled_ReturnsTwoFactorWithPasskeyMethod()
    {
        // Arrange
        var (platformUser, org, identity) = SeedFullUser();

        SetupNoRateLimit();
        SetupPasswordSuccess();
        SetupPlatformUserLookup(platformUser);
        SetupOrgMemberships(platformUser.Id, new PlatformUserOrgMembership
        {
            PlatformUserId = platformUser.Id,
            OrganizationId = org.Id,
            Role = "Consumer",
            JoinedAt = DateTimeOffset.UtcNow
        });

        _totpService.Setup(t => t.GetStatusAsync(identity.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TotpStatusResult { IsEnabled = false });

        var activePasskey = new PasskeyCredential
        {
            Id = Guid.NewGuid(),
            CredentialId = [1, 2, 3],
            PublicKeyCose = [4, 5, 6],
            PlatformUserId = platformUser.Id,
            Status = CredentialStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _passkeyService.Setup(p => p.GetCredentialsByOwnerAsync(
                platformUser.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { activePasskey });
        _totpService.Setup(t => t.GenerateLoginTokenAsync(identity.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync("passkey-login-token");

        var service = CreateService();

        // Act
        var result = await service.LoginAsync("user@test.com", "correct-password");

        // Assert
        result.Success.Should().BeTrue();
        result.TwoFactorRequired.Should().BeTrue();
        result.LoginToken.Should().Be("passkey-login-token");
        result.AvailableMethods.Should().Contain("passkey");
        result.AvailableMethods.Should().NotContain("totp");
    }

    [Fact]
    public async Task LoginAsync_NoActiveOrgs_ReturnsError()
    {
        // Arrange — platform user exists but all orgs are suspended
        var platformUserId = Guid.NewGuid();
        var platformUser = SeedPlatformUser(platformUserId);
        var orgId = Guid.NewGuid();

        var org = new Organization
        {
            Id = orgId, Name = "Suspended Org", Subdomain = "suspended",
            Status = OrganizationStatus.Suspended, CreatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.Organizations.Add(org);
        _dbContext.SaveChanges();

        SetupNoRateLimit();
        SetupPasswordSuccess();
        SetupPlatformUserLookup(platformUser);
        SetupOrgMemberships(platformUserId, new PlatformUserOrgMembership
        {
            PlatformUserId = platformUserId,
            OrganizationId = orgId,
            Role = "Consumer",
            JoinedAt = DateTimeOffset.UtcNow
        });

        var service = CreateService();

        // Act
        var result = await service.LoginAsync("user@test.com", "correct-password");

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("No active organizations");
    }

    [Fact]
    public async Task LoginAsync_WithSubdomain_IssuesOrgScopedToken()
    {
        // Arrange
        var platformUserId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var user = new UserIdentity
        {
            Id = Guid.NewGuid(),
            Email = "user@test.com",
            DisplayName = "Test User",
            OrganizationId = orgId,
            PlatformUserId = platformUserId,
            Status = IdentityStatus.Active
        };
        var org = new Organization
        {
            Id = orgId,
            Name = "Target Org",
            Subdomain = "target",
            Status = OrganizationStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var platformUser = SeedPlatformUser(platformUserId);
        _dbContext.Organizations.Add(org);
        _dbContext.UserIdentities.Add(user);
        _dbContext.SaveChanges();

        var membership = new PlatformUserOrgMembership
        {
            PlatformUserId = platformUserId,
            OrganizationId = orgId,
            Role = "Consumer",
            JoinedAt = DateTimeOffset.UtcNow
        };

        SetupNoRateLimit();
        SetupNo2Fa();
        _platformUserService.Setup(p => p.GetByEmailAsync("user@test.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(platformUser);
        _platformUserService.Setup(p => p.ValidatePasswordAsync(
                It.IsAny<PlatformUser>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PasswordAuthResult(true));
        _platformUserService.Setup(p => p.GetOrgMembershipsAsync(platformUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { membership });
        _tokenService.Setup(t => t.GenerateUserTokenAsync(user, org, platformUserId, It.IsAny<Tier>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TokenResponse { AccessToken = "org-token", RefreshToken = "org-refresh" });

        var service = CreateService();

        // Act
        var result = await service.LoginAsync("user@test.com", "correct-password", "target");

        // Assert
        result.Success.Should().BeTrue();
        result.Tokens.Should().NotBeNull();
        result.Tokens!.AccessToken.Should().Be("org-token");
    }

    [Fact]
    public async Task LoginAsync_WithSubdomain_NotMember_ReturnsError()
    {
        // Arrange
        var platformUser = SeedPlatformUser(Guid.NewGuid());
        var org = new Organization
        {
            Id = Guid.NewGuid(),
            Name = "Other Org",
            Subdomain = "other",
            Status = OrganizationStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.Organizations.Add(org);
        _dbContext.SaveChanges();

        SetupNoRateLimit();
        _platformUserService.Setup(p => p.GetByEmailAsync("user@test.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(platformUser);
        _platformUserService.Setup(p => p.ValidatePasswordAsync(
                It.IsAny<PlatformUser>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PasswordAuthResult(true));
        _platformUserService.Setup(p => p.GetOrgMembershipsAsync(platformUser.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PlatformUserOrgMembership>());

        var service = CreateService();

        // Act
        var result = await service.LoginAsync("user@test.com", "correct-password", "other");

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("not a member");
    }

    [Fact]
    public async Task CompleteOrgSelectionAsync_ValidToken_IssuesTokens()
    {
        // Arrange
        var (platformUser, org, identity) = SeedFullUser();
        var expectedTokens = new TokenResponse
        {
            AccessToken = "org-selected-token",
            RefreshToken = "org-selected-refresh"
        };

        SetupNo2Fa();
        _totpService.Setup(t => t.ValidateLoginTokenAsync("platform-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(identity.Id);
        _platformUserService.Setup(p => p.GetOrgMembershipsAsync(platformUser.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new PlatformUserOrgMembership
                {
                    PlatformUserId = platformUser.Id,
                    OrganizationId = org.Id,
                    Role = "Consumer",
                    JoinedAt = DateTimeOffset.UtcNow
                }
            });
        _orgRepo.Setup(r => r.GetByIdAsync(org.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(org);
        _tokenService.Setup(t => t.GenerateUserTokenAsync(
                It.IsAny<UserIdentity>(), It.IsAny<Organization>(), It.IsAny<Guid>(), It.IsAny<Tier>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedTokens);

        var service = CreateService();

        // Act
        var result = await service.CompleteOrgSelectionAsync("platform-token", org.Id);

        // Assert
        result.Success.Should().BeTrue();
        result.Tokens.Should().NotBeNull();
        result.Tokens!.AccessToken.Should().Be("org-selected-token");
    }

    [Fact]
    public async Task CompleteOrgSelectionAsync_ExpiredToken_ReturnsError()
    {
        // Arrange
        _totpService.Setup(t => t.ValidateLoginTokenAsync("expired-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);

        var service = CreateService();

        // Act
        var result = await service.CompleteOrgSelectionAsync("expired-token", Guid.NewGuid());

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("expired");
    }

    [Fact]
    public async Task CompleteOrgSelectionAsync_NotMember_ReturnsError()
    {
        // Arrange
        var (platformUser, org, identity) = SeedFullUser();
        var otherOrgId = Guid.NewGuid();

        _totpService.Setup(t => t.ValidateLoginTokenAsync("platform-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(identity.Id);
        _platformUserService.Setup(p => p.GetOrgMembershipsAsync(platformUser.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new PlatformUserOrgMembership
                {
                    PlatformUserId = platformUser.Id,
                    OrganizationId = org.Id,
                    Role = "Consumer",
                    JoinedAt = DateTimeOffset.UtcNow
                }
            });

        var service = CreateService();

        // Act — try to select a different org
        var result = await service.CompleteOrgSelectionAsync("platform-token", otherOrgId);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("not a member");
    }
}
