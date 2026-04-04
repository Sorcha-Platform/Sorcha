// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sorcha.Wallet.Core.Domain.Enums;
using Sorcha.Wallet.Core.Encryption.Configuration;
using Xunit;

namespace Sorcha.Wallet.Service.Tests.Encryption;

/// <summary>
/// Tests for WalletKeyManagementOptions and SigningModePolicy configuration binding.
/// </summary>
public class WalletKeyManagementOptionsTests
{
    [Fact]
    public void Defaults_ShouldHaveReasonableValues()
    {
        var options = new WalletKeyManagementOptions();

        options.DefaultKeyId.Should().Be("default-key");
        options.DekCacheTtlMinutes.Should().Be(30);
        options.DekCacheGracePeriodMinutes.Should().Be(5);
        options.MaxCachedDeks.Should().Be(1000);
        options.SigningPolicy.Should().NotBeNull();
    }

    [Fact]
    public void SigningModePolicy_Defaults_ShouldBeLocal()
    {
        var policy = new SigningModePolicy();

        policy.DefaultMode.Should().Be(SigningMode.Local);
        policy.AllowLocalToKmsMigration.Should().BeFalse();
        policy.AllowedKmsAlgorithms.Should().BeEmpty();
    }

    [Fact]
    public void Configuration_ShouldBindWalletKeyManagementOptions()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["WalletKeyManagement:DefaultKeyId"] = "custom-key-2026",
                ["WalletKeyManagement:DekCacheTtlMinutes"] = "60",
                ["WalletKeyManagement:DekCacheGracePeriodMinutes"] = "10",
                ["WalletKeyManagement:MaxCachedDeks"] = "500",
                ["WalletKeyManagement:SigningPolicy:DefaultMode"] = "KmsResident",
                ["WalletKeyManagement:SigningPolicy:AllowLocalToKmsMigration"] = "true",
                ["WalletKeyManagement:SigningPolicy:AllowedKmsAlgorithms:0"] = "ED25519",
                ["WalletKeyManagement:SigningPolicy:AllowedKmsAlgorithms:1"] = "NISTP256"
            })
            .Build();

        var services = new ServiceCollection();
        services.Configure<WalletKeyManagementOptions>(
            config.GetSection(WalletKeyManagementOptions.SectionName));

        var serviceProvider = services.BuildServiceProvider();

        // Act
        var options = serviceProvider.GetRequiredService<IOptions<WalletKeyManagementOptions>>().Value;

        // Assert
        options.DefaultKeyId.Should().Be("custom-key-2026");
        options.DekCacheTtlMinutes.Should().Be(60);
        options.DekCacheGracePeriodMinutes.Should().Be(10);
        options.MaxCachedDeks.Should().Be(500);
        options.SigningPolicy.DefaultMode.Should().Be(SigningMode.KmsResident);
        options.SigningPolicy.AllowLocalToKmsMigration.Should().BeTrue();
        options.SigningPolicy.AllowedKmsAlgorithms.Should().ContainInOrder("ED25519", "NISTP256");
    }

    [Fact]
    public void Configuration_ShouldUseDefaults_WhenSectionMissing()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var services = new ServiceCollection();
        services.Configure<WalletKeyManagementOptions>(
            config.GetSection(WalletKeyManagementOptions.SectionName));

        var serviceProvider = services.BuildServiceProvider();

        // Act
        var options = serviceProvider.GetRequiredService<IOptions<WalletKeyManagementOptions>>().Value;

        // Assert
        options.DefaultKeyId.Should().Be("default-key");
        options.DekCacheTtlMinutes.Should().Be(30);
        options.SigningPolicy.DefaultMode.Should().Be(SigningMode.Local);
    }

    [Fact]
    public void SectionName_ShouldBeCorrect()
    {
        WalletKeyManagementOptions.SectionName.Should().Be("WalletKeyManagement");
    }
}
