// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Moq;

using Sorcha.Cryptography.Interfaces;
using Sorcha.Wallet.Core.Domain.Enums;
using Sorcha.Wallet.Core.Encryption.Configuration;
using Sorcha.Wallet.Core.Encryption.Interfaces;
using Sorcha.Wallet.Core.Events.Interfaces;
using Sorcha.Wallet.Core.Repositories.Interfaces;
using Sorcha.Wallet.Core.Services.Implementation;
using Sorcha.Wallet.Core.Services.Interfaces;

using Xunit;

namespace Sorcha.Wallet.Core.Tests.Services;

public class SigningModePolicyTests
{
    private static WalletManager CreateManager(WalletKeyManagementOptions options)
    {
        var keyManagementMock = new Mock<IKeyManagementService>();
        var concreteKeyManagement = new KeyManagementService(
            Mock.Of<IKeyProtectionProvider>(),
            Mock.Of<ICryptoModule>(),
            Mock.Of<IWalletUtilities>(),
            Mock.Of<ILogger<KeyManagementService>>());

        var recoveryKeyServiceMock = new Mock<IRecoveryKeyService>();
        recoveryKeyServiceMock.Setup(r => r.GenerateRecoveryKeyAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new byte[32]);
        recoveryKeyServiceMock.Setup(r => r.EncryptMasterKeyAsync(It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("encrypted-master-key-blob");

        return new WalletManager(
            keyManagementMock.Object,
            concreteKeyManagement,
            Mock.Of<ITransactionService>(),
            Mock.Of<IDelegationService>(),
            Mock.Of<IWalletRepository>(),
            Mock.Of<IEventPublisher>(),
            Mock.Of<ILogger<WalletManager>>(),
            recoveryKeyServiceMock.Object,
            signingProvider: null,
            keyManagementOptions: Options.Create(options));
    }

    [Fact]
    public void ResolveSigningMode_KmsResidentPath_ReturnsKmsResident()
    {
        // Arrange — system path m/44'/0'/0'/0/100 is configured as KMS-resident
        var options = new WalletKeyManagementOptions
        {
            DefaultSigningMode = SigningMode.Local,
            KmsResidentPaths = ["m/44'/0'/0'/0/100"],
            AllowSigningModeOverride = true
        };
        var sut = CreateManager(options);

        // Act
        var result = sut.ResolveSigningMode(apiOverride: null, derivationPath: "m/44'/0'/0'/0/100");

        // Assert
        result.Should().Be(SigningMode.KmsResident);
    }

    [Fact]
    public void ResolveSigningMode_UserPath_ReturnsLocal()
    {
        // Arrange — user path m/44'/0'/0'/0/0 is NOT in KmsResidentPaths
        var options = new WalletKeyManagementOptions
        {
            DefaultSigningMode = SigningMode.Local,
            KmsResidentPaths = ["m/44'/0'/0'/0/100"],
            AllowSigningModeOverride = true
        };
        var sut = CreateManager(options);

        // Act
        var result = sut.ResolveSigningMode(apiOverride: null, derivationPath: "m/44'/0'/0'/0/0");

        // Assert
        result.Should().Be(SigningMode.Local);
    }

    [Fact]
    public void ResolveSigningMode_ApiOverrideKmsResident_WhenAllowed_ReturnsKmsResident()
    {
        // Arrange
        var options = new WalletKeyManagementOptions
        {
            DefaultSigningMode = SigningMode.Local,
            AllowSigningModeOverride = true
        };
        var sut = CreateManager(options);

        // Act
        var result = sut.ResolveSigningMode(apiOverride: SigningMode.KmsResident);

        // Assert
        result.Should().Be(SigningMode.KmsResident);
    }

    [Fact]
    public void ResolveSigningMode_ApiOverrideIgnored_WhenNotAllowed_UsesDefault()
    {
        // Arrange — AllowSigningModeOverride is false, so API override is ignored
        var options = new WalletKeyManagementOptions
        {
            DefaultSigningMode = SigningMode.Local,
            AllowSigningModeOverride = false
        };
        var sut = CreateManager(options);

        // Act
        var result = sut.ResolveSigningMode(apiOverride: SigningMode.KmsResident);

        // Assert
        result.Should().Be(SigningMode.Local);
    }

    [Fact]
    public void ResolveSigningMode_NoOverrideNoPath_FallsBackToDefault()
    {
        // Arrange
        var options = new WalletKeyManagementOptions
        {
            DefaultSigningMode = SigningMode.KmsResident,
            AllowSigningModeOverride = true
        };
        var sut = CreateManager(options);

        // Act
        var result = sut.ResolveSigningMode(apiOverride: null);

        // Assert
        result.Should().Be(SigningMode.KmsResident);
    }

    [Fact]
    public void ResolveSigningMode_ApiOverrideTakesPriority_OverPathMatch()
    {
        // Arrange — path matches KmsResident, but API override says Local
        var options = new WalletKeyManagementOptions
        {
            DefaultSigningMode = SigningMode.KmsResident,
            KmsResidentPaths = ["m/44'/0'/0'/0/100"],
            AllowSigningModeOverride = true
        };
        var sut = CreateManager(options);

        // Act — API says Local, path says KmsResident; API wins
        var result = sut.ResolveSigningMode(
            apiOverride: SigningMode.Local,
            derivationPath: "m/44'/0'/0'/0/100");

        // Assert
        result.Should().Be(SigningMode.Local);
    }

    [Fact]
    public void ResolveSigningMode_PathMatchCaseInsensitive()
    {
        // Arrange
        var options = new WalletKeyManagementOptions
        {
            DefaultSigningMode = SigningMode.Local,
            KmsResidentPaths = ["M/44'/0'/0'/0/100"],
            AllowSigningModeOverride = true
        };
        var sut = CreateManager(options);

        // Act
        var result = sut.ResolveSigningMode(apiOverride: null, derivationPath: "m/44'/0'/0'/0/100");

        // Assert
        result.Should().Be(SigningMode.KmsResident);
    }
}
