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

public class AzureSigningProviderTests
{
    private readonly Mock<KeyClient> _mockKeyClient;
    private readonly AzureSigningProvider _provider;

    public AzureSigningProviderTests()
    {
        _mockKeyClient = new Mock<KeyClient>();
        _provider = new AzureSigningProvider(
            _mockKeyClient.Object,
            Mock.Of<ILogger<AzureSigningProvider>>());
    }

    [Fact]
    public async Task CreateSigningKeyAsync_CreatesEcP256Key()
    {
        // Arrange
        var keyId = "signing-key-1";
        var mockKey = CreateMockEcKeyVaultKey(keyId);

        _mockKeyClient
            .Setup(c => c.CreateEcKeyAsync(
                It.Is<CreateEcKeyOptions>(o => o.Name == keyId && o.CurveName == KeyCurveName.P256),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(mockKey, Mock.Of<Response>()));

        // Act
        var result = await _provider.CreateSigningKeyAsync(keyId, "NISTP256");

        // Assert
        result.Should().NotBeNull();
        result.KeyId.Should().NotBeNullOrWhiteSpace();
        result.PublicKey.Should().HaveCount(64);
        result.Algorithm.Should().Be("NISTP256");
    }

    [Theory]
    [InlineData("P-256")]
    [InlineData("P256")]
    [InlineData("NISTP256")]
    public async Task CreateSigningKeyAsync_AcceptsP256Variants(string algorithm)
    {
        // Arrange
        var keyId = "signing-key";
        var mockKey = CreateMockEcKeyVaultKey(keyId);

        _mockKeyClient
            .Setup(c => c.CreateEcKeyAsync(
                It.IsAny<CreateEcKeyOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(mockKey, Mock.Of<Response>()));

        // Act
        var result = await _provider.CreateSigningKeyAsync(keyId, algorithm);

        // Assert
        result.Algorithm.Should().Be("NISTP256");
    }

    [Theory]
    [InlineData("ED25519")]
    [InlineData("RSA")]
    [InlineData("SECP256K1")]
    public async Task CreateSigningKeyAsync_RejectsUnsupportedAlgorithms(string algorithm)
    {
        // Act
        var act = () => _provider.CreateSigningKeyAsync("key-1", algorithm);

        // Assert
        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*not supported*KMS-resident*");
    }

    [Fact]
    public async Task SignAsync_CallsCryptographyClientSign()
    {
        // Arrange
        var kmsKeyId = "signing-key-1";
        var data = new byte[] { 1, 2, 3, 4 };
        var signature = new byte[] { 10, 20, 30, 40 };

        var mockCryptoClient = new Mock<CryptographyClient>();
        mockCryptoClient
            .Setup(c => c.SignAsync(
                SignatureAlgorithm.ES256,
                data,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CryptographyModelFactory.SignResult(
                keyId: kmsKeyId,
                signature: signature,
                algorithm: SignatureAlgorithm.ES256));

        _mockKeyClient
            .Setup(c => c.GetCryptographyClient(kmsKeyId, It.IsAny<string>()))
            .Returns(mockCryptoClient.Object);

        // Act
        var result = await _provider.SignAsync(kmsKeyId, data);

        // Assert
        result.Should().BeEquivalentTo(signature);
    }

    [Fact]
    public async Task VerifyAsync_ReturnsTrue_WhenSignatureValid()
    {
        // Arrange
        var kmsKeyId = "signing-key-1";
        var data = new byte[] { 1, 2, 3, 4 };
        var signature = new byte[] { 10, 20, 30, 40 };

        var mockCryptoClient = new Mock<CryptographyClient>();
        mockCryptoClient
            .Setup(c => c.VerifyAsync(
                SignatureAlgorithm.ES256,
                data,
                signature,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CryptographyModelFactory.VerifyResult(
                keyId: kmsKeyId,
                isValid: true,
                algorithm: SignatureAlgorithm.ES256));

        _mockKeyClient
            .Setup(c => c.GetCryptographyClient(kmsKeyId, It.IsAny<string>()))
            .Returns(mockCryptoClient.Object);

        // Act
        var result = await _provider.VerifyAsync(kmsKeyId, data, signature);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task VerifyAsync_ReturnsFalse_WhenSignatureInvalid()
    {
        // Arrange
        var kmsKeyId = "signing-key-1";
        var data = new byte[] { 1, 2, 3, 4 };
        var badSignature = new byte[] { 99, 99, 99 };

        var mockCryptoClient = new Mock<CryptographyClient>();
        mockCryptoClient
            .Setup(c => c.VerifyAsync(
                SignatureAlgorithm.ES256,
                data,
                badSignature,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CryptographyModelFactory.VerifyResult(
                keyId: kmsKeyId,
                isValid: false,
                algorithm: SignatureAlgorithm.ES256));

        _mockKeyClient
            .Setup(c => c.GetCryptographyClient(kmsKeyId, It.IsAny<string>()))
            .Returns(mockCryptoClient.Object);

        // Act
        var result = await _provider.VerifyAsync(kmsKeyId, data, badSignature);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task GetPublicKeyAsync_ReturnsPublicKeyBytes()
    {
        // Arrange
        var kmsKeyId = "signing-key-1";
        var mockKey = CreateMockEcKeyVaultKey(kmsKeyId);

        _mockKeyClient
            .Setup(c => c.GetKeyAsync(kmsKeyId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(mockKey, Mock.Of<Response>()));

        // Act
        var result = await _provider.GetPublicKeyAsync(kmsKeyId);

        // Assert
        result.Should().HaveCount(64, "P-256 uncompressed public key is X(32) + Y(32) = 64 bytes");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task SignAsync_ThrowsOnInvalidKeyId(string? kmsKeyId)
    {
        var act = () => _provider.SignAsync(kmsKeyId!, new byte[] { 1 });
        await act.Should().ThrowAsync<ArgumentException>();
    }

    /// <summary>
    /// Creates a mock EC P-256 KeyVaultKey using a real ephemeral ECDsa key.
    /// </summary>
    private static KeyVaultKey CreateMockEcKeyVaultKey(string keyId)
    {
        using var ecdsa = System.Security.Cryptography.ECDsa.Create(
            System.Security.Cryptography.ECCurve.NamedCurves.nistP256);

        var jwk = JsonWebKeyModelFactory.CreateFromECDsa(ecdsa, keyId);
        var keyProperties = KeyModelFactory.KeyProperties(
            id: new Uri($"https://test-vault.vault.azure.net/keys/{keyId}/version1"),
            name: keyId,
            createdOn: DateTimeOffset.UtcNow);

        return KeyModelFactory.KeyVaultKey(keyProperties, jwk);
    }
}

/// <summary>
/// Factory to create testable Azure SDK JsonWebKey instances from ECDsa keys.
/// </summary>
internal static class JsonWebKeyModelFactory
{
    public static JsonWebKey CreateFromECDsa(
        System.Security.Cryptography.ECDsa ecdsa, string keyId)
    {
        var parameters = ecdsa.ExportParameters(false);
        return KeyModelFactory.JsonWebKey(
            KeyType.Ec,
            id: $"https://test-vault.vault.azure.net/keys/{keyId}/version1",
            curveName: KeyCurveName.P256,
            x: parameters.Q.X,
            y: parameters.Q.Y);
    }
}
