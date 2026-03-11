// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

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
/// Unit tests for <see cref="RegistrationService"/>: self-registration with
/// password policy validation, email uniqueness, domain restrictions, and
/// verification email sending.
/// </summary>
public class RegistrationServiceTests : IDisposable
{
    private readonly TenantDbContext _dbContext;
    private readonly Mock<IPasswordPolicyService> _passwordPolicyService = new();
    private readonly Mock<IEmailVerificationService> _emailVerificationService = new();
    private readonly ILogger<RegistrationService> _logger = NullLogger<RegistrationService>.Instance;

    public RegistrationServiceTests()
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new TenantDbContext(options);
    }

    private RegistrationService CreateService() =>
        new(
            _dbContext,
            _passwordPolicyService.Object,
            _emailVerificationService.Object,
            _logger);

    private Organization CreateTestOrg(
        OrgType orgType = OrgType.Public,
        bool selfRegistrationEnabled = true,
        string[]? allowedEmailDomains = null)
    {
        var org = new Organization
        {
            Id = Guid.NewGuid(),
            Name = "Test Organization",
            Subdomain = "testorg",
            Status = OrganizationStatus.Active,
            OrgType = orgType,
            SelfRegistrationEnabled = selfRegistrationEnabled,
            AllowedEmailDomains = allowedEmailDomains ?? [],
            CreatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.Organizations.Add(org);
        _dbContext.SaveChanges();
        return org;
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

    [Fact]
    public async Task RegisterAsync_ValidRegistration_ReturnsSuccessWithUserId()
    {
        // Arrange
        CreateTestOrg();
        SetupValidPassword();
        _emailVerificationService.Setup(e => e.GenerateAndSendVerificationAsync(
                It.IsAny<UserIdentity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("verification-token");

        var service = CreateService();

        // Act
        var result = await service.RegisterAsync("testorg", "new@test.com", "StrongPassword123!", "New User");

        // Assert
        result.Success.Should().BeTrue();
        result.UserId.Should().NotBeNull();
        result.UserId.Should().NotBe(Guid.Empty);
        result.Message.Should().Contain("Account created");
        result.ValidationErrors.Should().BeNull();
        result.Error.Should().BeNull();

        // Verify user was persisted
        var user = await _dbContext.UserIdentities.FirstOrDefaultAsync(u => u.Email == "new@test.com");
        user.Should().NotBeNull();
        user!.DisplayName.Should().Be("New User");
        user.Status.Should().Be(IdentityStatus.Active);
        user.ProvisionedVia.Should().Be(ProvisioningMethod.Local);

        // Verify audit log
        var audit = await _dbContext.AuditLogEntries.FirstOrDefaultAsync(a => a.IdentityId == user.Id);
        audit.Should().NotBeNull();
        audit!.EventType.Should().Be(AuditEventType.SelfRegistration);

        // Verify verification email sent
        _emailVerificationService.Verify(e => e.GenerateAndSendVerificationAsync(
            It.Is<UserIdentity>(u => u.Email == "new@test.com"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RegisterAsync_WeakPassword_ReturnsValidationErrors()
    {
        // Arrange
        CreateTestOrg();
        SetupInvalidPassword("Password must be at least 12 characters", "Password found in breach list");

        var service = CreateService();

        // Act
        var result = await service.RegisterAsync("testorg", "new@test.com", "weak", "New User");

        // Assert
        result.Success.Should().BeFalse();
        result.ValidationErrors.Should().NotBeNull();
        result.ValidationErrors!.Should().ContainKey("password");
        result.ValidationErrors["password"].Should().HaveCount(2);

        // Verify no user was created
        var userCount = await _dbContext.UserIdentities.CountAsync();
        userCount.Should().Be(0);
    }

    [Fact]
    public async Task RegisterAsync_DuplicateEmail_ReturnsConflictError()
    {
        // Arrange
        var org = CreateTestOrg();
        SetupValidPassword();

        // Add existing user with same email
        _dbContext.UserIdentities.Add(new UserIdentity
        {
            Id = Guid.NewGuid(),
            OrganizationId = org.Id,
            Email = "existing@test.com",
            DisplayName = "Existing User",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("password"),
            Status = IdentityStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _dbContext.SaveChangesAsync();

        var service = CreateService();

        // Act
        var result = await service.RegisterAsync("testorg", "existing@test.com", "StrongPassword123!", "New User");

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("email already exists");
        result.ErrorStatusCode.Should().Be(409);
    }

    [Fact]
    public async Task RegisterAsync_SelfRegistrationDisabled_ReturnsForbidden()
    {
        // Arrange
        CreateTestOrg(selfRegistrationEnabled: false);

        var service = CreateService();

        // Act
        var result = await service.RegisterAsync("testorg", "new@test.com", "StrongPassword123!", "New User");

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Self-registration is not enabled");
        result.ErrorStatusCode.Should().Be(403);
    }

    [Fact]
    public async Task RegisterAsync_StandardOrg_ReturnsForbidden()
    {
        // Arrange
        CreateTestOrg(orgType: OrgType.Standard);

        var service = CreateService();

        // Act
        var result = await service.RegisterAsync("testorg", "new@test.com", "StrongPassword123!", "New User");

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Self-registration is not enabled");
        result.ErrorStatusCode.Should().Be(403);
    }

    [Fact]
    public async Task RegisterAsync_DomainRestrictionViolation_ReturnsForbidden()
    {
        // Arrange
        CreateTestOrg(allowedEmailDomains: ["allowed.com", "company.org"]);
        SetupValidPassword();

        var service = CreateService();

        // Act
        var result = await service.RegisterAsync("testorg", "user@disallowed.com", "StrongPassword123!", "New User");

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("restricted to specific email domains");
        result.ErrorStatusCode.Should().Be(403);
    }

    [Fact]
    public async Task RegisterAsync_AllowedDomain_ReturnsSuccess()
    {
        // Arrange
        CreateTestOrg(allowedEmailDomains: ["allowed.com"]);
        SetupValidPassword();
        _emailVerificationService.Setup(e => e.GenerateAndSendVerificationAsync(
                It.IsAny<UserIdentity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("verification-token");

        var service = CreateService();

        // Act
        var result = await service.RegisterAsync("testorg", "user@allowed.com", "StrongPassword123!", "New User");

        // Assert
        result.Success.Should().BeTrue();
        result.UserId.Should().NotBeNull();
    }

    [Fact]
    public async Task RegisterAsync_OrganizationNotFound_ReturnsValidationError()
    {
        // Arrange — no org seeded
        var service = CreateService();

        // Act
        var result = await service.RegisterAsync("nonexistent", "user@test.com", "StrongPassword123!", "New User");

        // Assert
        result.Success.Should().BeFalse();
        result.ValidationErrors.Should().NotBeNull();
        result.ValidationErrors!.Should().ContainKey("orgSubdomain");
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
