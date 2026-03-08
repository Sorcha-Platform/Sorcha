// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Sorcha.Tenant.Service.Models.Dtos;
using Sorcha.Tenant.Service.Tests.Infrastructure;
using Xunit;

namespace Sorcha.Tenant.Service.Tests.Endpoints;

public class IdpConfigurationEndpointTests : IClassFixture<TenantServiceWebApplicationFactory>, IAsyncLifetime
{
    private readonly TenantServiceWebApplicationFactory _factory;
    private HttpClient _adminClient = null!;
    private HttpClient _memberClient = null!;
    private HttpClient _unauthenticatedClient = null!;

    private static readonly Guid OrgId = TestDataSeeder.TestOrganizationId;
    private static readonly string BasePath = $"/api/organizations/{OrgId}/idp";

    public IdpConfigurationEndpointTests(TenantServiceWebApplicationFactory factory)
    {
        _factory = factory;
    }

    public async ValueTask InitializeAsync()
    {
        _adminClient = _factory.CreateAdminClient();
        _memberClient = _factory.CreateMemberClient();
        _unauthenticatedClient = _factory.CreateUnauthenticatedClient();
        await _factory.SeedTestDataAsync();
    }

    public ValueTask DisposeAsync()
    {
        _adminClient?.Dispose();
        _memberClient?.Dispose();
        _unauthenticatedClient?.Dispose();
        return ValueTask.CompletedTask;
    }

    #region GET /api/organizations/{orgId}/idp Tests

    [Fact]
    public async Task GetIdpConfiguration_NoConfig_Returns404()
    {
        var response = await _adminClient.GetAsync(BasePath);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetIdpConfiguration_WithConfig_Returns200WithResponse()
    {
        // Arrange — create a configuration first
        var request = CreateValidIdpRequest();
        await _adminClient.PutAsJsonAsync(BasePath, request);

        // Act
        var response = await _adminClient.GetAsync(BasePath);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<IdpConfigurationResponse>();
        result.Should().NotBeNull();
        result!.ProviderPreset.Should().Be(request.ProviderPreset);
        result.IssuerUrl.Should().Be(request.IssuerUrl);
        result.Scopes.Should().Contain("openid");
    }

    [Fact]
    public async Task GetIdpConfiguration_Unauthenticated_Returns401()
    {
        var response = await _unauthenticatedClient.GetAsync(BasePath);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetIdpConfiguration_NonAdmin_Returns403()
    {
        var response = await _memberClient.GetAsync(BasePath);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    #endregion

    #region PUT /api/organizations/{orgId}/idp Tests

    [Fact]
    public async Task PutIdpConfiguration_ValidRequest_Returns200()
    {
        // Arrange
        var request = CreateValidIdpRequest();

        // Act
        var response = await _adminClient.PutAsJsonAsync(BasePath, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<IdpConfigurationResponse>();
        result.Should().NotBeNull();
        result!.ProviderPreset.Should().Be("MicrosoftEntra");
        result.IssuerUrl.Should().Be(request.IssuerUrl);
        result.DisplayName.Should().Be("Corporate SSO");
        result.IsEnabled.Should().BeFalse("newly created IDP config should default to disabled");
    }

    [Fact]
    public async Task PutIdpConfiguration_InvalidRequest_Returns400()
    {
        // Arrange — missing required fields
        var request = new { ProviderPreset = "", IssuerUrl = "", ClientId = "", ClientSecret = "" };

        // Act
        var response = await _adminClient.PutAsJsonAsync(BasePath, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PutIdpConfiguration_Unauthenticated_Returns401()
    {
        var request = CreateValidIdpRequest();

        var response = await _unauthenticatedClient.PutAsJsonAsync(BasePath, request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PutIdpConfiguration_NonAdmin_Returns403()
    {
        var request = CreateValidIdpRequest();

        var response = await _memberClient.PutAsJsonAsync(BasePath, request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    #endregion

    #region DELETE /api/organizations/{orgId}/idp Tests

    [Fact]
    public async Task DeleteIdpConfiguration_ExistingConfig_Returns204()
    {
        // Arrange — create a configuration first
        var request = CreateValidIdpRequest();
        await _adminClient.PutAsJsonAsync(BasePath, request);

        // Act
        var response = await _adminClient.DeleteAsync(BasePath);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify it's gone
        var getResponse = await _adminClient.GetAsync(BasePath);
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteIdpConfiguration_NoConfig_Returns404()
    {
        var response = await _adminClient.DeleteAsync(BasePath);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteIdpConfiguration_Unauthenticated_Returns401()
    {
        var response = await _unauthenticatedClient.DeleteAsync(BasePath);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region POST /api/organizations/{orgId}/idp/discover Tests

    [Fact]
    public async Task PostDiscover_ValidIssuerUrl_Returns200WithEndpoints()
    {
        // Arrange
        var request = new DiscoverIdpRequest
        {
            IssuerUrl = "https://login.microsoftonline.com/common/v2.0"
        };

        // Act
        var response = await _adminClient.PostAsJsonAsync($"{BasePath}/discover", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<DiscoveryResponse>();
        result.Should().NotBeNull();
        result!.Issuer.Should().NotBeNullOrWhiteSpace();
        result.AuthorizationEndpoint.Should().NotBeNullOrWhiteSpace();
        result.TokenEndpoint.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task PostDiscover_InvalidIssuerUrl_Returns400()
    {
        var request = new DiscoverIdpRequest { IssuerUrl = "not-a-url" };

        var response = await _adminClient.PostAsJsonAsync($"{BasePath}/discover", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostDiscover_Unauthenticated_Returns401()
    {
        var request = new DiscoverIdpRequest
        {
            IssuerUrl = "https://login.microsoftonline.com/common/v2.0"
        };

        var response = await _unauthenticatedClient.PostAsJsonAsync($"{BasePath}/discover", request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region POST /api/organizations/{orgId}/idp/test Tests

    [Fact]
    public async Task PostTest_ConfiguredIdp_ReturnsTestResult()
    {
        // Arrange — create a configuration first
        var request = CreateValidIdpRequest();
        await _adminClient.PutAsJsonAsync(BasePath, request);

        // Act
        var response = await _adminClient.PostAsync($"{BasePath}/test", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<TestConnectionResponse>();
        result.Should().NotBeNull();
        result!.Message.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task PostTest_NoConfig_Returns404()
    {
        var response = await _adminClient.PostAsync($"{BasePath}/test", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PostTest_Unauthenticated_Returns401()
    {
        var response = await _unauthenticatedClient.PostAsync($"{BasePath}/test", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region POST /api/organizations/{orgId}/idp/toggle Tests

    [Fact]
    public async Task PostToggle_EnableDisable_Returns200()
    {
        // Arrange — create a configuration first
        var request = CreateValidIdpRequest();
        await _adminClient.PutAsJsonAsync(BasePath, request);

        // Act — enable
        var enableResponse = await _adminClient.PostAsJsonAsync($"{BasePath}/toggle",
            new ToggleIdpRequest { Enabled = true });

        // Assert — enabled
        enableResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var enabledResult = await enableResponse.Content.ReadFromJsonAsync<IdpConfigurationResponse>();
        enabledResult.Should().NotBeNull();
        enabledResult!.IsEnabled.Should().BeTrue();

        // Act — disable
        var disableResponse = await _adminClient.PostAsJsonAsync($"{BasePath}/toggle",
            new ToggleIdpRequest { Enabled = false });

        // Assert — disabled
        disableResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var disabledResult = await disableResponse.Content.ReadFromJsonAsync<IdpConfigurationResponse>();
        disabledResult.Should().NotBeNull();
        disabledResult!.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task PostToggle_NoConfig_Returns404()
    {
        var response = await _adminClient.PostAsJsonAsync($"{BasePath}/toggle",
            new ToggleIdpRequest { Enabled = true });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PostToggle_Unauthenticated_Returns401()
    {
        var response = await _unauthenticatedClient.PostAsJsonAsync($"{BasePath}/toggle",
            new ToggleIdpRequest { Enabled = true });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostToggle_NonAdmin_Returns403()
    {
        var response = await _memberClient.PostAsJsonAsync($"{BasePath}/toggle",
            new ToggleIdpRequest { Enabled = true });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    #endregion

    #region Helpers

    private static IdpConfigurationRequest CreateValidIdpRequest() => new()
    {
        ProviderPreset = "MicrosoftEntra",
        IssuerUrl = "https://login.microsoftonline.com/test-tenant-id/v2.0",
        ClientId = "test-client-id-12345",
        ClientSecret = "test-client-secret-67890",
        DisplayName = "Corporate SSO",
        Scopes = ["openid", "profile", "email"]
    };

    #endregion
}
