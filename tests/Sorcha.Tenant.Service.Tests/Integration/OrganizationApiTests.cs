// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Models.Dtos;
using Sorcha.Tenant.Service.Tests.Infrastructure;

namespace Sorcha.Tenant.Service.Tests.Integration;

/// <summary>
/// Integration tests for Organization API endpoints.
/// </summary>
public class OrganizationApiTests : IClassFixture<TenantServiceWebApplicationFactory>, IAsyncLifetime
{
    private readonly TenantServiceWebApplicationFactory _factory;
    private HttpClient _adminClient = null!;
    private HttpClient _memberClient = null!;
    private HttpClient _unauthenticatedClient = null!;

    public OrganizationApiTests(TenantServiceWebApplicationFactory factory)
    {
        _factory = factory;
    }

    public async ValueTask InitializeAsync()
    {
        _adminClient = _factory.CreateAdminClient();
        _memberClient = _factory.CreateMemberClient();
        _unauthenticatedClient = _factory.CreateUnauthenticatedClient();

        // Seed test data before each test class
        await _factory.SeedTestDataAsync();
    }

    public ValueTask DisposeAsync()
    {
        _adminClient?.Dispose();
        _memberClient?.Dispose();
        _unauthenticatedClient?.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task CreateOrganization_WithValidData_ReturnsCreated()
    {
        // Arrange
        var request = new CreateOrganizationRequest
        {
            Name = "Acme Corporation",
            Subdomain = "acme"
        };

        // Act
        var response = await _adminClient.PostAsJsonAsync("/api/organizations", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var org = await response.Content.ReadFromJsonAsync<OrganizationResponse>();
        org.Should().NotBeNull();
        org!.Name.Should().Be("Acme Corporation");
        org.Subdomain.Should().Be("acme");
        org.Status.Should().Be(OrganizationStatus.Active);
    }

    [Fact]
    public async Task CreateOrganization_WithDuplicateSubdomain_ReturnsConflict()
    {
        // Arrange - Create first organization
        var request1 = new CreateOrganizationRequest
        {
            Name = "First Org",
            Subdomain = "duplicate"
        };
        await _adminClient.PostAsJsonAsync("/api/organizations", request1);

        // Act - Try to create second with same subdomain
        var request2 = new CreateOrganizationRequest
        {
            Name = "Second Org",
            Subdomain = "duplicate"
        };
        var response = await _adminClient.PostAsJsonAsync("/api/organizations", request2);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task CreateOrganization_Unauthorized_ReturnsUnauthorized()
    {
        // Arrange
        var request = new CreateOrganizationRequest
        {
            Name = "Test Org",
            Subdomain = "test"
        };

        // Act
        var response = await _unauthenticatedClient.PostAsJsonAsync("/api/organizations", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetOrganization_WithValidId_ReturnsOrganization()
    {
        // Act
        var response = await _adminClient.GetAsync($"/api/organizations/{TestDataSeeder.TestOrganizationId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var org = await response.Content.ReadFromJsonAsync<OrganizationResponse>();
        org.Should().NotBeNull();
        org!.Id.Should().Be(TestDataSeeder.TestOrganizationId);
        org.Name.Should().Be("Test Organization");
    }

    [Fact]
    public async Task GetOrganization_WithInvalidId_ReturnsNotFound()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();

        // Act
        var response = await _adminClient.GetAsync($"/api/organizations/{nonExistentId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetOrganizationBySubdomain_WithValidSubdomain_ReturnsOrganization()
    {
        // Act
        var response = await _adminClient.GetAsync("/api/organizations/by-subdomain/test-org");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var org = await response.Content.ReadFromJsonAsync<OrganizationResponse>();
        org.Should().NotBeNull();
        org!.Subdomain.Should().Be("test-org");
    }

    [Fact]
    public async Task ListOrganizations_ReturnsOrganizations()
    {
        // Act
        var response = await _adminClient.GetAsync("/api/organizations");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<OrganizationListResponse>();
        result.Should().NotBeNull();
        result!.Organizations.Should().NotBeEmpty();
        result.Organizations.Should().Contain(o => o.Name == "Test Organization");
    }

    [Fact]
    public async Task ListOrganizations_WithPagination_ReturnsPagedResults()
    {
        // Arrange - Create multiple organizations
        for (int i = 0; i < 5; i++)
        {
            await _adminClient.PostAsJsonAsync("/api/organizations", new CreateOrganizationRequest
            {
                Name = $"Organization {i}",
                Subdomain = $"org{i}"
            });
        }

        // Act
        var response = await _adminClient.GetAsync("/api/organizations?pageSize=3&pageNumber=1");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<OrganizationListResponse>();
        result.Should().NotBeNull();
        result!.Organizations.Count.Should().BeLessThanOrEqualTo(3);
    }

    [Fact]
    public async Task UpdateOrganization_WithValidData_ReturnsUpdated()
    {
        // Arrange
        var request = new UpdateOrganizationRequest
        {
            Name = "Updated Test Organization",
            Status = OrganizationStatus.Active
        };

        // Act
        var response = await _adminClient.PutAsJsonAsync(
            $"/api/organizations/{TestDataSeeder.TestOrganizationId}",
            request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var org = await response.Content.ReadFromJsonAsync<OrganizationResponse>();
        org.Should().NotBeNull();
        org!.Name.Should().Be("Updated Test Organization");
    }

    [Fact]
    public async Task DeleteOrganization_WithValidId_ReturnsNoContent()
    {
        // Arrange - Create organization to delete
        var createResponse = await _adminClient.PostAsJsonAsync("/api/organizations", new CreateOrganizationRequest
        {
            Name = "To Delete",
            Subdomain = "todelete"
        });
        var created = await createResponse.Content.ReadFromJsonAsync<OrganizationResponse>();

        // Act
        var response = await _adminClient.DeleteAsync($"/api/organizations/{created!.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify soft deletion — org still exists but is now Inactive
        var getResponse = await _adminClient.GetAsync($"/api/organizations/{created.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var deactivated = await getResponse.Content.ReadFromJsonAsync<OrganizationResponse>();
        deactivated.Should().NotBeNull();
        deactivated!.Status.Should().Be(OrganizationStatus.Deleted);
    }

    [Fact]
    public async Task AddUserToOrganization_WithValidData_ReturnsCreated()
    {
        // Arrange
        var request = new AddUserToOrganizationRequest
        {
            Email = "newuser@test-org.sorcha.io",
            DisplayName = "New User",
            ExternalIdpUserId = "external-123",
            Roles = new[] { UserRole.Member }
        };

        // Act
        var response = await _adminClient.PostAsJsonAsync(
            $"/api/organizations/{TestDataSeeder.TestOrganizationId}/users",
            request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var user = await response.Content.ReadFromJsonAsync<UserResponse>();
        user.Should().NotBeNull();
        user!.Email.Should().Be("newuser@test-org.sorcha.io");
    }

    [Fact]
    public async Task ListOrganizationUsers_ReturnsUsers()
    {
        // Act
        var response = await _adminClient.GetAsync(
            $"/api/organizations/{TestDataSeeder.TestOrganizationId}/users");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<UserListResponse>();
        result.Should().NotBeNull();
        result!.Users.Should().NotBeEmpty();
        result.Users.Should().Contain(u => u.Email == "admin@test-org.sorcha.io");
    }

    [Fact]
    public async Task RemoveUserFromOrganization_WithValidId_ReturnsNoContent()
    {
        // Arrange - Add a user first
        var addResponse = await _adminClient.PostAsJsonAsync(
            $"/api/organizations/{TestDataSeeder.TestOrganizationId}/users",
            new AddUserToOrganizationRequest
            {
                Email = "toremove@test-org.sorcha.io",
                DisplayName = "To Remove",
                ExternalIdpUserId = "external-456",
                Roles = new[] { UserRole.Member }
            });
        var addedUser = await addResponse.Content.ReadFromJsonAsync<UserResponse>();

        // Act
        var response = await _adminClient.DeleteAsync(
            $"/api/organizations/{TestDataSeeder.TestOrganizationId}/users/{addedUser!.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task UpdateUserRole_WithValidData_ReturnsUpdated()
    {
        // Arrange
        var request = new UpdateUserRequest
        {
            Roles = new[] { UserRole.Administrator }
        };

        // Act
        var response = await _adminClient.PutAsJsonAsync(
            $"/api/organizations/{TestDataSeeder.TestOrganizationId}/users/{TestDataSeeder.MemberUserId}",
            request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var user = await response.Content.ReadFromJsonAsync<UserResponse>();
        user.Should().NotBeNull();
        user!.Roles.Should().Contain(UserRole.Administrator);
    }
}
