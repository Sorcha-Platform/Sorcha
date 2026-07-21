// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Models.Dtos;
using Sorcha.Tenant.Service.Services;
using Sorcha.Tenant.Service.Tests.Helpers;
using Xunit;

namespace Sorcha.Tenant.Service.Tests.Services;

public class OidcProvisioningServiceTests : IDisposable
{
    private readonly TenantDbContext _dbContext;
    private readonly Mock<ILogger<OidcProvisioningService>> _loggerMock;
    private readonly Guid _testOrgId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public OidcProvisioningServiceTests()
    {
        _dbContext = InMemoryDbContextFactory.Create();
        _loggerMock = new Mock<ILogger<OidcProvisioningService>>();

        // Seed test organization
        var org = new Organization
        {
            Id = _testOrgId,
            Name = "Test Organization",
            Subdomain = "testorg",
            Status = OrganizationStatus.Active,
            AllowedEmailDomains = [],
            CreatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.Organizations.Add(org);
        _dbContext.SaveChanges();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    private OidcProvisioningService CreateService() => new(_dbContext, _loggerMock.Object);

    #region ProvisionOrMatchUserAsync Tests

    [Fact]
    public async Task ProvisionOrMatchUserAsync_NewUser_CreatesUserWithMemberRoleAndOidcProvisioning()
    {
        // Arrange
        var service = CreateService();
        var claims = CreateClaims(sub: "oidc|12345", email: "alice@example.com", name: "Alice Smith");

        // Act
        var (user, isFirstLogin) = await service.ProvisionOrMatchUserAsync(_testOrgId, claims, CancellationToken.None);

        // Assert
        user.Should().NotBeNull();
        isFirstLogin.Should().BeTrue();
        user.Email.Should().Be("alice@example.com");
        user.DisplayName.Should().Be("Alice Smith");
        user.Roles.Should().ContainSingle().Which.Should().Be(UserRole.Consumer);
        user.ProvisionedVia.Should().Be(ProvisioningMethod.Oidc);
        user.OrganizationId.Should().Be(_testOrgId);
        user.Status.Should().Be(IdentityStatus.Active);
    }

    /// <summary>
    /// #1212 — the OIDC path matches and provisions purely by email (the IdP subject is not persisted),
    /// so an unverified email cannot be trusted as an identity key. A login whose IdP did not assert
    /// email_verified must be refused, not silently matched or provisioned.
    /// </summary>
    [Fact]
    public async Task ProvisionOrMatchUserAsync_UnverifiedEmail_IsRefused()
    {
        var service = CreateService();
        var claims = CreateClaims(sub: "oidc|unverified", email: "attacker@example.com", name: "Mallory",
            emailVerified: false);

        var act = () => service.ProvisionOrMatchUserAsync(_testOrgId, claims, CancellationToken.None);

        await act.Should().ThrowAsync<OidcEmailNotVerifiedException>();
    }

    [Fact]
    public async Task ProvisionOrMatchUserAsync_UnverifiedEmail_DoesNotMatchAnExistingUser()
    {
        // The takeover vector: an existing account must NOT be handed to a login carrying that email
        // unverified. Refuse before matching, and leave the existing user's LastLoginAt untouched.
        var service = CreateService();
        var existing = new UserIdentity
        {
            OrganizationId = _testOrgId,
            PlatformUserId = Guid.NewGuid(),
            Email = "victim@example.com",
            DisplayName = "Victim",
            Roles = [UserRole.Consumer],
            ProvisionedVia = ProvisioningMethod.Oidc,
            LastLoginAt = DateTimeOffset.UtcNow.AddDays(-30),
        };
        _dbContext.UserIdentities.Add(existing);
        await _dbContext.SaveChangesAsync();
        var originalLogin = existing.LastLoginAt;

        var claims = CreateClaims(sub: "oidc|attacker", email: "victim@example.com", name: "Not The Victim",
            emailVerified: false);

        var act = () => service.ProvisionOrMatchUserAsync(_testOrgId, claims, CancellationToken.None);

        await act.Should().ThrowAsync<OidcEmailNotVerifiedException>();
        existing.LastLoginAt.Should().Be(originalLogin, "an unverified login must not touch the existing account");
    }

    [Fact]
    public async Task ProvisionOrMatchUserAsync_ReturningUser_ReturnsExistingAndUpdatesLastLoginAt()
    {
        // Arrange
        var service = CreateService();
        var existingUser = new UserIdentity
        {
            OrganizationId = _testOrgId,
            PlatformUserId = Guid.NewGuid(),
            Email = "bob@example.com",
            DisplayName = "Bob Jones",
            Roles = [UserRole.Consumer],
            ProvisionedVia = ProvisioningMethod.Oidc,
            LastLoginAt = DateTimeOffset.UtcNow.AddDays(-7)
        };
        _dbContext.UserIdentities.Add(existingUser);
        await _dbContext.SaveChangesAsync();

        var claims = CreateClaims(sub: "oidc|existing", email: "bob@example.com", name: "Bob Jones");
        var beforeLogin = DateTimeOffset.UtcNow;

        // Act
        var (user, isFirstLogin) = await service.ProvisionOrMatchUserAsync(_testOrgId, claims, CancellationToken.None);

        // Assert
        user.Should().NotBeNull();
        isFirstLogin.Should().BeFalse();
        user.Id.Should().Be(existingUser.Id);
        user.LastLoginAt.Should().NotBeNull();
        user.LastLoginAt.Should().BeOnOrAfter(beforeLogin);
    }

    [Fact]
    public async Task ProvisionOrMatchUserAsync_EmailClaim_ExtractsEmailFromEmailClaim()
    {
        // Arrange
        var service = CreateService();
        var claims = CreateClaims(sub: "oidc|email-test", email: "primary@example.com", name: "Email Test");

        // Act
        var (user, _) = await service.ProvisionOrMatchUserAsync(_testOrgId, claims, CancellationToken.None);

        // Assert
        user.Email.Should().Be("primary@example.com");
    }

    [Fact]
    public async Task ProvisionOrMatchUserAsync_NoEmailClaim_FallsBackToPreferredUsername()
    {
        // Arrange
        var service = CreateService();
        var claims = CreateClaims(sub: "oidc|fallback", email: null, name: "Fallback User", preferredUsername: "fallback@example.com");

        // Act
        var (user, _) = await service.ProvisionOrMatchUserAsync(_testOrgId, claims, CancellationToken.None);

        // Assert
        user.Email.Should().Be("fallback@example.com");
    }

    [Fact]
    public async Task ProvisionOrMatchUserAsync_NoNameClaim_BuildsDisplayNameFromGivenAndFamilyName()
    {
        // Arrange
        var service = CreateService();
        var claims = CreateClaims(sub: "oidc|names", email: "names@example.com", name: null, givenName: "Jane", familyName: "Doe");

        // Act
        var (user, _) = await service.ProvisionOrMatchUserAsync(_testOrgId, claims, CancellationToken.None);

        // Assert
        user.DisplayName.Should().Be("Jane Doe");
    }

    #endregion

    #region CheckDomainRestrictionsAsync Tests

    [Fact]
    public async Task CheckDomainRestrictionsAsync_EmptyAllowedDomains_ReturnsTrue()
    {
        // Arrange
        var service = CreateService();
        // Organization seeded with AllowedEmailDomains = [] (no restrictions)

        // Act
        var result = await service.CheckDomainRestrictionsAsync(_testOrgId, "anyone@anydomain.com", CancellationToken.None);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task CheckDomainRestrictionsAsync_MatchingDomain_ReturnsTrue()
    {
        // Arrange
        var service = CreateService();
        var org = await _dbContext.Organizations.FindAsync(_testOrgId);
        org!.AllowedEmailDomains = ["acme.com", "example.com"];
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await service.CheckDomainRestrictionsAsync(_testOrgId, "alice@acme.com", CancellationToken.None);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task CheckDomainRestrictionsAsync_NonMatchingDomain_ReturnsFalse()
    {
        // Arrange
        var service = CreateService();
        var org = await _dbContext.Organizations.FindAsync(_testOrgId);
        org!.AllowedEmailDomains = ["acme.com"];
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await service.CheckDomainRestrictionsAsync(_testOrgId, "alice@other.com", CancellationToken.None);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region DetermineProfileCompletionAsync Tests

    [Fact]
    public async Task DetermineProfileCompletionAsync_MissingEmail_ReturnsTrue()
    {
        // Arrange
        var service = CreateService();
        var user = new UserIdentity
        {
            Email = string.Empty,
            DisplayName = "Has Name"
        };

        // Act
        var isIncomplete = await service.DetermineProfileCompletionAsync(user);

        // Assert
        isIncomplete.Should().BeTrue();
    }

    [Fact]
    public async Task DetermineProfileCompletionAsync_MissingDisplayName_ReturnsTrue()
    {
        // Arrange
        var service = CreateService();
        var user = new UserIdentity
        {
            Email = "has@email.com",
            DisplayName = string.Empty
        };

        // Act
        var isIncomplete = await service.DetermineProfileCompletionAsync(user);

        // Assert
        isIncomplete.Should().BeTrue();
    }

    [Fact]
    public async Task DetermineProfileCompletionAsync_CompleteProfile_ReturnsFalse()
    {
        // Arrange
        var service = CreateService();
        var user = new UserIdentity
        {
            Email = "complete@email.com",
            DisplayName = "Complete User"
        };

        // Act
        var isIncomplete = await service.DetermineProfileCompletionAsync(user);

        // Assert
        isIncomplete.Should().BeFalse();
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Creates an OidcUserClaims instance for testing with the specified claim values.
    /// </summary>
    private static OidcUserClaims CreateClaims(
        string sub,
        string? email,
        string? name,
        string? preferredUsername = null,
        string? upn = null,
        string? givenName = null,
        string? familyName = null,
        bool emailVerified = true)   // #1212: provisioning now requires a verified email; default the happy path to true
    {
        return new OidcUserClaims
        {
            Subject = sub,
            Email = email,
            DisplayName = name,
            PreferredUsername = preferredUsername,
            Upn = upn,
            GivenName = givenName,
            FamilyName = familyName,
            EmailVerified = emailVerified
        };
    }

    #endregion
}
