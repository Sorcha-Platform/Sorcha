// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Models.Dtos;
using Sorcha.Tenant.Service.Services;
using Sorcha.Tenant.Service.Tests.Helpers;

namespace Sorcha.Tenant.Service.Tests.Services;

/// <summary>
/// Unit tests for <see cref="PlatformUserProvisioningService"/>: admin-initiated
/// user provisioning with PlatformUser creation/reuse, UserIdentity, OrgMembership,
/// password hashing, email verification skip, and validation.
/// </summary>
public class PlatformUserProvisioningTests : IDisposable
{
    private readonly TenantDbContext _dbContext;
    private readonly Mock<IPasswordPolicyService> _passwordPolicyService = new();
    private readonly Guid _testOrgId = Guid.NewGuid();

    public PlatformUserProvisioningTests()
    {
        _dbContext = InMemoryDbContextFactory.Create();

        // Seed a test organization
        _dbContext.Organizations.Add(new Organization
        {
            Id = _testOrgId,
            Name = "Test Organization",
            Subdomain = "testorg",
            Status = OrganizationStatus.Active,
            OrgType = OrgType.Standard,
            AllowedEmailDomains = [],
            CreatedAt = DateTimeOffset.UtcNow
        });
        _dbContext.SaveChanges();
    }

    private PlatformUserProvisioningService CreateService() =>
        new(
            _dbContext,
            _passwordPolicyService.Object,
            NullLogger<PlatformUserProvisioningService>.Instance);

    private AdminProvisionUserRequest CreateRequest(
        string email = "newuser@test.com",
        string displayName = "New User",
        string role = "Consumer",
        string? password = null,
        bool skipEmailVerification = false,
        Guid? organizationId = null) =>
        new()
        {
            Email = email,
            DisplayName = displayName,
            OrganizationId = organizationId ?? _testOrgId,
            Role = role,
            Password = password,
            SkipEmailVerification = skipEmailVerification
        };

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
    public async Task ProvisionUserAsync_ValidRequest_CreatesPlatformUserWithCorrectFields()
    {
        // Arrange
        SetupValidPassword();
        var request = CreateRequest(
            email: "admin@company.com",
            displayName: "Admin User",
            role: "Administrator",
            password: "StrongPassword123!");
        var service = CreateService();

        // Act
        var result = await service.ProvisionUserAsync(request);

        // Assert
        result.Success.Should().BeTrue();
        result.Response.Should().NotBeNull();
        result.Response!.Email.Should().Be("admin@company.com");
        result.Response.DisplayName.Should().Be("Admin User");
        result.Response.Role.Should().Be("Administrator");

        // Verify PlatformUser persisted with correct fields
        var platformUser = await _dbContext.PlatformUsers
            .FirstOrDefaultAsync(p => p.Email == "admin@company.com");
        platformUser.Should().NotBeNull();
        platformUser!.DisplayName.Should().Be("Admin User");
        platformUser.PasswordHash.Should().NotBeNullOrEmpty();
        BCrypt.Net.BCrypt.Verify("StrongPassword123!", platformUser.PasswordHash).Should().BeTrue();
        platformUser.Status.Should().Be(PlatformUserStatus.Active);
    }

    [Fact]
    public async Task ProvisionUserAsync_ValidRequest_CreatesUserIdentityWithCorrectFields()
    {
        // Arrange
        var request = CreateRequest(role: "Administrator");
        var service = CreateService();

        // Act
        var result = await service.ProvisionUserAsync(request);

        // Assert
        result.Success.Should().BeTrue();

        var userIdentity = await _dbContext.UserIdentities
            .FirstOrDefaultAsync(u => u.Email == "newuser@test.com");
        userIdentity.Should().NotBeNull();
        userIdentity!.OrganizationId.Should().Be(_testOrgId);
        userIdentity.Roles.Should().Contain(UserRole.Administrator);
        userIdentity.ProvisionedVia.Should().Be(ProvisioningMethod.AdminCreated);
        userIdentity.ProfileCompleted.Should().BeTrue();
        userIdentity.Status.Should().Be(IdentityStatus.Active);
        userIdentity.PlatformUserId.Should().Be(result.Response!.UserId);
    }

    [Fact]
    public async Task ProvisionUserAsync_ValidRequest_CreatesOrgMembership()
    {
        // Arrange
        var request = CreateRequest(role: "Consumer");
        var service = CreateService();

        // Act
        var result = await service.ProvisionUserAsync(request);

        // Assert
        result.Success.Should().BeTrue();

        var membership = await _dbContext.PlatformUserOrgMemberships
            .FirstOrDefaultAsync(m =>
                m.PlatformUserId == result.Response!.UserId &&
                m.OrganizationId == _testOrgId);
        membership.Should().NotBeNull();
        membership!.Role.Should().Be("Consumer");
    }

    [Fact]
    public async Task ProvisionUserAsync_ExistingPlatformUser_ReusesAndSetsFlag()
    {
        // Arrange — create existing PlatformUser with same email
        var existingUser = new PlatformUser
        {
            Id = Guid.NewGuid(),
            Email = "existing@test.com",
            DisplayName = "Existing User",
            Status = PlatformUserStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.PlatformUsers.Add(existingUser);
        await _dbContext.SaveChangesAsync();

        var request = CreateRequest(email: "existing@test.com", displayName: "New Display Name");
        var service = CreateService();

        // Act
        var result = await service.ProvisionUserAsync(request);

        // Assert
        result.Success.Should().BeTrue();
        result.Response!.IsExistingPlatformUser.Should().BeTrue();
        result.Response.UserId.Should().Be(existingUser.Id);

        // Verify no duplicate PlatformUser created
        var platformUserCount = await _dbContext.PlatformUsers
            .CountAsync(p => p.Email == "existing@test.com");
        platformUserCount.Should().Be(1);
    }

    [Fact]
    public async Task ProvisionUserAsync_SkipEmailVerification_MarksEmailVerified()
    {
        // Arrange
        var request = CreateRequest(skipEmailVerification: true);
        var service = CreateService();

        // Act
        var result = await service.ProvisionUserAsync(request);

        // Assert
        result.Success.Should().BeTrue();
        result.Response!.EmailVerified.Should().BeTrue();

        var platformUser = await _dbContext.PlatformUsers.FindAsync(result.Response.UserId);
        platformUser!.EmailVerified.Should().BeTrue();
        platformUser.EmailVerifiedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ProvisionUserAsync_NoSkipEmailVerification_EmailNotVerified()
    {
        // Arrange
        var request = CreateRequest(skipEmailVerification: false);
        var service = CreateService();

        // Act
        var result = await service.ProvisionUserAsync(request);

        // Assert
        result.Success.Should().BeTrue();
        result.Response!.EmailVerified.Should().BeFalse();

        var platformUser = await _dbContext.PlatformUsers.FindAsync(result.Response.UserId);
        platformUser!.EmailVerified.Should().BeFalse();
    }

    [Fact]
    public async Task ProvisionUserAsync_InvalidOrganization_ReturnsNotFoundError()
    {
        // Arrange
        var request = CreateRequest(organizationId: Guid.NewGuid());
        var service = CreateService();

        // Act
        var result = await service.ProvisionUserAsync(request);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorStatusCode.Should().Be(404);
        result.Error.Should().Contain("Organization not found");
    }

    [Fact]
    public async Task ProvisionUserAsync_InvalidPassword_ReturnsValidationErrors()
    {
        // Arrange
        SetupInvalidPassword("Password must be at least 12 characters", "Password too common");
        var request = CreateRequest(password: "weak");
        var service = CreateService();

        // Act
        var result = await service.ProvisionUserAsync(request);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorStatusCode.Should().Be(400);
        result.ValidationErrors.Should().NotBeNull();
        result.ValidationErrors!.Should().ContainKey("Password");
        result.ValidationErrors["Password"].Should().HaveCount(2);
    }

    [Fact]
    public async Task ProvisionUserAsync_DuplicateUserInOrg_ReturnsConflict()
    {
        // Arrange — first provision a user
        var service = CreateService();
        await service.ProvisionUserAsync(CreateRequest(email: "dupe@test.com"));

        // Act — try to provision same email in same org
        var result = await service.ProvisionUserAsync(CreateRequest(email: "dupe@test.com"));

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorStatusCode.Should().Be(409);
        result.Error.Should().Contain("already exists");
    }

    [Fact]
    public async Task ProvisionUserAsync_InvalidRole_ReturnsValidationError()
    {
        // Arrange
        var request = CreateRequest(role: "SuperAdmin");
        var service = CreateService();

        // Act
        var result = await service.ProvisionUserAsync(request);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorStatusCode.Should().Be(400);
        result.ValidationErrors.Should().NotBeNull();
        result.ValidationErrors!.Should().ContainKey("Role");
    }

    [Fact]
    public async Task ProvisionUserAsync_NoPassword_CreatesUserWithNullPasswordHash()
    {
        // Arrange
        var request = CreateRequest(password: null);
        var service = CreateService();

        // Act
        var result = await service.ProvisionUserAsync(request);

        // Assert
        result.Success.Should().BeTrue();

        var platformUser = await _dbContext.PlatformUsers.FindAsync(result.Response!.UserId);
        platformUser!.PasswordHash.Should().BeNull();
    }

    [Fact]
    public async Task ProvisionUserAsync_ValidRequest_CreatesAuditLogEntry()
    {
        // Arrange
        var request = CreateRequest();
        var service = CreateService();

        // Act
        var result = await service.ProvisionUserAsync(request);

        // Assert
        result.Success.Should().BeTrue();

        var audit = await _dbContext.AuditLogEntries
            .FirstOrDefaultAsync(a =>
                a.IdentityId == result.Response!.UserIdentityId &&
                a.OrganizationId == _testOrgId);
        audit.Should().NotBeNull();
        audit!.EventType.Should().Be(AuditEventType.UserAddedToOrganization);
        audit.Success.Should().BeTrue();
    }

    [Fact]
    public async Task ProvisionUserAsync_ValidRequest_ResponseContainsOrgName()
    {
        // Arrange
        var request = CreateRequest();
        var service = CreateService();

        // Act
        var result = await service.ProvisionUserAsync(request);

        // Assert
        result.Success.Should().BeTrue();
        result.Response!.OrganizationName.Should().Be("Test Organization");
        result.Response.OrganizationId.Should().Be(_testOrgId);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
