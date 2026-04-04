// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.Logging;

using Moq;

using Sorcha.Wallet.Service.Services.Implementation;

using Xunit;

namespace Sorcha.Wallet.Service.Tests.Services;

public class SoftwareKeyProtectionProviderTests
{
    /// <summary>
    /// A valid 32-byte Base64-encoded key for testing.
    /// </summary>
    private static readonly string TestKeyBase64 = Convert.ToBase64String(new byte[32]
    {
        0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
        0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10,
        0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18,
        0x19, 0x1A, 0x1B, 0x1C, 0x1D, 0x1E, 0x1F, 0x20
    });

    private static SoftwareKeyProtectionProvider CreateProvider(string? keyBase64 = null)
    {
        var configData = new Dictionary<string, string?>();

        if (keyBase64 is not null)
        {
            configData["OrgKeyProtection:EncryptionKey"] = keyBase64;
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        var logger = new Mock<ILogger<SoftwareKeyProtectionProvider>>();

        return new SoftwareKeyProtectionProvider(configuration, logger.Object);
    }

    [Fact]
    public async Task EncryptDecrypt_RoundTrip_ReturnsOriginalSeed()
    {
        var sut = CreateProvider(TestKeyBase64);
        var originalSeed = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0xCA, 0xFE, 0xBA, 0xBE };

        var (encrypted, keyId) = await sut.EncryptSeedAsync(originalSeed);
        var decrypted = await sut.DecryptSeedAsync(encrypted, keyId);

        decrypted.Should().BeEquivalentTo(originalSeed);
    }

    [Fact]
    public async Task Encrypt_DifferentCalls_ProduceDifferentCiphertext()
    {
        var sut = CreateProvider(TestKeyBase64);
        var seed = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 };

        var (encrypted1, _) = await sut.EncryptSeedAsync(seed);
        var (encrypted2, _) = await sut.EncryptSeedAsync(seed);

        encrypted1.Should().NotBeEquivalentTo(encrypted2,
            "each encryption should use a random nonce producing different ciphertext");
    }

    [Fact]
    public async Task Encrypt_EmptySeed_ThrowsOrHandles()
    {
        var sut = CreateProvider(TestKeyBase64);
        var emptySeed = Array.Empty<byte>();

        // AES-GCM can encrypt zero-length plaintext; verify round-trip works
        var (encrypted, keyId) = await sut.EncryptSeedAsync(emptySeed);
        var decrypted = await sut.DecryptSeedAsync(encrypted, keyId);

        decrypted.Should().BeEquivalentTo(emptySeed);
    }

    [Fact]
    public void ProviderName_ReturnsSoftware()
    {
        var sut = CreateProvider(TestKeyBase64);

        sut.ProviderName.Should().Be("Software");
    }

    [Fact]
    public void Constructor_MissingKey_ThrowsInvalidOperationException()
    {
        var act = () => CreateProvider(keyBase64: null);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*OrgKeyProtection:EncryptionKey*");
    }
}
