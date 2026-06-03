// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Tests.Services;

/// <summary>
/// Tests for <see cref="SoftwareSecretProtectionProvider"/>: AES-256-GCM round-trip, envelope shape,
/// tamper detection, and constructor guards. Mirrors the Wallet SoftwareKeyProtectionProvider body.
/// </summary>
public class SoftwareSecretProtectionProviderTests
{
    private const string TestKeyId = "test-key-v1";

    private static byte[] TestKey()
    {
        var key = new byte[32];
        for (var i = 0; i < key.Length; i++) key[i] = (byte)i;
        return key;
    }

    private static SoftwareSecretProtectionProvider Create() => new(TestKey(), TestKeyId);

    [Fact]
    public async Task EncryptAsync_ThenDecryptAsync_RoundTrips()
    {
        var provider = Create();
        var plaintext = Encoding.UTF8.GetBytes("JBSWY3DPEHPK3PXP");

        var (cipher, keyId) = await provider.EncryptAsync(plaintext);
        keyId.Should().Be(TestKeyId);

        var recovered = await provider.DecryptAsync(cipher, keyId);
        recovered.Should().Equal(plaintext);
    }

    [Fact]
    public async Task EncryptAsync_ProducesNonceCiphertextTagEnvelope_NotPlaintext()
    {
        var provider = Create();
        var plaintext = Encoding.UTF8.GetBytes("a-totp-secret-value");

        var (cipher, _) = await provider.EncryptAsync(plaintext);

        cipher.Length.Should().Be(12 + plaintext.Length + 16);
        // The ciphertext portion (after the 12-byte nonce) must differ from the plaintext.
        cipher.Skip(12).Take(plaintext.Length).Should().NotEqual(plaintext);
    }

    [Fact]
    public async Task EncryptAsync_CalledTwice_ProducesDifferentCiphertext_DueToRandomNonce()
    {
        var provider = Create();
        var plaintext = Encoding.UTF8.GetBytes("a-totp-secret-value");

        var (a, _) = await provider.EncryptAsync(plaintext);
        var (b, _) = await provider.EncryptAsync(plaintext);

        a.Should().NotEqual(b);
    }

    [Fact]
    public async Task DecryptAsync_TamperedCiphertext_Throws()
    {
        var provider = Create();
        var (cipher, keyId) = await provider.EncryptAsync(Encoding.UTF8.GetBytes("a-totp-secret-value"));
        cipher[^1] ^= 0xFF; // corrupt the auth tag

        var act = async () => await provider.DecryptAsync(cipher, keyId);

        await act.Should().ThrowAsync<AuthenticationTagMismatchException>();
    }

    [Fact]
    public async Task DecryptAsync_WrongKey_Throws()
    {
        var (cipher, keyId) = await Create().EncryptAsync(Encoding.UTF8.GetBytes("a-totp-secret-value"));
        var otherKey = new byte[32];
        otherKey[0] = 0xAB;
        var otherProvider = new SoftwareSecretProtectionProvider(otherKey, keyId);

        var act = async () => await otherProvider.DecryptAsync(cipher, keyId);

        await act.Should().ThrowAsync<AuthenticationTagMismatchException>();
    }

    [Fact]
    public async Task DecryptAsync_TooShort_ThrowsArgumentException()
    {
        var provider = Create();

        var act = async () => await provider.DecryptAsync(new byte[10], TestKeyId);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public void Constructor_KeyNot32Bytes_Throws()
    {
        var act = () => new SoftwareSecretProtectionProvider(new byte[16], TestKeyId);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_BlankKeyId_Throws()
    {
        var act = () => new SoftwareSecretProtectionProvider(TestKey(), "  ");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ProviderName_IsSoftware()
    {
        Create().ProviderName.Should().Be("Software");
    }
}
