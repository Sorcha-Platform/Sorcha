// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Cryptography.Core;
using Sorcha.Cryptography.Enums;
using Xunit;

namespace Sorcha.Cryptography.Tests.Unit.Pqc;

public class PqcEncryptionFlowTests : IDisposable
{
    private readonly PqcEncapsulationProvider _provider = new();
    private readonly CryptoModule _cryptoModule = new();

    public void Dispose() => _provider.Dispose();

    [Fact]
    public async Task EncryptWithKem_ThenDecryptWithKem_RecoversPlaintext()
    {
        // Generate ML-KEM-768 key pair
        var keyResult = _provider.GenerateMlKem768KeyPair();
        keyResult.IsSuccess.Should().BeTrue();

        var plaintext = System.Text.Encoding.UTF8.GetBytes("Hello, quantum-safe world!");

        // Encrypt
        var encryptResult = await _provider.EncryptWithKemAsync(
            plaintext, keyResult.Value.PublicKey.Key!);
        encryptResult.IsSuccess.Should().BeTrue();

        // Decrypt
        var decryptResult = await _provider.DecryptWithKemAsync(
            encryptResult.Value!, keyResult.Value.PrivateKey.Key!);
        decryptResult.IsSuccess.Should().BeTrue();

        decryptResult.Value.Should().BeEquivalentTo(plaintext);
    }

    [Fact]
    public async Task EncryptWithKem_ProducesPackedCiphertext()
    {
        var keyResult = _provider.GenerateMlKem768KeyPair();
        var plaintext = System.Text.Encoding.UTF8.GetBytes("test payload");

        var result = await _provider.EncryptWithKemAsync(
            plaintext, keyResult.Value.PublicKey.Key!);

        result.IsSuccess.Should().BeTrue();
        // Packed format: [1088 KEM] [24 nonce] [ciphertext + 16 tag]
        result.Value!.Length.Should().BeGreaterThan(1088 + 24,
            "packed ciphertext must contain KEM ciphertext + nonce + symmetric ciphertext");
    }

    [Fact]
    public async Task EncryptWithKem_WrongKey_FailsDecryption()
    {
        var senderKeys = _provider.GenerateMlKem768KeyPair();
        var recipientKeys = _provider.GenerateMlKem768KeyPair();
        var wrongKeys = _provider.GenerateMlKem768KeyPair();

        var plaintext = System.Text.Encoding.UTF8.GetBytes("secret data");

        // Encrypt with recipient's public key
        var encryptResult = await _provider.EncryptWithKemAsync(
            plaintext, recipientKeys.Value.PublicKey.Key!);
        encryptResult.IsSuccess.Should().BeTrue();

        // Try to decrypt with wrong private key
        var decryptResult = await _provider.DecryptWithKemAsync(
            encryptResult.Value!, wrongKeys.Value.PrivateKey.Key!);

        // Should fail (either error result or wrong plaintext)
        decryptResult.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task EncryptWithKem_EmptyPlaintext_Fails()
    {
        var keyResult = _provider.GenerateMlKem768KeyPair();

        var result = await _provider.EncryptWithKemAsync(
            Array.Empty<byte>(), keyResult.Value.PublicKey.Key!);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task EncryptWithKem_NullPublicKey_Fails()
    {
        var plaintext = System.Text.Encoding.UTF8.GetBytes("test");

        var result = await _provider.EncryptWithKemAsync(plaintext, null!);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task DecryptWithKem_TruncatedCiphertext_Fails()
    {
        var keyResult = _provider.GenerateMlKem768KeyPair();

        var result = await _provider.DecryptWithKemAsync(
            new byte[100], keyResult.Value.PrivateKey.Key!);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task CryptoModule_EncryptDecrypt_MlKem768_RoundTrip()
    {
        // Use CryptoModule for the full round trip via the Encrypt/Decrypt path
        var keyResult = await _cryptoModule.GenerateKeySetAsync(WalletNetworks.ML_KEM_768);
        keyResult.IsSuccess.Should().BeTrue();

        var plaintext = System.Text.Encoding.UTF8.GetBytes("CryptoModule ML-KEM test");

        // Encrypt (KEM encapsulation + XChaCha20-Poly1305 via CryptoModule.EncryptAsync)
        var encryptResult = await _cryptoModule.EncryptAsync(
            plaintext, (byte)WalletNetworks.ML_KEM_768, keyResult.Value.PublicKey.Key!);
        encryptResult.IsSuccess.Should().BeTrue();

        // CryptoModule now uses EncryptWithKemAsync (KEM + XChaCha20-Poly1305)
        // Decrypt should return the original plaintext
        var decryptResult = await _cryptoModule.DecryptAsync(
            encryptResult.Value!, (byte)WalletNetworks.ML_KEM_768, keyResult.Value.PrivateKey.Key!);
        decryptResult.IsSuccess.Should().BeTrue();
        decryptResult.Value.Should().BeEquivalentTo(plaintext, "decrypt should return original plaintext");
    }

    [Fact]
    public async Task SharedSecret_FromEncapsulate_MatchesDecapsulate()
    {
        var keyResult = _provider.GenerateMlKem768KeyPair();
        keyResult.IsSuccess.Should().BeTrue();

        // Encapsulate
        var encapResult = _provider.Encapsulate(keyResult.Value.PublicKey.Key!);
        encapResult.IsSuccess.Should().BeTrue();

        // Decapsulate
        var decapResult = _provider.Decapsulate(
            encapResult.Value!.Ciphertext, keyResult.Value.PrivateKey.Key!);
        decapResult.IsSuccess.Should().BeTrue();

        // Shared secrets must match
        encapResult.Value!.SharedSecret.Should().BeEquivalentTo(decapResult.Value);
    }
}
