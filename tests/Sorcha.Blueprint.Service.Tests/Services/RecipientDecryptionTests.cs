// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Services.Implementation;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.Cryptography.Enums;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Cryptography.Models;
using Sorcha.ServiceClients.Wallet;
using Sorcha.TransactionHandler.Encryption.Models;

namespace Sorcha.Blueprint.Service.Tests.Services;

/// <summary>
/// Tests for recipient decryption in TransactionRetrievalService.
/// Covers T053: authorized decrypt, unauthorized denied, AEAD-tag tamper rejection (#1695 —
/// supersedes the removed plaintextHash confirmation-oracle check), legacy unencrypted backward
/// compatibility, and rotated key error message.
/// </summary>
public class RecipientDecryptionTests
{
    private readonly Mock<IWalletServiceClient> _mockWalletClient;
    private readonly Mock<ISymmetricCrypto> _mockSymmetricCrypto;
    private readonly Mock<ILogger<TransactionRetrievalService>> _mockLogger;
    private readonly TransactionRetrievalService _service;

    public RecipientDecryptionTests()
    {
        _mockWalletClient = new Mock<IWalletServiceClient>();
        _mockSymmetricCrypto = new Mock<ISymmetricCrypto>();
        _mockLogger = new Mock<ILogger<TransactionRetrievalService>>();
        _service = new TransactionRetrievalService(
            _mockWalletClient.Object,
            _mockSymmetricCrypto.Object,
            _mockLogger.Object);
    }

    [Fact]
    public async Task DecryptPayloadForRecipient_AuthorizedRecipient_ReturnsDecryptedFields()
    {
        // Arrange
        var walletAddress = "wallet-recipient-1";
        var symmetricKey = new byte[32]; // 256-bit key
        Array.Fill(symmetricKey, (byte)0xAA);

        var plaintextPayload = new Dictionary<string, object>
        {
            ["name"] = "Alice",
            ["amount"] = 42
        };
        var plaintextBytes = JsonSerializer.SerializeToUtf8Bytes(plaintextPayload);

        var encryptedGroups = new[]
        {
            new EncryptedPayloadGroup
            {
                GroupId = "group-1",
                Ciphertext = new byte[64],
                Nonce = new byte[24],
                EncryptionAlgorithm = EncryptionType.XCHACHA20_POLY1305,
                WrappedKeys =
                [
                    new WrappedKey
                    {
                        WalletAddress = walletAddress,
                        EncryptedKey = new byte[48],
                        Algorithm = WalletNetworks.ED25519
                    }
                ]
            }
        };

        // Mock wallet service returns the unwrapped symmetric key
        _mockWalletClient
            .Setup(w => w.DecryptPayloadAsync(walletAddress, It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(symmetricKey);

        // Mock symmetric crypto returns plaintext bytes
        _mockSymmetricCrypto
            .Setup(s => s.DecryptAsync(It.IsAny<SymmetricCiphertext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CryptoResult<byte[]>.Success(plaintextBytes));

        // Act
        var result = await _service.DecryptPayloadForRecipientAsync(encryptedGroups, walletAddress);

        // Assert
        result.Success.Should().BeTrue();
        result.DecryptedPayload.Should().NotBeNull();
        result.DecryptedPayload.Should().ContainKey("name");
        result.DecryptedPayload.Should().ContainKey("amount");
        result.Error.Should().BeNull();

        _mockWalletClient.Verify(w =>
            w.DecryptPayloadAsync(walletAddress, It.IsAny<byte[]>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DecryptPayloadForRecipient_UnauthorizedWallet_ReturnsDeniedError()
    {
        // Arrange
        var unauthorizedWallet = "wallet-unauthorized";
        var encryptedGroups = new[]
        {
            new EncryptedPayloadGroup
            {
                GroupId = "group-1",
                Ciphertext = new byte[64],
                Nonce = new byte[24],
                EncryptionAlgorithm = EncryptionType.XCHACHA20_POLY1305,
                WrappedKeys =
                [
                    new WrappedKey
                    {
                        WalletAddress = "wallet-authorized-only",
                        EncryptedKey = new byte[48],
                        Algorithm = WalletNetworks.ED25519
                    }
                ]
            }
        };

        // Act
        var result = await _service.DecryptPayloadForRecipientAsync(encryptedGroups, unauthorizedWallet);

        // Assert
        result.Success.Should().BeFalse();
        result.DecryptedPayload.Should().BeNull();
        result.Error.Should().Contain("Access denied");
        result.Error.Should().Contain("wallet");
        result.Error.Should().Contain("not authorized for any disclosure group");

        // Wallet service should never be called for unauthorized wallets
        _mockWalletClient.Verify(w =>
            w.DecryptPayloadAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task DecryptPayloadForRecipient_TamperedCiphertext_RejectedByAeadTag()
    {
        // Issue #1695 — the separate plaintextHash confirmation-oracle check is gone. This test is
        // the assertion that removing it is safe: tampered ciphertext (or a wrong key — the AEAD
        // tag cannot distinguish the two) is still rejected, because it fails XChaCha20-Poly1305
        // authentication before this method ever gets a plaintext to inspect.
        var walletAddress = "wallet-recipient-1";
        var symmetricKey = new byte[32];

        var encryptedGroups = new[]
        {
            new EncryptedPayloadGroup
            {
                GroupId = "group-1",
                Ciphertext = new byte[64],
                Nonce = new byte[24],
                EncryptionAlgorithm = EncryptionType.XCHACHA20_POLY1305,
                WrappedKeys =
                [
                    new WrappedKey
                    {
                        WalletAddress = walletAddress,
                        EncryptedKey = new byte[48],
                        Algorithm = WalletNetworks.ED25519
                    }
                ]
            }
        };

        _mockWalletClient
            .Setup(w => w.DecryptPayloadAsync(walletAddress, It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(symmetricKey);

        // Tampered ciphertext (or a stale/rotated key) fails Poly1305 tag verification —
        // ISymmetricCrypto surfaces this as a failed CryptoResult, never a "successful" decrypt of
        // garbage bytes.
        _mockSymmetricCrypto
            .Setup(s => s.DecryptAsync(It.IsAny<SymmetricCiphertext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CryptoResult<byte[]>.Failure(
                CryptoStatus.DecryptionFailed, "Authentication tag verification failed"));

        // Act
        var result = await _service.DecryptPayloadForRecipientAsync(encryptedGroups, walletAddress);

        // Assert — rejected via the AEAD failure path, with no plaintext-hash check involved.
        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNull();
        result.Error.Should().Contain("group-1");
    }

    [Fact]
    public void DecryptPayloadForRecipient_LegacyUnencryptedTransaction_ReturnsPlaintextDirectly()
    {
        // Arrange — transaction has no encrypted payload groups (legacy/unencrypted)
        EncryptedPayloadGroup[]? nullGroups = null;
        var emptyGroups = Array.Empty<EncryptedPayloadGroup>();

        // Act & Assert — static helper method detects legacy transactions
        ITransactionRetrievalService.IsLegacyTransaction(nullGroups).Should().BeTrue();
        ITransactionRetrievalService.IsLegacyTransaction(emptyGroups).Should().BeTrue();

        // Non-empty groups are NOT legacy
        var nonEmptyGroups = new[]
        {
            new EncryptedPayloadGroup
            {
                GroupId = "group-1",
                Ciphertext = new byte[16],
                Nonce = new byte[24],
                EncryptionAlgorithm = EncryptionType.XCHACHA20_POLY1305,
                WrappedKeys =
                [
                    new WrappedKey
                    {
                        WalletAddress = "wallet-1",
                        EncryptedKey = new byte[48],
                        Algorithm = WalletNetworks.ED25519
                    }
                ]
            }
        };
        ITransactionRetrievalService.IsLegacyTransaction(nonEmptyGroups).Should().BeFalse();
    }

    [Fact]
    public async Task DecryptPayloadForRecipient_RotatedKey_ReturnsClearErrorMessage()
    {
        // Arrange
        var walletAddress = "wallet-recipient-1";
        var encryptedGroups = new[]
        {
            new EncryptedPayloadGroup
            {
                GroupId = "group-1",
                Ciphertext = new byte[64],
                Nonce = new byte[24],
                EncryptionAlgorithm = EncryptionType.XCHACHA20_POLY1305,
                WrappedKeys =
                [
                    new WrappedKey
                    {
                        WalletAddress = walletAddress,
                        EncryptedKey = new byte[48],
                        Algorithm = WalletNetworks.ED25519
                    }
                ]
            }
        };

        // Wallet service throws — simulating key rotation where the original key is gone
        _mockWalletClient
            .Setup(w => w.DecryptPayloadAsync(walletAddress, It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Failed to decrypt: key not found or rotated"));

        // Act
        var result = await _service.DecryptPayloadForRecipientAsync(encryptedGroups, walletAddress);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNull();
        result.Error.Should().Contain(walletAddress);
        result.Error.Should().Contain("rotated");
        result.Error.Should().Contain("original");
        result.Error.Should().Contain("key");
    }
}
