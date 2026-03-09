// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Data.Repositories;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Tests.Helpers;
using Xunit;

namespace Sorcha.Tenant.Service.Tests.Data.Repositories;

public class OrganizationRepositoryTests : IDisposable
{
    private readonly TenantDbContext _context;
    private readonly IOrganizationRepository _repository;

    public OrganizationRepositoryTests()
    {
        _context = InMemoryDbContextFactory.Create();
        _repository = new OrganizationRepository(_context);
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    [Fact]
    public async Task CreateAsync_ShouldAddOrganizationToDatabase()
    {
        // Arrange
        var organization = new Organization
        {
            Name = "Acme Corporation",
            Subdomain = "acme",
            Status = OrganizationStatus.Active
        };

        // Act
        var result = await _repository.CreateAsync(organization);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().NotBeEmpty();
        result.Name.Should().Be("Acme Corporation");
        result.Subdomain.Should().Be("acme");

        // Verify it's in the database
        var saved = await _repository.GetByIdAsync(result.Id);
        saved.Should().NotBeNull();
        saved!.Name.Should().Be("Acme Corporation");
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReturnOrganization_WhenExists()
    {
        // Arrange
        var organization = new Organization
        {
            Name = "Test Org",
            Subdomain = "testorg",
            Status = OrganizationStatus.Active
        };
        var created = await _repository.CreateAsync(organization);

        // Act
        var result = await _repository.GetByIdAsync(created.Id);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(created.Id);
        result.Name.Should().Be("Test Org");
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReturnNull_WhenNotExists()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();

        // Act
        var result = await _repository.GetByIdAsync(nonExistentId);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetBySubdomainAsync_ShouldReturnOrganization_WhenExists()
    {
        // Arrange
        var organization = new Organization
        {
            Name = "Subdomain Test",
            Subdomain = "subtest",
            Status = OrganizationStatus.Active
        };
        await _repository.CreateAsync(organization);

        // Act
        var result = await _repository.GetBySubdomainAsync("subtest");

        // Assert
        result.Should().NotBeNull();
        result!.Subdomain.Should().Be("subtest");
        result.Name.Should().Be("Subdomain Test");
    }

    [Fact]
    public async Task GetBySubdomainAsync_ShouldReturnNull_WhenNotExists()
    {
        // Act
        var result = await _repository.GetBySubdomainAsync("nonexistent");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAllActiveAsync_ShouldReturnOnlyActiveOrganizations()
    {
        // Arrange
        await _repository.CreateAsync(new Organization { Name = "Active 1", Subdomain = "active1", Status = OrganizationStatus.Active });
        await _repository.CreateAsync(new Organization { Name = "Active 2", Subdomain = "active2", Status = OrganizationStatus.Active });
        await _repository.CreateAsync(new Organization { Name = "Suspended", Subdomain = "suspended", Status = OrganizationStatus.Suspended });
        await _repository.CreateAsync(new Organization { Name = "Deleted", Subdomain = "deleted", Status = OrganizationStatus.Deleted });

        // Act
        var result = await _repository.GetAllActiveAsync();

        // Assert
        result.Should().HaveCount(2);
        result.Should().AllSatisfy(org => org.Status.Should().Be(OrganizationStatus.Active));
    }

    [Fact]
    public async Task GetAllAsync_ShouldReturnAllOrganizations()
    {
        // Arrange
        await _repository.CreateAsync(new Organization { Name = "Org 1", Subdomain = "org1", Status = OrganizationStatus.Active });
        await _repository.CreateAsync(new Organization { Name = "Org 2", Subdomain = "org2", Status = OrganizationStatus.Suspended });
        await _repository.CreateAsync(new Organization { Name = "Org 3", Subdomain = "org3", Status = OrganizationStatus.Deleted });

        // Act
        var result = await _repository.GetAllAsync();

        // Assert
        result.Should().HaveCount(3);
    }

    [Fact]
    public async Task UpdateAsync_ShouldUpdateOrganizationProperties()
    {
        // Arrange
        var organization = new Organization
        {
            Name = "Original Name",
            Subdomain = "original",
            Status = OrganizationStatus.Active
        };
        var created = await _repository.CreateAsync(organization);

        // Act
        created.Name = "Updated Name";
        created.Status = OrganizationStatus.Suspended;
        var updated = await _repository.UpdateAsync(created);

        // Assert
        updated.Name.Should().Be("Updated Name");
        updated.Status.Should().Be(OrganizationStatus.Suspended);

        // Verify in database
        var fetched = await _repository.GetByIdAsync(created.Id);
        fetched!.Name.Should().Be("Updated Name");
        fetched.Status.Should().Be(OrganizationStatus.Suspended);
    }

    [Fact]
    public async Task DeleteAsync_ShouldSetStatusToDeleted()
    {
        // Arrange
        var organization = new Organization
        {
            Name = "To Be Deleted",
            Subdomain = "tobedeleted",
            Status = OrganizationStatus.Active
        };
        var created = await _repository.CreateAsync(organization);

        // Act
        await _repository.DeleteAsync(created.Id);

        // Assert
        var deleted = await _repository.GetByIdAsync(created.Id);
        deleted.Should().NotBeNull();
        deleted!.Status.Should().Be(OrganizationStatus.Deleted);
    }

    [Fact]
    public async Task SubdomainExistsAsync_ShouldReturnTrue_WhenSubdomainExists()
    {
        // Arrange
        await _repository.CreateAsync(new Organization
        {
            Name = "Existing Org",
            Subdomain = "existing",
            Status = OrganizationStatus.Active
        });

        // Act
        var exists = await _repository.SubdomainExistsAsync("existing");

        // Assert
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task SubdomainExistsAsync_ShouldReturnFalse_WhenSubdomainDoesNotExist()
    {
        // Act
        var exists = await _repository.SubdomainExistsAsync("nonexistent");

        // Assert
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task CreateAsync_ShouldIncludeIdentityProviderNavigation()
    {
        // Arrange
        var organization = new Organization
        {
            Name = "Org With IDP",
            Subdomain = "orgidp",
            Status = OrganizationStatus.Active,
            IdentityProvider = new IdentityProviderConfiguration
            {
                ProviderPreset = IdentityProviderType.MicrosoftEntra,
                IssuerUrl = "https://login.microsoftonline.com/tenant-id/v2.0",
                ClientId = "client-id",
                ClientSecretEncrypted = new byte[] { 1, 2, 3 },
                Scopes = new[] { "openid", "profile", "email" }
            }
        };

        // Act
        var created = await _repository.CreateAsync(organization);

        // Assert
        var fetched = await _repository.GetByIdAsync(created.Id);
        fetched.Should().NotBeNull();
        fetched!.IdentityProvider.Should().NotBeNull();
        fetched.IdentityProvider!.ProviderPreset.Should().Be(IdentityProviderType.MicrosoftEntra);
    }
}
