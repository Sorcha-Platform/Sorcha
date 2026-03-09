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
            ExternalIdpSubject = "ext_user_123",
            Email = "alice@example.com",
            DisplayName = "Alice Johnson",
            Roles = new[] { UserRole.Member },
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
            ExternalIdpSubject = "ext_user_456",
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
    public async Task GetUserByExternalIdAsync_ShouldReturnUser_WhenExists()
    {
        // Arrange
        var user = new UserIdentity
        {
            OrganizationId = Guid.NewGuid(),
            ExternalIdpSubject = "azure_entra_user_789",
            Email = "charlie@example.com",
            DisplayName = "Charlie Brown",
            Status = IdentityStatus.Active
        };
        await _repository.CreateUserAsync(user);

        // Act
        var result = await _repository.GetUserByExternalIdAsync("azure_entra_user_789");

        // Assert
        result.Should().NotBeNull();
        result!.ExternalIdpSubject.Should().Be("azure_entra_user_789");
        result.Email.Should().Be("charlie@example.com");
    }

    [Fact]
    public async Task GetUserByExternalIdAsync_ShouldReturnNull_WhenNotExists()
    {
        // Act
        var result = await _repository.GetUserByExternalIdAsync("nonexistent_user");

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
            ExternalIdpSubject = "ext_user_email_test",
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
            ExternalIdpSubject = "user1",
            Email = "user1@example.com",
            DisplayName = "User One",
            Status = IdentityStatus.Active
        });
        await _repository.CreateUserAsync(new UserIdentity
        {
            OrganizationId = orgId,
            ExternalIdpSubject = "user2",
            Email = "user2@example.com",
            DisplayName = "User Two",
            Status = IdentityStatus.Active
        });
        await _repository.CreateUserAsync(new UserIdentity
        {
            OrganizationId = orgId,
            ExternalIdpSubject = "user3",
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
            ExternalIdpSubject = "user_all_1",
            Email = "all1@example.com",
            DisplayName = "All User One",
            Status = IdentityStatus.Active
        });
        await _repository.CreateUserAsync(new UserIdentity
        {
            OrganizationId = orgId,
            ExternalIdpSubject = "user_all_2",
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
            ExternalIdpSubject = "update_test_user",
            Email = "original@example.com",
            DisplayName = "Original Name",
            Roles = new[] { UserRole.Member },
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
            ExternalIdpSubject = "deactivate_test",
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

    #region PublicIdentity Tests

    [Fact]
    public async Task CreatePublicIdentityAsync_ShouldAddPublicIdentityToDatabase()
    {
        // Arrange
        var publicIdentity = new PublicIdentity
        {
            PassKeyCredentialId = new byte[] { 1, 2, 3, 4, 5 },
            PublicKeyCose = new byte[] { 10, 20, 30, 40, 50 },
            SignatureCounter = 0,
            DeviceType = "YubiKey 5"
        };

        // Act
        var result = await _repository.CreatePublicIdentityAsync(publicIdentity);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().NotBe(Guid.Empty);
        result.PassKeyCredentialId.Should().BeEquivalentTo(new byte[] { 1, 2, 3, 4, 5 });

        // Verify in database
        var saved = await _repository.GetPublicIdentityByIdAsync(result.Id);
        saved.Should().NotBeNull();
    }

    [Fact]
    public async Task GetPublicIdentityByCredentialIdAsync_ShouldReturnIdentity_WhenExists()
    {
        // Arrange
        var credentialId = new byte[] { 11, 22, 33, 44, 55 };
        var publicIdentity = new PublicIdentity
        {
            PassKeyCredentialId = credentialId,
            PublicKeyCose = new byte[] { 100, 200 },
            DeviceType = "Windows Hello"
        };
        await _repository.CreatePublicIdentityAsync(publicIdentity);

        // Act
        var result = await _repository.GetPublicIdentityByCredentialIdAsync(credentialId);

        // Assert
        result.Should().NotBeNull();
        result!.PassKeyCredentialId.Should().BeEquivalentTo(credentialId);
    }

    [Fact]
    public async Task UpdatePublicIdentityAsync_ShouldUpdateSignatureCounter()
    {
        // Arrange
        var publicIdentity = new PublicIdentity
        {
            PassKeyCredentialId = new byte[] { 99, 88, 77 },
            PublicKeyCose = new byte[] { 1, 2 },
            SignatureCounter = 0
        };
        var created = await _repository.CreatePublicIdentityAsync(publicIdentity);

        // Act
        created.SignatureCounter = 5;
        created.LastUsedAt = DateTimeOffset.UtcNow;
        var updated = await _repository.UpdatePublicIdentityAsync(created);

        // Assert
        updated.SignatureCounter.Should().Be(5);
        updated.LastUsedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task DeletePublicIdentityAsync_ShouldRemovePublicIdentity()
    {
        // Arrange
        var publicIdentity = new PublicIdentity
        {
            PassKeyCredentialId = new byte[] { 111, 222 },
            PublicKeyCose = new byte[] { 1, 2, 3 }
        };
        var created = await _repository.CreatePublicIdentityAsync(publicIdentity);

        // Act
        await _repository.DeletePublicIdentityAsync(created.Id);

        // Assert
        var deleted = await _repository.GetPublicIdentityByIdAsync(created.Id);
        deleted.Should().BeNull();
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
