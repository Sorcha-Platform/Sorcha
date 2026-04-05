// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.Blueprint.Service.Services.Implementation;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.Cryptography.Enums;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Cryptography.Models;

namespace Sorcha.Blueprint.Service.Tests;

public class TransactionBuilderServiceFileTests
{
    private readonly Mock<ICryptoModule> _mockCryptoModule;
    private readonly Mock<IHashProvider> _mockHashProvider;
    private readonly Mock<ISymmetricCrypto> _mockSymmetricCrypto;
    private readonly Mock<ILogger<TransactionBuilderService>> _mockLogger;
    private readonly TransactionBuilderService _service;

    public TransactionBuilderServiceFileTests()
    {
        _mockCryptoModule = new Mock<ICryptoModule>();
        _mockHashProvider = new Mock<IHashProvider>();
        _mockSymmetricCrypto = new Mock<ISymmetricCrypto>();
        _mockLogger = new Mock<ILogger<TransactionBuilderService>>();
        _service = new TransactionBuilderService(
            _mockCryptoModule.Object,
            _mockHashProvider.Object,
            _mockSymmetricCrypto.Object,
            _mockLogger.Object);
    }

    private static SymmetricCiphertext MakeCiphertext(byte[] data, byte[] nonce) =>
        new()
        {
            Data = data,
            Key = new byte[32],
            IV = nonce,
            Type = EncryptionType.XCHACHA20_POLY1305
        };

    private void SetupEncryptSuccess(byte[] ciphertext, byte[] nonce)
    {
        _mockSymmetricCrypto
            .Setup(s => s.EncryptAsync(
                It.IsAny<byte[]>(),
                It.IsAny<EncryptionType>(),
                It.IsAny<byte[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CryptoResult<SymmetricCiphertext>.Success(MakeCiphertext(ciphertext, nonce)));
    }

    #region EncryptFileChunkAsync

    [Fact]
    public async Task EncryptFileChunkAsync_ValidInputs_ReturnsEncryptedResult()
    {
        // Arrange
        var chunkContent = new byte[1024];
        Random.Shared.NextBytes(chunkContent);
        var masterKey = new byte[32];
        Random.Shared.NextBytes(masterKey);
        var salt = new byte[32];
        Random.Shared.NextBytes(salt);

        var expectedCiphertext = new byte[1040];
        var expectedNonce = new byte[24];
        Random.Shared.NextBytes(expectedCiphertext);
        Random.Shared.NextBytes(expectedNonce);
        SetupEncryptSuccess(expectedCiphertext, expectedNonce);

        // Act
        var result = await _service.EncryptFileChunkAsync(
            chunkContent, masterKey, salt, chunkIndex: 0, senderWallet: "wallet-alice");

        // Assert
        result.Should().NotBeNull();
        result.EncryptedContent.Should().NotBeNull();
        result.Nonce.Should().NotBeNull();
        result.EncryptedContent.Should().BeEquivalentTo(expectedCiphertext);
        result.Nonce.Should().BeEquivalentTo(expectedNonce);
    }

    [Fact]
    public async Task EncryptFileChunkAsync_DifferentIndices_ProduceDifferentCiphertexts()
    {
        // Arrange
        var chunkContent = new byte[512];
        Random.Shared.NextBytes(chunkContent);
        var masterKey = new byte[32];
        Random.Shared.NextBytes(masterKey);
        var salt = new byte[32];
        Random.Shared.NextBytes(salt);

        // Capture the key passed to EncryptAsync so we can verify they differ per index
        var capturedKeys = new List<byte[]>();

        _mockSymmetricCrypto
            .Setup(s => s.EncryptAsync(
                It.IsAny<byte[]>(),
                It.IsAny<EncryptionType>(),
                It.IsAny<byte[]>(),
                It.IsAny<CancellationToken>()))
            .Callback<byte[], EncryptionType, byte[]?, CancellationToken>((_, _, key, _) =>
            {
                if (key is not null)
                    capturedKeys.Add(key.ToArray());
            })
            .ReturnsAsync(() =>
            {
                // Return a unique ciphertext each time so results differ
                var ct = new byte[32];
                Random.Shared.NextBytes(ct);
                var iv = new byte[24];
                Random.Shared.NextBytes(iv);
                return CryptoResult<SymmetricCiphertext>.Success(MakeCiphertext(ct, iv));
            });

        // Act
        var result0 = await _service.EncryptFileChunkAsync(
            chunkContent, masterKey, salt, chunkIndex: 0, senderWallet: "wallet-alice");
        var result1 = await _service.EncryptFileChunkAsync(
            chunkContent, masterKey, salt, chunkIndex: 1, senderWallet: "wallet-alice");

        // Assert — HKDF binds to chunkIndex so derived keys must differ
        capturedKeys.Should().HaveCount(2);
        capturedKeys[0].Should().NotBeEquivalentTo(capturedKeys[1],
            "each chunk index must produce a distinct HKDF-derived key");

        // The mock returns distinct random ciphertexts, so the results also differ
        result0.EncryptedContent.Should().NotBeEquivalentTo(result1.EncryptedContent);
    }

    [Fact]
    public async Task EncryptFileChunkAsync_NullContent_ThrowsArgumentException()
    {
        // Arrange
        var masterKey = new byte[32];
        var salt = new byte[32];

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.EncryptFileChunkAsync(null!, masterKey, salt, 0, "wallet-alice"));
    }

    [Fact]
    public async Task EncryptFileChunkAsync_EmptyContent_ThrowsArgumentException()
    {
        // Arrange
        var masterKey = new byte[32];
        var salt = new byte[32];

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => _service.EncryptFileChunkAsync(Array.Empty<byte>(), masterKey, salt, 0, "wallet-alice"));

        ex.ParamName.Should().Be("chunkContent");
    }

    [Fact]
    public async Task EncryptFileChunkAsync_InvalidKeySize_ThrowsArgumentException()
    {
        // Arrange — 16-byte key is invalid; HkdfKeyDerivation requires exactly 32 bytes
        var chunkContent = new byte[64];
        Random.Shared.NextBytes(chunkContent);
        var shortKey = new byte[16];
        var salt = new byte[32];

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.EncryptFileChunkAsync(chunkContent, shortKey, salt, 0, "wallet-alice"));
    }

    #endregion

    #region CreateFileUploadSession

    [Fact]
    public void CreateFileUploadSession_ReturnsMasterKeyAndSalt()
    {
        // Act
        var session = _service.CreateFileUploadSession();

        // Assert
        session.Should().NotBeNull();
        session.MasterFileKey.Should().NotBeNull().And.HaveCount(32);
        session.Salt.Should().NotBeNull().And.HaveCount(32);
    }

    [Fact]
    public void CreateFileUploadSession_ReturnsUniqueKeys()
    {
        // Act
        var session1 = _service.CreateFileUploadSession();
        var session2 = _service.CreateFileUploadSession();

        // Assert
        session1.MasterFileKey.Should().NotBeEquivalentTo(session2.MasterFileKey,
            "each session must have a cryptographically unique master key");
    }

    [Fact]
    public void CreateFileUploadSession_ReturnsUniqueSalts()
    {
        // Act
        var session1 = _service.CreateFileUploadSession();
        var session2 = _service.CreateFileUploadSession();

        // Assert
        session1.Salt.Should().NotBeEquivalentTo(session2.Salt,
            "each session must have a cryptographically unique salt");
    }

    #endregion
}
