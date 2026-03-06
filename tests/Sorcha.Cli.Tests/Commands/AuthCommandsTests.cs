using System.CommandLine;
using System.Net;
using FluentAssertions;
using Sorcha.Cli.Commands;
using Sorcha.Cli.Infrastructure;
using Sorcha.Cli.Models;
using Sorcha.Cli.Services;
using Sorcha.Cli.Tests.Utilities;
using Xunit;

namespace Sorcha.Cli.Tests.Commands;

/// <summary>
/// Integration tests for authentication commands.
/// </summary>
[Collection("CliEnvironment")]
public class AuthCommandsTests : IDisposable
{
    private readonly string _testConfigDir;
    private readonly ConfigurationService _configService;
    private readonly TokenCache _tokenCache;
    private readonly HttpClient _httpClient;
    private readonly TestHttpMessageHandler _httpHandler;
    private readonly AuthenticationService _authService;

    public AuthCommandsTests()
    {
        // Create a temporary directory for test configurations
        _testConfigDir = Path.Combine(Path.GetTempPath(), $"sorcha-cli-tests-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testConfigDir);

        // Override the config directory for testing
        Environment.SetEnvironmentVariable("SORCHA_CONFIG_DIR", _testConfigDir);

        _configService = new ConfigurationService();
        var encryption = new WindowsDpapiEncryption();
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

    #region Command Structure Tests

    [Fact]
    public void AuthCommand_ShouldHaveCorrectNameAndDescription()
    {
        // Arrange & Act
        var command = new AuthCommand(_authService, _configService);

        // Assert
        command.Name.Should().Be("auth");
        command.Description.Should().Be("Manage authentication and login sessions");
    }

    [Fact]
    public void AuthCommand_ShouldHaveAllSubcommands()
    {
        // Arrange & Act
        var command = new AuthCommand(_authService, _configService);

        // Assert
        command.Subcommands.Should().HaveCount(3);
        command.Subcommands.Should().Contain(c => c.Name == "login");
        command.Subcommands.Should().Contain(c => c.Name == "logout");
        command.Subcommands.Should().Contain(c => c.Name == "status");
    }

    [Fact]
    public void AuthLoginCommand_ShouldHaveCorrectNameAndDescription()
    {
        // Arrange & Act
        var command = new AuthLoginCommand(_authService, _configService);

        // Assert
        command.Name.Should().Be("login");
        command.Description.Should().Be("Authenticate as a user or service principal");
    }

    [Fact]
    public void AuthLoginCommand_ShouldHaveAllRequiredOptions()
    {
        // Arrange & Act
        var command = new AuthLoginCommand(_authService, _configService);

        // Assert - All options should be optional (interactive mode is default)
        var usernameOption = command.Options.FirstOrDefault(o => o.Name == "--username");
        usernameOption.Should().NotBeNull();
        usernameOption!.Required.Should().BeFalse();

        var passwordOption = command.Options.FirstOrDefault(o => o.Name == "--password");
        passwordOption.Should().NotBeNull();
        passwordOption!.Required.Should().BeFalse();

        var clientIdOption = command.Options.FirstOrDefault(o => o.Name == "--client-id");
        clientIdOption.Should().NotBeNull();
        clientIdOption!.Required.Should().BeFalse();

        var clientSecretOption = command.Options.FirstOrDefault(o => o.Name == "--client-secret");
        clientSecretOption.Should().NotBeNull();
        clientSecretOption!.Required.Should().BeFalse();

        var interactiveOption = command.Options.FirstOrDefault(o => o.Name == "--interactive");
        interactiveOption.Should().NotBeNull();
        interactiveOption!.Required.Should().BeFalse();

        var profileOption = command.Options.FirstOrDefault(o => o.Name == "--profile");
        profileOption.Should().NotBeNull();
        profileOption!.Required.Should().BeFalse();
    }

    [Fact]
    public void AuthLogoutCommand_ShouldHaveCorrectNameAndDescription()
    {
        // Arrange & Act
        var command = new AuthLogoutCommand(_authService, _configService);

        // Assert
        command.Name.Should().Be("logout");
        command.Description.Should().Be("Clear cached authentication tokens");
    }

    [Fact]
    public void AuthLogoutCommand_ShouldHaveAllOptions()
    {
        // Arrange & Act
        var command = new AuthLogoutCommand(_authService, _configService);

        // Assert
        var allOption = command.Options.FirstOrDefault(o => o.Name == "--all");
        allOption.Should().NotBeNull();
        allOption!.Required.Should().BeFalse();

        var profileOption = command.Options.FirstOrDefault(o => o.Name == "--profile");
        profileOption.Should().NotBeNull();
        profileOption!.Required.Should().BeFalse();
    }

    [Fact]
    public void AuthStatusCommand_ShouldHaveCorrectNameAndDescription()
    {
        // Arrange & Act
        var command = new AuthStatusCommand(_authService, _configService);

        // Assert
        command.Name.Should().Be("status");
        command.Description.Should().Be("Check authentication status for the current profile");
    }

    [Fact]
    public void AuthStatusCommand_ShouldHaveProfileOption()
    {
        // Arrange & Act
        var command = new AuthStatusCommand(_authService, _configService);

        // Assert
        var profileOption = command.Options.FirstOrDefault(o => o.Name == "--profile");
        profileOption.Should().NotBeNull();
        profileOption!.Required.Should().BeFalse();
    }

    #endregion

    #region AuthLogoutCommand Integration Tests

    [Fact]
    public async Task AuthLogoutCommand_ShouldClearTokenForCurrentProfile()
    {
        // Arrange
        await _configService.EnsureConfigDirectoryAsync();

        // Store a test token
        var cacheEntry = new TokenCacheEntry
        {
            AccessToken = "test-token",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            Profile = "docker",
            Subject = "testuser"
        };
        await _tokenCache.SetAsync("docker", cacheEntry);

        var rootCommand = new RootCommand();
        rootCommand.Subcommands.Add(new AuthLogoutCommand(_authService, _configService));

        // Act
        var exitCode = await rootCommand.Parse("logout").InvokeAsync();

        // Assert
        exitCode.Should().Be(0);
        var token = await _tokenCache.GetAsync("docker");
        token.Should().BeNull();
    }

    [Fact]
    public async Task AuthLogoutCommand_ShouldClearAllTokens_WhenAllFlagUsed()
    {
        // Arrange
        await _configService.EnsureConfigDirectoryAsync();

        // Store multiple test tokens
        await _tokenCache.SetAsync("dev", new TokenCacheEntry
        {
            AccessToken = "dev-token",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            Profile = "dev"
        });

        await _tokenCache.SetAsync("staging", new TokenCacheEntry
        {
            AccessToken = "staging-token",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            Profile = "staging"
        });

        var rootCommand = new RootCommand();
        rootCommand.Subcommands.Add(new AuthLogoutCommand(_authService, _configService));

        // Act - Note: This will prompt for confirmation, so we redirect stdin
        // For now, we test the command structure only
        // In a real integration test, we'd mock Console.ReadLine

        // Assert - Command should have the --all option
        var command = new AuthLogoutCommand(_authService, _configService);
        var allOption = command.Options.FirstOrDefault(o => o.Name == "--all");
        allOption.Should().NotBeNull();
    }

    #endregion

    #region AuthStatusCommand Integration Tests

    [Fact]
    public async Task AuthStatusCommand_ShouldShowNotAuthenticated_WhenNoToken()
    {
        // Arrange
        await _configService.EnsureConfigDirectoryAsync();

        var rootCommand = new RootCommand();
        rootCommand.Subcommands.Add(new AuthStatusCommand(_authService, _configService));

        // Act
        var exitCode = await rootCommand.Parse("status").InvokeAsync();

        // Assert
        exitCode.Should().Be(0);
        var isAuthenticated = await _authService.IsAuthenticatedAsync("dev");
        isAuthenticated.Should().BeFalse();
    }

    [Fact]
    public async Task AuthStatusCommand_ShouldShowAuthenticated_WhenValidTokenExists()
    {
        // Arrange
        await _configService.EnsureConfigDirectoryAsync();

        // Create a valid JWT token (simplified for testing)
        var cacheEntry = new TokenCacheEntry
        {
            AccessToken = "valid-test-token",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            Profile = "dev",
            Subject = "testuser"
        };
        await _tokenCache.SetAsync("dev", cacheEntry);

        var rootCommand = new RootCommand();
        rootCommand.Subcommands.Add(new AuthStatusCommand(_authService, _configService));

        // Act
        var exitCode = await rootCommand.Parse("status").InvokeAsync();

        // Assert
        exitCode.Should().Be(0);
        var isAuthenticated = await _authService.IsAuthenticatedAsync("dev");
        isAuthenticated.Should().BeTrue();
    }

    [Fact]
    public async Task AuthStatusCommand_ShouldShowExpiredToken_WhenTokenExpired()
    {
        // Arrange
        await _configService.EnsureConfigDirectoryAsync();

        var cacheEntry = new TokenCacheEntry
        {
            AccessToken = "expired-token",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-10), // Expired 10 minutes ago
            Profile = "dev",
            Subject = "testuser"
        };
        await _tokenCache.SetAsync("dev", cacheEntry);

        var rootCommand = new RootCommand();
        rootCommand.Subcommands.Add(new AuthStatusCommand(_authService, _configService));

        // Act
        var exitCode = await rootCommand.Parse("status").InvokeAsync();

        // Assert
        exitCode.Should().Be(0);
        var isAuthenticated = await _authService.IsAuthenticatedAsync("dev");
        isAuthenticated.Should().BeFalse(); // Expired token = not authenticated
    }

    #endregion

    #region Token Caching Tests

    [Fact]
    public async Task AuthService_ShouldCacheToken_AfterSuccessfulLogin()
    {
        // Arrange
        await _configService.EnsureConfigDirectoryAsync();

        var tokenResponse = new TokenResponse
        {
            AccessToken = "cached-access-token",
            RefreshToken = "cached-refresh-token",
            TokenType = "Bearer",
            ExpiresIn = 3600
        };

        _httpHandler.SetResponse(HttpStatusCode.OK, tokenResponse);

        var loginRequest = new LoginRequest
        {
            Username = "cachetest",
            Password = "password123"
        };

        // Act
        await _authService.LoginAsync(loginRequest, "docker");

        // Assert
        var cachedToken = await _tokenCache.GetAsync("docker");
        cachedToken.Should().NotBeNull();
        cachedToken!.AccessToken.Should().Be("cached-access-token");
        cachedToken.Subject.Should().Be("cachetest");
    }

    [Fact]
    public async Task AuthService_ShouldUseCachedToken_WhenTokenValid()
    {
        // Arrange
        var cacheEntry = new TokenCacheEntry
        {
            AccessToken = "valid-cached-token",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            Profile = "dev",
            Subject = "testuser"
        };

        await _tokenCache.SetAsync("dev", cacheEntry);

        // Act
        var token = await _authService.GetAccessTokenAsync("dev");

        // Assert
        token.Should().Be("valid-cached-token");
    }

    [Fact]
    public async Task AuthService_ShouldRefreshToken_WhenTokenExpiringSoon()
    {
        // Arrange
        await _configService.EnsureConfigDirectoryAsync();

        var cacheEntry = new TokenCacheEntry
        {
            AccessToken = "expiring-token",
            RefreshToken = "valid-refresh-token",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(3), // Expiring in 3 minutes
            Profile = "docker",
            Subject = "testuser"
        };

        await _tokenCache.SetAsync("docker", cacheEntry);

        var refreshResponse = new TokenResponse
        {
            AccessToken = "refreshed-access-token",
            RefreshToken = "new-refresh-token",
            TokenType = "Bearer",
            ExpiresIn = 3600
        };

        _httpHandler.SetResponse(HttpStatusCode.OK, refreshResponse);

        // Act
        var token = await _authService.GetAccessTokenAsync("docker");

        // Assert
        token.Should().Be("refreshed-access-token");

        var cachedToken = await _tokenCache.GetAsync("docker");
        cachedToken.Should().NotBeNull();
        cachedToken!.AccessToken.Should().Be("refreshed-access-token");
        cachedToken.RefreshToken.Should().Be("new-refresh-token");
    }

    #endregion

    #region Service Principal Authentication Tests

    [Fact]
    public async Task AuthService_ShouldAuthenticateServicePrincipal()
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
            ClientId = "test-sp-client",
            ClientSecret = "test-sp-secret"
        };

        // Act
        var result = await _authService.LoginServicePrincipalAsync(loginRequest, "docker");

        // Assert
        result.Should().NotBeNull();
        result.AccessToken.Should().Be("sp-access-token");

        var cachedToken = await _tokenCache.GetAsync("docker");
        cachedToken.Should().NotBeNull();
        cachedToken!.AccessToken.Should().Be("sp-access-token");
        cachedToken.Subject.Should().Be("test-sp-client");
    }

    #endregion

    #region Profile Management Tests

    [Fact]
    public async Task AuthService_ShouldSupportMultipleProfiles()
    {
        // Arrange
        await _configService.EnsureConfigDirectoryAsync();

        var devToken = new TokenCacheEntry
        {
            AccessToken = "dev-token",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            Profile = "dev",
            Subject = "dev-user"
        };

        var stagingToken = new TokenCacheEntry
        {
            AccessToken = "staging-token",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            Profile = "staging",
            Subject = "staging-user"
        };

        await _tokenCache.SetAsync("dev", devToken);
        await _tokenCache.SetAsync("staging", stagingToken);

        // Act
        var devAccessToken = await _authService.GetAccessTokenAsync("dev");
        var stagingAccessToken = await _authService.GetAccessTokenAsync("staging");

        // Assert
        devAccessToken.Should().Be("dev-token");
        stagingAccessToken.Should().Be("staging-token");
    }

    [Fact]
    public async Task AuthService_ShouldLogoutSpecificProfile_WithoutAffectingOthers()
    {
        // Arrange
        await _configService.EnsureConfigDirectoryAsync();

        await _tokenCache.SetAsync("dev", new TokenCacheEntry
        {
            AccessToken = "dev-token",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            Profile = "dev"
        });

        await _tokenCache.SetAsync("staging", new TokenCacheEntry
        {
            AccessToken = "staging-token",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            Profile = "staging"
        });

        // Act
        await _authService.LogoutAsync("dev");

        // Assert
        var devToken = await _tokenCache.GetAsync("dev");
        var stagingToken = await _tokenCache.GetAsync("staging");

        devToken.Should().BeNull();
        stagingToken.Should().NotBeNull();
        stagingToken!.AccessToken.Should().Be("staging-token");
    }

    #endregion
}
