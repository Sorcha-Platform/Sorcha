// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

// This contract suite intentionally targets the deprecated IEncryptionProvider until it is
// removed; new code uses IKeyProtectionProvider. Suppress the obsolete-usage warning file-wide.
#pragma warning disable CS0618
using FluentAssertions;
using Sorcha.Wallet.Core.Encryption.Interfaces;

namespace Sorcha.Wallet.Service.Tests.Encryption;

/// <summary>
/// Contract tests for IEncryptionProvider. All implementations must satisfy these tests.
/// Derive from this class and implement CreateProvider() to test a specific implementation.
/// Platform-specific tests should conditionally skip where unsupported.
/// </summary>
public abstract class EncryptionProviderContractTests
{
    protected abstract IEncryptionProvider CreateProvider();

    private IEncryptionProvider Sut => CreateProvider();

    // ===========================
    // GetDefaultKeyId
    // ===========================

    [Fact]
    public void GetDefaultKeyId_ReturnsNonEmptyString()
    {
        var result = Sut.GetDefaultKeyId();

        result.Should().NotBeNullOrWhiteSpace();
    }

    // ===========================
    // CreateKeyAsync + KeyExistsAsync
    // ===========================

    [Fact]
    public async Task CreateKeyAsync_ThenKeyExistsAsync_ReturnsTrue()
    {
        var sut = Sut;
        var keyId = $"contract-key-{Guid.NewGuid():N}";

        await sut.CreateKeyAsync(keyId);
        var exists = await sut.KeyExistsAsync(keyId);

        exists.Should().BeTrue();
    }

    [Fact]
    public async Task KeyExistsAsync_DefaultKey_ReturnsTrue()
    {
        var sut = Sut;
        var defaultKeyId = sut.GetDefaultKeyId();

        var exists = await sut.KeyExistsAsync(defaultKeyId);

        exists.Should().BeTrue();
    }

    [Fact]
    public async Task KeyExistsAsync_NonexistentKey_ReturnsFalse()
    {
        var exists = await Sut.KeyExistsAsync("nonexistent-key-id");

        exists.Should().BeFalse();
    }

    // ===========================
    // EncryptAsync + DecryptAsync (round-trip)
    // ===========================

    [Fact]
    public async Task EncryptAsync_ThenDecryptAsync_RoundTripsData()
    {
        var sut = Sut;
        var keyId = sut.GetDefaultKeyId();
        var plaintext = "Hello, encryption contract test!"u8.ToArray();

        var ciphertext = await sut.EncryptAsync(plaintext, keyId);
        var decrypted = await sut.DecryptAsync(ciphertext, keyId);

        decrypted.Should().BeEquivalentTo(plaintext);
    }

    [Fact]
    public async Task EncryptAsync_ReturnsBase64String()
    {
        var sut = Sut;
        var keyId = sut.GetDefaultKeyId();
        var plaintext = new byte[] { 1, 2, 3, 4, 5 };

        var ciphertext = await sut.EncryptAsync(plaintext, keyId);

        ciphertext.Should().NotBeNullOrWhiteSpace();
        // Should be valid base64
        var act = () => Convert.FromBase64String(ciphertext);
        act.Should().NotThrow();
    }

    [Fact]
    public async Task EncryptAsync_ProducesDifferentCiphertextEachTime()
    {
        var sut = Sut;
        var keyId = sut.GetDefaultKeyId();
        var plaintext = "Same plaintext"u8.ToArray();

        var ciphertext1 = await sut.EncryptAsync(plaintext, keyId);
        var ciphertext2 = await sut.EncryptAsync(plaintext, keyId);

        // Due to random nonce/IV, ciphertexts should differ
        ciphertext1.Should().NotBe(ciphertext2);
    }

    [Fact]
    public async Task EncryptAsync_EmptyData_ThrowsArgumentException()
    {
        var sut = Sut;
        var keyId = sut.GetDefaultKeyId();

        var act = async () => await sut.EncryptAsync(Array.Empty<byte>(), keyId);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task EncryptAsync_LargeData_RoundTrips()
    {
        var sut = Sut;
        var keyId = sut.GetDefaultKeyId();
        var plaintext = new byte[10_000];
        Random.Shared.NextBytes(plaintext);

        var ciphertext = await sut.EncryptAsync(plaintext, keyId);
        var decrypted = await sut.DecryptAsync(ciphertext, keyId);

        decrypted.Should().BeEquivalentTo(plaintext);
    }

    [Fact]
    public async Task EncryptAsync_WithCreatedKey_RoundTrips()
    {
        var sut = Sut;
        var keyId = $"contract-roundtrip-{Guid.NewGuid():N}";
        await sut.CreateKeyAsync(keyId);
        var plaintext = "Custom key test"u8.ToArray();

        var ciphertext = await sut.EncryptAsync(plaintext, keyId);
        var decrypted = await sut.DecryptAsync(ciphertext, keyId);

        decrypted.Should().BeEquivalentTo(plaintext);
    }

    [Fact]
    public async Task DecryptAsync_WithWrongKey_Throws()
    {
        var sut = Sut;
        var keyId1 = $"contract-key1-{Guid.NewGuid():N}";
        var keyId2 = $"contract-key2-{Guid.NewGuid():N}";
        await sut.CreateKeyAsync(keyId1);
        await sut.CreateKeyAsync(keyId2);
        var plaintext = "Wrong key test"u8.ToArray();

        var ciphertext = await sut.EncryptAsync(plaintext, keyId1);
        var act = async () => await sut.DecryptAsync(ciphertext, keyId2);

        await act.Should().ThrowAsync<Exception>();
    }
}
