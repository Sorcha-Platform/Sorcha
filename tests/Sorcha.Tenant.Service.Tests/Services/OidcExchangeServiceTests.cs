// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

// TODO: Interface IOidcExchangeService to be created in Sorcha.Tenant.Service.Services
// TODO: Implementation OidcExchangeService to be created in Sorcha.Tenant.Service.Services

using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Models.Dtos;
using Sorcha.Tenant.Service.Services;
using Sorcha.Tenant.Service.Tests.Helpers;
using Xunit;

namespace Sorcha.Tenant.Service.Tests.Services;

public class OidcExchangeServiceTests : IDisposable
{
    private readonly TenantDbContext _dbContext;
    private readonly Mock<IOidcDiscoveryService> _discoveryServiceMock;
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;
    private readonly Mock<IDistributedCache> _cacheMock;
    private readonly Mock<ILogger<OidcExchangeService>> _loggerMock;

    private static readonly Guid TestOrgId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly IdentityProviderConfiguration TestIdpConfig = new()
    {
        Id = Guid.NewGuid(),
        OrganizationId = TestOrgId,
        ProviderPreset = IdentityProviderType.GenericOidc,
        IssuerUrl = "https://login.example.com/tenant-id/v2.0",
        ClientId = "test-client-id",
        ClientSecretEncrypted = Encoding.UTF8.GetBytes("encrypted-secret"),
        Scopes = ["openid", "profile", "email"],
        AuthorizationEndpoint = "https://login.example.com/authorize",
        TokenEndpoint = "https://login.example.com/token",
        JwksUri = "https://login.example.com/.well-known/jwks.json",
        IsEnabled = true,
        DisplayName = "Test Provider"
    };

    public OidcExchangeServiceTests()
    {
        _dbContext = InMemoryDbContextFactory.Create();
        _discoveryServiceMock = new Mock<IOidcDiscoveryService>();
        _httpClientFactoryMock = new Mock<IHttpClientFactory>();
        _cacheMock = new Mock<IDistributedCache>();
        _loggerMock = new Mock<ILogger<OidcExchangeService>>();

        // Seed test organization with IDP config
        SeedTestData();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        GC.SuppressFinalize(this);
    }

    #region Helper Methods

    /// <summary>
    /// Creates the service under test.
    /// TODO: Uncomment once OidcExchangeService implementation exists.
    /// </summary>
    private OidcExchangeService CreateService()
    {
        // TODO: Replace with actual constructor once implementation is created
        return new OidcExchangeService(
            _dbContext,
            _discoveryServiceMock.Object,
            _httpClientFactoryMock.Object,
            _cacheMock.Object,
            _loggerMock.Object
        );
    }

    private void SeedTestData()
    {
        var org = new Organization
        {
            Id = TestOrgId,
            Name = "Test Organization",
            Subdomain = "testorg",
            Status = OrganizationStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _dbContext.Organizations.Add(org);

        var idpConfig = new IdentityProviderConfiguration
        {
            Id = TestIdpConfig.Id,
            OrganizationId = TestOrgId,
            ProviderPreset = TestIdpConfig.ProviderPreset,
            IssuerUrl = TestIdpConfig.IssuerUrl,
            ClientId = TestIdpConfig.ClientId,
            ClientSecretEncrypted = TestIdpConfig.ClientSecretEncrypted,
            Scopes = TestIdpConfig.Scopes,
            AuthorizationEndpoint = TestIdpConfig.AuthorizationEndpoint,
            TokenEndpoint = TestIdpConfig.TokenEndpoint,
            JwksUri = TestIdpConfig.JwksUri,
            IsEnabled = true,
            DisplayName = TestIdpConfig.DisplayName
        };

        _dbContext.IdentityProviderConfigurations.Add(idpConfig);
        _dbContext.SaveChanges();
    }

    private void SetupCacheWithState(string state, object stateData)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(stateData);
        _cacheMock
            .Setup(c => c.GetAsync($"oidc:state:{state}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(json);
    }

    #endregion

    #region GenerateAuthorizationUrlAsync Tests

    [Fact]
    public async Task GenerateAuthorizationUrlAsync_ValidOrg_ReturnsUrlWithRequiredOidcParams()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.GenerateAuthorizationUrlAsync(TestOrgId, redirectUrl: null);

        // Assert
        result.Should().NotBeNull();
        result.AuthorizationUrl.Should().NotBeNullOrEmpty();

        var uri = new Uri(result.AuthorizationUrl);
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);

        query["response_type"].Should().Be("code");
        query["client_id"].Should().Be("test-client-id");
        query["redirect_uri"].Should().NotBeNullOrEmpty();
        query["scope"].Should().Contain("openid");
        query["state"].Should().NotBeNullOrEmpty();
        query["nonce"].Should().NotBeNullOrEmpty();
        query["code_challenge"].Should().NotBeNullOrEmpty();
        query["code_challenge_method"].Should().Be("S256");
    }

    [Fact]
    public async Task GenerateAuthorizationUrlAsync_ValidOrg_StoresStateInCache()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.GenerateAuthorizationUrlAsync(TestOrgId, redirectUrl: null);

        // Assert
        result.State.Should().NotBeNullOrEmpty();

        _cacheMock.Verify(
            c => c.SetAsync(
                It.Is<string>(key => key.StartsWith("oidc:state:")),
                It.IsAny<byte[]>(),
                It.Is<DistributedCacheEntryOptions>(opts =>
                    opts.AbsoluteExpirationRelativeToNow == TimeSpan.FromMinutes(10)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GenerateAuthorizationUrlAsync_WithCustomRedirectUrl_IncludesRedirectInUrl()
    {
        // Arrange
        var service = CreateService();
        var customRedirect = "https://app.example.com/callback";

        // Act
        var result = await service.GenerateAuthorizationUrlAsync(TestOrgId, redirectUrl: customRedirect);

        // Assert
        result.AuthorizationUrl.Should().NotBeNullOrEmpty();
        var uri = new Uri(result.AuthorizationUrl);
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        query["redirect_uri"].Should().Contain("callback");
    }

    [Fact]
    public async Task GenerateAuthorizationUrlAsync_NoIdpConfigured_ThrowsInvalidOperationException()
    {
        // Arrange
        var orgWithoutIdp = Guid.NewGuid();
        var service = CreateService();

        // Act
        var act = () => service.GenerateAuthorizationUrlAsync(orgWithoutIdp, redirectUrl: null);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*identity provider*");
    }

    #endregion

    #region ExchangeCodeAsync Tests

    [Fact]
    public async Task ExchangeCodeAsync_ValidCode_ReturnsSuccessResult()
    {
        // Arrange
        var state = Guid.NewGuid().ToString("N");
        var stateData = new
        {
            OrgId = TestOrgId,
            Nonce = Guid.NewGuid().ToString("N"),
            CodeVerifier = "test-code-verifier-with-sufficient-length-for-pkce",
            RedirectUri = "https://app.example.com/callback",
            CreatedAt = DateTimeOffset.UtcNow
        };
        SetupCacheWithState(state, stateData);

        // Mock HTTP client for token endpoint
        var tokenResponse = new
        {
            access_token = "external-access-token",
            id_token = "eyJhbGciOiJSUzI1NiJ9.eyJzdWIiOiJ1c2VyMTIzIiwiZW1haWwiOiJ1c2VyQGV4YW1wbGUuY29tIiwiZW1haWxfdmVyaWZpZWQiOnRydWUsIm5hbWUiOiJUZXN0IFVzZXIifQ.fake-signature",
            token_type = "Bearer",
            expires_in = 3600
        };

        var handler = new MockHttpMessageHandler(
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(tokenResponse),
                    Encoding.UTF8,
                    "application/json")
            });

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://login.example.com") };
        _httpClientFactoryMock
            .Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(httpClient);

        var service = CreateService();

        // Act
        var result = await service.ExchangeCodeAsync("valid-auth-code", state, "testorg");

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task ExchangeCodeAsync_InvalidState_ThrowsInvalidOperationException()
    {
        // Arrange
        var unknownState = Guid.NewGuid().ToString("N");
        _cacheMock
            .Setup(c => c.GetAsync($"oidc:state:{unknownState}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        var service = CreateService();

        // Act
        var act = () => service.ExchangeCodeAsync("some-code", unknownState, "testorg");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*state*");
    }

    [Fact]
    public async Task ExchangeCodeAsync_TokenEndpointError_ReturnsFailureResult()
    {
        // Arrange
        var state = Guid.NewGuid().ToString("N");
        var stateData = new
        {
            OrgId = TestOrgId,
            Nonce = Guid.NewGuid().ToString("N"),
            CodeVerifier = "test-code-verifier-with-sufficient-length-for-pkce",
            RedirectUri = "https://app.example.com/callback",
            CreatedAt = DateTimeOffset.UtcNow
        };
        SetupCacheWithState(state, stateData);

        var handler = new MockHttpMessageHandler(
            new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new { error = "invalid_grant", error_description = "Code expired" }),
                    Encoding.UTF8,
                    "application/json")
            });

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://login.example.com") };
        _httpClientFactoryMock
            .Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(httpClient);

        var service = CreateService();

        // Act
        var result = await service.ExchangeCodeAsync("expired-code", state, "testorg");

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
    }

    #endregion

    #region ValidateIdTokenAsync Tests

    [Fact]
    public async Task ValidateIdTokenAsync_ValidToken_ReturnsClaims()
    {
        // Arrange
        // Note: In a real test, this would use a properly signed JWT.
        // This test validates the happy path structure; actual JWT validation
        // requires a matching JWKS endpoint mock with real RSA keys.
        var service = CreateService();

        // TODO: Create a properly signed test JWT once implementation exists.
        // For now, this test documents the expected behavior.
        var validIdToken = CreateTestJwt(
            sub: "user-123",
            email: "user@example.com",
            name: "Test User",
            issuer: TestIdpConfig.IssuerUrl,
            audience: TestIdpConfig.ClientId,
            expiry: DateTimeOffset.UtcNow.AddHours(1));

        // Mock JWKS endpoint for signature validation
        SetupJwksEndpoint();

        // Act & Assert — adjust once real implementation available
        // The test verifies the method returns OidcUserClaims with correct fields
        var result = await service.ValidateIdTokenAsync(validIdToken, TestIdpConfig);

        result.Should().NotBeNull();
        result.Subject.Should().Be("user-123");
        result.Email.Should().Be("user@example.com");
        result.DisplayName.Should().Be("Test User");
    }

    [Fact]
    public async Task ValidateIdTokenAsync_ExpiredToken_ThrowsSecurityException()
    {
        // Arrange
        var service = CreateService();

        var expiredToken = CreateTestJwt(
            sub: "user-123",
            email: "user@example.com",
            name: "Test User",
            issuer: TestIdpConfig.IssuerUrl,
            audience: TestIdpConfig.ClientId,
            expiry: DateTimeOffset.UtcNow.AddHours(-1));

        SetupJwksEndpoint();

        // Act
        var act = () => service.ValidateIdTokenAsync(expiredToken, TestIdpConfig);

        // Assert
        await act.Should().ThrowAsync<Exception>()
            .Where(e => e.Message.Contains("expired") || e.Message.Contains("token"));
    }

    [Fact]
    public async Task ValidateIdTokenAsync_WrongIssuer_ThrowsSecurityException()
    {
        // Arrange
        var service = CreateService();

        var wrongIssuerConfig = new IdentityProviderConfiguration
        {
            Id = Guid.NewGuid(),
            OrganizationId = TestOrgId,
            IssuerUrl = "https://wrong-issuer.example.com",
            ClientId = TestIdpConfig.ClientId,
            ClientSecretEncrypted = TestIdpConfig.ClientSecretEncrypted,
            Scopes = TestIdpConfig.Scopes,
            JwksUri = TestIdpConfig.JwksUri,
            IsEnabled = true
        };

        var token = CreateTestJwt(
            sub: "user-123",
            email: "user@example.com",
            name: "Test User",
            issuer: TestIdpConfig.IssuerUrl, // Issued by original, not wrong-issuer
            audience: TestIdpConfig.ClientId,
            expiry: DateTimeOffset.UtcNow.AddHours(1));

        SetupJwksEndpoint();

        // Act
        var act = () => service.ValidateIdTokenAsync(token, wrongIssuerConfig);

        // Assert
        await act.Should().ThrowAsync<Exception>()
            .Where(e => e.Message.Contains("issuer") || e.Message.Contains("token"));
    }

    #endregion

    #region Test JWT Helpers

    /// <summary>
    /// Creates a test JWT string for validation tests.
    /// TODO: Replace with real RSA-signed JWT once implementation is wired up.
    /// This placeholder produces a structurally valid but unsigned JWT.
    /// </summary>
    private static string CreateTestJwt(
        string sub,
        string email,
        string name,
        string issuer,
        string audience,
        DateTimeOffset expiry)
    {
        var header = Convert.ToBase64String(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { alg = "RS256", typ = "JWT" })))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var payload = Convert.ToBase64String(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
            {
                sub,
                email,
                email_verified = true,
                name,
                iss = issuer,
                aud = audience,
                exp = expiry.ToUnixTimeSeconds(),
                iat = DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeSeconds(),
                nonce = Guid.NewGuid().ToString("N")
            })))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        // Unsigned — real tests will need RSA-signed tokens
        return $"{header}.{payload}.fake-signature";
    }

    /// <summary>
    /// Sets up a mock JWKS endpoint response.
    /// TODO: Populate with real RSA public key once implementation validates signatures.
    /// </summary>
    private void SetupJwksEndpoint()
    {
        var jwksJson = JsonSerializer.Serialize(new { keys = Array.Empty<object>() });

        var handler = new MockHttpMessageHandler(
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(jwksJson, Encoding.UTF8, "application/json")
            });

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://login.example.com") };
        _httpClientFactoryMock
            .Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(httpClient);
    }

    #endregion

    #region MockHttpMessageHandler

    /// <summary>
    /// Simple HTTP message handler mock that returns a preconfigured response.
    /// </summary>
    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;

        public MockHttpMessageHandler(HttpResponseMessage response)
        {
            _response = response;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_response);
        }
    }

    #endregion
}
