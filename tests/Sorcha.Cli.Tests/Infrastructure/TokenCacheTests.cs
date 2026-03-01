using Sorcha.Cli.Infrastructure;
using Sorcha.Cli.Models;

namespace Sorcha.Cli.Tests.Infrastructure;

/// <summary>
/// Collection to ensure TokenCache tests run sequentially
/// (they share environment variables and file system state).
/// </summary>
[Collection("CliEnvironment")]
public class TokenCacheTests : IDisposable
{
    private readonly string _testConfigDir;
    private readonly WindowsDpapiEncryption _encryption;
    private readonly TokenCache _cache;

    public TokenCacheTests()
    {
        // Create a temporary directory for test token cache
        _testConfigDir = Path.Combine(Path.GetTempPath(), $"sorcha-cli-tests-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testConfigDir);

        // Override the config directory for testing
        Environment.SetEnvironmentVariable("SORCHA_CONFIG_DIR", _testConfigDir);

        _encryption = new WindowsDpapiEncryption();
        _cache = new TokenCache(_encryption);
    }

    public void Dispose()
    {
        // Clean up test directory
        if (Directory.Exists(_testConfigDir))
        {
            try
            {
                // Wait a moment for any file handles to be released
                Thread.Sleep(100);
                Directory.Delete(_testConfigDir, recursive: true);
            }
            catch (IOException)
            {
                // Ignore cleanup errors in tests
            }
        }

        Environment.SetEnvironmentVariable("SORCHA_CONFIG_DIR", null);
    }

    [Fact]
    public async Task SetAndGetAsync_ShouldStoreAndRetrieveToken()
    {
        // Arrange
        var entry = new TokenCacheEntry
        {
            AccessToken = "test-access-token",
            RefreshToken = "test-refresh-token",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            Profile = "test-profile",
            Subject = "test-user"
        };

        // Act
        await _cache.SetAsync("test-profile", entry);
        var retrieved = await _cache.GetAsync("test-profile");

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.AccessToken.Should().Be("test-access-token");
        retrieved.RefreshToken.Should().Be("test-refresh-token");
        retrieved.Profile.Should().Be("test-profile");
        retrieved.Subject.Should().Be("test-user");
    }

    [Fact]
    public async Task GetAsync_ShouldReturnNull_WhenTokenDoesNotExist()
    {
        // Act
        var retrieved = await _cache.GetAsync("nonexistent");

        // Assert
        retrieved.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_ShouldReturnNull_WhenTokenIsExpired()
    {
        // Arrange
        var entry = new TokenCacheEntry
        {
            AccessToken = "expired-token",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-10), // Expired 10 minutes ago
            Profile = "test-profile"
        };

        await _cache.SetAsync("test-profile", entry);

        // Act
        var retrieved = await _cache.GetAsync("test-profile");

        // Assert
        retrieved.Should().BeNull();
    }

    [Fact]
    public async Task ExistsAsync_ShouldReturnTrue_WhenValidTokenExists()
    {
        // Arrange
        var entry = new TokenCacheEntry
        {
            AccessToken = "valid-token",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            Profile = "test-profile"
        };

        await _cache.SetAsync("test-profile", entry);

        // Act
        var exists = await _cache.ExistsAsync("test-profile");

        // Assert
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_ShouldReturnFalse_WhenTokenIsExpired()
    {
        // Arrange
        var entry = new TokenCacheEntry
        {
            AccessToken = "expired-token",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            Profile = "test-profile"
        };

        await _cache.SetAsync("test-profile", entry);

        // Act
        var exists = await _cache.ExistsAsync("test-profile");

        // Assert
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task ClearAsync_ShouldRemoveToken()
    {
        // Arrange
        var entry = new TokenCacheEntry
        {
            AccessToken = "test-token",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            Profile = "test-profile"
        };

        await _cache.SetAsync("test-profile", entry);

        // Act
        await _cache.ClearAsync("test-profile");
        var retrieved = await _cache.GetAsync("test-profile");

        // Assert
        retrieved.Should().BeNull();
    }

    [Fact]
    public async Task ClearAllAsync_ShouldRemoveAllTokens()
    {
        // Arrange
        var entry1 = new TokenCacheEntry
        {
            AccessToken = "token1",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            Profile = "profile1"
        };

        var entry2 = new TokenCacheEntry
        {
            AccessToken = "token2",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            Profile = "profile2"
        };

        await _cache.SetAsync("profile1", entry1);
        await _cache.SetAsync("profile2", entry2);

        // Act
        await _cache.ClearAllAsync();
        var retrieved1 = await _cache.GetAsync("profile1");
        var retrieved2 = await _cache.GetAsync("profile2");

        // Assert
        retrieved1.Should().BeNull();
        retrieved2.Should().BeNull();
    }

    [Fact]
    public async Task ListCachedProfilesAsync_ShouldReturnAllProfiles()
    {
        // Arrange
        var entry1 = new TokenCacheEntry
        {
            AccessToken = "token1",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            Profile = "profile1"
        };

        var entry2 = new TokenCacheEntry
        {
            AccessToken = "token2",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            Profile = "profile2"
        };

        await _cache.SetAsync("profile1", entry1);
        await _cache.SetAsync("profile2", entry2);

        // Act
        var profiles = await _cache.ListCachedProfilesAsync();

        // Assert
        profiles.Should().Contain("profile1");
        profiles.Should().Contain("profile2");
    }
}
