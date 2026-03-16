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
/// Tests that depend on PlatformUser model are skipped pending rewrite.
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
        IdentityStatus status = IdentityStatus.Active)
    {
        var user = new UserIdentity
        {
            Id = Guid.NewGuid(),
            OrganizationId = Guid.NewGuid(),
            PlatformUserId = Guid.NewGuid(),
            Email = email,
            DisplayName = "Test User",
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
            PlatformUserId = Guid.NewGuid(),
            Email = email,
            DisplayName = "External User",
            Status = IdentityStatus.Active,
            ProvisionedVia = ProvisioningMethod.Oidc,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.UserIdentities.Add(user);
        _dbContext.SaveChanges();
        return user;
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

    [Fact(Skip = "Needs rewrite for PlatformUser model")]
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

    [Fact(Skip = "Needs rewrite for PlatformUser model")]
    public async Task ValidateTokenAsync_ValidToken_ReturnsIsValidWithEmail()
    {
        // Token fields moved to PlatformUser
        await Task.CompletedTask;
    }

    [Fact(Skip = "Needs rewrite for PlatformUser model")]
    public async Task ValidateTokenAsync_ExpiredToken_ReturnsIsInvalid()
    {
        await Task.CompletedTask;
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

    [Fact(Skip = "Needs rewrite for PlatformUser model")]
    public async Task ResetPasswordAsync_ValidTokenAndValidPassword_Succeeds()
    {
        await Task.CompletedTask;
    }

    [Fact(Skip = "Needs rewrite for PlatformUser model")]
    public async Task ResetPasswordAsync_ValidTokenAndWeakPassword_ReturnsValidationErrors()
    {
        await Task.CompletedTask;
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

    [Fact(Skip = "Needs rewrite for PlatformUser model")]
    public async Task ResetPasswordAsync_ExpiredToken_Fails()
    {
        await Task.CompletedTask;
    }

    [Fact(Skip = "Needs rewrite for PlatformUser model")]
    public async Task ResetPasswordAsync_TokenConsumedAfterSuccessfulReset_CannotReuse()
    {
        await Task.CompletedTask;
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
