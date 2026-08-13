// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Tenant.Service.IntegrationTests.Fixtures;
using Sorcha.Tenant.Service.Models.Dtos;

namespace Sorcha.Tenant.Service.IntegrationTests;

/// <summary>
/// Integration tests for Service-to-Service Authentication API endpoints.
/// </summary>
public class ServiceAuthApiTests : IClassFixture<TenantServiceWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly HttpClient _adminClient;
    private readonly HttpClient _unauthClient;
    private readonly TenantServiceWebApplicationFactory _factory;

    public ServiceAuthApiTests(TenantServiceWebApplicationFactory factory)
    {
        _factory = factory;
        _unauthClient = _factory.CreateUnauthenticatedClient();
        _client = _factory.CreateAuthenticatedClient();
        _adminClient = _factory.CreateAdminClient();
    }

    #region Client Credentials Token Tests

    [Fact]
    public async Task GetServiceToken_OnPublicEndpoint_ShouldReturnBadRequest_ForClientCredentials()
    {
        // #1397 — the PUBLIC /api/service-auth/token endpoint (proxied by the API Gateway) no longer
        // serves client_credentials at all, regardless of whether the credentials are valid. This is
        // the fix under test: minting a service-tier token here used to require only the committed
        // dev client secrets, no bearer, reachable from the open internet.
        var formData = new Dictionary<string, string>
        {
            ["client_id"] = "some-client",
            ["client_secret"] = "some-secret",
            ["grant_type"] = "client_credentials"
        };

        // Act
        var response = await _unauthClient.PostAsync("/api/service-auth/token", new FormUrlEncodedContent(formData));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetServiceToken_ShouldReturnUnauthorized_WithInvalidCredentials()
    {
        // Arrange — internal-only endpoint (#1397); not routed by the public API Gateway.
        var formData = new Dictionary<string, string>
        {
            ["client_id"] = "invalid-client",
            ["client_secret"] = "invalid-secret",
            ["grant_type"] = "client_credentials"
        };

        // Act
        var response = await _unauthClient.PostAsync("/api/internal/service-auth/token", new FormUrlEncodedContent(formData));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetServiceToken_ShouldReturnBadRequest_WithInvalidGrantType()
    {
        // Arrange — this exercises the PUBLIC endpoint's password-grant validation (missing
        // username/password), unaffected by the #1397 client_credentials move.
        var formData = new Dictionary<string, string>
        {
            ["client_id"] = "some-client",
            ["client_secret"] = "some-secret",
            ["grant_type"] = "password" // Invalid grant type
        };

        // Act
        var response = await _unauthClient.PostAsync("/api/service-auth/token", new FormUrlEncodedContent(formData));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetServiceToken_ShouldReturnBadRequest_WithMissingClientId()
    {
        // Arrange — internal-only endpoint (#1397).
        var formData = new Dictionary<string, string>
        {
            ["client_id"] = "",
            ["client_secret"] = "some-secret",
            ["grant_type"] = "client_credentials"
        };

        // Act
        var response = await _unauthClient.PostAsync("/api/internal/service-auth/token", new FormUrlEncodedContent(formData));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetServiceToken_ShouldReturnBadRequest_WithMissingClientSecret()
    {
        // Arrange — internal-only endpoint (#1397).
        var formData = new Dictionary<string, string>
        {
            ["client_id"] = "some-client",
            ["client_secret"] = "",
            ["grant_type"] = "client_credentials"
        };

        // Act
        var response = await _unauthClient.PostAsync("/api/internal/service-auth/token", new FormUrlEncodedContent(formData));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region Delegated Token Tests

    [Fact]
    public async Task GetDelegatedToken_ShouldReturnUnauthorized_WithInvalidCredentials()
    {
        // Arrange — internal-only endpoint (#1397); not routed by the public API Gateway.
        var request = new DelegatedTokenRequest
        {
            ClientId = "invalid-client",
            ClientSecret = "invalid-secret",
            DelegatedUserId = Guid.NewGuid()
        };

        // Act
        var response = await _unauthClient.PostAsJsonAsync("/api/internal/service-auth/token/delegated", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetDelegatedToken_ShouldReturnBadRequest_WithMissingDelegatedUserId()
    {
        // Arrange — internal-only endpoint (#1397).
        var request = new DelegatedTokenRequest
        {
            ClientId = "some-client",
            ClientSecret = "some-secret",
            DelegatedUserId = Guid.Empty
        };

        // Act
        var response = await _unauthClient.PostAsJsonAsync("/api/internal/service-auth/token/delegated", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetDelegatedToken_OnPublicRoute_ShouldReturnNotFound()
    {
        // #1397 — /token/delegated no longer exists under the public /api/service-auth group.
        var request = new DelegatedTokenRequest
        {
            ClientId = "some-client",
            ClientSecret = "some-secret",
            DelegatedUserId = Guid.NewGuid()
        };

        // Act
        var response = await _unauthClient.PostAsJsonAsync("/api/service-auth/token/delegated", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region Service Principal Management Tests

    [Fact]
    public async Task RegisterServicePrincipal_ShouldReturnUnauthorized_WithoutAuth()
    {
        // Arrange
        var request = new RegisterServicePrincipalRequest
        {
            ServiceName = "test-service",
            Scopes = ["tenant:read", "tenant:write"]
        };

        // Act
        var response = await _unauthClient.PostAsJsonAsync("/api/service-principals", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RegisterServicePrincipal_ShouldReturnForbidden_WhenNotAdmin()
    {
        // Arrange
        var request = new RegisterServicePrincipalRequest
        {
            ServiceName = "test-service",
            Scopes = ["tenant:read"]
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/service-principals", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RegisterServicePrincipal_ShouldReturnCreated_WhenAdmin()
    {
        // Arrange
        var request = new RegisterServicePrincipalRequest
        {
            ServiceName = $"test-service-{Guid.NewGuid():N}".Substring(0, 30),
            Scopes = ["tenant:read", "tenant:write"]
        };

        // Act
        var response = await _adminClient.PostAsJsonAsync("/api/service-principals", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var result = await response.Content.ReadFromJsonAsync<ServicePrincipalRegistrationResponse>();
        result.Should().NotBeNull();
        result!.ClientId.Should().StartWith("service-");
        result.ClientSecret.Should().NotBeNullOrEmpty();
        result.Scopes.Should().Contain("tenant:read");
    }

    [Fact]
    public async Task ListServicePrincipals_ShouldReturnUnauthorized_WithoutAuth()
    {
        // Act
        var response = await _unauthClient.GetAsync("/api/service-principals");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ListServicePrincipals_ShouldReturnForbidden_WhenNotAdmin()
    {
        // Act
        var response = await _client.GetAsync("/api/service-principals");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ListServicePrincipals_ShouldReturnList_WhenAdmin()
    {
        // Arrange - Create a service principal first
        await _adminClient.PostAsJsonAsync("/api/service-principals", new RegisterServicePrincipalRequest
        {
            ServiceName = $"list-test-{Guid.NewGuid():N}".Substring(0, 30),
            Scopes = ["tenant:read"]
        });

        // Act
        var response = await _adminClient.GetAsync("/api/service-principals");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<ServicePrincipalListResponse>();
        result.Should().NotBeNull();
        result!.ServicePrincipals.Should().HaveCountGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task GetServicePrincipal_ShouldReturnNotFound_WhenDoesNotExist()
    {
        // Act
        var response = await _adminClient.GetAsync($"/api/service-principals/{Guid.NewGuid()}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetServicePrincipal_ShouldReturnPrincipal_WhenExists()
    {
        // Arrange
        var createResponse = await _adminClient.PostAsJsonAsync("/api/service-principals", new RegisterServicePrincipalRequest
        {
            ServiceName = $"get-test-{Guid.NewGuid():N}".Substring(0, 30),
            Scopes = ["tenant:read"]
        });
        var created = await createResponse.Content.ReadFromJsonAsync<ServicePrincipalRegistrationResponse>();

        // Act
        var response = await _adminClient.GetAsync($"/api/service-principals/{created!.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<ServicePrincipalResponse>();
        result.Should().NotBeNull();
        result!.Id.Should().Be(created.Id);
    }

    [Fact]
    public async Task GetServicePrincipalByClientId_ShouldReturnPrincipal_WhenExists()
    {
        // Arrange
        var createResponse = await _adminClient.PostAsJsonAsync("/api/service-principals", new RegisterServicePrincipalRequest
        {
            ServiceName = $"clientid-test-{Guid.NewGuid():N}".Substring(0, 30),
            Scopes = ["tenant:read"]
        });
        var created = await createResponse.Content.ReadFromJsonAsync<ServicePrincipalRegistrationResponse>();

        // Act
        var response = await _adminClient.GetAsync($"/api/service-principals/by-client/{created!.ClientId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<ServicePrincipalResponse>();
        result.Should().NotBeNull();
        result!.ClientId.Should().Be(created.ClientId);
    }

    [Fact]
    public async Task UpdateServicePrincipalScopes_ShouldUpdateScopes_WhenAdmin()
    {
        // Arrange
        var createResponse = await _adminClient.PostAsJsonAsync("/api/service-principals", new RegisterServicePrincipalRequest
        {
            ServiceName = $"scopes-test-{Guid.NewGuid():N}".Substring(0, 30),
            Scopes = ["tenant:read"]
        });
        var created = await createResponse.Content.ReadFromJsonAsync<ServicePrincipalRegistrationResponse>();

        var newScopes = new[] { "tenant:read", "tenant:write", "tenant:admin" };

        // Act
        var response = await _adminClient.PutAsJsonAsync($"/api/service-principals/{created!.Id}/scopes", newScopes);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<ServicePrincipalResponse>();
        result!.Scopes.Should().HaveCount(3);
        result.Scopes.Should().Contain("tenant:admin");
    }

    [Fact]
    public async Task SuspendServicePrincipal_ShouldSuspend_WhenAdmin()
    {
        // Arrange
        var createResponse = await _adminClient.PostAsJsonAsync("/api/service-principals", new RegisterServicePrincipalRequest
        {
            ServiceName = $"suspend-test-{Guid.NewGuid():N}".Substring(0, 30),
            Scopes = ["tenant:read"]
        });
        var created = await createResponse.Content.ReadFromJsonAsync<ServicePrincipalRegistrationResponse>();

        // Act
        var response = await _adminClient.PostAsync($"/api/service-principals/{created!.Id}/suspend", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify suspension
        var getResponse = await _adminClient.GetAsync($"/api/service-principals/{created.Id}");
        var sp = await getResponse.Content.ReadFromJsonAsync<ServicePrincipalResponse>();
        sp!.Status.Should().Be("Suspended");
    }

    [Fact]
    public async Task ReactivateServicePrincipal_ShouldReactivate_WhenAdmin()
    {
        // Arrange
        var createResponse = await _adminClient.PostAsJsonAsync("/api/service-principals", new RegisterServicePrincipalRequest
        {
            ServiceName = $"reactivate-test-{Guid.NewGuid():N}".Substring(0, 30),
            Scopes = ["tenant:read"]
        });
        var created = await createResponse.Content.ReadFromJsonAsync<ServicePrincipalRegistrationResponse>();

        // Suspend first
        await _adminClient.PostAsync($"/api/service-principals/{created!.Id}/suspend", null);

        // Act
        var response = await _adminClient.PostAsync($"/api/service-principals/{created.Id}/reactivate", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify reactivation
        var getResponse = await _adminClient.GetAsync($"/api/service-principals/{created.Id}");
        var sp = await getResponse.Content.ReadFromJsonAsync<ServicePrincipalResponse>();
        sp!.Status.Should().Be("Active");
    }

    [Fact]
    public async Task RevokeServicePrincipal_ShouldReturnNoContent_WhenAdmin()
    {
        // Arrange
        var createResponse = await _adminClient.PostAsJsonAsync("/api/service-principals", new RegisterServicePrincipalRequest
        {
            ServiceName = $"revoke-test-{Guid.NewGuid():N}".Substring(0, 30),
            Scopes = ["tenant:read"]
        });
        var created = await createResponse.Content.ReadFromJsonAsync<ServicePrincipalRegistrationResponse>();

        // Act
        var response = await _adminClient.DeleteAsync($"/api/service-principals/{created!.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    #endregion

    #region Secret Rotation Tests

    [Fact]
    public async Task RotateSecret_ShouldReturnUnauthorized_WithInvalidCredentials()
    {
        // Arrange — internal-only endpoint (#1397); not routed by the public API Gateway.
        var request = new RotateSecretRequest
        {
            CurrentSecret = "wrong-secret"
        };

        // Act
        var response = await _unauthClient.PostAsJsonAsync("/api/internal/service-auth/rotate-secret?clientId=some-client", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RotateSecret_ShouldReturnBadRequest_WithMissingClientId()
    {
        // Arrange — internal-only endpoint (#1397).
        var request = new RotateSecretRequest
        {
            CurrentSecret = "some-secret"
        };

        // Act
        var response = await _unauthClient.PostAsJsonAsync("/api/internal/service-auth/rotate-secret", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RotateSecret_ShouldReturnBadRequest_WithMissingCurrentSecret()
    {
        // Arrange — internal-only endpoint (#1397).
        var request = new RotateSecretRequest
        {
            CurrentSecret = ""
        };

        // Act
        var response = await _unauthClient.PostAsJsonAsync("/api/internal/service-auth/rotate-secret?clientId=some-client", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RotateSecret_ShouldRotate_WithValidCredentials()
    {
        // Arrange - Create a service principal first
        var createResponse = await _adminClient.PostAsJsonAsync("/api/service-principals", new RegisterServicePrincipalRequest
        {
            ServiceName = $"rotate-test-{Guid.NewGuid():N}".Substring(0, 30),
            Scopes = ["tenant:read"]
        });
        var created = await createResponse.Content.ReadFromJsonAsync<ServicePrincipalRegistrationResponse>();

        var request = new RotateSecretRequest
        {
            CurrentSecret = created!.ClientSecret
        };

        // Act — internal-only endpoint (#1397).
        var response = await _unauthClient.PostAsJsonAsync($"/api/internal/service-auth/rotate-secret?clientId={created.ClientId}", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<RotateSecretResponse>();
        result.Should().NotBeNull();
        result!.NewClientSecret.Should().NotBeNullOrEmpty();
        result.NewClientSecret.Should().NotBe(created.ClientSecret);
    }

    [Fact]
    public async Task RotateSecret_OnPublicRoute_ShouldReturnNotFound()
    {
        // #1397 — /rotate-secret no longer exists under the public /api/service-auth group.
        var request = new RotateSecretRequest
        {
            CurrentSecret = "some-secret"
        };

        // Act
        var response = await _unauthClient.PostAsJsonAsync("/api/service-auth/rotate-secret?clientId=some-client", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion
}
