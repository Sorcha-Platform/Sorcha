// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Sorcha.Tenant.Service.Models.Dtos;
using Sorcha.Tenant.Service.Tests.Infrastructure;
using Xunit;

namespace Sorcha.Tenant.Service.Tests.Endpoints;

public class OidcEndpointTests : IClassFixture<TenantServiceWebApplicationFactory>, IAsyncLifetime
{
    private readonly TenantServiceWebApplicationFactory _factory;
    private HttpClient _unauthenticatedClient = null!;
    private HttpClient _authenticatedClient = null!;

    private const string TestOrgSubdomain = "test-org";

    public OidcEndpointTests(TenantServiceWebApplicationFactory factory)
    {
        _factory = factory;
    }

    public async ValueTask InitializeAsync()
    {
        _authenticatedClient = _factory.CreateAdminClient();
        _unauthenticatedClient = _factory.CreateUnauthenticatedClient();
        await _factory.SeedTestDataAsync();
    }

    public ValueTask DisposeAsync()
    {
        _authenticatedClient?.Dispose();
        _unauthenticatedClient?.Dispose();
        return ValueTask.CompletedTask;
    }

    #region POST /api/auth/oidc/initiate Tests

    [Fact]
    public async Task PostInitiate_ValidOrg_Returns200WithAuthorizationUrl()
    {
        // NOTE: This test requires IOidcExchangeService to be mocked in WebApplicationFactory
        // to return a valid authorization URL. Will return 404 until endpoints are implemented.

        // Arrange
        var request = new OidcInitiateRequest
        {
            OrgSubdomain = TestOrgSubdomain,
            RedirectUrl = "https://app.sorcha.io/callback"
        };

        // Act
        var response = await _unauthenticatedClient.PostAsJsonAsync("/api/auth/oidc/initiate", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<OidcInitiateResponse>();
        result.Should().NotBeNull();
        result!.AuthorizationUrl.Should().NotBeNullOrWhiteSpace();
        result.State.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task PostInitiate_UnknownOrg_Returns404()
    {
        // Arrange
        var request = new OidcInitiateRequest
        {
            OrgSubdomain = "nonexistent-org",
            RedirectUrl = "https://app.sorcha.io/callback"
        };

        // Act
        var response = await _unauthenticatedClient.PostAsJsonAsync("/api/auth/oidc/initiate", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region GET /api/auth/callback/{orgSubdomain} Tests

    [Fact]
    public async Task GetCallback_ValidCodeAndState_ReturnsSuccessResult()
    {
        // NOTE: This test requires IOidcExchangeService to be mocked in WebApplicationFactory
        // to handle code exchange. The mock should return a valid OidcCallbackResult with tokens.
        // Will return 404 until endpoints are implemented.

        // Arrange
        var code = "valid-auth-code";
        var state = "valid-state-token";

        // Act
        var response = await _unauthenticatedClient.GetAsync(
            $"/api/auth/callback/{TestOrgSubdomain}?code={code}&state={state}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<OidcCallbackResult>();
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task GetCallback_InvalidState_Returns400()
    {
        // Arrange
        var code = "valid-auth-code";
        var state = "invalid-or-tampered-state";

        // Act
        var response = await _unauthenticatedClient.GetAsync(
            $"/api/auth/callback/{TestOrgSubdomain}?code={code}&state={state}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetCallback_MissingCode_Returns400()
    {
        // Arrange — no code parameter, only state
        var state = "valid-state-token";

        // Act
        var response = await _unauthenticatedClient.GetAsync(
            $"/api/auth/callback/{TestOrgSubdomain}?state={state}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region POST /api/auth/oidc/complete-profile Tests

    [Fact]
    public async Task PostCompleteProfile_Authenticated_ReturnsUpdatedProfile()
    {
        // NOTE: This test requires a valid OIDC-authenticated user session.
        // The authenticated client simulates a user who has completed OIDC login
        // but needs to fill in missing profile fields.

        // Arrange
        var request = new OidcCompleteProfileRequest
        {
            DisplayName = "Updated Display Name",
            Email = "updated@test-org.sorcha.io"
        };

        // Act
        var response = await _authenticatedClient.PostAsJsonAsync("/api/auth/oidc/complete-profile", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PostCompleteProfile_Unauthenticated_Returns401()
    {
        // Arrange
        var request = new OidcCompleteProfileRequest
        {
            DisplayName = "Updated Display Name",
            Email = "updated@test-org.sorcha.io"
        };

        // Act
        var response = await _unauthenticatedClient.PostAsJsonAsync("/api/auth/oidc/complete-profile", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region POST /api/auth/verify-email Tests

    [Fact]
    public async Task PostVerifyEmail_ValidToken_ReturnsSuccess()
    {
        // NOTE: This test requires IOidcExchangeService or IEmailVerificationService
        // to be mocked in WebApplicationFactory to validate the token.
        // Will return 404 until endpoints are implemented.

        // Arrange
        var request = new VerifyEmailRequest
        {
            Token = "valid-verification-token"
        };

        // Act
        var response = await _unauthenticatedClient.PostAsJsonAsync("/api/auth/verify-email", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<EmailVerificationResponse>();
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task PostVerifyEmail_InvalidToken_Returns400()
    {
        // Arrange
        var request = new VerifyEmailRequest
        {
            Token = "invalid-or-expired-token"
        };

        // Act
        var response = await _unauthenticatedClient.PostAsJsonAsync("/api/auth/verify-email", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostVerifyEmail_ExpiredToken_Returns400()
    {
        // Arrange
        var request = new VerifyEmailRequest
        {
            Token = "expired-verification-token"
        };

        // Act
        var response = await _unauthenticatedClient.PostAsJsonAsync("/api/auth/verify-email", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region POST /api/auth/resend-verification Tests

    [Fact]
    public async Task PostResendVerification_Authenticated_ReturnsSuccess()
    {
        // NOTE: This test requires IEmailVerificationService to be mocked
        // in WebApplicationFactory to handle email sending.

        // Arrange
        var request = new ResendVerificationRequest
        {
            Email = "admin@test-org.sorcha.io"
        };

        // Act
        var response = await _authenticatedClient.PostAsJsonAsync("/api/auth/resend-verification", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PostResendVerification_Unauthenticated_Returns401()
    {
        // Arrange
        var request = new ResendVerificationRequest
        {
            Email = "admin@test-org.sorcha.io"
        };

        // Act
        var response = await _unauthenticatedClient.PostAsJsonAsync("/api/auth/resend-verification", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion
}
