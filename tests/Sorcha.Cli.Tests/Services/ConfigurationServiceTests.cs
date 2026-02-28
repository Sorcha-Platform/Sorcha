using Sorcha.Cli.Models;
using Sorcha.Cli.Services;

namespace Sorcha.Cli.Tests.Services;

/// <summary>
/// Collection to ensure ConfigurationService tests run sequentially
/// (they share environment variables and file system state).
/// </summary>
[Collection("CliEnvironment")]
public class ConfigurationServiceTests : IDisposable
{
    private readonly string _testConfigDir;
    private readonly string _originalConfigDir;
    private readonly ConfigurationService _service;

    public ConfigurationServiceTests()
    {
        // Create a temporary directory for test configurations
        _testConfigDir = Path.Combine(Path.GetTempPath(), $"sorcha-cli-tests-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testConfigDir);

        // Override the config directory for testing
        _originalConfigDir = Environment.GetEnvironmentVariable("SORCHA_CONFIG_DIR") ?? string.Empty;
        Environment.SetEnvironmentVariable("SORCHA_CONFIG_DIR", _testConfigDir);

        _service = new ConfigurationService();
    }

    public void Dispose()
    {
        // Restore original config directory
        Environment.SetEnvironmentVariable("SORCHA_CONFIG_DIR", _originalConfigDir);

        // Clean up test directory
        if (Directory.Exists(_testConfigDir))
        {
            Directory.Delete(_testConfigDir, recursive: true);
        }
    }

    [Fact]
    public async Task GetConfigurationAsync_ShouldCreateDefaultConfiguration_WhenFileDoesNotExist()
    {
        // Act
        var config = await _service.GetConfigurationAsync();

        // Assert
        config.Should().NotBeNull();
        config.ActiveProfile.Should().Be("docker");
        config.Profiles.Should().ContainKey("docker");
        config.DefaultOutputFormat.Should().Be("table");
        config.VerboseLogging.Should().BeFalse();
        config.QuietMode.Should().BeFalse();
    }

    [Fact]
    public async Task GetProfileAsync_ShouldReturnProfile_WhenProfileExists()
    {
        // Arrange
        await _service.EnsureConfigDirectoryAsync();

        // Act
        var profile = await _service.GetProfileAsync("docker");

        // Assert
        profile.Should().NotBeNull();
        profile!.Name.Should().Be("docker");
        profile.ServiceUrl.Should().Be("http://localhost");
        profile.GetAuthTokenUrl().Should().Contain("localhost");
    }

    [Fact]
    public async Task GetProfileAsync_ShouldReturnNull_WhenProfileDoesNotExist()
    {
        // Arrange
        await _service.EnsureConfigDirectoryAsync();

        // Act
        var profile = await _service.GetProfileAsync("nonexistent");

        // Assert
        profile.Should().BeNull();
    }

    [Fact]
    public async Task UpsertProfileAsync_ShouldCreateNewProfile()
    {
        // Arrange
        var newProfile = new Profile
        {
            Name = "custom",
            TenantServiceUrl = "https://custom.example.com",
            RegisterServiceUrl = "https://custom-register.example.com",
            PeerServiceUrl = "https://custom-peer.example.com",
            WalletServiceUrl = "https://custom-wallet.example.com",
            AuthTokenUrl = "https://custom.example.com/auth/token",
            VerifySsl = true,
            TimeoutSeconds = 60
        };

        // Act
        await _service.UpsertProfileAsync(newProfile);
        var retrieved = await _service.GetProfileAsync("custom");

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.Name.Should().Be("custom");
        retrieved.TenantServiceUrl.Should().Be("https://custom.example.com");
        retrieved.TimeoutSeconds.Should().Be(60);
    }

    [Fact]
    public async Task UpsertProfileAsync_ShouldUpdateExistingProfile()
    {
        // Arrange
        var config = await _service.GetConfigurationAsync();
        var dockerProfile = config.Profiles["docker"];
        dockerProfile.TimeoutSeconds = 120;

        // Act
        await _service.UpsertProfileAsync(dockerProfile);
        var retrieved = await _service.GetProfileAsync("docker");

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.TimeoutSeconds.Should().Be(120);
    }

    [Fact]
    public async Task SetActiveProfileAsync_ShouldSetActiveProfile()
    {
        // Arrange
        await _service.EnsureConfigDirectoryAsync();

        // Create a new profile to set as active
        var stagingProfile = new Profile
        {
            Name = "staging",
            ServiceUrl = "https://staging.sorcha.io",
            VerifySsl = true,
            TimeoutSeconds = 30
        };
        await _service.UpsertProfileAsync(stagingProfile);

        // Act
        await _service.SetActiveProfileAsync("staging");
        var activeProfile = await _service.GetActiveProfileAsync();

        // Assert
        activeProfile.Should().NotBeNull();
        activeProfile!.Name.Should().Be("staging");
    }

    [Fact]
    public async Task SetActiveProfileAsync_ShouldThrow_WhenProfileDoesNotExist()
    {
        // Arrange
        await _service.EnsureConfigDirectoryAsync();

        // Act
        var act = async () => await _service.SetActiveProfileAsync("nonexistent");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*does not exist*");
    }

    [Fact]
    public async Task DeleteProfileAsync_ShouldRemoveProfile()
    {
        // Arrange
        await _service.EnsureConfigDirectoryAsync();
        var customProfile = new Profile
        {
            Name = "to-delete",
            TenantServiceUrl = "https://example.com",
            RegisterServiceUrl = "https://example.com",
            PeerServiceUrl = "https://example.com",
            WalletServiceUrl = "https://example.com",
            AuthTokenUrl = "https://example.com/auth/token"
        };
        await _service.UpsertProfileAsync(customProfile);

        // Act
        await _service.DeleteProfileAsync("to-delete");
        var retrieved = await _service.GetProfileAsync("to-delete");

        // Assert
        retrieved.Should().BeNull();
    }

    [Fact]
    public async Task DeleteProfileAsync_ShouldClearActiveProfile_WhenDeletingActiveProfile()
    {
        // Arrange
        await _service.EnsureConfigDirectoryAsync();
        var customProfile = new Profile
        {
            Name = "active-to-delete",
            TenantServiceUrl = "https://example.com",
            RegisterServiceUrl = "https://example.com",
            PeerServiceUrl = "https://example.com",
            WalletServiceUrl = "https://example.com",
            AuthTokenUrl = "https://example.com/auth/token"
        };
        await _service.UpsertProfileAsync(customProfile);
        await _service.SetActiveProfileAsync("active-to-delete");

        // Act
        await _service.DeleteProfileAsync("active-to-delete");
        var config = await _service.GetConfigurationAsync();

        // Assert
        config.ActiveProfile.Should().NotBe("active-to-delete");
    }

    [Fact]
    public async Task ListProfilesAsync_ShouldReturnAllProfiles()
    {
        // Arrange
        await _service.EnsureConfigDirectoryAsync();

        // Act
        var profiles = await _service.ListProfilesAsync();

        // Assert
        profiles.Should().HaveCountGreaterThanOrEqualTo(1);
        profiles.Should().Contain(p => p.Name == "docker");
    }

    [Fact]
    public async Task SaveAndLoadConfiguration_ShouldPersistData()
    {
        // Arrange
        var config = await _service.GetConfigurationAsync();
        config.DefaultOutputFormat = "json";
        config.VerboseLogging = true;

        // Act
        await _service.SaveConfigurationAsync(config);
        var loadedConfig = await _service.GetConfigurationAsync();

        // Assert
        loadedConfig.DefaultOutputFormat.Should().Be("json");
        loadedConfig.VerboseLogging.Should().BeTrue();
    }
}
