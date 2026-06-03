// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
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
    private readonly Mock<IOidcSigningKeyResolver> _signingKeyResolverMock;
    private readonly ISecretProtectionProvider _protection;

    // RS256 test signing key — the production code verifies the ID-token JWS against the keys the
    // resolver returns, so test tokens are signed with this key and the resolver mock returns its public part.
    private const string TestKeyId = "test-signing-key-1";
    private static readonly RSA TestRsa = RSA.Create(2048);
    private static readonly RsaSecurityKey TestSigningKey = new(TestRsa) { KeyId = TestKeyId };

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
        _signingKeyResolverMock = new Mock<IOidcSigningKeyResolver>();
        _signingKeyResolverMock
            .Setup(r => r.GetSigningKeysAsync(It.IsAny<IdentityProviderConfiguration>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SecurityKey[] { TestSigningKey });

        var key = new byte[32];
        for (var i = 0; i < key.Length; i++) key[i] = (byte)(i + 7);
        _protection = new SoftwareSecretProtectionProvider(key, "test-key-v1");

        // Seed test organization with IDP config
        SeedTestData();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        GC.SuppressFinalize(this);
    }

    #region Helper Methods

    private OidcExchangeService CreateService(IOidcSigningKeyResolver? signingKeyResolver = null)
    {
        return new OidcExchangeService(
            _dbContext,
            _discoveryServiceMock.Object,
            _httpClientFactoryMock.Object,
            _cacheMock.Object,
            _protection,
            signingKeyResolver ?? _signingKeyResolverMock.Object,
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

        var (encryptedSecret, secretKeyId) = _protection
            .EncryptAsync(Encoding.UTF8.GetBytes("test-secret")).GetAwaiter().GetResult();
        var idpConfig = new IdentityProviderConfiguration
        {
            Id = TestIdpConfig.Id,
            OrganizationId = TestOrgId,
            ProviderPreset = TestIdpConfig.ProviderPreset,
            IssuerUrl = TestIdpConfig.IssuerUrl,
            ClientId = TestIdpConfig.ClientId,
            ClientSecretEncrypted = encryptedSecret,
            ClientSecretKeyId = secretKeyId,
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
    public async Task ExchangeCodeAsync_ValidCode_ReturnsSuccessResultWithClaims()
    {
        // Arrange
        var state = Guid.NewGuid().ToString("N");
        var nonce = Guid.NewGuid().ToString("N");

        // Use the proper OidcFlowState shape (matching the internal record)
        var stateData = new
        {
            OrgId = TestOrgId,
            Nonce = nonce,
            CodeVerifier = "test-code-verifier-with-sufficient-length-for-pkce",
            RedirectUri = "https://app.example.com/callback",
            PostLoginRedirectUrl = (string?)null,
            CreatedAt = DateTimeOffset.UtcNow
        };
        SetupCacheWithState(state, stateData);

        // Create a valid ID token matching the test IDP config (nonce must match flow state)
        var validIdToken = CreateTestJwt(
            sub: "user123",
            email: "user@example.com",
            name: "Test User",
            issuer: TestIdpConfig.IssuerUrl,
            audience: TestIdpConfig.ClientId,
            expiry: DateTimeOffset.UtcNow.AddHours(1),
            nonce: nonce);

        var tokenResponse = new
        {
            access_token = "external-access-token",
            id_token = validIdToken,
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
        result.Claims.Should().NotBeNull();
        result.Claims!.Subject.Should().Be("user123");
        result.Claims.Email.Should().Be("user@example.com");
        result.OrgId.Should().Be(TestOrgId);
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
            PostLoginRedirectUrl = (string?)null,
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
    public async Task ValidateIdTokenAsync_ValidSignedToken_ReturnsClaims()
    {
        // Arrange
        var service = CreateService();
        var expectedNonce = Guid.NewGuid().ToString("N");

        var validIdToken = CreateTestJwt(
            sub: "user-123",
            email: "user@example.com",
            name: "Test User",
            issuer: TestIdpConfig.IssuerUrl,
            audience: TestIdpConfig.ClientId,
            expiry: DateTimeOffset.UtcNow.AddHours(1),
            nonce: expectedNonce);

        // Act
        var result = await service.ValidateIdTokenAsync(validIdToken, TestIdpConfig, expectedNonce);

        // Assert
        result.Should().NotBeNull();
        result.Subject.Should().Be("user-123");
        result.Email.Should().Be("user@example.com");
        result.DisplayName.Should().Be("Test User");
    }

    [Fact]
    public async Task ValidateIdTokenAsync_TamperedSignature_Throws()
    {
        // Arrange — a validly-structured token whose signature is corrupted.
        var service = CreateService();
        var expectedNonce = Guid.NewGuid().ToString("N");
        var token = CreateTestJwt(
            sub: "user-123", email: "user@example.com", name: "Test User",
            issuer: TestIdpConfig.IssuerUrl, audience: TestIdpConfig.ClientId,
            expiry: DateTimeOffset.UtcNow.AddHours(1), nonce: expectedNonce);
        var parts = token.Split('.');
        var tampered = $"{parts[0]}.{parts[1]}.{parts[2][..^4]}AAAA"; // corrupt the signature segment

        // Act
        var act = () => service.ValidateIdTokenAsync(tampered, TestIdpConfig, expectedNonce);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*signature*");
    }

    [Fact]
    public async Task ValidateIdTokenAsync_NoSigningKeys_Throws_FailClosed()
    {
        // Arrange — the provider publishes no usable signing keys.
        var emptyResolver = new Mock<IOidcSigningKeyResolver>();
        emptyResolver
            .Setup(r => r.GetSigningKeysAsync(It.IsAny<IdentityProviderConfiguration>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<SecurityKey>());
        var service = CreateService(emptyResolver.Object);
        var expectedNonce = Guid.NewGuid().ToString("N");
        var token = CreateTestJwt(
            sub: "user-123", email: "user@example.com", name: "Test User",
            issuer: TestIdpConfig.IssuerUrl, audience: TestIdpConfig.ClientId,
            expiry: DateTimeOffset.UtcNow.AddHours(1), nonce: expectedNonce);

        // Act
        var act = () => service.ValidateIdTokenAsync(token, TestIdpConfig, expectedNonce);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*cannot be verified*");
    }

    [Fact]
    public async Task ValidateIdTokenAsync_KeyFetchFailure_Throws_FailClosed()
    {
        // Arrange — the resolver cannot obtain keys (e.g. JWKS unreachable).
        var failingResolver = new Mock<IOidcSigningKeyResolver>();
        failingResolver
            .Setup(r => r.GetSigningKeysAsync(It.IsAny<IdentityProviderConfiguration>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Failed to fetch JWKS."));
        var service = CreateService(failingResolver.Object);
        var expectedNonce = Guid.NewGuid().ToString("N");
        var token = CreateTestJwt(
            sub: "user-123", email: "user@example.com", name: "Test User",
            issuer: TestIdpConfig.IssuerUrl, audience: TestIdpConfig.ClientId,
            expiry: DateTimeOffset.UtcNow.AddHours(1), nonce: expectedNonce);

        // Act
        var act = () => service.ValidateIdTokenAsync(token, TestIdpConfig, expectedNonce);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ValidateIdTokenAsync_ExpiredToken_ThrowsSecurityException()
    {
        // Arrange
        var service = CreateService();
        var expectedNonce = Guid.NewGuid().ToString("N");

        var expiredToken = CreateTestJwt(
            sub: "user-123",
            email: "user@example.com",
            name: "Test User",
            issuer: TestIdpConfig.IssuerUrl,
            audience: TestIdpConfig.ClientId,
            expiry: DateTimeOffset.UtcNow.AddHours(-1),
            nonce: expectedNonce);


        // Act
        var act = () => service.ValidateIdTokenAsync(expiredToken, TestIdpConfig, expectedNonce);

        // Assert
        await act.Should().ThrowAsync<Exception>()
            .Where(e => e.Message.Contains("expired") || e.Message.Contains("token"));
    }

    [Fact]
    public async Task ValidateIdTokenAsync_WrongIssuer_ThrowsSecurityException()
    {
        // Arrange
        var service = CreateService();
        var expectedNonce = Guid.NewGuid().ToString("N");

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
            expiry: DateTimeOffset.UtcNow.AddHours(1),
            nonce: expectedNonce);


        // Act
        var act = () => service.ValidateIdTokenAsync(token, wrongIssuerConfig, expectedNonce);

        // Assert
        await act.Should().ThrowAsync<Exception>()
            .Where(e => e.Message.Contains("issuer") || e.Message.Contains("token"));
    }

    #endregion

    #region Test JWT Helpers

    /// <summary>
    /// Creates a real RS256-signed test JWT using <see cref="TestSigningKey"/>. The production code
    /// verifies the signature against the keys the resolver returns (the public part of this key), so
    /// these tokens validate. The payload JSON shape matches what <c>ValidateIdTokenAsync</c> expects.
    /// </summary>
    private static string CreateTestJwt(
        string sub,
        string email,
        string name,
        string issuer,
        string audience,
        DateTimeOffset expiry,
        string? nonce = null)
    {
        var header = B64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", typ = "JWT", kid = TestKeyId }));
        var payload = B64Url(JsonSerializer.SerializeToUtf8Bytes(new
        {
            sub,
            email,
            email_verified = true,
            name,
            iss = issuer,
            aud = audience,
            exp = expiry.ToUnixTimeSeconds(),
            iat = DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeSeconds(),
            nonce = nonce ?? Guid.NewGuid().ToString("N")
        }));

        var signingInput = $"{header}.{payload}";
        var signature = TestRsa.SignData(
            Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{signingInput}.{B64Url(signature)}";
    }

    private static string B64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

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
