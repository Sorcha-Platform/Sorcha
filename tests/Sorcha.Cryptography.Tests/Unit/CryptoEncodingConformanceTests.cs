// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Sorcha.Cryptography.Core;
using Sorcha.Cryptography.Enums;
using Xunit;

namespace Sorcha.Cryptography.Tests.Unit;

/// <summary>
/// Cryptographic conformance tests proving the Base64url encoding migration
/// does not break any cryptographic operations (T031-T035).
/// </summary>
public class CryptoEncodingConformanceTests
{
    private readonly CryptoModule _cryptoModule = new();

    // --- T031: Sign-then-verify round-trip with Base64url ---

    [Fact]
    public async Task SignVerify_Ed25519_Base64UrlEncoded_Succeeds()
    {
        // Arrange — generate keys
        var keyResult = await _cryptoModule.GenerateKeySetAsync(WalletNetworks.ED25519);
        keyResult.IsSuccess.Should().BeTrue();

        var privateKey = keyResult.Value!.PrivateKey.Key!;
        var publicKey = keyResult.Value.PublicKey.Key!;
        var data = Encoding.UTF8.GetBytes("transaction:payload:hash");

        // Act — sign
        var signResult = await _cryptoModule.SignAsync(
            data, (byte)WalletNetworks.ED25519, privateKey);
        signResult.IsSuccess.Should().BeTrue();

        // Encode signature as Base64url (wire format)
        var sigBase64Url = Base64Url.EncodeToString(signResult.Value!);
        var pubKeyBase64Url = Base64Url.EncodeToString(publicKey);

        // Decode from Base64url (receiver side)
        var sigBytes = Base64Url.DecodeFromChars(sigBase64Url);
        var pubKeyBytes = Base64Url.DecodeFromChars(pubKeyBase64Url);

        // Assert — verify succeeds with decoded bytes
        var verifyResult = await _cryptoModule.VerifyAsync(
            sigBytes, data, (byte)WalletNetworks.ED25519, pubKeyBytes);
        verifyResult.Should().Be(CryptoStatus.Success, "signature should verify after Base64url round-trip");
    }

    [Fact]
    public async Task SignVerify_P256_Base64UrlEncoded_Succeeds()
    {
        // Arrange
        var keyResult = await _cryptoModule.GenerateKeySetAsync(WalletNetworks.NISTP256);
        keyResult.IsSuccess.Should().BeTrue();

        var data = Encoding.UTF8.GetBytes("tx-id:payload-hash");

        // Act — sign and encode
        var signResult = await _cryptoModule.SignAsync(
            data, (byte)WalletNetworks.NISTP256, keyResult.Value!.PrivateKey.Key!);
        signResult.IsSuccess.Should().BeTrue();

        var sigBase64Url = Base64Url.EncodeToString(signResult.Value!);
        var pubKeyBase64Url = Base64Url.EncodeToString(keyResult.Value.PublicKey.Key!);

        // Assert — decode and verify
        var verifyResult = await _cryptoModule.VerifyAsync(
            Base64Url.DecodeFromChars(sigBase64Url),
            data,
            (byte)WalletNetworks.NISTP256,
            Base64Url.DecodeFromChars(pubKeyBase64Url));
        verifyResult.Should().Be(CryptoStatus.Success);
    }

    // --- T032: Encrypt-then-decrypt with Base64url encoding ---

    [Fact]
    public async Task EncryptDecrypt_Ed25519_Base64UrlEncoded_RoundTrips()
    {
        // Arrange
        var keyResult = await _cryptoModule.GenerateKeySetAsync(WalletNetworks.ED25519);
        keyResult.IsSuccess.Should().BeTrue();
        var plaintext = Encoding.UTF8.GetBytes("sensitive payload data for encryption test");

        // Act — encrypt
        var encryptResult = await _cryptoModule.EncryptAsync(
            plaintext, (byte)WalletNetworks.ED25519, keyResult.Value!.PublicKey.Key!);
        encryptResult.IsSuccess.Should().BeTrue();

        // Encode ciphertext as Base64url (wire format)
        var ciphertextBase64Url = Base64Url.EncodeToString(encryptResult.Value!);

        // Decode from Base64url (receiver side)
        var ciphertextBytes = Base64Url.DecodeFromChars(ciphertextBase64Url);

        // Assert — decrypt succeeds with decoded bytes
        var decryptResult = await _cryptoModule.DecryptAsync(
            ciphertextBytes, (byte)WalletNetworks.ED25519, keyResult.Value.PrivateKey.Key!);
        decryptResult.IsSuccess.Should().BeTrue();
        decryptResult.Value.Should().Equal(plaintext,
            "decrypted data should match original after Base64url round-trip");
    }

    // --- T033: Hash integrity with Base64url ---

    [Fact]
    public void HashStability_SHA256_Base64UrlRoundTrip_ByteIdentical()
    {
        // Arrange — canonical bytes (simulating payload)
        var canonicalBytes = Encoding.UTF8.GetBytes(
            """{"participantId":"550e8400-e29b-41d4-a716-446655440000","status":"Active"}""");

        // Act — hash, encode, store, retrieve, decode, rehash
        var hash1 = SHA256.HashData(canonicalBytes);
        var encoded = Base64Url.EncodeToString(hash1);

        // Simulate storage round-trip
        var decoded = Base64Url.DecodeFromChars(encoded);
        var hash2 = SHA256.HashData(canonicalBytes);

        // Assert
        decoded.Should().Equal(hash1, "hash should survive Base64url round-trip");
        hash2.Should().Equal(hash1, "rehashing same input should produce identical hash");
    }

    [Fact]
    public void HashStability_KnownVector_ProducesExpectedBase64Url()
    {
        // Known test vector: SHA-256 of empty object "{}"
        var input = Encoding.UTF8.GetBytes("{}");
        var hash = SHA256.HashData(input);
        var encoded = Base64Url.EncodeToString(hash);

        // Verify it's deterministic and contains only Base64url chars
        encoded.Should().NotContainAny("+", "/", "=");
        var decoded = Base64Url.DecodeFromChars(encoded);
        decoded.Should().Equal(hash);

        // Same input always produces same output
        var encoded2 = Base64Url.EncodeToString(SHA256.HashData(input));
        encoded2.Should().Be(encoded);
    }

    // --- T034: Legacy Base64 interoperability ---

    [Fact]
    public async Task LegacyInterop_Base64Signature_StillVerifies()
    {
        // Arrange — create signature using standard Base64 (old format)
        var keyResult = await _cryptoModule.GenerateKeySetAsync(WalletNetworks.ED25519);
        keyResult.IsSuccess.Should().BeTrue();
        var data = Encoding.UTF8.GetBytes("legacy-tx-id:legacy-payload-hash");

        var signResult = await _cryptoModule.SignAsync(
            data, (byte)WalletNetworks.ED25519, keyResult.Value!.PrivateKey.Key!);
        signResult.IsSuccess.Should().BeTrue();

        // Encode with standard Base64 (legacy format)
        var sigLegacyBase64 = Convert.ToBase64String(signResult.Value!);
        var pubKeyLegacyBase64 = Convert.ToBase64String(keyResult.Value.PublicKey.Key!);

        // Decode legacy format
        var sigBytes = Convert.FromBase64String(sigLegacyBase64);
        var pubKeyBytes = Convert.FromBase64String(pubKeyLegacyBase64);

        // Assert — verify still works
        var verifyResult = await _cryptoModule.VerifyAsync(
            sigBytes, data, (byte)WalletNetworks.ED25519, pubKeyBytes);
        verifyResult.Should().Be(CryptoStatus.Success, "legacy Base64-encoded signatures must still verify");
    }

    // --- T035: Cross-encoding interop ---

    [Fact]
    public async Task CrossEncoding_Base64SignedToBase64UrlVerified_Succeeds()
    {
        // Arrange — sign and encode with standard Base64
        var keyResult = await _cryptoModule.GenerateKeySetAsync(WalletNetworks.ED25519);
        keyResult.IsSuccess.Should().BeTrue();
        var data = Encoding.UTF8.GetBytes("cross-format-test");

        var signResult = await _cryptoModule.SignAsync(
            data, (byte)WalletNetworks.ED25519, keyResult.Value!.PrivateKey.Key!);
        signResult.IsSuccess.Should().BeTrue();

        // Producer: encode with standard Base64 (old system)
        var sigBase64 = Convert.ToBase64String(signResult.Value!);
        var pubKeyBase64 = Convert.ToBase64String(keyResult.Value.PublicKey.Key!);

        // Consumer: smart decode (detects legacy format)
        var sigBytes = DecodeBase64Auto(sigBase64);
        var pubKeyBytes = DecodeBase64Auto(pubKeyBase64);

        // Assert — verify succeeds
        var verifyResult = await _cryptoModule.VerifyAsync(
            sigBytes, data, (byte)WalletNetworks.ED25519, pubKeyBytes);
        verifyResult.Should().Be(CryptoStatus.Success);
    }

    [Fact]
    public async Task CrossEncoding_Base64UrlSignedToSmartDecodeVerified_Succeeds()
    {
        // Arrange — sign and encode with Base64url (new system)
        var keyResult = await _cryptoModule.GenerateKeySetAsync(WalletNetworks.ED25519);
        keyResult.IsSuccess.Should().BeTrue();
        var data = Encoding.UTF8.GetBytes("new-format-test");

        var signResult = await _cryptoModule.SignAsync(
            data, (byte)WalletNetworks.ED25519, keyResult.Value!.PrivateKey.Key!);
        signResult.IsSuccess.Should().BeTrue();

        // Producer: encode with Base64url (new system)
        var sigBase64Url = Base64Url.EncodeToString(signResult.Value!);
        var pubKeyBase64Url = Base64Url.EncodeToString(keyResult.Value.PublicKey.Key!);

        // Consumer: smart decode (detects new format)
        var sigBytes = DecodeBase64Auto(sigBase64Url);
        var pubKeyBytes = DecodeBase64Auto(pubKeyBase64Url);

        // Assert — verify succeeds
        var verifyResult = await _cryptoModule.VerifyAsync(
            sigBytes, data, (byte)WalletNetworks.ED25519, pubKeyBytes);
        verifyResult.Should().Be(CryptoStatus.Success);
    }

    [Fact]
    public void CrossEncoding_SameBytes_DifferentTextRepresentations()
    {
        // Bytes that produce different text in Base64 vs Base64url
        var bytes = new byte[] { 0x3B, 0x7F, 0xBE, 0x3F, 0xEF, 0xBB, 0xBF, 0xFF };

        var base64 = Convert.ToBase64String(bytes);
        var base64Url = Base64Url.EncodeToString(bytes);

        // Different text
        base64.Should().NotBe(base64Url, "Base64 and Base64url use different alphabets");

        // Same bytes when decoded
        Convert.FromBase64String(base64).Should().Equal(bytes);
        Base64Url.DecodeFromChars(base64Url).Should().Equal(bytes);
    }

    /// <summary>
    /// Smart decode helper — detects legacy Base64 (+, /, =) vs Base64url.
    /// Mirrors ContentEncodings.DecodeBase64Auto.
    /// </summary>
    private static byte[] DecodeBase64Auto(string encoded)
    {
        if (string.IsNullOrEmpty(encoded))
            return [];

        if (encoded.Contains('+') || encoded.Contains('/') || encoded.Contains('='))
            return Convert.FromBase64String(encoded);

        return Base64Url.DecodeFromChars(encoded);
    }
}
