using System.Net;
using System.Text.Json;
using Sorcha.Cli.Infrastructure;
using Sorcha.Cli.Models;
using Sorcha.Cli.Services;
using Sorcha.Cli.Tests.Utilities;
using Sorcha.Tenant.Models.Auth;

namespace Sorcha.Cli.Tests.Services;

[Collection("CliEnvironment")]
public class AuthenticationServiceTests : IDisposable
{
    private readonly string _testConfigDir;
    private readonly ConfigurationService _configService;
    private readonly TokenCache _tokenCache;
    private readonly HttpClient _httpClient;
    private readonly TestHttpMessageHandler _httpHandler;
    private readonly AuthenticationService _authService;

    public AuthenticationServiceTests()
    {
        // Create a temporary directory for test configurations
        _testConfigDir = Path.Combine(Path.GetTempPath(), $"sorcha-cli-tests-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testConfigDir);

        // Override the config directory for testing
        Environment.SetEnvironmentVariable("SORCHA_CONFIG_DIR", _testConfigDir);

        _configService = new ConfigurationService();
        var encryption = new TestEncryptionProvider();
        _tokenCache = new TokenCache(encryption);

        _httpHandler = new TestHttpMessageHandler();
        _httpClient = new HttpClient(_httpHandler);

        _authService = new AuthenticationService(_configService, _tokenCache, _httpClient);
    }

    public void Dispose()
    {
        _httpClient.Dispose();

        // Clean up test directory
        if (Directory.Exists(_testConfigDir))
        {
            try
            {
                Thread.Sleep(100);
                Directory.Delete(_testConfigDir, recursive: true);
            }
            catch (IOException)
            {
                // Ignore cleanup errors
            }
        }

        Environment.SetEnvironmentVariable("SORCHA_CONFIG_DIR", null);
    }

    [Fact]
    public async Task LoginAsync_SingleOrgAccount_AuthenticatesDirectly_AndCachesToken()
    {
        // Arrange — the server returns a token directly for a single-org account.
        await _configService.EnsureConfigDirectoryAsync();

        var tokenResponse = new TokenResponse
        {
            AccessToken = "test-access-token",
            RefreshToken = "test-refresh-token",
            TokenType = "Bearer",
            ExpiresIn = 3600
        };

        _httpHandler.SetResponse(HttpStatusCode.OK, tokenResponse);

        var loginRequest = new UserLoginRequest
        {
            Email = "testuser@example.com",
            Password = "testpass"
        };

        // Act
        var result = await _authService.LoginAsync(loginRequest, "docker");

        // Assert
        result.Should().NotBeNull();
        result.AccessToken.Should().Be("test-access-token");
        result.RefreshToken.Should().Be("test-refresh-token");

        // Verify the request went to the JSON user-login endpoint, not the OAuth2 token endpoint.
        _httpHandler.Requests.Should().ContainSingle();
        _httpHandler.Requests[0].RequestUri!.AbsolutePath.Should().Be("/api/auth/login");

        // Verify token was cached
        var cachedToken = await _tokenCache.GetAsync("docker");
        cachedToken.Should().NotBeNull();
        cachedToken!.AccessToken.Should().Be("test-access-token");
        cachedToken.Subject.Should().Be("testuser@example.com");
    }

    [Fact]
    public async Task LoginAsync_MultiOrgAccount_NoOrganizationIdSupplied_ThrowsOrgSelectionRequired()
    {
        // Arrange — the server returns requires_org_selection instead of a token.
        await _configService.EnsureConfigDirectoryAsync();

        var orgId1 = Guid.NewGuid();
        var orgId2 = Guid.NewGuid();
        var orgSelectionResponse = new OrgSelectionResponse
        {
            RequiresOrgSelection = true,
            PlatformLoginToken = "platform-login-token",
            Organizations =
            [
                new OrgSelectionEntry { OrganizationId = orgId1, Name = "Org One", Subdomain = "org-one", Role = "Administrator" },
                new OrgSelectionEntry { OrganizationId = orgId2, Name = "Org Two", Subdomain = "org-two", Role = "Member" }
            ]
        };

        _httpHandler.SetResponse(HttpStatusCode.OK, orgSelectionResponse);

        var loginRequest = new UserLoginRequest { Email = "multi@example.com", Password = "testpass" };

        // Act
        var act = () => _authService.LoginAsync(loginRequest, "docker");

        // Assert
        var exception = await act.Should().ThrowAsync<OrgSelectionRequiredException>();
        exception.Which.PlatformLoginToken.Should().Be("platform-login-token");
        exception.Which.Organizations.Should().HaveCount(2);
        exception.Which.Organizations.Should().Contain(o => o.OrganizationId == orgId1 && o.Name == "Org One");

        // No token should have been cached — login did not complete.
        var cachedToken = await _tokenCache.GetAsync("docker");
        cachedToken.Should().BeNull();
    }

    [Fact]
    public async Task LoginAsync_MultiOrgAccount_OrganizationIdPreSelected_CompletesLoginInOneCall()
    {
        // Arrange — org selection required, but the caller pre-selected an org via --organization-id.
        await _configService.EnsureConfigDirectoryAsync();

        var orgId = Guid.NewGuid();
        var orgSelectionResponse = new OrgSelectionResponse
        {
            RequiresOrgSelection = true,
            PlatformLoginToken = "platform-login-token",
            Organizations = [new OrgSelectionEntry { OrganizationId = orgId, Name = "Pre-selected Org", Role = "Administrator" }]
        };

        _httpHandler.EnqueueResponse(HttpStatusCode.OK, orgSelectionResponse);

        var tokenResponse = new TokenResponse
        {
            AccessToken = "final-access-token",
            RefreshToken = "final-refresh-token",
            TokenType = "Bearer",
            ExpiresIn = 3600
        };
        _httpHandler.EnqueueResponse(HttpStatusCode.OK, tokenResponse);

        var loginRequest = new UserLoginRequest { Email = "preselect@example.com", Password = "testpass" };

        // Act
        var result = await _authService.LoginAsync(loginRequest, "docker", organizationId: orgId);

        // Assert
        result.AccessToken.Should().Be("final-access-token");

        _httpHandler.Requests.Should().HaveCount(2);
        _httpHandler.Requests[0].RequestUri!.AbsolutePath.Should().Be("/api/auth/login");
        _httpHandler.Requests[1].RequestUri!.AbsolutePath.Should().Be("/api/auth/select-org");

        var cachedToken = await _tokenCache.GetAsync("docker");
        cachedToken.Should().NotBeNull();
        cachedToken!.AccessToken.Should().Be("final-access-token");
        cachedToken.Subject.Should().Be("preselect@example.com");
    }

    [Fact]
    public async Task CompleteOrgSelectionAsync_ValidToken_ReturnsAndCachesFinalToken()
    {
        // Arrange
        await _configService.EnsureConfigDirectoryAsync();

        var tokenResponse = new TokenResponse
        {
            AccessToken = "select-org-access-token",
            TokenType = "Bearer",
            ExpiresIn = 3600
        };
        _httpHandler.SetResponse(HttpStatusCode.OK, tokenResponse);

        // Act
        var result = await _authService.CompleteOrgSelectionAsync(
            "platform-login-token", Guid.NewGuid(), "docker", "user@example.com");

        // Assert
        result.AccessToken.Should().Be("select-org-access-token");

        var cachedToken = await _tokenCache.GetAsync("docker");
        cachedToken.Should().NotBeNull();
        cachedToken!.AccessToken.Should().Be("select-org-access-token");
        cachedToken.Subject.Should().Be("user@example.com");
    }

    [Fact]
    public async Task LoginServicePrincipalAsync_ShouldAuthenticateServicePrincipal_AndCacheToken()
    {
        // Arrange
        await _configService.EnsureConfigDirectoryAsync();

        var tokenResponse = new TokenResponse
        {
            AccessToken = "sp-access-token",
            TokenType = "Bearer",
            ExpiresIn = 3600
        };

        _httpHandler.SetResponse(HttpStatusCode.OK, tokenResponse);

        var loginRequest = new ServicePrincipalLoginRequest
        {
            ClientId = "test-client-id",
            ClientSecret = "test-client-secret"
        };

        // Act
        var result = await _authService.LoginServicePrincipalAsync(loginRequest, "docker");

        // Assert
        result.Should().NotBeNull();
        result.AccessToken.Should().Be("sp-access-token");

        // Verify token was cached
        var cachedToken = await _tokenCache.GetAsync("docker");
        cachedToken.Should().NotBeNull();
        cachedToken!.AccessToken.Should().Be("sp-access-token");
        cachedToken.Subject.Should().Be("test-client-id");

        // Verify the request went to the INTERNAL-only token endpoint (#1397/#1406), not the
        // public /api/service-auth/token route.
        _httpHandler.Requests.Should().ContainSingle();
        _httpHandler.Requests[0].RequestUri!.AbsolutePath.Should().Be("/api/internal/service-auth/token");
    }

    [Fact]
    public async Task LoginServicePrincipalAsync_InternalEndpointUnreachable_SurfacesClearInternalNetworkError()
    {
        // Arrange — simulates the public API Gateway's catch-all 404 for /api/internal/* (#1397),
        // the situation a host-run CLI hits when it is outside the Sorcha trust network.
        await _configService.EnsureConfigDirectoryAsync();

        _httpHandler.SetResponse(HttpStatusCode.NotFound, null);

        var loginRequest = new ServicePrincipalLoginRequest
        {
            ClientId = "test-client-id",
            ClientSecret = "test-client-secret"
        };

        // Act
        var act = () => _authService.LoginServicePrincipalAsync(loginRequest, "docker");

        // Assert — the error must explain this is an internal-network operation, not a generic
        // "authentication failed" message (CLAUDE.md #18 honesty principle applied to auth).
        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain("INTERNAL-only");
        exception.Which.Message.Should().Contain("trust network");

        // No token should have been cached.
        var cachedToken = await _tokenCache.GetAsync("docker");
        cachedToken.Should().BeNull();
    }

    [Fact]
    public async Task GetAccessTokenAsync_ShouldReturnCachedToken_WhenValid()
    {
        // Arrange
        var cacheEntry = new TokenCacheEntry
        {
            AccessToken = "cached-token",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            Profile = "dev"
        };

        await _tokenCache.SetAsync("dev", cacheEntry);

        // Act
        var token = await _authService.GetAccessTokenAsync("dev");

        // Assert
        token.Should().Be("cached-token");
    }

    [Fact]
    public async Task GetAccessTokenAsync_ShouldReturnNull_WhenNotAuthenticated()
    {
        // Act
        var token = await _authService.GetAccessTokenAsync("dev");

        // Assert
        token.Should().BeNull();
    }

    [Fact]
    public async Task GetAccessTokenAsync_ShouldRefreshToken_WhenExpiringSoon()
    {
        // Arrange
        await _configService.EnsureConfigDirectoryAsync();

        var cacheEntry = new TokenCacheEntry
        {
            AccessToken = "old-token",
            RefreshToken = "refresh-token",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(3), // Expiring in 3 minutes
            Profile = "docker"
        };

        await _tokenCache.SetAsync("docker", cacheEntry);

        var refreshResponse = new TokenResponse
        {
            AccessToken = "new-refreshed-token",
            RefreshToken = "new-refresh-token",
            TokenType = "Bearer",
            ExpiresIn = 3600
        };

        _httpHandler.SetResponse(HttpStatusCode.OK, refreshResponse);

        // Act
        var token = await _authService.GetAccessTokenAsync("docker");

        // Assert
        token.Should().Be("new-refreshed-token");

        // Verify new token was cached
        var cachedToken = await _tokenCache.GetAsync("docker");
        cachedToken.Should().NotBeNull();
        cachedToken!.AccessToken.Should().Be("new-refreshed-token");
    }

    [Fact]
    public async Task RefreshTokenAsync_ShouldReturnNewToken_WhenRefreshSucceeds()
    {
        // Arrange
        await _configService.EnsureConfigDirectoryAsync();

        var cacheEntry = new TokenCacheEntry
        {
            AccessToken = "old-token",
            RefreshToken = "refresh-token",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10), // Still valid, but could be refreshed
            Profile = "docker"
        };

        await _tokenCache.SetAsync("docker", cacheEntry);

        var refreshResponse = new TokenResponse
        {
            AccessToken = "refreshed-token",
            RefreshToken = "new-refresh-token",
            TokenType = "Bearer",
            ExpiresIn = 3600
        };

        _httpHandler.SetResponse(HttpStatusCode.OK, refreshResponse);

        // Act
        var result = await _authService.RefreshTokenAsync("docker");

        // Assert
        result.Should().NotBeNull();
        result!.AccessToken.Should().Be("refreshed-token");
    }

    [Fact]
    public async Task RefreshTokenAsync_ShouldReturnNull_WhenNoRefreshToken()
    {
        // Arrange
        var cacheEntry = new TokenCacheEntry
        {
            AccessToken = "token-without-refresh",
            RefreshToken = null, // No refresh token
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10), // Still valid
            Profile = "dev"
        };

        await _tokenCache.SetAsync("dev", cacheEntry);

        // Act
        var result = await _authService.RefreshTokenAsync("dev");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task IsAuthenticatedAsync_ShouldReturnTrue_WhenValidTokenExists()
    {
        // Arrange
        var cacheEntry = new TokenCacheEntry
        {
            AccessToken = "valid-token",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            Profile = "dev"
        };

        await _tokenCache.SetAsync("dev", cacheEntry);

        // Act
        var isAuthenticated = await _authService.IsAuthenticatedAsync("dev");

        // Assert
        isAuthenticated.Should().BeTrue();
    }

    [Fact]
    public async Task IsAuthenticatedAsync_ShouldReturnFalse_WhenNotAuthenticated()
    {
        // Act
        var isAuthenticated = await _authService.IsAuthenticatedAsync("dev");

        // Assert
        isAuthenticated.Should().BeFalse();
    }

    [Fact]
    public async Task LogoutAsync_ShouldClearCachedToken()
    {
        // Arrange
        var cacheEntry = new TokenCacheEntry
        {
            AccessToken = "token-to-logout",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            Profile = "dev"
        };

        await _tokenCache.SetAsync("dev", cacheEntry);

        // Act
        await _authService.LogoutAsync("dev");

        // Assert
        var token = await _tokenCache.GetAsync("dev");
        token.Should().BeNull();
    }

    [Fact]
    public async Task LogoutAllAsync_ShouldClearAllCachedTokens()
    {
        // Arrange
        var entry1 = new TokenCacheEntry
        {
            AccessToken = "token1",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            Profile = "dev"
        };

        var entry2 = new TokenCacheEntry
        {
            AccessToken = "token2",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            Profile = "staging"
        };

        await _tokenCache.SetAsync("dev", entry1);
        await _tokenCache.SetAsync("staging", entry2);

        // Act
        await _authService.LogoutAllAsync();

        // Assert
        var token1 = await _tokenCache.GetAsync("dev");
        var token2 = await _tokenCache.GetAsync("staging");
        token1.Should().BeNull();
        token2.Should().BeNull();
    }
}
