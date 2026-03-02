// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Cryptography.Core;
using Sorcha.Cryptography.Enums;
using Xunit;

namespace Sorcha.Cryptography.Tests.Unit;

public class CryptoModuleNistP256Tests
{
    private readonly CryptoModule _crypto = new();

    [Fact]
    public async Task EncryptDecrypt_RoundTrip_ReturnsOriginalData()
    {
        // Arrange
        var keyResult = await _crypto.GenerateKeySetAsync(WalletNetworks.NISTP256);
        keyResult.IsSuccess.Should().BeTrue();
        var keys = keyResult.Value!;
        var plaintext = "Hello, World!"u8.ToArray();

        // Act
        var encResult = await _crypto.EncryptAsync(plaintext, (byte)WalletNetworks.NISTP256, keys.PublicKey.Key!);
        encResult.IsSuccess.Should().BeTrue("P-256 encryption should succeed");

        var decResult = await _crypto.DecryptAsync(encResult.Value!, (byte)WalletNetworks.NISTP256, keys.PrivateKey.Key!);
        decResult.IsSuccess.Should().BeTrue("P-256 decryption should succeed");

        // Assert
        decResult.Value.Should().BeEquivalentTo(plaintext);
    }

    [Fact]
    public async Task EncryptDecrypt_LargePayload_ReturnsOriginalData()
    {
        // Arrange
        var keyResult = await _crypto.GenerateKeySetAsync(WalletNetworks.NISTP256);
        keyResult.IsSuccess.Should().BeTrue();
        var keys = keyResult.Value!;
        var plaintext = new byte[10_000];
        Random.Shared.NextBytes(plaintext);

        // Act
        var encResult = await _crypto.EncryptAsync(plaintext, (byte)WalletNetworks.NISTP256, keys.PublicKey.Key!);
        encResult.IsSuccess.Should().BeTrue();

        var decResult = await _crypto.DecryptAsync(encResult.Value!, (byte)WalletNetworks.NISTP256, keys.PrivateKey.Key!);
        decResult.IsSuccess.Should().BeTrue();

        // Assert
        decResult.Value.Should().BeEquivalentTo(plaintext);
    }

    [Fact]
    public async Task Encrypt_SingleByte_RoundTrips()
    {
        var keyResult = await _crypto.GenerateKeySetAsync(WalletNetworks.NISTP256);
        var keys = keyResult.Value!;
        var plaintext = new byte[] { 0x42 };

        var encResult = await _crypto.EncryptAsync(plaintext, (byte)WalletNetworks.NISTP256, keys.PublicKey.Key!);
        encResult.IsSuccess.Should().BeTrue();

        var decResult = await _crypto.DecryptAsync(encResult.Value!, (byte)WalletNetworks.NISTP256, keys.PrivateKey.Key!);
        decResult.IsSuccess.Should().BeTrue();
        decResult.Value.Should().BeEquivalentTo(plaintext);
    }

    [Fact]
    public async Task Encrypt_InvalidPublicKeySize_ReturnsFailure()
    {
        var plaintext = "test"u8.ToArray();
        var badKey = new byte[33]; // Wrong size - should be 64

        var result = await _crypto.EncryptAsync(plaintext, (byte)WalletNetworks.NISTP256, badKey);
        result.IsSuccess.Should().BeFalse();
        result.Status.Should().Be(CryptoStatus.InvalidKey);
    }

    [Fact]
    public async Task Decrypt_InvalidPrivateKeySize_ReturnsFailure()
    {
        var ciphertext = new byte[100]; // Dummy
        var badKey = new byte[33]; // Wrong size - should be 32

        var result = await _crypto.DecryptAsync(ciphertext, (byte)WalletNetworks.NISTP256, badKey);
        result.IsSuccess.Should().BeFalse();
        result.Status.Should().Be(CryptoStatus.InvalidKey);
    }

    [Fact]
    public async Task Decrypt_TruncatedCiphertext_ReturnsFailure()
    {
        var shortCiphertext = new byte[50]; // Too short for ECIES

        var keyResult = await _crypto.GenerateKeySetAsync(WalletNetworks.NISTP256);
        var keys = keyResult.Value!;

        var result = await _crypto.DecryptAsync(shortCiphertext, (byte)WalletNetworks.NISTP256, keys.PrivateKey.Key!);
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Decrypt_WrongKey_ReturnsFailure()
    {
        var keyResult1 = await _crypto.GenerateKeySetAsync(WalletNetworks.NISTP256);
        var keyResult2 = await _crypto.GenerateKeySetAsync(WalletNetworks.NISTP256);
        var keys1 = keyResult1.Value!;
        var keys2 = keyResult2.Value!;
        var plaintext = "secret"u8.ToArray();

        var encResult = await _crypto.EncryptAsync(plaintext, (byte)WalletNetworks.NISTP256, keys1.PublicKey.Key!);
        encResult.IsSuccess.Should().BeTrue();

        // Decrypt with wrong key - AES-GCM authentication will fail
        var decResult = await _crypto.DecryptAsync(encResult.Value!, (byte)WalletNetworks.NISTP256, keys2.PrivateKey.Key!);
        decResult.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Encrypt_CiphertextFormat_HasCorrectStructure()
    {
        var keyResult = await _crypto.GenerateKeySetAsync(WalletNetworks.NISTP256);
        var keys = keyResult.Value!;
        var plaintext = new byte[100];

        var encResult = await _crypto.EncryptAsync(plaintext, (byte)WalletNetworks.NISTP256, keys.PublicKey.Key!);
        encResult.IsSuccess.Should().BeTrue();

        // Format: [ephemeral pub X (32)] [ephemeral pub Y (32)] [nonce (12)] [ciphertext (100)] [tag (16)]
        // Total = 64 + 12 + 100 + 16 = 192
        encResult.Value!.Length.Should().Be(64 + 12 + plaintext.Length + 16);
    }

    [Fact]
    public async Task Encrypt_32ByteKey_RoundTrips()
    {
        // Specific test for wrapping a 32-byte symmetric key (encryption pipeline use case)
        var keyResult = await _crypto.GenerateKeySetAsync(WalletNetworks.NISTP256);
        var keys = keyResult.Value!;
        var symmetricKey = new byte[32];
        Random.Shared.NextBytes(symmetricKey);

        var encResult = await _crypto.EncryptAsync(symmetricKey, (byte)WalletNetworks.NISTP256, keys.PublicKey.Key!);
        encResult.IsSuccess.Should().BeTrue();

        var decResult = await _crypto.DecryptAsync(encResult.Value!, (byte)WalletNetworks.NISTP256, keys.PrivateKey.Key!);
        decResult.IsSuccess.Should().BeTrue();
        decResult.Value.Should().BeEquivalentTo(symmetricKey);
    }

    [Fact]
    public async Task Encrypt_DifferentEncryptions_ProduceDifferentCiphertexts()
    {
        var keyResult = await _crypto.GenerateKeySetAsync(WalletNetworks.NISTP256);
        var keys = keyResult.Value!;
        var plaintext = "same data"u8.ToArray();

        var enc1 = await _crypto.EncryptAsync(plaintext, (byte)WalletNetworks.NISTP256, keys.PublicKey.Key!);
        var enc2 = await _crypto.EncryptAsync(plaintext, (byte)WalletNetworks.NISTP256, keys.PublicKey.Key!);

        // Ephemeral keys and nonces should differ
        enc1.Value.Should().NotBeEquivalentTo(enc2.Value);
    }
}
