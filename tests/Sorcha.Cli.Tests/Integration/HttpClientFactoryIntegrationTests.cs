using System.Net;
using FluentAssertions;
using Sorcha.Cli.Models;
using Sorcha.Cli.Services;
using Xunit;

namespace Sorcha.Cli.Tests.Integration;

/// <summary>
/// Integration tests for HttpClientFactory with mock HTTP responses.
/// </summary>
[Collection("CliEnvironment")]
public class HttpClientFactoryIntegrationTests : IDisposable
{
    private readonly string _testConfigDir;
    private readonly IConfigurationService _configService;
    private readonly MockHttpMessageHandler _mockHandler;

    public HttpClientFactoryIntegrationTests()
    {
        _testConfigDir = Path.Combine(Path.GetTempPath(), $"sorcha-cli-integration-tests-{Guid.NewGuid()}");
        Environment.SetEnvironmentVariable("SORCHA_CONFIG_DIR", _testConfigDir);

        // Ensure directory exists
        Directory.CreateDirectory(_testConfigDir);

        _configService = new ConfigurationService();
        _mockHandler = new MockHttpMessageHandler();

        // Initialize configuration
        _configService.EnsureConfigDirectoryAsync().Wait();

        // Set up test profile
        var profile = new Profile
        {
            Name = "test",
            TenantServiceUrl = "https://test-tenant-service.local",
            RegisterServiceUrl = "https://test-register-service.local",
            WalletServiceUrl = "https://test-wallet-service.local",
            VerifySsl = false,
            TimeoutSeconds = 30
        };

        _configService.UpsertProfileAsync(profile).Wait();
    }

    [Fact]
    public async Task CreateTenantServiceClient_ShouldCreateClientWithCorrectBaseAddress()
    {
        // Arrange
        var factory = new HttpClientFactory(_configService);

        // Act
        var client = await factory.CreateTenantServiceClientAsync("test");

        // Assert
        client.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateTenantServiceClient_ShouldThrow_WhenProfileDoesNotExist()
    {
        // Arrange
        var factory = new HttpClientFactory(_configService);

        // Act
        Func<Task> act = async () => await factory.CreateTenantServiceClientAsync("nonexistent");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Profile 'nonexistent' does not exist.");
    }

    [Fact]
    public async Task TenantServiceClient_ListOrganizations_ShouldMakeCorrectRequest()
    {
        // Arrange
        var response = new OrganizationListResponse
        {
            Organizations =
            [
                new() { Id = Guid.Parse("00000000-0000-0000-0001-000000000001"), Name = "Org 1", Status = OrganizationStatus.Active, CreatedAt = DateTimeOffset.UtcNow },
                new() { Id = Guid.Parse("00000000-0000-0000-0001-000000000002"), Name = "Org 2", Status = OrganizationStatus.Active, CreatedAt = DateTimeOffset.UtcNow }
            ],
            TotalCount = 2
        };

        _mockHandler.SetupResponse(HttpMethod.Get, "/api/organizations", HttpStatusCode.OK, response);

        var httpClient = new HttpClient(_mockHandler)
        {
            BaseAddress = new Uri("https://test-tenant-service.local")
        };

        var client = Refit.RestService.For<ITenantServiceClient>(httpClient);

        // Act
        var result = await client.ListOrganizationsAsync("Bearer test-token");

        // Assert
        result.Should().NotBeNull();
        result.Organizations.Should().HaveCount(2);
        result.Organizations[0].Id.Should().Be(Guid.Parse("00000000-0000-0000-0001-000000000001"));
        result.Organizations[1].Id.Should().Be(Guid.Parse("00000000-0000-0000-0001-000000000002"));

        _mockHandler.Requests.Should().HaveCount(1);
        _mockHandler.Requests[0].Method.Should().Be(HttpMethod.Get);
        _mockHandler.Requests[0].RequestUri!.AbsolutePath.Should().Be("/api/organizations");
    }

    [Fact]
    public async Task TenantServiceClient_GetOrganization_ShouldMakeCorrectRequest()
    {
        // Arrange
        var orgId = Guid.Parse("00000000-0000-0000-0001-000000000123");
        var organization = new Organization
        {
            Id = orgId,
            Name = "Test Org",
            Subdomain = "testorg",
            Status = OrganizationStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _mockHandler.SetupResponse(HttpMethod.Get, "/api/organizations/org-123", HttpStatusCode.OK, organization);

        var httpClient = new HttpClient(_mockHandler)
        {
            BaseAddress = new Uri("https://test-tenant-service.local")
        };

        var client = Refit.RestService.For<ITenantServiceClient>(httpClient);

        // Act
        var result = await client.GetOrganizationAsync("org-123", "Bearer test-token");

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be(orgId);
        result.Name.Should().Be("Test Org");
        result.Subdomain.Should().Be("testorg");

        _mockHandler.Requests.Should().HaveCount(1);
        _mockHandler.Requests[0].Method.Should().Be(HttpMethod.Get);
    }

    [Fact]
    public async Task TenantServiceClient_CreateOrganization_ShouldMakeCorrectRequest()
    {
        // Arrange
        var request = new CreateOrganizationRequest
        {
            Name = "New Org",
            Subdomain = "neworg"
        };

        var newOrgId = Guid.Parse("00000000-0000-0000-0001-00000000cafe");
        var response = new Organization
        {
            Id = newOrgId,
            Name = request.Name,
            Subdomain = request.Subdomain,
            Status = OrganizationStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _mockHandler.SetupResponse(HttpMethod.Post, "/api/organizations", HttpStatusCode.Created, response);

        var httpClient = new HttpClient(_mockHandler)
        {
            BaseAddress = new Uri("https://test-tenant-service.local")
        };

        var client = Refit.RestService.For<ITenantServiceClient>(httpClient);

        // Act
        var result = await client.CreateOrganizationAsync(request, "Bearer test-token");

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be(newOrgId);
        result.Name.Should().Be("New Org");

        _mockHandler.Requests.Should().HaveCount(1);
        _mockHandler.Requests[0].Method.Should().Be(HttpMethod.Post);
    }

    [Fact]
    public async Task TenantServiceClient_UpdateOrganization_ShouldMakeCorrectRequest()
    {
        // Arrange
        var request = new UpdateOrganizationRequest
        {
            Name = "Updated Org",
            Status = OrganizationStatus.Active
        };

        var orgId = Guid.Parse("00000000-0000-0000-0001-000000000123");
        var response = new Organization
        {
            Id = orgId,
            Name = "Updated Org",
            Status = OrganizationStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _mockHandler.SetupResponse(HttpMethod.Put, "/api/organizations/org-123", HttpStatusCode.OK, response);

        var httpClient = new HttpClient(_mockHandler)
        {
            BaseAddress = new Uri("https://test-tenant-service.local")
        };

        var client = Refit.RestService.For<ITenantServiceClient>(httpClient);

        // Act
        var result = await client.UpdateOrganizationAsync("org-123", request, "Bearer test-token");

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be(orgId);
        result.Name.Should().Be("Updated Org");

        _mockHandler.Requests.Should().HaveCount(1);
        _mockHandler.Requests[0].Method.Should().Be(HttpMethod.Put);
    }

    [Fact]
    public async Task TenantServiceClient_DeleteOrganization_ShouldMakeCorrectRequest()
    {
        // Arrange
        _mockHandler.SetupResponse(HttpMethod.Delete, "/api/organizations/org-123", HttpStatusCode.NoContent);

        var httpClient = new HttpClient(_mockHandler)
        {
            BaseAddress = new Uri("https://test-tenant-service.local")
        };

        var client = Refit.RestService.For<ITenantServiceClient>(httpClient);

        // Act
        await client.DeleteOrganizationAsync("org-123", "Bearer test-token");

        // Assert
        _mockHandler.Requests.Should().HaveCount(1);
        _mockHandler.Requests[0].Method.Should().Be(HttpMethod.Delete);
        _mockHandler.Requests[0].RequestUri!.AbsolutePath.Should().Be("/api/organizations/org-123");
    }

    [Fact]
    public async Task TenantServiceClient_ListUsers_ShouldMakeCorrectRequest()
    {
        // Arrange
        var users = new List<User>
        {
            new() { Id = "user-1", Username = "user1", Email = "user1@test.com", OrganizationId = "org-123", IsActive = true, CreatedAt = DateTimeOffset.UtcNow },
            new() { Id = "user-2", Username = "user2", Email = "user2@test.com", OrganizationId = "org-123", IsActive = true, CreatedAt = DateTimeOffset.UtcNow }
        };

        _mockHandler.SetupResponse(HttpMethod.Get, "/api/organizations/org-123/users", HttpStatusCode.OK, users);

        var httpClient = new HttpClient(_mockHandler)
        {
            BaseAddress = new Uri("https://test-tenant-service.local")
        };

        var client = Refit.RestService.For<ITenantServiceClient>(httpClient);

        // Act
        var result = await client.ListUsersAsync("org-123", "Bearer test-token");

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2);
        result[0].Username.Should().Be("user1");
        result[1].Username.Should().Be("user2");

        _mockHandler.Requests.Should().HaveCount(1);
        _mockHandler.Requests[0].Method.Should().Be(HttpMethod.Get);
    }

    [Fact]
    public async Task TenantServiceClient_ListServicePrincipals_ShouldMakeCorrectRequest()
    {
        // Arrange
        var principals = new List<ServicePrincipal>
        {
            new() { ClientId = "sp-1", Name = "API Service 1", OrganizationId = "org-123", IsActive = true, CreatedAt = DateTimeOffset.UtcNow },
            new() { ClientId = "sp-2", Name = "API Service 2", OrganizationId = "org-123", IsActive = true, CreatedAt = DateTimeOffset.UtcNow }
        };

        _mockHandler.SetupResponse(HttpMethod.Get, "/api/organizations/org-123/principals", HttpStatusCode.OK, principals);

        var httpClient = new HttpClient(_mockHandler)
        {
            BaseAddress = new Uri("https://test-tenant-service.local")
        };

        var client = Refit.RestService.For<ITenantServiceClient>(httpClient);

        // Act
        var result = await client.ListServicePrincipalsAsync("org-123", "Bearer test-token");

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2);
        result[0].Name.Should().Be("API Service 1");
        result[1].Name.Should().Be("API Service 2");

        _mockHandler.Requests.Should().HaveCount(1);
        _mockHandler.Requests[0].Method.Should().Be(HttpMethod.Get);
    }

    public void Dispose()
    {
        _mockHandler.Dispose();

        // Clean up test directory
        if (Directory.Exists(_testConfigDir))
        {
            try
            {
                Thread.Sleep(100); // Allow file handles to close
                Directory.Delete(_testConfigDir, true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

        Environment.SetEnvironmentVariable("SORCHA_CONFIG_DIR", null);
    }
}
