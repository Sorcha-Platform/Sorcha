using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sorcha.Wallet.Core.Encryption.Configuration;
using Sorcha.Wallet.Core.Encryption.Interfaces;
using Sorcha.Wallet.Core.Encryption.Providers;
using Sorcha.Wallet.Service.Extensions;

namespace Sorcha.Wallet.Service.Tests.Encryption;

/// <summary>
/// Integration tests for encryption provider configuration and DI registration
/// </summary>
public class EncryptionProviderConfigurationTests
{
    [Fact]
    public void Configuration_ShouldBindEncryptionProviderOptions_Successfully()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EncryptionProvider:Type"] = "Local",
                ["EncryptionProvider:DefaultKeyId"] = "test-key-2025"
            })
            .Build();

        var services = new ServiceCollection();
        services.Configure<EncryptionProviderOptions>(
            config.GetSection(EncryptionProviderOptions.SectionName));

        var serviceProvider = services.BuildServiceProvider();

        // Act
        var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<EncryptionProviderOptions>>().Value;

        // Assert
        options.Type.Should().Be("Local");
        options.DefaultKeyId.Should().Be("test-key-2025");
    }

    [Fact]
    public void Configuration_ShouldBindWindowsDpapiOptions_Successfully()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EncryptionProvider:Type"] = "WindowsDpapi",
                ["EncryptionProvider:DefaultKeyId"] = "wallet-key-2025",
                ["EncryptionProvider:WindowsDpapi:KeyStorePath"] = "C:\\test\\keys",
                ["EncryptionProvider:WindowsDpapi:Scope"] = "CurrentUser"
            })
            .Build();

        var services = new ServiceCollection();
        services.Configure<EncryptionProviderOptions>(
            config.GetSection(EncryptionProviderOptions.SectionName));

        var serviceProvider = services.BuildServiceProvider();

        // Act
        var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<EncryptionProviderOptions>>().Value;

        // Assert
        options.Type.Should().Be("WindowsDpapi");
        options.WindowsDpapi.Should().NotBeNull();
        options.WindowsDpapi!.KeyStorePath.Should().Be("C:\\test\\keys");
        options.WindowsDpapi.Scope.Should().Be("CurrentUser");
    }

    [Fact]
    public void Configuration_ShouldBindLinuxSecretServiceOptions_Successfully()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EncryptionProvider:Type"] = "LinuxSecretService",
                ["EncryptionProvider:DefaultKeyId"] = "wallet-key-2025",
                ["EncryptionProvider:LinuxSecretService:ServiceName"] = "sorcha-test",
                ["EncryptionProvider:LinuxSecretService:FallbackKeyStorePath"] = "/var/test/keys"
            })
            .Build();

        var services = new ServiceCollection();
        services.Configure<EncryptionProviderOptions>(
            config.GetSection(EncryptionProviderOptions.SectionName));

        var serviceProvider = services.BuildServiceProvider();

        // Act
        var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<EncryptionProviderOptions>>().Value;

        // Assert
        options.Type.Should().Be("LinuxSecretService");
        options.LinuxSecretService.Should().NotBeNull();
        options.LinuxSecretService!.ServiceName.Should().Be("sorcha-test");
        options.LinuxSecretService.FallbackKeyStorePath.Should().Be("/var/test/keys");
    }

    [Fact]
    public void Configuration_ShouldBindAzureKeyVaultOptions_Successfully()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EncryptionProvider:Type"] = "AzureKeyVault",
                ["EncryptionProvider:DefaultKeyId"] = "wallet-key-2025",
                ["EncryptionProvider:AzureKeyVault:VaultUri"] = "https://test-vault.vault.azure.net/",
                ["EncryptionProvider:AzureKeyVault:DefaultKeyName"] = "test-key",
                ["EncryptionProvider:AzureKeyVault:UseManagedIdentity"] = "true",
                ["EncryptionProvider:AzureKeyVault:EnableDekCache"] = "true",
                ["EncryptionProvider:AzureKeyVault:DekCacheTtlMinutes"] = "60",
                ["EncryptionProvider:AzureKeyVault:AllowStaleDeksOnOutage"] = "true"
            })
            .Build();

        var services = new ServiceCollection();
        services.Configure<EncryptionProviderOptions>(
            config.GetSection(EncryptionProviderOptions.SectionName));

        var serviceProvider = services.BuildServiceProvider();

        // Act
        var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<EncryptionProviderOptions>>().Value;

        // Assert
        options.Type.Should().Be("AzureKeyVault");
        options.AzureKeyVault.Should().NotBeNull();
        options.AzureKeyVault!.VaultUri.Should().Be("https://test-vault.vault.azure.net/");
        options.AzureKeyVault.DefaultKeyName.Should().Be("test-key");
        options.AzureKeyVault.UseManagedIdentity.Should().BeTrue();
        options.AzureKeyVault.EnableDekCache.Should().BeTrue();
        options.AzureKeyVault.DekCacheTtlMinutes.Should().Be(60);
        options.AzureKeyVault.AllowStaleDeksOnOutage.Should().BeTrue();
    }

    [Fact]
    public void DIRegistration_ShouldRegisterLocalProvider_WhenTypeIsLocal()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EncryptionProvider:Type"] = "Local",
                ["EncryptionProvider:DefaultKeyId"] = "test-key"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddWalletService(config);

        var serviceProvider = services.BuildServiceProvider();

        // Act
        var provider = serviceProvider.GetRequiredService<IEncryptionProvider>();

        // Assert
        provider.Should().NotBeNull();
        provider.Should().BeOfType<LocalEncryptionProvider>();
        // LocalEncryptionProvider uses hardcoded default key for development
        provider.GetDefaultKeyId().Should().Be("local-default-key");
    }

    [Fact]
    public void DIRegistration_ShouldRegisterWindowsDpapiProvider_WhenTypeIsWindowsDpapi_AndOnWindows()
    {
        // Skip if not on Windows
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // Arrange
        var testKeyPath = Path.Combine(Path.GetTempPath(), $"test-keys-{Guid.NewGuid()}");

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EncryptionProvider:Type"] = "WindowsDpapi",
                ["EncryptionProvider:DefaultKeyId"] = "test-key",
                ["EncryptionProvider:WindowsDpapi:KeyStorePath"] = testKeyPath,
                ["EncryptionProvider:WindowsDpapi:Scope"] = "CurrentUser"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddWalletService(config);

        var serviceProvider = services.BuildServiceProvider();

        try
        {
            // Act
            var provider = serviceProvider.GetRequiredService<IEncryptionProvider>();

            // Assert
            provider.Should().NotBeNull();
            provider.Should().BeOfType<WindowsDpapiEncryptionProvider>();
            provider.GetDefaultKeyId().Should().Be("test-key");
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(testKeyPath))
            {
                Directory.Delete(testKeyPath, recursive: true);
            }
        }
    }

    [Fact]
    public void DIRegistration_ShouldFallbackToLocalProvider_WhenWindowsDpapiRequestedOnLinux()
    {
        // Skip if on Windows (this test is for non-Windows platforms)
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EncryptionProvider:Type"] = "WindowsDpapi",
                ["EncryptionProvider:DefaultKeyId"] = "test-key"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddWalletService(config);

        var serviceProvider = services.BuildServiceProvider();

        // Act
        var provider = serviceProvider.GetRequiredService<IEncryptionProvider>();

        // Assert
        provider.Should().NotBeNull();
        provider.Should().BeOfType<LocalEncryptionProvider>();
    }

    [Fact]
    public void DIRegistration_ShouldRegisterLinuxProvider_WhenTypeIsLinuxSecretService_AndOnLinux()
    {
        // Skip if not on Linux
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        // Arrange
        var testKeyPath = Path.Combine(Path.GetTempPath(), $"test-linux-keys-{Guid.NewGuid()}");

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EncryptionProvider:Type"] = "LinuxSecretService",
                ["EncryptionProvider:DefaultKeyId"] = "test-key",
                ["EncryptionProvider:LinuxSecretService:ServiceName"] = "test-service",
                ["EncryptionProvider:LinuxSecretService:FallbackKeyStorePath"] = testKeyPath
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddWalletService(config);

        var serviceProvider = services.BuildServiceProvider();

        try
        {
            // Act
            var provider = serviceProvider.GetRequiredService<IEncryptionProvider>();

            // Assert
            provider.Should().NotBeNull();
            provider.Should().BeOfType<LinuxSecretServiceEncryptionProvider>();
            provider.GetDefaultKeyId().Should().Be("test-key");
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(testKeyPath))
            {
                Directory.Delete(testKeyPath, recursive: true);
            }
        }
    }

    [Fact]
    public void DIRegistration_ShouldFallbackToLocalProvider_WhenLinuxSecretServiceRequestedOnWindows()
    {
        // Skip if on Linux (this test is for non-Linux platforms)
        if (OperatingSystem.IsLinux())
        {
            return;
        }

        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EncryptionProvider:Type"] = "LinuxSecretService",
                ["EncryptionProvider:DefaultKeyId"] = "test-key"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddWalletService(config);

        var serviceProvider = services.BuildServiceProvider();

        // Act
        var provider = serviceProvider.GetRequiredService<IEncryptionProvider>();

        // Assert
        provider.Should().NotBeNull();
        provider.Should().BeOfType<LocalEncryptionProvider>();
    }

    [Fact]
    public void DIRegistration_ShouldFallbackToLocalProvider_WhenInvalidTypeProvided()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EncryptionProvider:Type"] = "InvalidType",
                ["EncryptionProvider:DefaultKeyId"] = "test-key"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddWalletService(config);

        var serviceProvider = services.BuildServiceProvider();

        // Act
        var provider = serviceProvider.GetRequiredService<IEncryptionProvider>();

        // Assert
        provider.Should().NotBeNull();
        provider.Should().BeOfType<LocalEncryptionProvider>();
    }

    [Fact]
    public void Configuration_ShouldUseDefaultValues_WhenOptionsNotProvided()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EncryptionProvider:Type"] = "WindowsDpapi"
            })
            .Build();

        var services = new ServiceCollection();
        services.Configure<EncryptionProviderOptions>(
            config.GetSection(EncryptionProviderOptions.SectionName));

        var serviceProvider = services.BuildServiceProvider();

        // Act
        var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<EncryptionProviderOptions>>().Value;

        // Assert - Should use default values from EncryptionProviderOptions
        options.Type.Should().Be("WindowsDpapi");
        options.DefaultKeyId.Should().Be("default-key"); // Default from EncryptionProviderOptions
    }

    [Fact]
    public void DIRegistration_ShouldRegisterProviderAsSingleton()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EncryptionProvider:Type"] = "Local",
                ["EncryptionProvider:DefaultKeyId"] = "test-key"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddWalletService(config);

        var serviceProvider = services.BuildServiceProvider();

        // Act
        var provider1 = serviceProvider.GetRequiredService<IEncryptionProvider>();
        var provider2 = serviceProvider.GetRequiredService<IEncryptionProvider>();

        // Assert - Should be same instance (singleton)
        provider1.Should().BeSameAs(provider2);
    }

    [Fact]
    public void DIRegistration_ShouldRegisterKeyProtectionProvider_WhenTypeIsLocal()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EncryptionProvider:Type"] = "Local",
                ["EncryptionProvider:DefaultKeyId"] = "test-key"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddWalletService(config);

        var serviceProvider = services.BuildServiceProvider();

        // Act
        var keyProtectionProvider = serviceProvider.GetRequiredService<Sorcha.Wallet.Core.Encryption.Interfaces.IKeyProtectionProvider>();

        // Assert
        keyProtectionProvider.Should().NotBeNull();
        keyProtectionProvider.Should().BeOfType<LocalEncryptionProvider>();
    }

    [Fact]
    public void DIRegistration_ShouldResolveSameInstance_ForBothInterfaces()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EncryptionProvider:Type"] = "Local",
                ["EncryptionProvider:DefaultKeyId"] = "test-key"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddWalletService(config);

        var serviceProvider = services.BuildServiceProvider();

        // Act
        var encryptionProvider = serviceProvider.GetRequiredService<IEncryptionProvider>();
        var keyProtectionProvider = serviceProvider.GetRequiredService<Sorcha.Wallet.Core.Encryption.Interfaces.IKeyProtectionProvider>();

        // Assert — they should be the same object
        keyProtectionProvider.Should().BeSameAs(encryptionProvider);
    }

    [Fact]
    public void Configuration_ShouldSupportCaseInsensitiveProviderType()
    {
        // Arrange - Test various casing
        var testCases = new[] { "local", "LOCAL", "Local", "LoCAL" };

        foreach (var providerType in testCases)
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["EncryptionProvider:Type"] = providerType,
                    ["EncryptionProvider:DefaultKeyId"] = "test-key"
                })
                .Build();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddWalletService(config);

            var serviceProvider = services.BuildServiceProvider();

            // Act
            var provider = serviceProvider.GetRequiredService<IEncryptionProvider>();

            // Assert
            provider.Should().BeOfType<LocalEncryptionProvider>($"type '{providerType}' should be recognized");
        }
    }
}
