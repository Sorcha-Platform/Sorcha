// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Cryptography.Core;
using Sorcha.Cryptography.Enums;
using Xunit;

namespace Sorcha.Cryptography.Tests.Unit.Pqc;

/// <summary>
/// Tests ML-KEM-768 encrypt/decrypt round-trips through CryptoModule,
/// verifying the EncryptWithKemAsync / DecryptWithKemAsync integration.
/// </summary>
public class CryptoModuleMlKemTests
{
    private readonly CryptoModule _crypto = new();

    [Fact]
    public async Task EncryptDecrypt_RoundTrip_ReturnsOriginalData()
    {
        // Arrange
        var keyResult = await _crypto.GenerateKeySetAsync(WalletNetworks.ML_KEM_768);
        keyResult.IsSuccess.Should().BeTrue();
        var keys = keyResult.Value!;
        var plaintext = "Hello, post-quantum world!"u8.ToArray();

        // Act
        var encResult = await _crypto.EncryptAsync(plaintext, (byte)WalletNetworks.ML_KEM_768, keys.PublicKey.Key!);
        encResult.IsSuccess.Should().BeTrue("ML-KEM-768 encryption should succeed");

        var decResult = await _crypto.DecryptAsync(encResult.Value!, (byte)WalletNetworks.ML_KEM_768, keys.PrivateKey.Key!);
        decResult.IsSuccess.Should().BeTrue("ML-KEM-768 decryption should succeed");

        // Assert
        decResult.Value.Should().BeEquivalentTo(plaintext);
    }

    [Fact]
    public async Task EncryptDecrypt_32ByteSymmetricKey_RoundTrips()
    {
        // This is the encryption pipeline use case: wrapping a 32-byte symmetric key
        var keyResult = await _crypto.GenerateKeySetAsync(WalletNetworks.ML_KEM_768);
        keyResult.IsSuccess.Should().BeTrue();
        var keys = keyResult.Value!;
        var symmetricKey = new byte[32];
        Random.Shared.NextBytes(symmetricKey);

        var encResult = await _crypto.EncryptAsync(symmetricKey, (byte)WalletNetworks.ML_KEM_768, keys.PublicKey.Key!);
        encResult.IsSuccess.Should().BeTrue();

        var decResult = await _crypto.DecryptAsync(encResult.Value!, (byte)WalletNetworks.ML_KEM_768, keys.PrivateKey.Key!);
        decResult.IsSuccess.Should().BeTrue();
        decResult.Value.Should().BeEquivalentTo(symmetricKey);
    }

    [Fact]
    public async Task EncryptDecrypt_LargePayload_RoundTrips()
    {
        var keyResult = await _crypto.GenerateKeySetAsync(WalletNetworks.ML_KEM_768);
        var keys = keyResult.Value!;
        var plaintext = new byte[5000];
        Random.Shared.NextBytes(plaintext);

        var encResult = await _crypto.EncryptAsync(plaintext, (byte)WalletNetworks.ML_KEM_768, keys.PublicKey.Key!);
        encResult.IsSuccess.Should().BeTrue();

        var decResult = await _crypto.DecryptAsync(encResult.Value!, (byte)WalletNetworks.ML_KEM_768, keys.PrivateKey.Key!);
        decResult.IsSuccess.Should().BeTrue();
        decResult.Value.Should().BeEquivalentTo(plaintext);
    }

    [Fact]
    public async Task Encrypt_CiphertextFormat_HasKemPrefix()
    {
        var keyResult = await _crypto.GenerateKeySetAsync(WalletNetworks.ML_KEM_768);
        var keys = keyResult.Value!;
        var plaintext = new byte[100];

        var encResult = await _crypto.EncryptAsync(plaintext, (byte)WalletNetworks.ML_KEM_768, keys.PublicKey.Key!);
        encResult.IsSuccess.Should().BeTrue();

        // Format: [KEM ciphertext (1088)] [nonce (24)] [symmetric ciphertext (var)]
        // Minimum length: 1088 + 24 + plaintext.Length + auth_tag
        encResult.Value!.Length.Should().BeGreaterThan(1088 + 24);
    }

    [Fact]
    public async Task Decrypt_WrongKey_ReturnsFailure()
    {
        var keyResult1 = await _crypto.GenerateKeySetAsync(WalletNetworks.ML_KEM_768);
        var keyResult2 = await _crypto.GenerateKeySetAsync(WalletNetworks.ML_KEM_768);
        var keys1 = keyResult1.Value!;
        var keys2 = keyResult2.Value!;
        var plaintext = "secret"u8.ToArray();

        var encResult = await _crypto.EncryptAsync(plaintext, (byte)WalletNetworks.ML_KEM_768, keys1.PublicKey.Key!);
        encResult.IsSuccess.Should().BeTrue();

        var decResult = await _crypto.DecryptAsync(encResult.Value!, (byte)WalletNetworks.ML_KEM_768, keys2.PrivateKey.Key!);
        decResult.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Decrypt_TruncatedCiphertext_ReturnsFailure()
    {
        var keyResult = await _crypto.GenerateKeySetAsync(WalletNetworks.ML_KEM_768);
        var keys = keyResult.Value!;
        var shortCiphertext = new byte[100]; // Way too short

        var decResult = await _crypto.DecryptAsync(shortCiphertext, (byte)WalletNetworks.ML_KEM_768, keys.PrivateKey.Key!);
        decResult.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Encrypt_DifferentEncryptions_ProduceDifferentCiphertexts()
    {
        var keyResult = await _crypto.GenerateKeySetAsync(WalletNetworks.ML_KEM_768);
        var keys = keyResult.Value!;
        var plaintext = "same data"u8.ToArray();

        var enc1 = await _crypto.EncryptAsync(plaintext, (byte)WalletNetworks.ML_KEM_768, keys.PublicKey.Key!);
        var enc2 = await _crypto.EncryptAsync(plaintext, (byte)WalletNetworks.ML_KEM_768, keys.PublicKey.Key!);

        enc1.Value.Should().NotBeEquivalentTo(enc2.Value);
    }
}
