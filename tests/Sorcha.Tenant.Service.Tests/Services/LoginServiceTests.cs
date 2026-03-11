// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Tenant.Service.Data.Repositories;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Models.Dtos;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Tests.Services;

/// <summary>
/// Unit tests for <see cref="LoginService"/>: email/password authentication,
/// BCrypt verification, 2FA detection, rate limiting, and token issuance.
/// </summary>
public class LoginServiceTests
{
    private readonly Mock<IIdentityRepository> _identityRepo = new();
    private readonly Mock<IOrganizationRepository> _orgRepo = new();
    private readonly Mock<ITokenService> _tokenService = new();
    private readonly Mock<ITotpService> _totpService = new();
    private readonly Mock<IPasskeyService> _passkeyService = new();
    private readonly Mock<ITokenRevocationService> _revocationService = new();
    private readonly ILogger<LoginService> _logger = NullLogger<LoginService>.Instance;

    private LoginService CreateService() =>
        new(
            _identityRepo.Object,
            _orgRepo.Object,
            _tokenService.Object,
            _totpService.Object,
            _passkeyService.Object,
            _revocationService.Object,
            _logger);

    private static UserIdentity CreateTestUser(string email = "user@test.com", string? passwordHash = null)
    {
        return new UserIdentity
        {
            Id = Guid.NewGuid(),
            Email = email,
            DisplayName = "Test User",
            OrganizationId = Guid.NewGuid(),
            Status = IdentityStatus.Active,
            PasswordHash = passwordHash ?? BCrypt.Net.BCrypt.HashPassword("correct-password")
        };
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
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PasskeyCredential>());
    }

    [Fact]
    public async Task LoginAsync_ValidCredentials_ReturnsTokens()
    {
        // Arrange
        var user = CreateTestUser();
        var org = CreateTestOrg(user.OrganizationId);
        var expectedTokens = new TokenResponse
        {
            AccessToken = "access-token-123",
            RefreshToken = "refresh-token-456"
        };

        SetupNoRateLimit();
        SetupNo2Fa();
        _identityRepo.Setup(r => r.GetUserByEmailAsync("user@test.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _orgRepo.Setup(r => r.GetByIdAsync(user.OrganizationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(org);
        _tokenService.Setup(t => t.GenerateUserTokenAsync(user, org, It.IsAny<CancellationToken>()))
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
        SetupNoRateLimit();

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

        SetupNoRateLimit();
        _identityRepo.Setup(r => r.GetUserByEmailAsync("user@test.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _orgRepo.Setup(r => r.GetByIdAsync(user.OrganizationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(org);

        _totpService.Setup(t => t.GetStatusAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TotpStatusResult { IsEnabled = true });
        _passkeyService.Setup(p => p.GetCredentialsByOwnerAsync(
                OwnerTypes.OrgUser, user.Id, It.IsAny<CancellationToken>()))
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
            It.IsAny<UserIdentity>(), It.IsAny<Organization>(), It.IsAny<CancellationToken>()), Times.Never);
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

        // Should not even attempt user lookup
        _identityRepo.Verify(r => r.GetUserByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LoginAsync_ExternalIdpUser_ReturnsError()
    {
        // Arrange
        var user = CreateTestUser(passwordHash: null!);
        user.PasswordHash = null; // External IDP user has no password

        SetupNoRateLimit();
        _identityRepo.Setup(r => r.GetUserByEmailAsync("external@test.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var service = CreateService();

        // Act
        var result = await service.LoginAsync("external@test.com", "any-password");

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Be("Invalid email or password.");

        _revocationService.Verify(r => r.IncrementFailedAuthAttemptsAsync("external@test.com", It.IsAny<CancellationToken>()), Times.Once);
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
            OwnerType = OwnerTypes.OrgUser,
            OwnerId = user.Id,
            Status = CredentialStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _passkeyService.Setup(p => p.GetCredentialsByOwnerAsync(
                OwnerTypes.OrgUser, user.Id, It.IsAny<CancellationToken>()))
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
}
