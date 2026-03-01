// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Cryptography.Core;
using Sorcha.Cryptography.Enums;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Cryptography.Models;
using Sorcha.TransactionHandler.Encryption;
using Sorcha.TransactionHandler.Encryption.Models;

namespace Sorcha.TransactionHandler.Tests.Encryption;

/// <summary>
/// Integration tests for <see cref="EncryptionPipelineService"/> using real cryptographic implementations.
/// Verifies end-to-end envelope encryption with all four supported algorithms:
/// ED25519 (Curve25519 SealedBox), NIST P-256 (ECIES), RSA-4096 (OAEP-SHA256), and ML-KEM-768 (KEM).
/// </summary>
public class MixedAlgorithmEncryptionTests
{
    private readonly ICryptoModule _cryptoModule;
    private readonly ISymmetricCrypto _symmetricCrypto;
    private readonly IHashProvider _hashProvider;
    private readonly EncryptionPipelineService _sut;

    /// <summary>
    /// Standard test payload used across all tests.
    /// </summary>
    private static readonly Dictionary<string, object> TestPayload = new()
    {
        ["name"] = "Alice",
        ["amount"] = 100
    };

    /// <summary>
    /// Deterministic GroupId: SHA-256 hex hash of sorted, pipe-separated field names "amount|name".
    /// </summary>
    private static readonly string TestGroupId = ComputeGroupId("amount", "name");

    public MixedAlgorithmEncryptionTests()
    {
        _cryptoModule = new CryptoModule();
        _symmetricCrypto = new SymmetricCrypto();
        _hashProvider = new HashProvider();

        _sut = new EncryptionPipelineService(
            _symmetricCrypto,
            _cryptoModule,
            _hashProvider,
            new Mock<ILogger<EncryptionPipelineService>>().Object);
    }

    #region T031: Mixed-algorithm integration test

    [Fact]
    public async Task EncryptDisclosedPayloads_MixedAlgorithms_AllRecipientsCanDecrypt()
    {
        // Arrange — generate key pairs for all 4 algorithms
        var ed25519Keys = await _cryptoModule.GenerateKeySetAsync(WalletNetworks.ED25519);
        var p256Keys = await _cryptoModule.GenerateKeySetAsync(WalletNetworks.NISTP256);
        var rsaKeys = await _cryptoModule.GenerateKeySetAsync(WalletNetworks.RSA4096);
        var mlKemKeys = await _cryptoModule.GenerateKeySetAsync(WalletNetworks.ML_KEM_768);

        ed25519Keys.IsSuccess.Should().BeTrue("ED25519 key generation should succeed");
        p256Keys.IsSuccess.Should().BeTrue("NIST P-256 key generation should succeed");
        rsaKeys.IsSuccess.Should().BeTrue("RSA-4096 key generation should succeed");
        mlKemKeys.IsSuccess.Should().BeTrue("ML-KEM-768 key generation should succeed");

        var recipients = new[]
        {
            new RecipientInfo
            {
                WalletAddress = "ws1q_ed25519",
                PublicKey = ed25519Keys.Value!.PublicKey.Key!,
                Algorithm = WalletNetworks.ED25519,
                Source = KeySource.Register
            },
            new RecipientInfo
            {
                WalletAddress = "ws1q_p256",
                PublicKey = p256Keys.Value!.PublicKey.Key!,
                Algorithm = WalletNetworks.NISTP256,
                Source = KeySource.Register
            },
            new RecipientInfo
            {
                WalletAddress = "ws1q_rsa4096",
                PublicKey = rsaKeys.Value!.PublicKey.Key!,
                Algorithm = WalletNetworks.RSA4096,
                Source = KeySource.Register
            },
            new RecipientInfo
            {
                WalletAddress = "ws1q_mlkem768",
                PublicKey = mlKemKeys.Value!.PublicKey.Key!,
                Algorithm = WalletNetworks.ML_KEM_768,
                Source = KeySource.Register
            }
        };

        var group = new DisclosureGroup
        {
            GroupId = TestGroupId,
            DisclosedFields = ["amount", "name"],
            FilteredPayload = TestPayload,
            Recipients = recipients
        };

        // Act
        var result = await _sut.EncryptDisclosedPayloadsAsync([group]);

        // Assert — encryption succeeded with 1 group and 4 wrapped keys
        result.Success.Should().BeTrue($"Encryption should succeed, but got error: {result.Error}");
        result.Groups.Should().HaveCount(1);
        result.Groups[0].WrappedKeys.Should().HaveCount(4);
        result.Groups[0].GroupId.Should().Be(TestGroupId);
        result.Groups[0].EncryptionAlgorithm.Should().Be(EncryptionType.XCHACHA20_POLY1305);

        // Serialize expected plaintext for comparison
        var expectedPlaintextBytes = JsonSerializer.SerializeToUtf8Bytes(
            TestPayload, new JsonSerializerOptions { WriteIndented = false });

        // Verify each recipient can unwrap and decrypt
        var privateKeys = new Dictionary<string, (byte[] PrivateKey, WalletNetworks Algorithm)>
        {
            ["ws1q_ed25519"] = (ed25519Keys.Value!.PrivateKey.Key!, WalletNetworks.ED25519),
            ["ws1q_p256"] = (p256Keys.Value!.PrivateKey.Key!, WalletNetworks.NISTP256),
            ["ws1q_rsa4096"] = (rsaKeys.Value!.PrivateKey.Key!, WalletNetworks.RSA4096),
            ["ws1q_mlkem768"] = (mlKemKeys.Value!.PrivateKey.Key!, WalletNetworks.ML_KEM_768)
        };

        byte[]? firstDecryptedSymKey = null;

        foreach (var wrappedKey in result.Groups[0].WrappedKeys)
        {
            var (privateKey, algorithm) = privateKeys[wrappedKey.WalletAddress];

            // Unwrap the symmetric key
            var unwrapResult = await _cryptoModule.DecryptAsync(
                wrappedKey.EncryptedKey, (byte)algorithm, privateKey);
            unwrapResult.IsSuccess.Should().BeTrue(
                $"Key unwrap should succeed for {wrappedKey.WalletAddress} ({algorithm}), " +
                $"but got: {unwrapResult.ErrorMessage}");

            var decryptedSymKey = unwrapResult.Value!;

            // All recipients should get the same symmetric key
            if (firstDecryptedSymKey == null)
            {
                // Clone to preserve the value — SymmetricCiphertext.Dispose() will zeroize Key
                firstDecryptedSymKey = (byte[])decryptedSymKey.Clone();
            }
            else
            {
                decryptedSymKey.Should().BeEquivalentTo(firstDecryptedSymKey,
                    $"All recipients should recover the same symmetric key, " +
                    $"but {wrappedKey.WalletAddress} got a different one");
            }

            // Decrypt the ciphertext using the unwrapped symmetric key
            // Clone the key, nonce, and ciphertext to avoid SymmetricCiphertext.Dispose() zeroizing them
            var decryptResult = await _symmetricCrypto.DecryptAsync(new SymmetricCiphertext
            {
                Data = (byte[])result.Groups[0].Ciphertext.Clone(),
                Key = (byte[])decryptedSymKey.Clone(),
                IV = (byte[])result.Groups[0].Nonce.Clone(),
                Type = result.Groups[0].EncryptionAlgorithm
            });
            decryptResult.IsSuccess.Should().BeTrue(
                $"Payload decryption should succeed for {wrappedKey.WalletAddress}");

            // Verify plaintext matches original
            var decryptedJson = Encoding.UTF8.GetString(decryptResult.Value!);
            var originalJson = Encoding.UTF8.GetString(expectedPlaintextBytes);
            decryptedJson.Should().Be(originalJson,
                $"Decrypted plaintext should match original for {wrappedKey.WalletAddress}");

            // Verify integrity hash
            var hash = _hashProvider.ComputeHash(decryptResult.Value!, HashType.SHA256);
            hash.Should().BeEquivalentTo(result.Groups[0].PlaintextHash,
                $"Plaintext hash should match for {wrappedKey.WalletAddress}");
        }
    }

    #endregion

    #region T032: ED25519 individual round-trip

    [Fact]
    public async Task EncryptDisclosedPayloads_ED25519_KeyWrapRoundTrip()
    {
        await VerifyKeyWrapRoundTrip(WalletNetworks.ED25519);
    }

    #endregion

    #region T033: P-256 ECIES individual round-trip

    [Fact]
    public async Task EncryptDisclosedPayloads_NistP256_KeyWrapRoundTrip()
    {
        await VerifyKeyWrapRoundTrip(WalletNetworks.NISTP256);
    }

    #endregion

    #region T034: RSA-4096 individual round-trip

    [Fact]
    public async Task EncryptDisclosedPayloads_RSA4096_KeyWrapRoundTrip()
    {
        await VerifyKeyWrapRoundTrip(WalletNetworks.RSA4096);
    }

    #endregion

    #region T035: ML-KEM-768 individual round-trip

    [Fact]
    public async Task EncryptDisclosedPayloads_MlKem768_KeyWrapRoundTrip()
    {
        await VerifyKeyWrapRoundTrip(WalletNetworks.ML_KEM_768);
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Verifies the full encrypt-unwrap-decrypt round trip for a single algorithm.
    /// Generates a real key pair, encrypts a test payload through the pipeline,
    /// unwraps the symmetric key, decrypts the ciphertext, and verifies the plaintext and hash.
    /// </summary>
    private async Task VerifyKeyWrapRoundTrip(WalletNetworks algorithm)
    {
        // Generate key pair
        var keyResult = await _cryptoModule.GenerateKeySetAsync(algorithm);
        keyResult.IsSuccess.Should().BeTrue($"Key generation should succeed for {algorithm}");

        var publicKey = keyResult.Value!.PublicKey.Key!;
        var privateKey = keyResult.Value!.PrivateKey.Key!;

        // Build single-recipient group
        var recipient = new RecipientInfo
        {
            WalletAddress = $"ws1q_{algorithm}",
            PublicKey = publicKey,
            Algorithm = algorithm,
            Source = KeySource.Register
        };

        var group = new DisclosureGroup
        {
            GroupId = TestGroupId,
            DisclosedFields = ["amount", "name"],
            FilteredPayload = TestPayload,
            Recipients = [recipient]
        };

        // Encrypt
        var result = await _sut.EncryptDisclosedPayloadsAsync([group]);
        result.Success.Should().BeTrue(
            $"Encryption should succeed for {algorithm}, but got error: {result.Error}");
        result.Groups.Should().HaveCount(1);
        result.Groups[0].WrappedKeys.Should().HaveCount(1);

        var encryptedGroup = result.Groups[0];

        // Verify metadata
        encryptedGroup.GroupId.Should().Be(TestGroupId);
        encryptedGroup.EncryptionAlgorithm.Should().Be(EncryptionType.XCHACHA20_POLY1305);
        encryptedGroup.Ciphertext.Should().NotBeNullOrEmpty();
        encryptedGroup.Nonce.Should().NotBeNullOrEmpty();
        encryptedGroup.PlaintextHash.Should().NotBeNullOrEmpty();
        encryptedGroup.WrappedKeys[0].WalletAddress.Should().Be($"ws1q_{algorithm}");
        encryptedGroup.WrappedKeys[0].Algorithm.Should().Be(algorithm);

        // Unwrap the symmetric key
        var wrappedKey = encryptedGroup.WrappedKeys[0];
        var unwrapResult = await _cryptoModule.DecryptAsync(
            wrappedKey.EncryptedKey, (byte)algorithm, privateKey);
        unwrapResult.IsSuccess.Should().BeTrue(
            $"Key unwrap should succeed for {algorithm}, but got: {unwrapResult.ErrorMessage}");

        var decryptedSymKey = unwrapResult.Value!;
        decryptedSymKey.Should().HaveCount(32,
            "Unwrapped symmetric key should be 32 bytes (XChaCha20-Poly1305 key size)");

        // Decrypt the payload
        var decryptResult = await _symmetricCrypto.DecryptAsync(new SymmetricCiphertext
        {
            Data = encryptedGroup.Ciphertext,
            Key = decryptedSymKey,
            IV = encryptedGroup.Nonce,
            Type = encryptedGroup.EncryptionAlgorithm
        });
        decryptResult.IsSuccess.Should().BeTrue(
            $"Payload decryption should succeed for {algorithm}, but got: {decryptResult.ErrorMessage}");

        // Verify plaintext matches original
        var expectedPlaintextBytes = JsonSerializer.SerializeToUtf8Bytes(
            TestPayload, new JsonSerializerOptions { WriteIndented = false });
        var decryptedJson = Encoding.UTF8.GetString(decryptResult.Value!);
        var expectedJson = Encoding.UTF8.GetString(expectedPlaintextBytes);
        decryptedJson.Should().Be(expectedJson,
            $"Decrypted plaintext should match original for {algorithm}");

        // Verify integrity hash
        var hash = _hashProvider.ComputeHash(decryptResult.Value!, HashType.SHA256);
        hash.Should().BeEquivalentTo(encryptedGroup.PlaintextHash,
            $"SHA-256 hash of decrypted plaintext should match PlaintextHash for {algorithm}");
    }

    /// <summary>
    /// Computes a deterministic GroupId as a SHA-256 hex hash of sorted, pipe-separated field names.
    /// </summary>
    private static string ComputeGroupId(params string[] fields)
    {
        var sorted = fields.OrderBy(f => f).ToArray();
        var joined = string.Join("|", sorted);
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(joined));
        return Convert.ToHexStringLower(hashBytes);
    }

    #endregion
}
