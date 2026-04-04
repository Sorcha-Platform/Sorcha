// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Azure;
using Azure.Security.KeyVault.Keys;
using Azure.Security.KeyVault.Keys.Cryptography;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Sorcha.Wallet.Providers.Azure.Tests;

public class AzureKeyProtectionProviderTests
{
    private readonly Mock<KeyClient> _mockKeyClient;
    private readonly AzureKeyProtectionProvider _provider;

    public AzureKeyProtectionProviderTests()
    {
        _mockKeyClient = new Mock<KeyClient>();
        _provider = new AzureKeyProtectionProvider(
            _mockKeyClient.Object,
            Mock.Of<ILogger<AzureKeyProtectionProvider>>());
    }

    [Fact]
    public async Task CreateKeyAsync_CreatesRsaKeyInVault()
    {
        // Arrange
        var keyId = "test-key";
        var mockKeyVaultKey = CreateMockKeyVaultKey(keyId);
        _mockKeyClient
            .Setup(c => c.CreateRsaKeyAsync(
                It.Is<CreateRsaKeyOptions>(o => o.Name == keyId && o.KeySize == 2048),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(mockKeyVaultKey, Mock.Of<Response>()));

        // Act
        var result = await _provider.CreateKeyAsync(keyId);

        // Assert
        result.Should().NotBeNullOrWhiteSpace();
        _mockKeyClient.Verify(c => c.CreateRsaKeyAsync(
            It.Is<CreateRsaKeyOptions>(o => o.Name == keyId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WrapKeyAsync_CallsCryptographyClientWrap()
    {
        // Arrange
        var keyId = "test-key";
        var plaintext = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var wrappedBytes = new byte[] { 10, 20, 30, 40 };

        var mockCryptoClient = new Mock<CryptographyClient>();
        mockCryptoClient
            .Setup(c => c.WrapKeyAsync(
                KeyWrapAlgorithm.RsaOaep256,
                plaintext,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CryptographyModelFactory.WrapResult(
                keyId: keyId,
                key: wrappedBytes,
                algorithm: KeyWrapAlgorithm.RsaOaep256));

        _mockKeyClient
            .Setup(c => c.GetCryptographyClient(keyId, It.IsAny<string>()))
            .Returns(mockCryptoClient.Object);

        // Act
        var result = await _provider.WrapKeyAsync(keyId, plaintext);

        // Assert
        result.Should().BeEquivalentTo(wrappedBytes);
        mockCryptoClient.Verify(c => c.WrapKeyAsync(
            KeyWrapAlgorithm.RsaOaep256,
            plaintext,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UnwrapKeyAsync_CallsCryptographyClientUnwrap()
    {
        // Arrange
        var keyId = "test-key";
        var ciphertext = new byte[] { 10, 20, 30, 40 };
        var unwrappedBytes = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        var mockCryptoClient = new Mock<CryptographyClient>();
        mockCryptoClient
            .Setup(c => c.UnwrapKeyAsync(
                KeyWrapAlgorithm.RsaOaep256,
                ciphertext,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CryptographyModelFactory.UnwrapResult(
                keyId: keyId,
                key: unwrappedBytes,
                algorithm: KeyWrapAlgorithm.RsaOaep256));

        _mockKeyClient
            .Setup(c => c.GetCryptographyClient(keyId, It.IsAny<string>()))
            .Returns(mockCryptoClient.Object);

        // Act
        var result = await _provider.UnwrapKeyAsync(keyId, ciphertext);

        // Assert
        result.Should().BeEquivalentTo(unwrappedBytes);
        mockCryptoClient.Verify(c => c.UnwrapKeyAsync(
            KeyWrapAlgorithm.RsaOaep256,
            ciphertext,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task KeyExistsAsync_ReturnsTrue_WhenKeyExists()
    {
        // Arrange
        var keyId = "existing-key";
        var mockKeyVaultKey = CreateMockKeyVaultKey(keyId);
        _mockKeyClient
            .Setup(c => c.GetKeyAsync(keyId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(mockKeyVaultKey, Mock.Of<Response>()));

        // Act
        var result = await _provider.KeyExistsAsync(keyId);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task KeyExistsAsync_ReturnsFalse_WhenKeyNotFound()
    {
        // Arrange
        var keyId = "missing-key";
        _mockKeyClient
            .Setup(c => c.GetKeyAsync(keyId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(404, "Key not found"));

        // Act
        var result = await _provider.KeyExistsAsync(keyId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task KeyExistsAsync_Throws_WhenNon404Error()
    {
        // Arrange
        var keyId = "error-key";
        _mockKeyClient
            .Setup(c => c.GetKeyAsync(keyId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(403, "Forbidden"));

        // Act & Assert
        var act = () => _provider.KeyExistsAsync(keyId);
        await act.Should().ThrowAsync<RequestFailedException>();
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        // Act & Assert
        var act = () => _provider.Dispose();
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task CreateKeyAsync_ThrowsOnInvalidKeyId(string? keyId)
    {
        var act = () => _provider.CreateKeyAsync(keyId!);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    private static KeyVaultKey CreateMockKeyVaultKey(string keyId)
    {
        // Use the KeyModelFactory to create a testable KeyVaultKey
        var keyProperties = KeyModelFactory.KeyProperties(
            id: new Uri($"https://test-vault.vault.azure.net/keys/{keyId}/version1"),
            name: keyId);

        return KeyModelFactory.KeyVaultKey(keyProperties, KeyModelFactory.JsonWebKey(
            KeyType.Rsa,
            id: $"https://test-vault.vault.azure.net/keys/{keyId}/version1"));
    }
}
