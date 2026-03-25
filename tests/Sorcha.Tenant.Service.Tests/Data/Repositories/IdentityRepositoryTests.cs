// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Data.Repositories;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Tests.Helpers;
using Xunit;

namespace Sorcha.Tenant.Service.Tests.Data.Repositories;

public class IdentityRepositoryTests : IDisposable
{
    private readonly TenantDbContext _context;
    private readonly IIdentityRepository _repository;

    public IdentityRepositoryTests()
    {
        _context = InMemoryDbContextFactory.Create();
        _repository = new IdentityRepository(_context);
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    #region UserIdentity Tests

    [Fact]
    public async Task CreateUserAsync_ShouldAddUserToDatabase()
    {
        // Arrange
        var user = new UserIdentity
        {
            OrganizationId = Guid.NewGuid(),
            PlatformUserId = Guid.NewGuid(),
            Email = "alice@example.com",
            DisplayName = "Alice Johnson",
            Roles = new[] { UserRole.Consumer },
            Status = IdentityStatus.Active
        };

        // Act
        var result = await _repository.CreateUserAsync(user);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().NotBe(Guid.Empty);
        result.Email.Should().Be("alice@example.com");
        result.DisplayName.Should().Be("Alice Johnson");

        // Verify in database
        var saved = await _repository.GetUserByIdAsync(result.Id);
        saved.Should().NotBeNull();
        saved!.Email.Should().Be("alice@example.com");
    }

    [Fact]
    public async Task GetUserByIdAsync_ShouldReturnUser_WhenExists()
    {
        // Arrange
        var user = new UserIdentity
        {
            OrganizationId = Guid.NewGuid(),
            PlatformUserId = Guid.NewGuid(),
            Email = "bob@example.com",
            DisplayName = "Bob Smith",
            Status = IdentityStatus.Active
        };
        var created = await _repository.CreateUserAsync(user);

        // Act
        var result = await _repository.GetUserByIdAsync(created.Id);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(created.Id);
        result.Email.Should().Be("bob@example.com");
    }

    [Fact]
    public async Task GetUserByIdAsync_ShouldReturnNull_WhenNotExists()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();

        // Act
        var result = await _repository.GetUserByIdAsync(nonExistentId);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetUserByEmailAsync_ShouldReturnUser_WhenExists()
    {
        // Arrange
        var user = new UserIdentity
        {
            OrganizationId = Guid.NewGuid(),
            PlatformUserId = Guid.NewGuid(),
            Email = "diana@example.com",
            DisplayName = "Diana Prince",
            Status = IdentityStatus.Active
        };
        await _repository.CreateUserAsync(user);

        // Act
        var result = await _repository.GetUserByEmailAsync("diana@example.com");

        // Assert
        result.Should().NotBeNull();
        result!.Email.Should().Be("diana@example.com");
    }

    [Fact]
    public async Task GetUserByEmailAsync_ShouldReturnNull_WhenNotExists()
    {
        // Act
        var result = await _repository.GetUserByEmailAsync("nonexistent@example.com");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetActiveUsersAsync_ShouldReturnOnlyActiveUsers()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        await _repository.CreateUserAsync(new UserIdentity
        {
            OrganizationId = orgId,
            PlatformUserId = Guid.NewGuid(),
            Email = "user1@example.com",
            DisplayName = "User One",
            Status = IdentityStatus.Active
        });
        await _repository.CreateUserAsync(new UserIdentity
        {
            OrganizationId = orgId,
            PlatformUserId = Guid.NewGuid(),
            Email = "user2@example.com",
            DisplayName = "User Two",
            Status = IdentityStatus.Active
        });
        await _repository.CreateUserAsync(new UserIdentity
        {
            OrganizationId = orgId,
            PlatformUserId = Guid.NewGuid(),
            Email = "user3@example.com",
            DisplayName = "User Three",
            Status = IdentityStatus.Suspended
        });

        // Act
        var result = await _repository.GetActiveUsersAsync(orgId);

        // Assert
        result.Should().HaveCount(2);
        result.Should().AllSatisfy(u => u.Status.Should().Be(IdentityStatus.Active));
    }

    [Fact]
    public async Task GetAllUsersAsync_ShouldReturnAllUsers()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        await _repository.CreateUserAsync(new UserIdentity
        {
            OrganizationId = orgId,
            PlatformUserId = Guid.NewGuid(),
            Email = "all1@example.com",
            DisplayName = "All User One",
            Status = IdentityStatus.Active
        });
        await _repository.CreateUserAsync(new UserIdentity
        {
            OrganizationId = orgId,
            PlatformUserId = Guid.NewGuid(),
            Email = "all2@example.com",
            DisplayName = "All User Two",
            Status = IdentityStatus.Suspended
        });

        // Act
        var result = await _repository.GetAllUsersAsync(orgId);

        // Assert
        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task UpdateUserAsync_ShouldUpdateUserProperties()
    {
        // Arrange
        var user = new UserIdentity
        {
            OrganizationId = Guid.NewGuid(),
            PlatformUserId = Guid.NewGuid(),
            Email = "original@example.com",
            DisplayName = "Original Name",
            Roles = new[] { UserRole.Consumer },
            Status = IdentityStatus.Active
        };
        var created = await _repository.CreateUserAsync(user);

        // Act
        created.DisplayName = "Updated Name";
        created.Roles = new[] { UserRole.Administrator };
        created.LastLoginAt = DateTimeOffset.UtcNow;
        var updated = await _repository.UpdateUserAsync(created);

        // Assert
        updated.DisplayName.Should().Be("Updated Name");
        updated.Roles.Should().Contain(UserRole.Administrator);
        updated.LastLoginAt.Should().NotBeNull();

        // Verify in database
        var fetched = await _repository.GetUserByIdAsync(created.Id);
        fetched!.DisplayName.Should().Be("Updated Name");
    }

    [Fact]
    public async Task DeactivateUserAsync_ShouldSetStatusToInactive()
    {
        // Arrange
        var user = new UserIdentity
        {
            OrganizationId = Guid.NewGuid(),
            PlatformUserId = Guid.NewGuid(),
            Email = "deactivate@example.com",
            DisplayName = "Deactivate User",
            Status = IdentityStatus.Active
        };
        var created = await _repository.CreateUserAsync(user);

        // Act
        await _repository.DeactivateUserAsync(created.Id);

        // Assert
        var deactivated = await _repository.GetUserByIdAsync(created.Id);
        deactivated.Should().NotBeNull();
        deactivated!.Status.Should().Be(IdentityStatus.Suspended);
    }

    #endregion

    #region ServicePrincipal Tests

    [Fact]
    public async Task CreateServicePrincipalAsync_ShouldAddServicePrincipalToDatabase()
    {
        // Arrange
        var servicePrincipal = new ServicePrincipal
        {
            ServiceName = "Blueprint Service",
            ClientId = "blueprint_svc_client_123",
            ClientSecretEncrypted = new byte[] { 5, 10, 15, 20 },
            Scopes = new[] { "blueprint.read", "blueprint.write" },
            Status = ServicePrincipalStatus.Active
        };

        // Act
        var result = await _repository.CreateServicePrincipalAsync(servicePrincipal);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().NotBe(Guid.Empty);
        result.ServiceName.Should().Be("Blueprint Service");

        // Verify in database
        var saved = await _repository.GetServicePrincipalByIdAsync(result.Id);
        saved.Should().NotBeNull();
    }

    [Fact]
    public async Task GetServicePrincipalByClientIdAsync_ShouldReturnServicePrincipal_WhenExists()
    {
        // Arrange
        var servicePrincipal = new ServicePrincipal
        {
            ServiceName = "Wallet Service",
            ClientId = "wallet_svc_client_456",
            ClientSecretEncrypted = new byte[] { 1, 2 },
            Scopes = new[] { "wallet.read", "wallet.write" },
            Status = ServicePrincipalStatus.Active
        };
        await _repository.CreateServicePrincipalAsync(servicePrincipal);

        // Act
        var result = await _repository.GetServicePrincipalByClientIdAsync("wallet_svc_client_456");

        // Assert
        result.Should().NotBeNull();
        result!.ClientId.Should().Be("wallet_svc_client_456");
    }

    [Fact]
    public async Task GetServicePrincipalByNameAsync_ShouldReturnServicePrincipal_WhenExists()
    {
        // Arrange
        var servicePrincipal = new ServicePrincipal
        {
            ServiceName = "Register Service",
            ClientId = "register_svc_client_789",
            ClientSecretEncrypted = new byte[] { 3, 4 },
            Scopes = new[] { "register.read" },
            Status = ServicePrincipalStatus.Active
        };
        await _repository.CreateServicePrincipalAsync(servicePrincipal);

        // Act
        var result = await _repository.GetServicePrincipalByNameAsync("Register Service");

        // Assert
        result.Should().NotBeNull();
        result!.ServiceName.Should().Be("Register Service");
    }

    [Fact]
    public async Task GetActiveServicePrincipalsAsync_ShouldReturnOnlyActiveServicePrincipals()
    {
        // Arrange
        await _repository.CreateServicePrincipalAsync(new ServicePrincipal
        {
            ServiceName = "Active Service 1",
            ClientId = "active1",
            ClientSecretEncrypted = new byte[] { 1 },
            Scopes = new[] { "scope1" },
            Status = ServicePrincipalStatus.Active
        });
        await _repository.CreateServicePrincipalAsync(new ServicePrincipal
        {
            ServiceName = "Active Service 2",
            ClientId = "active2",
            ClientSecretEncrypted = new byte[] { 2 },
            Scopes = new[] { "scope2" },
            Status = ServicePrincipalStatus.Active
        });
        await _repository.CreateServicePrincipalAsync(new ServicePrincipal
        {
            ServiceName = "Revoked Service",
            ClientId = "revoked1",
            ClientSecretEncrypted = new byte[] { 3 },
            Scopes = new[] { "scope3" },
            Status = ServicePrincipalStatus.Revoked
        });

        // Act
        var result = await _repository.GetActiveServicePrincipalsAsync();

        // Assert
        result.Should().HaveCount(2);
        result.Should().AllSatisfy(sp => sp.Status.Should().Be(ServicePrincipalStatus.Active));
    }

    [Fact]
    public async Task UpdateServicePrincipalAsync_ShouldUpdateServicePrincipalProperties()
    {
        // Arrange
        var servicePrincipal = new ServicePrincipal
        {
            ServiceName = "Update Test Service",
            ClientId = "update_test_client",
            ClientSecretEncrypted = new byte[] { 1, 2, 3 },
            Scopes = new[] { "original.scope" },
            Status = ServicePrincipalStatus.Active
        };
        var created = await _repository.CreateServicePrincipalAsync(servicePrincipal);

        // Act
        created.Scopes = new[] { "updated.scope", "new.scope" };
        created.Status = ServicePrincipalStatus.Active;
        var updated = await _repository.UpdateServicePrincipalAsync(created);

        // Assert
        updated.Scopes.Should().HaveCount(2);
        updated.Scopes.Should().Contain("updated.scope");

        // Verify in database
        var fetched = await _repository.GetServicePrincipalByIdAsync(created.Id);
        fetched!.Scopes.Should().Contain("new.scope");
    }

    [Fact]
    public async Task DeactivateServicePrincipalAsync_ShouldSetStatusToRevoked()
    {
        // Arrange
        var servicePrincipal = new ServicePrincipal
        {
            ServiceName = "Deactivate Test Service",
            ClientId = "deactivate_test_sp",
            ClientSecretEncrypted = new byte[] { 99 },
            Scopes = new[] { "test.scope" },
            Status = ServicePrincipalStatus.Active
        };
        var created = await _repository.CreateServicePrincipalAsync(servicePrincipal);

        // Act
        await _repository.DeactivateServicePrincipalAsync(created.Id);

        // Assert
        var deactivated = await _repository.GetServicePrincipalByIdAsync(created.Id);
        deactivated.Should().NotBeNull();
        deactivated!.Status.Should().Be(ServicePrincipalStatus.Revoked);
    }

    #endregion
}
