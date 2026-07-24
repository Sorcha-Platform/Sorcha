// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Wallet.Contracts.Constants;
using Sorcha.Wallet.Core.Domain.ValueObjects;
using Sorcha.Wallet.Core.Repositories.Interfaces;
using Sorcha.Wallet.Core.Services.Interfaces;
using Sorcha.Wallet.Service.Services.Implementation;
using Xunit;
using WalletEntity = Sorcha.Wallet.Core.Domain.Entities.Wallet;

namespace Sorcha.Wallet.Service.Tests.Services;

/// <summary>
/// Tests for HaipIssuerCoKeyService (FR-028 through FR-034).
/// </summary>
public class HaipIssuerCoKeyServiceTests
{
    private readonly Mock<IWalletRepository> _repoMock = new();
    private readonly Mock<IKeyManagementService> _keyMgmtMock = new();
    private readonly HaipIssuerCoKeyService _service;

    public HaipIssuerCoKeyServiceTests()
    {
        _service = new HaipIssuerCoKeyService(
            _repoMock.Object,
            _keyMgmtMock.Object,
            Mock.Of<ILogger<HaipIssuerCoKeyService>>());
    }

    private static WalletEntity CreateTestWallet(string algorithm = "ES256", bool haipIssuer = true) => new()
    {
        Address = "ws1qissuer1",
        EncryptedPrivateKey = "encrypted-key-data",
        EncryptionKeyId = "key-001",
        Algorithm = algorithm,
        Owner = "user-1",
        Tenant = "tenant-1",
        Name = "Issuer Wallet",
        PublicKey = Convert.ToBase64String(new byte[32]),
        HaipIssuer = haipIssuer
    };

    [Fact]
    public async Task ClassicalPrimaryWallet_WithHaipIssuerFlag_UsesPrimaryKeyDirectly()
    {
        // FR-029: Classical primary wallet uses its own key, no derivation
        var masterKey = new byte[64];
        var wallet = CreateTestWallet(algorithm: "ES256", haipIssuer: true);

        _repoMock.Setup(r => r.GetByAddressAsync("ws1qissuer1", false, false, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(wallet);
        _keyMgmtMock.Setup(k => k.DecryptPrivateKeyAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(masterKey);

        var (privateKey, _, algorithm) = await _service.GetSigningKeyForHaipIssuanceAsync("ws1qissuer1");

        algorithm.Should().Be("ES256");
        privateKey.Should().BeEquivalentTo(masterKey);

        // DeriveKeyAtPathAsync should NOT have been called
        _keyMgmtMock.Verify(k =>
            k.DeriveKeyAtPathAsync(It.IsAny<byte[]>(), It.IsAny<DerivationPath>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task PqcPrimaryWallet_WithHaipIssuerFlag_DerivesClassicalCoKey()
    {
        // FR-030: PQC wallet derives a classical co-key
        var masterKey = new byte[64];
        var derivedPrivate = new byte[32];
        var derivedPublic = new byte[65];
        var wallet = CreateTestWallet(algorithm: "ML-DSA-65", haipIssuer: true);
        DerivationPath? capturedPath = null;

        _repoMock.Setup(r => r.GetByAddressAsync("ws1qissuer1", false, false, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(wallet);
        _keyMgmtMock.Setup(k => k.DecryptPrivateKeyAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(masterKey);
        _keyMgmtMock.Setup(k => k.DeriveKeyAtPathAsync(masterKey, It.IsAny<DerivationPath>(), "ES256"))
            .Callback<byte[], DerivationPath, string>((_, path, _) => capturedPath = path)
            .ReturnsAsync((derivedPrivate, derivedPublic));

        var (privateKey, publicKey, algorithm) = await _service.GetSigningKeyForHaipIssuanceAsync("ws1qissuer1");

        algorithm.Should().Be("ES256", "HAIP co-key should default to ES256");
        privateKey.Should().BeEquivalentTo(derivedPrivate);
        publicKey.Should().BeEquivalentTo(derivedPublic);
        capturedPath.Should().NotBeNull();
        capturedPath!.ToString().Should().Contain("106",
            because: "HAIP issuer co-key uses index 106 in the derivation path");
    }

    [Fact]
    public async Task PqcPrimaryWallet_WithoutHaipIssuerFlag_RefusesWithCapabilityError()
    {
        // FR-034: Missing HaipIssuer capability → clear error
        var wallet = CreateTestWallet(algorithm: "ML-DSA-65", haipIssuer: false);

        _repoMock.Setup(r => r.GetByAddressAsync("ws1qissuer1", false, false, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(wallet);

        var act = () => _service.GetSigningKeyForHaipIssuanceAsync("ws1qissuer1");

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Contain("HaipIssuer");
    }

    [Fact]
    public async Task Ed25519PrimaryWallet_WithHaipIssuerFlag_UsesPrimaryKeyDirectly()
    {
        // FR-029: Ed25519 is classical — use primary key
        var masterKey = new byte[64];
        var wallet = CreateTestWallet(algorithm: "ED25519", haipIssuer: true);

        _repoMock.Setup(r => r.GetByAddressAsync("ws1qissuer1", false, false, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(wallet);
        _keyMgmtMock.Setup(k => k.DecryptPrivateKeyAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(masterKey);

        var (_, _, algorithm) = await _service.GetSigningKeyForHaipIssuanceAsync("ws1qissuer1");

        algorithm.Should().Be("ED25519");
        _keyMgmtMock.Verify(k =>
            k.DeriveKeyAtPathAsync(It.IsAny<byte[]>(), It.IsAny<DerivationPath>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task WalletNotFound_ThrowsKeyNotFoundException()
    {
        _repoMock.Setup(r => r.GetByAddressAsync("nonexistent", false, false, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WalletEntity?)null);

        var act = () => _service.GetSigningKeyForHaipIssuanceAsync("nonexistent");

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
