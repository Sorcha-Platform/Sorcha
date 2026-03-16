// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Data.Repositories;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Models.Dtos;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Tests.Services;

/// <summary>
/// Unit tests for <see cref="LoginService"/>: email/password authentication,
/// progressive lockout via PlatformUserService, 2FA detection, rate limiting, and token issuance.
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
    private readonly ILogger<LoginService> _logger = NullLogger<LoginService>.Instance;
    private readonly TenantDbContext _dbContext;

    public LoginServiceTests()
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseInMemoryDatabase(databaseName: $"LoginServiceTests-{Guid.NewGuid()}")
            .Options;
        _dbContext = new TenantDbContext(options);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    private LoginService CreateService() =>
        new(
            _dbContext,
            _identityRepo.Object,
            _orgRepo.Object,
            _tokenService.Object,
            _totpService.Object,
            _passkeyService.Object,
            _revocationService.Object,
            _platformUserService.Object,
            _logger);

    private static UserIdentity CreateTestUser(string email = "user@test.com")
    {
        return new UserIdentity
        {
            Id = Guid.NewGuid(),
            Email = email,
            DisplayName = "Test User",
            OrganizationId = Guid.NewGuid(),
            PlatformUserId = Guid.NewGuid(),
            Status = IdentityStatus.Active
        };
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

    private static Organization CreateTestOrg(Guid orgId) =>
        new()
        {
            Id = orgId,
            Name = "Test Organization",
            Subdomain = "testorg",
            Status = OrganizationStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow
        };

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

    [Fact]
    public async Task LoginAsync_ValidCredentials_ReturnsTokens()
    {
        // Arrange
        var user = CreateTestUser();
        var org = CreateTestOrg(user.OrganizationId);
        SeedPlatformUser(user.PlatformUserId);
        var expectedTokens = new TokenResponse
        {
            AccessToken = "access-token-123",
            RefreshToken = "refresh-token-456"
        };

        SetupNoRateLimit();
        SetupNo2Fa();
        SetupPasswordSuccess();
        _identityRepo.Setup(r => r.GetUserByEmailAsync("user@test.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _orgRepo.Setup(r => r.GetByIdAsync(user.OrganizationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(org);
        _tokenService.Setup(t => t.GenerateUserTokenAsync(user, org, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
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

        _revocationService.Verify(r => r.ResetFailedAuthAttemptsAsync("user@test.com", It.IsAny<CancellationToken>()), Times.Once);
        _identityRepo.Verify(r => r.UpdateUserAsync(It.Is<UserIdentity>(u => u.LastLoginAt != null), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LoginAsync_InvalidPassword_ReturnsError()
    {
        // Arrange
        var user = CreateTestUser();
        SeedPlatformUser(user.PlatformUserId);
        SetupNoRateLimit();
        SetupPasswordFailure();

        _identityRepo.Setup(r => r.GetUserByEmailAsync("user@test.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

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
        var user = CreateTestUser();
        SeedPlatformUser(user.PlatformUserId);
        SetupNoRateLimit();

        _identityRepo.Setup(r => r.GetUserByEmailAsync("user@test.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
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
        var user = CreateTestUser();
        SeedPlatformUser(user.PlatformUserId);
        SetupNoRateLimit();

        _identityRepo.Setup(r => r.GetUserByEmailAsync("user@test.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
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

        _identityRepo.Setup(r => r.GetUserByEmailAsync("nobody@test.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserIdentity?)null);

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
        var user = CreateTestUser();
        var org = CreateTestOrg(user.OrganizationId);
        SeedPlatformUser(user.PlatformUserId);
        SetupPasswordSuccess();

        SetupNoRateLimit();
        _identityRepo.Setup(r => r.GetUserByEmailAsync("user@test.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _orgRepo.Setup(r => r.GetByIdAsync(user.OrganizationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(org);

        _totpService.Setup(t => t.GetStatusAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TotpStatusResult { IsEnabled = true });
        _passkeyService.Setup(p => p.GetCredentialsByOwnerAsync(
                user.PlatformUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PasskeyCredential>());
        _totpService.Setup(t => t.GenerateLoginTokenAsync(user.Id, It.IsAny<CancellationToken>()))
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

        // Should not issue JWT tokens when 2FA is required
        _tokenService.Verify(t => t.GenerateUserTokenAsync(
            It.IsAny<UserIdentity>(), It.IsAny<Organization>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
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
        _identityRepo.Verify(r => r.GetUserByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LoginAsync_InactiveUser_ReturnsError()
    {
        // Arrange
        var user = CreateTestUser();
        user.Status = IdentityStatus.Suspended;

        SetupNoRateLimit();
        _identityRepo.Setup(r => r.GetUserByEmailAsync("user@test.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

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
        var user = CreateTestUser();
        var org = CreateTestOrg(user.OrganizationId);
        SeedPlatformUser(user.PlatformUserId);
        SetupPasswordSuccess();

        SetupNoRateLimit();
        _identityRepo.Setup(r => r.GetUserByEmailAsync("user@test.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _orgRepo.Setup(r => r.GetByIdAsync(user.OrganizationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(org);

        _totpService.Setup(t => t.GetStatusAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TotpStatusResult { IsEnabled = false });

        var activePasskey = new PasskeyCredential
        {
            Id = Guid.NewGuid(),
            CredentialId = [1, 2, 3],
            PublicKeyCose = [4, 5, 6],
            PlatformUserId = user.PlatformUserId,
            Status = CredentialStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _passkeyService.Setup(p => p.GetCredentialsByOwnerAsync(
                user.PlatformUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { activePasskey });
        _totpService.Setup(t => t.GenerateLoginTokenAsync(user.Id, It.IsAny<CancellationToken>()))
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
    public async Task LoginAsync_OrganizationNotFound_ReturnsError()
    {
        // Arrange
        var user = CreateTestUser();
        SeedPlatformUser(user.PlatformUserId);
        SetupPasswordSuccess();

        SetupNoRateLimit();
        _identityRepo.Setup(r => r.GetUserByEmailAsync("user@test.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _orgRepo.Setup(r => r.GetByIdAsync(user.OrganizationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Organization?)null);

        var service = CreateService();

        // Act
        var result = await service.LoginAsync("user@test.com", "correct-password");

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Be("Invalid email or password.");
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
            Role = "Member",
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
        _tokenService.Setup(t => t.GenerateUserTokenAsync(user, org, platformUserId, It.IsAny<CancellationToken>()))
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
}
