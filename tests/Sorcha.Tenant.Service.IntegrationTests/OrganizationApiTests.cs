// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Polly;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.IntegrationTests.Fixtures;
using Sorcha.Tenant.Service.Models.Dtos;
using StackExchange.Redis;

namespace Sorcha.Tenant.Service.IntegrationTests;

/// <summary>
/// Integration tests for Organization API endpoints.
/// </summary>
public class OrganizationApiTests : IClassFixture<TenantServiceWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly HttpClient _adminClient;
    private readonly TenantServiceWebApplicationFactory _factory;

    public OrganizationApiTests(TenantServiceWebApplicationFactory factory)
    {
        _factory = factory;
        _client = _factory.CreateAuthenticatedClient();
        _adminClient = _factory.CreateAdminClient();
    }

    #region Create Organization Tests

    [Fact]
    public async Task CreateOrganization_ShouldReturnCreated_WhenValidRequest()
    {
        // Arrange
        var request = new CreateOrganizationRequest
        {
            Name = "Acme Corporation",
            Subdomain = $"acme-{Guid.NewGuid():N}".Substring(0, 20)
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/organizations", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var result = await response.Content.ReadFromJsonAsync<OrganizationResponse>();
        result.Should().NotBeNull();
        result!.Name.Should().Be("Acme Corporation");
        result.Subdomain.Should().StartWith("acme-");
        result.Id.Should().NotBeEmpty();
        result.Status.Should().Be(Models.OrganizationStatus.Active);
    }

    [Fact]
    public async Task CreateOrganization_ShouldReturnBadRequest_WhenSubdomainTooShort()
    {
        // Arrange
        var request = new CreateOrganizationRequest
        {
            Name = "Short Sub Corp",
            Subdomain = "ab" // Too short, must be at least 3 chars
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/organizations", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateOrganization_ShouldReturnBadRequest_WhenSubdomainInvalid()
    {
        // Arrange
        var request = new CreateOrganizationRequest
        {
            Name = "Invalid Corp",
            Subdomain = "invalid_subdomain!" // Contains underscore and exclamation
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/organizations", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateOrganization_ShouldReturnBadRequest_WhenSubdomainReserved()
    {
        // Arrange
        var request = new CreateOrganizationRequest
        {
            Name = "Admin Corp",
            Subdomain = "admin" // Reserved subdomain
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/organizations", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateOrganization_WithBranding_ShouldIncludeBrandingInResponse()
    {
        // Arrange
        var request = new CreateOrganizationRequest
        {
            Name = "Branded Corp",
            Subdomain = $"branded-{Guid.NewGuid():N}".Substring(0, 20),
            Branding = new BrandingConfigurationDto
            {
                LogoUrl = "https://example.com/logo.png",
                PrimaryColor = "#FF5733",
                SecondaryColor = "#33FF57",
                CompanyTagline = "Innovation First"
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/organizations", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var result = await response.Content.ReadFromJsonAsync<OrganizationResponse>();
        result!.Branding.Should().NotBeNull();
        result.Branding!.PrimaryColor.Should().Be("#FF5733");
        result.Branding.CompanyTagline.Should().Be("Innovation First");
    }

    [Fact]
    public async Task CreateOrganization_ShouldReturnUnauthorized_WhenNotAuthenticated()
    {
        // Arrange
        var unauthClient = _factory.CreateClient();
        var request = new CreateOrganizationRequest
        {
            Name = "No Auth Corp",
            Subdomain = "noauth"
        };

        // Act
        var response = await unauthClient.PostAsJsonAsync("/api/organizations", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region Get Organization Tests

    [Fact]
    public async Task GetOrganization_ShouldReturnOrganization_WhenExists()
    {
        // Arrange - Create organization first
        var createRequest = new CreateOrganizationRequest
        {
            Name = "Get Test Corp",
            Subdomain = $"gettest-{Guid.NewGuid():N}".Substring(0, 20)
        };
        var createResponse = await _client.PostAsJsonAsync("/api/organizations", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<OrganizationResponse>();

        // Act
        var response = await _client.GetAsync($"/api/organizations/{created!.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<OrganizationResponse>();
        result.Should().NotBeNull();
        result!.Id.Should().Be(created.Id);
        result.Name.Should().Be("Get Test Corp");
    }

    [Fact]
    public async Task GetOrganization_ShouldReturnNotFound_WhenDoesNotExist()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();

        // Act
        var response = await _client.GetAsync($"/api/organizations/{nonExistentId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetOrganizationBySubdomain_ShouldReturnOrganization_WhenExists()
    {
        // Arrange
        var subdomain = $"subtest-{Guid.NewGuid():N}".Substring(0, 20);
        var createRequest = new CreateOrganizationRequest
        {
            Name = "Subdomain Test Corp",
            Subdomain = subdomain
        };
        await _client.PostAsJsonAsync("/api/organizations", createRequest);

        // Act - This endpoint allows anonymous
        var unauthClient = _factory.CreateClient();
        var response = await unauthClient.GetAsync($"/api/organizations/by-subdomain/{subdomain}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<OrganizationResponse>();
        result.Should().NotBeNull();
        result!.Subdomain.Should().Be(subdomain);
    }

    [Fact]
    public async Task GetOrganizationBySubdomain_ShouldReturnNotFound_WhenDoesNotExist()
    {
        // Act
        var response = await _client.GetAsync("/api/organizations/by-subdomain/nonexistent-subdomain");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region Validate Subdomain Tests

    [Fact]
    public async Task ValidateSubdomain_ShouldReturnValid_WhenSubdomainAvailable()
    {
        // Arrange
        var subdomain = $"available-{Guid.NewGuid():N}".Substring(0, 20);

        // Act - This endpoint allows anonymous
        var unauthClient = _factory.CreateClient();
        var response = await unauthClient.GetAsync($"/api/organizations/validate-subdomain/{subdomain}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ValidateSubdomain_ShouldReturnBadRequest_WhenSubdomainTaken()
    {
        // Arrange - Create organization with subdomain first
        var subdomain = $"taken-{Guid.NewGuid():N}".Substring(0, 20);
        var createRequest = new CreateOrganizationRequest
        {
            Name = "Taken Corp",
            Subdomain = subdomain
        };
        await _client.PostAsJsonAsync("/api/organizations", createRequest);

        // Act
        var unauthClient = _factory.CreateClient();
        var response = await unauthClient.GetAsync($"/api/organizations/validate-subdomain/{subdomain}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ValidateSubdomain_ShouldReturnBadRequest_WhenReserved()
    {
        // Act
        var unauthClient = _factory.CreateClient();
        var response = await unauthClient.GetAsync("/api/organizations/validate-subdomain/www");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region Update Organization Tests

    [Fact]
    public async Task UpdateOrganization_ShouldUpdateName_WhenAdmin()
    {
        // Arrange - Create organization first
        var createRequest = new CreateOrganizationRequest
        {
            Name = "Original Name",
            Subdomain = $"update-{Guid.NewGuid():N}".Substring(0, 20)
        };
        var createResponse = await _client.PostAsJsonAsync("/api/organizations", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<OrganizationResponse>();

        var updateRequest = new UpdateOrganizationRequest
        {
            Name = "Updated Name"
        };

        // Act - Use admin client
        var response = await _adminClient.PutAsJsonAsync($"/api/organizations/{created!.Id}", updateRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<OrganizationResponse>();
        result!.Name.Should().Be("Updated Name");
        result.Id.Should().Be(created.Id);
    }

    [Fact]
    public async Task UpdateOrganization_ShouldReturnNotFound_WhenDoesNotExist()
    {
        // Arrange
        var updateRequest = new UpdateOrganizationRequest
        {
            Name = "New Name"
        };

        // Act
        var response = await _adminClient.PutAsJsonAsync($"/api/organizations/{Guid.NewGuid()}", updateRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UpdateOrganization_ShouldReturnForbidden_WhenNotAdmin()
    {
        // Arrange
        var createRequest = new CreateOrganizationRequest
        {
            Name = "Forbidden Update Corp",
            Subdomain = $"forbid-{Guid.NewGuid():N}".Substring(0, 20)
        };
        var createResponse = await _client.PostAsJsonAsync("/api/organizations", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<OrganizationResponse>();

        var updateRequest = new UpdateOrganizationRequest
        {
            Name = "Should Not Update"
        };

        // Act - Use non-admin client
        var response = await _client.PutAsJsonAsync($"/api/organizations/{created!.Id}", updateRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    #endregion

    #region Delete Organization Tests

    [Fact]
    public async Task DeleteOrganization_ShouldReturnNoContent_WhenAdmin()
    {
        // Arrange
        var createRequest = new CreateOrganizationRequest
        {
            Name = "To Delete Corp",
            Subdomain = $"delete-{Guid.NewGuid():N}".Substring(0, 20)
        };
        var createResponse = await _client.PostAsJsonAsync("/api/organizations", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<OrganizationResponse>();

        // Act
        var response = await _adminClient.DeleteAsync($"/api/organizations/{created!.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify it's deactivated (soft delete)
        var getResponse = await _client.GetAsync($"/api/organizations/{created.Id}");
        var org = await getResponse.Content.ReadFromJsonAsync<OrganizationResponse>();
        org!.Status.Should().Be(Models.OrganizationStatus.Deleted);
    }

    [Fact]
    public async Task DeleteOrganization_ShouldReturnNotFound_WhenDoesNotExist()
    {
        // Act
        var response = await _adminClient.DeleteAsync($"/api/organizations/{Guid.NewGuid()}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteOrganization_ShouldReturnForbidden_WhenNotAdmin()
    {
        // Arrange
        var createRequest = new CreateOrganizationRequest
        {
            Name = "Forbidden Delete Corp",
            Subdomain = $"forbidd-{Guid.NewGuid():N}".Substring(0, 20)
        };
        var createResponse = await _client.PostAsJsonAsync("/api/organizations", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<OrganizationResponse>();

        // Act - Use non-admin client
        var response = await _client.DeleteAsync($"/api/organizations/{created!.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    #endregion

    #region List Organizations Tests

    [Fact]
    public async Task ListOrganizations_ShouldReturnList_WhenAdmin()
    {
        // Arrange - Create some organizations
        await _client.PostAsJsonAsync("/api/organizations", new CreateOrganizationRequest
        {
            Name = "List Test Corp 1",
            Subdomain = $"list1-{Guid.NewGuid():N}".Substring(0, 20)
        });
        await _client.PostAsJsonAsync("/api/organizations", new CreateOrganizationRequest
        {
            Name = "List Test Corp 2",
            Subdomain = $"list2-{Guid.NewGuid():N}".Substring(0, 20)
        });

        // Act
        var response = await _adminClient.GetAsync("/api/organizations");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<OrganizationListResponse>();
        result.Should().NotBeNull();
        result!.Organizations.Should().HaveCountGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task ListOrganizations_ShouldReturnForbidden_WhenNotAdmin()
    {
        // Act
        var response = await _client.GetAsync("/api/organizations");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    #endregion

    #region User Management Tests

    [Fact]
    public async Task AddUserToOrganization_ShouldReturnCreated_WhenAdmin()
    {
        // Arrange - Create organization first
        var createOrgRequest = new CreateOrganizationRequest
        {
            Name = "User Test Corp",
            Subdomain = $"usertest-{Guid.NewGuid():N}".Substring(0, 20)
        };
        var createOrgResponse = await _client.PostAsJsonAsync("/api/organizations", createOrgRequest);
        var org = await createOrgResponse.Content.ReadFromJsonAsync<OrganizationResponse>();

        var addUserRequest = new AddUserToOrganizationRequest
        {
            Email = "user@example.com",
            DisplayName = "Test User",
            ExternalIdpSubject = "external-user-123",
            Roles = [Models.UserRole.Member]
        };

        // Act
        var response = await _adminClient.PostAsJsonAsync($"/api/organizations/{org!.Id}/users", addUserRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var user = await response.Content.ReadFromJsonAsync<UserResponse>();
        user.Should().NotBeNull();
        user!.Email.Should().Be("user@example.com");
        user.DisplayName.Should().Be("Test User");
        user.OrganizationId.Should().Be(org.Id);
    }

    [Fact]
    public async Task AddUserToOrganization_ShouldReturnForbidden_WhenNotAdmin()
    {
        // Arrange
        var createOrgRequest = new CreateOrganizationRequest
        {
            Name = "Forbidden User Corp",
            Subdomain = $"forbidu-{Guid.NewGuid():N}".Substring(0, 20)
        };
        var createOrgResponse = await _client.PostAsJsonAsync("/api/organizations", createOrgRequest);
        var org = await createOrgResponse.Content.ReadFromJsonAsync<OrganizationResponse>();

        var addUserRequest = new AddUserToOrganizationRequest
        {
            Email = "user@example.com",
            DisplayName = "Test User",
            ExternalIdpSubject = "ext-1"
        };

        // Act - Use non-admin client
        var response = await _client.PostAsJsonAsync($"/api/organizations/{org!.Id}/users", addUserRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ListOrganizationUsers_ShouldReturnUsers()
    {
        // Arrange - Create organization and add users
        var createOrgRequest = new CreateOrganizationRequest
        {
            Name = "List Users Corp",
            Subdomain = $"listusers-{Guid.NewGuid():N}".Substring(0, 20)
        };
        var createOrgResponse = await _client.PostAsJsonAsync("/api/organizations", createOrgRequest);
        var org = await createOrgResponse.Content.ReadFromJsonAsync<OrganizationResponse>();

        await _adminClient.PostAsJsonAsync($"/api/organizations/{org!.Id}/users", new AddUserToOrganizationRequest
        {
            Email = "user1@example.com",
            DisplayName = "User One",
            ExternalIdpSubject = $"ext-{Guid.NewGuid()}"
        });

        await _adminClient.PostAsJsonAsync($"/api/organizations/{org.Id}/users", new AddUserToOrganizationRequest
        {
            Email = "user2@example.com",
            DisplayName = "User Two",
            ExternalIdpSubject = $"ext-{Guid.NewGuid()}"
        });

        // Act
        var response = await _client.GetAsync($"/api/organizations/{org.Id}/users");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<UserListResponse>();
        result.Should().NotBeNull();
        result!.Users.Should().HaveCount(2);
        result.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task GetOrganizationUser_ShouldReturnUser_WhenExists()
    {
        // Arrange
        var createOrgRequest = new CreateOrganizationRequest
        {
            Name = "Get User Corp",
            Subdomain = $"getuser-{Guid.NewGuid():N}".Substring(0, 20)
        };
        var createOrgResponse = await _client.PostAsJsonAsync("/api/organizations", createOrgRequest);
        var org = await createOrgResponse.Content.ReadFromJsonAsync<OrganizationResponse>();

        var addUserResponse = await _adminClient.PostAsJsonAsync($"/api/organizations/{org!.Id}/users", new AddUserToOrganizationRequest
        {
            Email = "getme@example.com",
            DisplayName = "Get Me User",
            ExternalIdpSubject = $"ext-{Guid.NewGuid()}"
        });
        var user = await addUserResponse.Content.ReadFromJsonAsync<UserResponse>();

        // Act
        var response = await _client.GetAsync($"/api/organizations/{org.Id}/users/{user!.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<UserResponse>();
        result!.Id.Should().Be(user.Id);
        result.Email.Should().Be("getme@example.com");
    }

    [Fact]
    public async Task UpdateOrganizationUser_ShouldUpdateUserDetails_WhenAdmin()
    {
        // Arrange
        var createOrgRequest = new CreateOrganizationRequest
        {
            Name = "Update User Corp",
            Subdomain = $"updateuser-{Guid.NewGuid():N}".Substring(0, 20)
        };
        var createOrgResponse = await _client.PostAsJsonAsync("/api/organizations", createOrgRequest);
        var org = await createOrgResponse.Content.ReadFromJsonAsync<OrganizationResponse>();

        var addUserResponse = await _adminClient.PostAsJsonAsync($"/api/organizations/{org!.Id}/users", new AddUserToOrganizationRequest
        {
            Email = "updateme@example.com",
            DisplayName = "Original Name",
            ExternalIdpSubject = $"ext-{Guid.NewGuid()}",
            Roles = [Models.UserRole.Member]
        });
        var user = await addUserResponse.Content.ReadFromJsonAsync<UserResponse>();

        var updateRequest = new UpdateUserRequest
        {
            DisplayName = "Updated Name",
            Roles = [Models.UserRole.Administrator]
        };

        // Act
        var response = await _adminClient.PutAsJsonAsync($"/api/organizations/{org.Id}/users/{user!.Id}", updateRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<UserResponse>();
        result!.DisplayName.Should().Be("Updated Name");
        result.Roles.Should().Contain(Models.UserRole.Administrator);
    }

    [Fact]
    public async Task RemoveUserFromOrganization_ShouldReturnNoContent_WhenAdmin()
    {
        // Arrange
        var createOrgRequest = new CreateOrganizationRequest
        {
            Name = "Remove User Corp",
            Subdomain = $"removeuser-{Guid.NewGuid():N}".Substring(0, 20)
        };
        var createOrgResponse = await _client.PostAsJsonAsync("/api/organizations", createOrgRequest);
        var org = await createOrgResponse.Content.ReadFromJsonAsync<OrganizationResponse>();

        var addUserResponse = await _adminClient.PostAsJsonAsync($"/api/organizations/{org!.Id}/users", new AddUserToOrganizationRequest
        {
            Email = "removeme@example.com",
            DisplayName = "Remove Me",
            ExternalIdpSubject = $"ext-{Guid.NewGuid()}"
        });
        var user = await addUserResponse.Content.ReadFromJsonAsync<UserResponse>();

        // Act
        var response = await _adminClient.DeleteAsync($"/api/organizations/{org.Id}/users/{user!.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify user is deactivated (soft delete sets status to Suspended)
        var getResponse = await _client.GetAsync($"/api/organizations/{org.Id}/users/{user.Id}");
        var deactivatedUser = await getResponse.Content.ReadFromJsonAsync<UserResponse>();
        deactivatedUser!.Status.Should().Be(Models.IdentityStatus.Suspended);
    }

    #endregion
}
