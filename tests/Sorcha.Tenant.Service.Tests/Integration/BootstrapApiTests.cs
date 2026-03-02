// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models.Dtos;
using Sorcha.Tenant.Service.Tests.Infrastructure;

namespace Sorcha.Tenant.Service.Tests.Integration;

/// <summary>
/// Integration tests for Bootstrap API endpoints.
/// </summary>
public class BootstrapApiTests : IClassFixture<TenantServiceWebApplicationFactory>, IAsyncLifetime
{
    private readonly TenantServiceWebApplicationFactory _factory;
    private HttpClient _client = null!;

    public BootstrapApiTests(TenantServiceWebApplicationFactory factory)
    {
        _factory = factory;
    }

    public async ValueTask InitializeAsync()
    {
        // Bootstrap is an unauthenticated endpoint - anyone can call it (should be protected in production)
        _client = _factory.CreateUnauthenticatedClient();

        // Reset the database before each test to clear the BootstrapCompleted one-shot guard
        // and any previously created orgs/users, ensuring test isolation
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        await context.Database.EnsureDeletedAsync();
        await context.Database.EnsureCreatedAsync();
    }

    public ValueTask DisposeAsync()
    {
        _client?.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Bootstrap_WithValidData_ReturnsCreatedWithOrganizationAndUser()
    {
        // Arrange
        var request = new BootstrapRequest
        {
            OrganizationName = "Bootstrap Test Org",
            OrganizationSubdomain = $"bootstrap-{Guid.NewGuid():N}",
            OrganizationDescription = "Test organization for bootstrap",
            AdminEmail = $"admin-{Guid.NewGuid():N}@example.com",
            AdminName = "Bootstrap Admin",
            AdminPassword = "SecureP@ss123!",
            CreateServicePrincipal = false
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/tenants/bootstrap", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var result = await response.Content.ReadFromJsonAsync<BootstrapResponse>();
        result.Should().NotBeNull();
        result!.OrganizationId.Should().NotBeEmpty();
        result.OrganizationName.Should().Be("Bootstrap Test Org");
        result.OrganizationSubdomain.Should().Be(request.OrganizationSubdomain);
        result.AdminUserId.Should().NotBeEmpty();
        result.AdminEmail.Should().Be(request.AdminEmail);
        result.AdminAccessToken.Should().NotBeNull();
        result.AdminRefreshToken.Should().NotBeNull();
        result.ServicePrincipalId.Should().BeNull();
        result.ServicePrincipalClientId.Should().BeNull();
        result.ServicePrincipalClientSecret.Should().BeNull();
        result.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));

        // Verify location header
        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.ToString().Should().Contain($"/api/organizations/{result.OrganizationId}");
    }

    [Fact]
    public async Task Bootstrap_WithServicePrincipal_CreatesServicePrincipalAndReturnsCredentials()
    {
        // Arrange
        var request = new BootstrapRequest
        {
            OrganizationName = "Bootstrap SP Test Org",
            OrganizationSubdomain = $"bootstrap-sp-{Guid.NewGuid():N}",
            AdminEmail = $"admin-sp-{Guid.NewGuid():N}@example.com",
            AdminName = "Bootstrap Admin with SP",
            AdminPassword = "SecureP@ss123!",
            CreateServicePrincipal = true,
            ServicePrincipalName = "test-automation-principal"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/tenants/bootstrap", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var result = await response.Content.ReadFromJsonAsync<BootstrapResponse>();
        result.Should().NotBeNull();
        result!.ServicePrincipalId.Should().NotBeNull();
        result.ServicePrincipalClientId.Should().NotBeNullOrEmpty();
        result.ServicePrincipalClientSecret.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Bootstrap_WithMissingOrganizationName_ReturnsValidationError()
    {
        // Arrange
        var request = new BootstrapRequest
        {
            OrganizationName = "", // Invalid - empty
            OrganizationSubdomain = "test-subdomain",
            AdminEmail = "admin@example.com",
            AdminName = "Admin User",
            AdminPassword = "SecureP@ss123!"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/tenants/bootstrap", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Bootstrap_WithInvalidSubdomain_ReturnsValidationError()
    {
        // Arrange
        var request = new BootstrapRequest
        {
            OrganizationName = "Test Org",
            OrganizationSubdomain = "Invalid_Subdomain!", // Invalid - contains uppercase and special chars
            AdminEmail = "admin@example.com",
            AdminName = "Admin User",
            AdminPassword = "SecureP@ss123!"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/tenants/bootstrap", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Bootstrap_WithInvalidEmail_ReturnsValidationError()
    {
        // Arrange
        var request = new BootstrapRequest
        {
            OrganizationName = "Test Org",
            OrganizationSubdomain = $"test-{Guid.NewGuid():N}",
            AdminEmail = "not-an-email", // Invalid email
            AdminName = "Admin User",
            AdminPassword = "SecureP@ss123!"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/tenants/bootstrap", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Bootstrap_WithWeakPassword_ReturnsValidationError()
    {
        // Arrange
        var request = new BootstrapRequest
        {
            OrganizationName = "Test Org",
            OrganizationSubdomain = $"test-{Guid.NewGuid():N}",
            AdminEmail = "admin@example.com",
            AdminName = "Admin User",
            AdminPassword = "weak" // Too short, no uppercase, no special chars
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/tenants/bootstrap", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Bootstrap_WithDuplicateSubdomain_ReturnsConflict()
    {
        // Arrange
        var subdomain = $"duplicate-{Guid.NewGuid():N}";

        var request1 = new BootstrapRequest
        {
            OrganizationName = "First Org",
            OrganizationSubdomain = subdomain,
            AdminEmail = $"admin1-{Guid.NewGuid():N}@example.com",
            AdminName = "Admin 1",
            AdminPassword = "SecureP@ss123!"
        };

        // Act - Create first organization
        var response1 = await _client.PostAsJsonAsync("/api/tenants/bootstrap", request1);
        response1.StatusCode.Should().Be(HttpStatusCode.Created);

        // Arrange - Try to create second with same subdomain
        var request2 = new BootstrapRequest
        {
            OrganizationName = "Second Org",
            OrganizationSubdomain = subdomain, // Duplicate
            AdminEmail = $"admin2-{Guid.NewGuid():N}@example.com",
            AdminName = "Admin 2",
            AdminPassword = "SecureP@ss123!"
        };

        // Act - Create second organization
        var response2 = await _client.PostAsJsonAsync("/api/tenants/bootstrap", request2);

        // Assert
        response2.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Bootstrap_CreatedAdminCanLoginWithPassword()
    {
        // Arrange - Bootstrap with new admin
        var adminEmail = $"admin-login-{Guid.NewGuid():N}@example.com";
        var adminPassword = "SecureP@ss123!";

        var bootstrapRequest = new BootstrapRequest
        {
            OrganizationName = "Login Test Org",
            OrganizationSubdomain = $"login-test-{Guid.NewGuid():N}",
            AdminEmail = adminEmail,
            AdminName = "Login Test Admin",
            AdminPassword = adminPassword
        };

        var bootstrapResponse = await _client.PostAsJsonAsync("/api/tenants/bootstrap", bootstrapRequest);
        bootstrapResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        // Act - Try to login with created admin credentials
        var loginRequest = new LoginRequest
        {
            Email = adminEmail,
            Password = adminPassword
        };

        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", loginRequest);

        // Assert
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var loginResult = await loginResponse.Content.ReadFromJsonAsync<TokenResponse>();
        loginResult.Should().NotBeNull();
        loginResult!.AccessToken.Should().NotBeNullOrEmpty();
        loginResult.RefreshToken.Should().NotBeNullOrEmpty();
        loginResult.TokenType.Should().Be("Bearer");
    }
}
