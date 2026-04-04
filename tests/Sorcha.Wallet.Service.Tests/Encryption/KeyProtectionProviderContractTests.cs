// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Wallet.Core.Encryption.Interfaces;

namespace Sorcha.Wallet.Service.Tests.Encryption;

/// <summary>
/// Contract tests for IKeyProtectionProvider. All implementations must satisfy these tests.
/// Derive from this class and implement CreateProvider() to test a specific implementation.
/// </summary>
public abstract class KeyProtectionProviderContractTests
{
    protected abstract IKeyProtectionProvider CreateProvider();

    private IKeyProtectionProvider Sut => CreateProvider();

    // ===========================
    // CreateKeyAsync
    // ===========================

    [Fact]
    public async Task CreateKeyAsync_ReturnsKeyId()
    {
        var sut = Sut;
        var keyId = $"kpp-contract-{Guid.NewGuid():N}";

        var result = await sut.CreateKeyAsync(keyId);

        result.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task CreateKeyAsync_ThenKeyExistsAsync_ReturnsTrue()
    {
        var sut = Sut;
        var keyId = $"kpp-exists-{Guid.NewGuid():N}";

        await sut.CreateKeyAsync(keyId);
        var exists = await sut.KeyExistsAsync(keyId);

        exists.Should().BeTrue();
    }

    [Fact]
    public async Task KeyExistsAsync_NonexistentKey_ReturnsFalse()
    {
        var exists = await Sut.KeyExistsAsync("nonexistent-kpp-key");

        exists.Should().BeFalse();
    }

    // ===========================
    // WrapKeyAsync + UnwrapKeyAsync (round-trip)
    // ===========================

    [Fact]
    public async Task WrapKeyAsync_ThenUnwrapKeyAsync_RoundTripsData()
    {
        var sut = Sut;
        var keyId = $"kpp-roundtrip-{Guid.NewGuid():N}";
        await sut.CreateKeyAsync(keyId);

        var plaintext = new byte[32];
        Random.Shared.NextBytes(plaintext);

        var wrapped = await sut.WrapKeyAsync(keyId, plaintext);
        var unwrapped = await sut.UnwrapKeyAsync(keyId, wrapped);

        unwrapped.Should().BeEquivalentTo(plaintext);
    }

    [Fact]
    public async Task WrapKeyAsync_ReturnsNonNullResult()
    {
        var sut = Sut;
        var keyId = $"kpp-wrap-{Guid.NewGuid():N}";
        await sut.CreateKeyAsync(keyId);

        var plaintext = new byte[32];
        Random.Shared.NextBytes(plaintext);

        var wrapped = await sut.WrapKeyAsync(keyId, plaintext);

        wrapped.Should().NotBeNull();
        wrapped.Should().NotBeEmpty();
    }
}
