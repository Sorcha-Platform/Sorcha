// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Sorcha.Tenant.Service.Models.Dtos;
using Sorcha.Tenant.Service.Tests.Infrastructure;

namespace Sorcha.Tenant.Service.Tests.Integration;

/// <summary>
/// Full-flow integration tests for the OIDC authentication pipeline.
/// Tests end-to-end flows: initiate → callback → JWT issuance, profile completion, and email verification.
/// Uses mocked IOidcExchangeService/IOidcProvisioningService/IEmailVerificationService in WebApplicationFactory.
/// </summary>
public class OidcIntegrationTests : IClassFixture<TenantServiceWebApplicationFactory>, IAsyncLifetime
{
    private readonly TenantServiceWebApplicationFactory _factory;
    private HttpClient _unauthenticatedClient = null!;
    private HttpClient _authenticatedClient = null!;

    private const string TestOrgSubdomain = "test-org";

    public OidcIntegrationTests(TenantServiceWebApplicationFactory factory)
    {
        _factory = factory;
    }

    public async ValueTask InitializeAsync()
    {
        _unauthenticatedClient = _factory.CreateUnauthenticatedClient();
        _authenticatedClient = _factory.CreateMemberClient();
        await _factory.SeedTestDataAsync();
    }

    public ValueTask DisposeAsync()
    {
        _unauthenticatedClient?.Dispose();
        _authenticatedClient?.Dispose();
        return ValueTask.CompletedTask;
    }

    #region Full OIDC Flow: Initiate → Callback → JWT

    [Fact]
    public async Task OidcFullFlow_InitiateAndCallback_IssuesJwt()
    {
        // Step 1: Initiate OIDC login flow
        var initiateRequest = new OidcInitiateRequest
        {
            OrgSubdomain = TestOrgSubdomain,
            RedirectUrl = "https://app.sorcha.io/callback"
        };

        var initiateResponse = await _unauthenticatedClient.PostAsJsonAsync(
            "/api/auth/oidc/initiate", initiateRequest);
        initiateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var initiateResult = await initiateResponse.Content.ReadFromJsonAsync<OidcInitiateResponse>();
        initiateResult.Should().NotBeNull();
        initiateResult!.AuthorizationUrl.Should().Contain("authorize");
        initiateResult.State.Should().NotBeNullOrWhiteSpace();

        // Step 2: Simulate IDP callback with valid authorization code
        // The mock IOidcExchangeService recognizes "valid-auth-code" + "valid-state-token"
        var callbackResponse = await _unauthenticatedClient.GetAsync(
            $"/api/auth/callback/{TestOrgSubdomain}?code=valid-auth-code&state=valid-state-token");
        callbackResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var callbackResult = await callbackResponse.Content.ReadFromJsonAsync<OidcCallbackResult>();
        callbackResult.Should().NotBeNull();
        callbackResult!.Success.Should().BeTrue();
        callbackResult.UserId.Should().NotBeNull();

        // Step 3: Verify JWT was issued (the mock returns a user with complete profile + no 2FA)
        callbackResult.AccessToken.Should().NotBeNullOrWhiteSpace("full JWT should be issued for complete user");
        callbackResult.RefreshToken.Should().NotBeNullOrWhiteSpace();
        callbackResult.ExpiresIn.Should().BeGreaterThan(0);
        callbackResult.Requires2FA.Should().BeFalse();
        callbackResult.RequiresProfileCompletion.Should().BeFalse();
    }

    [Fact]
    public async Task OidcFullFlow_InitiateWithInvalidOrg_Returns404()
    {
        var initiateRequest = new OidcInitiateRequest
        {
            OrgSubdomain = "nonexistent-org",
            RedirectUrl = "https://app.sorcha.io/callback"
        };

        var response = await _unauthenticatedClient.PostAsJsonAsync(
            "/api/auth/oidc/initiate", initiateRequest);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task OidcFullFlow_CallbackWithInvalidState_Returns400()
    {
        // Attempt callback with tampered state — should be rejected
        var response = await _unauthenticatedClient.GetAsync(
            $"/api/auth/callback/{TestOrgSubdomain}?code=valid-auth-code&state=invalid-or-tampered-state");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var result = await response.Content.ReadFromJsonAsync<OidcCallbackResult>();
        result.Should().NotBeNull();
        result!.Success.Should().BeFalse();
        result.Error.Should().Contain("Authentication failed");
    }

    [Fact]
    public async Task OidcFullFlow_CallbackWithMissingCode_Returns400()
    {
        var response = await _unauthenticatedClient.GetAsync(
            $"/api/auth/callback/{TestOrgSubdomain}?state=valid-state-token");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var result = await response.Content.ReadFromJsonAsync<OidcCallbackResult>();
        result.Should().NotBeNull();
        result!.Success.Should().BeFalse();
        result.Error.Should().Contain("code");
    }

    [Fact]
    public async Task OidcFullFlow_CallbackWithMissingState_Returns400()
    {
        var response = await _unauthenticatedClient.GetAsync(
            $"/api/auth/callback/{TestOrgSubdomain}?code=valid-auth-code");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var result = await response.Content.ReadFromJsonAsync<OidcCallbackResult>();
        result.Should().NotBeNull();
        result!.Success.Should().BeFalse();
        result.Error.Should().Contain("state");
    }

    #endregion

    #region Profile Completion Flow

    [Fact]
    public async Task ProfileCompletion_AuthenticatedUser_UpdatesAndIssuesJwt()
    {
        // Simulate a user who completed OIDC but needs to fill missing profile fields
        var request = new OidcCompleteProfileRequest
        {
            DisplayName = "Jane Doe",
            Email = "jane.doe@test-org.sorcha.io"
        };

        var response = await _authenticatedClient.PostAsJsonAsync(
            "/api/auth/oidc/complete-profile", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<OidcCallbackResult>();
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
        result.UserId.Should().NotBeNull();
    }

    [Fact]
    public async Task ProfileCompletion_Unauthenticated_Returns401()
    {
        var request = new OidcCompleteProfileRequest
        {
            DisplayName = "Jane Doe",
            Email = "jane.doe@test-org.sorcha.io"
        };

        var response = await _unauthenticatedClient.PostAsJsonAsync(
            "/api/auth/oidc/complete-profile", request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region Email Verification Flow

    [Fact]
    public async Task EmailVerification_FullFlow_VerifiesAndConfirms()
    {
        // Step 1: Verify email with a valid token
        var verifyRequest = new VerifyEmailRequest
        {
            Token = "valid-verification-token"
        };

        var verifyResponse = await _unauthenticatedClient.PostAsJsonAsync(
            "/api/auth/verify-email", verifyRequest);
        verifyResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var verifyResult = await verifyResponse.Content.ReadFromJsonAsync<EmailVerificationResponse>();
        verifyResult.Should().NotBeNull();
        verifyResult!.Success.Should().BeTrue();
        verifyResult.Message.Should().Contain("verified");
    }

    [Fact]
    public async Task EmailVerification_ExpiredToken_Returns400()
    {
        var request = new VerifyEmailRequest
        {
            Token = "expired-verification-token"
        };

        var response = await _unauthenticatedClient.PostAsJsonAsync("/api/auth/verify-email", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var result = await response.Content.ReadFromJsonAsync<EmailVerificationResponse>();
        result.Should().NotBeNull();
        result!.Success.Should().BeFalse();
        result.Message.Should().Contain("expired");
    }

    [Fact]
    public async Task EmailVerification_ResendAuthenticated_SendsEmail()
    {
        var request = new ResendVerificationRequest
        {
            Email = "member@test-org.sorcha.io"
        };

        var response = await _authenticatedClient.PostAsJsonAsync(
            "/api/auth/resend-verification", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<EmailVerificationResponse>();
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
    }

    [Fact]
    public async Task EmailVerification_ResendUnauthenticated_Returns401()
    {
        var request = new ResendVerificationRequest
        {
            Email = "member@test-org.sorcha.io"
        };

        var response = await _unauthenticatedClient.PostAsJsonAsync(
            "/api/auth/resend-verification", request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region Cross-Cutting Concerns

    [Fact]
    public async Task OidcEndpoints_AnonymousAccess_AllowedForPublicEndpoints()
    {
        // Verify public endpoints don't require auth
        var initiateResponse = await _unauthenticatedClient.PostAsJsonAsync(
            "/api/auth/oidc/initiate",
            new OidcInitiateRequest { OrgSubdomain = TestOrgSubdomain });
        initiateResponse.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized,
            "POST /api/auth/oidc/initiate should be AllowAnonymous");

        var callbackResponse = await _unauthenticatedClient.GetAsync(
            $"/api/auth/callback/{TestOrgSubdomain}?code=valid-auth-code&state=valid-state-token");
        callbackResponse.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized,
            "GET /api/auth/callback should be AllowAnonymous");

        var verifyResponse = await _unauthenticatedClient.PostAsJsonAsync(
            "/api/auth/verify-email",
            new VerifyEmailRequest { Token = "any-token" });
        verifyResponse.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized,
            "POST /api/auth/verify-email should be AllowAnonymous");
    }

    [Fact]
    public async Task OidcEndpoints_ProtectedAccess_RequiresAuthForSecuredEndpoints()
    {
        // Verify protected endpoints require auth
        var profileResponse = await _unauthenticatedClient.PostAsJsonAsync(
            "/api/auth/oidc/complete-profile",
            new OidcCompleteProfileRequest { DisplayName = "Test" });
        profileResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "POST /api/auth/oidc/complete-profile should RequireAuthorization");

        var resendResponse = await _unauthenticatedClient.PostAsJsonAsync(
            "/api/auth/resend-verification",
            new ResendVerificationRequest { Email = "test@test.com" });
        resendResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "POST /api/auth/resend-verification should RequireAuthorization");
    }

    #endregion
}
