// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text;

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Tests.Services;

/// <summary>
/// Unit tests for <see cref="PasswordResetService"/>: token generation,
/// validation, password update, user enumeration prevention, and one-time use.
/// </summary>
public class PasswordResetServiceTests : IDisposable
{
    private readonly TenantDbContext _dbContext;
    private readonly Mock<IPasswordPolicyService> _passwordPolicyService = new();
    private readonly Mock<IEmailSender> _emailSender = new();
    private readonly ILogger<PasswordResetService> _logger = NullLogger<PasswordResetService>.Instance;

    public PasswordResetServiceTests()
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new TenantDbContext(options);
    }

    private PasswordResetService CreateService() =>
        new(
            _dbContext,
            _passwordPolicyService.Object,
            _emailSender.Object,
            _logger);

    private UserIdentity CreateTestUser(
        string email = "user@test.com",
        string password = "existing-password",
        IdentityStatus status = IdentityStatus.Active)
    {
        var user = new UserIdentity
        {
            Id = Guid.NewGuid(),
            OrganizationId = Guid.NewGuid(),
            Email = email,
            DisplayName = "Test User",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.UserIdentities.Add(user);
        _dbContext.SaveChanges();
        return user;
    }

    private UserIdentity CreateExternalIdpUser(string email = "external@test.com")
    {
        var user = new UserIdentity
        {
            Id = Guid.NewGuid(),
            OrganizationId = Guid.NewGuid(),
            Email = email,
            DisplayName = "External User",
            PasswordHash = null,
            ExternalIdpSubject = "google|12345",
            Status = IdentityStatus.Active,
            ProvisionedVia = ProvisioningMethod.Oidc,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.UserIdentities.Add(user);
        _dbContext.SaveChanges();
        return user;
    }

    /// <summary>
    /// Simulates the token generation that happens inside PasswordResetService
    /// so we can test ValidateTokenAsync and ResetPasswordAsync with known tokens.
    /// </summary>
    private string SetupResetToken(UserIdentity user, DateTime? expiresAt = null)
    {
        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        var rawToken = Convert.ToBase64String(tokenBytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        user.PasswordResetTokenHash = Convert.ToHexStringLower(hashBytes);
        user.PasswordResetTokenExpiresAt = expiresAt ?? DateTime.UtcNow.AddHours(1);
        _dbContext.SaveChanges();

        return rawToken;
    }

    private void SetupValidPassword()
    {
        _passwordPolicyService.Setup(p => p.ValidateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PasswordValidationResult { IsValid = true, Errors = [] });
    }

    private void SetupInvalidPassword(params string[] errors)
    {
        _passwordPolicyService.Setup(p => p.ValidateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PasswordValidationResult { IsValid = false, Errors = errors.ToList() });
    }

    // --- RequestResetAsync tests ---

    [Fact]
    public async Task RequestResetAsync_ExistingUser_SendsEmailAndReturnsTrue()
    {
        // Arrange
        var user = CreateTestUser();
        var service = CreateService();

        // Act
        var result = await service.RequestResetAsync("user@test.com", "https://app.sorcha.io/auth/reset-password");

        // Assert
        result.Should().BeTrue();

        // Verify email was sent
        _emailSender.Verify(e => e.SendAsync(
            "user@test.com",
            "Reset your password",
            It.Is<string>(body => body.Contains("Reset Password")),
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify token hash was stored (not null)
        var updatedUser = await _dbContext.UserIdentities.FirstAsync(u => u.Id == user.Id);
        updatedUser.PasswordResetTokenHash.Should().NotBeNullOrWhiteSpace();
        updatedUser.PasswordResetTokenExpiresAt.Should().NotBeNull();
        updatedUser.PasswordResetTokenExpiresAt.Should().BeAfter(DateTime.UtcNow);
    }

    [Fact]
    public async Task RequestResetAsync_NonexistentUser_ReturnsTrueWithoutSendingEmail()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.RequestResetAsync("nobody@test.com", "https://app.sorcha.io/auth/reset-password");

        // Assert
        result.Should().BeTrue();

        // Verify no email was sent (prevents user enumeration)
        _emailSender.Verify(e => e.SendAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RequestResetAsync_InactiveUser_ReturnsTrueWithoutSendingEmail()
    {
        // Arrange
        CreateTestUser(status: IdentityStatus.Suspended);
        var service = CreateService();

        // Act
        var result = await service.RequestResetAsync("user@test.com", "https://app.sorcha.io/auth/reset-password");

        // Assert
        result.Should().BeTrue();
        _emailSender.Verify(e => e.SendAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RequestResetAsync_ExternalIdpUser_ReturnsTrueWithoutSendingEmail()
    {
        // Arrange
        CreateExternalIdpUser("external@test.com");
        var service = CreateService();

        // Act
        var result = await service.RequestResetAsync("external@test.com", "https://app.sorcha.io/auth/reset-password");

        // Assert
        result.Should().BeTrue();
        _emailSender.Verify(e => e.SendAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    // --- ValidateTokenAsync tests ---

    [Fact]
    public async Task ValidateTokenAsync_ValidToken_ReturnsIsValidWithEmail()
    {
        // Arrange
        var user = CreateTestUser();
        var rawToken = SetupResetToken(user);
        var service = CreateService();

        // Act
        var result = await service.ValidateTokenAsync(rawToken);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Email.Should().Be("user@test.com");
        result.Error.Should().BeNull();
    }

    [Fact]
    public async Task ValidateTokenAsync_ExpiredToken_ReturnsIsInvalid()
    {
        // Arrange
        var user = CreateTestUser();
        var rawToken = SetupResetToken(user, expiresAt: DateTime.UtcNow.AddHours(-1));
        var service = CreateService();

        // Act
        var result = await service.ValidateTokenAsync(rawToken);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain("expired");
        result.Email.Should().BeNull();
    }

    [Fact]
    public async Task ValidateTokenAsync_InvalidToken_ReturnsIsInvalid()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.ValidateTokenAsync("completely-bogus-token");

        // Assert
        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain("Invalid");
        result.Email.Should().BeNull();
    }

    [Fact]
    public async Task ValidateTokenAsync_EmptyToken_ReturnsIsInvalid()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.ValidateTokenAsync("");

        // Assert
        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain("required");
    }

    // --- ResetPasswordAsync tests ---

    [Fact]
    public async Task ResetPasswordAsync_ValidTokenAndValidPassword_Succeeds()
    {
        // Arrange
        var user = CreateTestUser(password: "old-password-123");
        var rawToken = SetupResetToken(user);
        SetupValidPassword();
        var service = CreateService();

        // Act
        var result = await service.ResetPasswordAsync(rawToken, "NewStrongPassword456!");

        // Assert
        result.Success.Should().BeTrue();
        result.Error.Should().BeNull();
        result.ValidationErrors.Should().BeNull();

        // Verify password was updated
        var updatedUser = await _dbContext.UserIdentities.FirstAsync(u => u.Id == user.Id);
        BCrypt.Net.BCrypt.Verify("NewStrongPassword456!", updatedUser.PasswordHash).Should().BeTrue();

        // Verify token was consumed (cleared)
        updatedUser.PasswordResetTokenHash.Should().BeNull();
        updatedUser.PasswordResetTokenExpiresAt.Should().BeNull();
    }

    [Fact]
    public async Task ResetPasswordAsync_ValidTokenAndWeakPassword_ReturnsValidationErrors()
    {
        // Arrange
        var user = CreateTestUser();
        var rawToken = SetupResetToken(user);
        SetupInvalidPassword("Password must be at least 12 characters", "Password found in breach list");
        var service = CreateService();

        // Act
        var result = await service.ResetPasswordAsync(rawToken, "weak");

        // Assert
        result.Success.Should().BeFalse();
        result.ValidationErrors.Should().NotBeNull();
        result.ValidationErrors!.Should().ContainKey("password");
        result.ValidationErrors["password"].Should().HaveCount(2);

        // Verify token was NOT consumed (user can retry with a better password)
        var updatedUser = await _dbContext.UserIdentities.FirstAsync(u => u.Id == user.Id);
        updatedUser.PasswordResetTokenHash.Should().NotBeNull();
    }

    [Fact]
    public async Task ResetPasswordAsync_InvalidToken_Fails()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.ResetPasswordAsync("completely-bogus-token", "NewPassword123!");

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Invalid");
    }

    [Fact]
    public async Task ResetPasswordAsync_ExpiredToken_Fails()
    {
        // Arrange
        var user = CreateTestUser();
        var rawToken = SetupResetToken(user, expiresAt: DateTime.UtcNow.AddHours(-1));
        var service = CreateService();

        // Act
        var result = await service.ResetPasswordAsync(rawToken, "NewPassword123!");

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("expired");
    }

    [Fact]
    public async Task ResetPasswordAsync_TokenConsumedAfterSuccessfulReset_CannotReuse()
    {
        // Arrange
        var user = CreateTestUser();
        var rawToken = SetupResetToken(user);
        SetupValidPassword();
        var service = CreateService();

        // Act — first reset succeeds
        var firstResult = await service.ResetPasswordAsync(rawToken, "FirstNewPassword123!");
        firstResult.Success.Should().BeTrue();

        // Act — second reset with same token fails
        var secondResult = await service.ResetPasswordAsync(rawToken, "SecondNewPassword456!");

        // Assert
        secondResult.Success.Should().BeFalse();
        secondResult.Error.Should().Contain("Invalid");
    }

    [Fact]
    public async Task ResetPasswordAsync_EmptyToken_Fails()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.ResetPasswordAsync("", "NewPassword123!");

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("required");
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
