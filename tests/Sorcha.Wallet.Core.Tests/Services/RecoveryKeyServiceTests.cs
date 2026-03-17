// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Cryptography.Enums;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Cryptography.Models;
using Sorcha.Wallet.Core.Services.Implementation;
using Xunit;

namespace Sorcha.Wallet.Core.Tests.Services;

/// <summary>
/// Tests for RecoveryKeyService: key generation, wrapping/unwrapping, master key encryption.
/// </summary>
public class RecoveryKeyServiceTests
{
    private readonly Mock<ISymmetricCrypto> _symmetricCryptoMock;
    private readonly Mock<ICryptoModule> _cryptoModuleMock;
    private readonly RecoveryKeyService _sut;

    public RecoveryKeyServiceTests()
    {
        _symmetricCryptoMock = new Mock<ISymmetricCrypto>();
        _cryptoModuleMock = new Mock<ICryptoModule>();
        var loggerMock = new Mock<ILogger<RecoveryKeyService>>();
        _sut = new RecoveryKeyService(
            _symmetricCryptoMock.Object,
            _cryptoModuleMock.Object,
            loggerMock.Object);
    }

    [Fact]
    public async Task GenerateRecoveryKeyAsync_Returns32Bytes()
    {
        var key = await _sut.GenerateRecoveryKeyAsync();

        key.Should().HaveCount(32, "AES-256 requires a 32-byte key");
    }

    [Fact]
    public async Task GenerateRecoveryKeyAsync_ReturnsUniqueKeys()
    {
        var key1 = await _sut.GenerateRecoveryKeyAsync();
        var key2 = await _sut.GenerateRecoveryKeyAsync();

        key1.Should().NotEqual(key2);
    }

    [Fact]
    public async Task WrapRecoveryKeyAsync_SuccessfulWrap_ReturnsBase64()
    {
        var recoveryKey = new byte[32];
        var publicKey = new byte[32];
        var encrypted = new byte[] { 1, 2, 3, 4 };

        _cryptoModuleMock
            .Setup(c => c.EncryptAsync(recoveryKey, It.IsAny<byte>(), publicKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CryptoResult<byte[]>.Success(encrypted));

        var result = await _sut.WrapRecoveryKeyAsync(recoveryKey, publicKey, "ED25519");

        result.Should().NotBeNullOrEmpty();
        Convert.FromBase64String(result).Should().Equal(encrypted);
    }

    [Fact]
    public async Task WrapRecoveryKeyAsync_CryptoFailure_ThrowsCryptographicException()
    {
        var recoveryKey = new byte[32];
        var publicKey = new byte[32];

        _cryptoModuleMock
            .Setup(c => c.EncryptAsync(It.IsAny<byte[]>(), It.IsAny<byte>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CryptoResult<byte[]>.Failure(CryptoStatus.EncryptionFailed, "Test failure"));

        var act = () => _sut.WrapRecoveryKeyAsync(recoveryKey, publicKey, "ED25519");

        await act.Should().ThrowAsync<System.Security.Cryptography.CryptographicException>()
            .WithMessage("*Test failure*");
    }

    [Fact]
    public async Task UnwrapRecoveryKeyAsync_SuccessfulUnwrap_ReturnsRecoveryKey()
    {
        var recoveryKey = new byte[] { 10, 20, 30 };
        var encryptedBase64 = Convert.ToBase64String(new byte[] { 1, 2, 3 });
        var privateKey = new byte[32];

        _cryptoModuleMock
            .Setup(c => c.DecryptAsync(It.IsAny<byte[]>(), It.IsAny<byte>(), privateKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CryptoResult<byte[]>.Success(recoveryKey));

        var result = await _sut.UnwrapRecoveryKeyAsync(encryptedBase64, privateKey, "ED25519");

        result.Should().Equal(recoveryKey);
    }

    [Fact]
    public async Task EncryptMasterKeyAsync_ReturnsBase64Blob()
    {
        var masterKey = new byte[] { 1, 2, 3, 4, 5 };
        var recoveryKey = new byte[32];
        var iv = new byte[12];
        var ciphertext = new byte[] { 99, 98, 97 };

        var symmetricResult = new SymmetricCiphertext
        {
            Data = ciphertext,
            Key = recoveryKey,
            IV = iv,
            Type = EncryptionType.AES_GCM
        };

        _symmetricCryptoMock
            .Setup(s => s.EncryptAsync(masterKey, EncryptionType.AES_GCM, recoveryKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CryptoResult<SymmetricCiphertext>.Success(symmetricResult));

        var result = await _sut.EncryptMasterKeyAsync(masterKey, recoveryKey);

        result.Should().NotBeNullOrEmpty();
        // Should be valid Base64
        var decoded = Convert.FromBase64String(result);
        decoded.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task EncryptDecryptMasterKey_Roundtrip_Succeeds()
    {
        var masterKey = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var recoveryKey = new byte[32];
        var iv = new byte[12];
        var ciphertext = new byte[] { 99, 98, 97 };

        var symmetricCiphertext = new SymmetricCiphertext
        {
            Data = ciphertext,
            Key = recoveryKey,
            IV = iv,
            Type = EncryptionType.AES_GCM
        };

        _symmetricCryptoMock
            .Setup(s => s.EncryptAsync(masterKey, EncryptionType.AES_GCM, recoveryKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CryptoResult<SymmetricCiphertext>.Success(symmetricCiphertext));

        _symmetricCryptoMock
            .Setup(s => s.DecryptAsync(It.IsAny<SymmetricCiphertext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CryptoResult<byte[]>.Success(masterKey));

        var encrypted = await _sut.EncryptMasterKeyAsync(masterKey, recoveryKey);
        var decrypted = await _sut.DecryptMasterKeyAsync(encrypted, recoveryKey);

        decrypted.Should().Equal(masterKey);
    }

    [Fact]
    public async Task DecryptMasterKeyAsync_CryptoFailure_ThrowsCryptographicException()
    {
        var recoveryKey = new byte[32];
        // Create a valid blob: [4-byte IV length][12 bytes IV][some data]
        var iv = new byte[12];
        var data = new byte[] { 1, 2, 3 };
        var blob = new byte[4 + 12 + 3];
        BitConverter.GetBytes(12).CopyTo(blob, 0);
        iv.CopyTo(blob, 4);
        data.CopyTo(blob, 16);
        var encryptedBlob = Convert.ToBase64String(blob);

        _symmetricCryptoMock
            .Setup(s => s.DecryptAsync(It.IsAny<SymmetricCiphertext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CryptoResult<byte[]>.Failure(CryptoStatus.DecryptionFailed, "Bad key"));

        var act = () => _sut.DecryptMasterKeyAsync(encryptedBlob, recoveryKey);

        await act.Should().ThrowAsync<System.Security.Cryptography.CryptographicException>()
            .WithMessage("*Bad key*");
    }

    [Fact]
    public void MapAlgorithm_InvalidAlgorithm_ThrowsArgumentException()
    {
        var act = () => _sut.WrapRecoveryKeyAsync(new byte[32], new byte[32], "INVALID");

        act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Unsupported algorithm*");
    }
}
