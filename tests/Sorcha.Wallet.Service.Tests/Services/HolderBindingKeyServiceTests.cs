// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text.Json;
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
/// Tests for HolderBindingKeyService (FR-022 through FR-027).
/// </summary>
public class HolderBindingKeyServiceTests
{
    private readonly Mock<IWalletRepository> _repoMock = new();
    private readonly Mock<IKeyManagementService> _keyMgmtMock = new();
    private readonly HolderBindingKeyService _service;

    public HolderBindingKeyServiceTests()
    {
        _service = new HolderBindingKeyService(
            _repoMock.Object,
            _keyMgmtMock.Object,
            Mock.Of<ILogger<HolderBindingKeyService>>());
    }

    private static WalletEntity CreateTestWallet(string algorithm = "ES256") => new()
    {
        Address = "ws1qtest123",
        EncryptedPrivateKey = "encrypted-key-data",
        EncryptionKeyId = "key-001",
        Algorithm = algorithm,
        Owner = "user-1",
        Tenant = "tenant-1",
        Name = "Test Wallet",
        PublicKey = Convert.ToBase64String(new byte[32])
    };

    [Fact]
    public async Task GetPublicJwkAsync_ReturnsDeterministicKey_AcrossCalls()
    {
        // Arrange
        var wallet = CreateTestWallet();
        var masterKey = new byte[64];
        var derivedPrivate = new byte[32];
        var derivedPublic = GenerateP256PublicKeyBytes();

        _repoMock.Setup(r => r.GetByAddressAsync("ws1qtest123", false, false, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(wallet);
        _keyMgmtMock.Setup(k => k.DecryptPrivateKeyAsync("encrypted-key-data", "key-001"))
            .ReturnsAsync(masterKey);
        _keyMgmtMock.Setup(k => k.DeriveKeyAtPathAsync(masterKey, It.IsAny<DerivationPath>(), "ES256"))
            .ReturnsAsync((derivedPrivate, derivedPublic));

        // Act — call twice
        var jwk1 = await _service.GetPublicJwkAsync("ws1qtest123");
        var jwk2 = await _service.GetPublicJwkAsync("ws1qtest123");

        // Assert — same key returned both times
        jwk1.GetRawText().Should().Be(jwk2.GetRawText());
        jwk1.GetProperty("kty").GetString().Should().Be("EC");
        jwk1.GetProperty("crv").GetString().Should().Be("P-256");
    }

    [Fact]
    public async Task GetPublicJwkAsync_UsesCorrectDerivationPath()
    {
        // Arrange
        var wallet = CreateTestWallet();
        var masterKey = new byte[64];
        var derivedPublic = GenerateP256PublicKeyBytes();
        DerivationPath? capturedPath = null;

        _repoMock.Setup(r => r.GetByAddressAsync("ws1qtest123", false, false, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(wallet);
        _keyMgmtMock.Setup(k => k.DecryptPrivateKeyAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(masterKey);
        _keyMgmtMock.Setup(k => k.DeriveKeyAtPathAsync(masterKey, It.IsAny<DerivationPath>(), "ES256"))
            .Callback<byte[], DerivationPath, string>((_, path, _) => capturedPath = path)
            .ReturnsAsync((new byte[32], derivedPublic));

        // Act
        await _service.GetPublicJwkAsync("ws1qtest123");

        // Assert — derivation path is the holder binding path
        capturedPath.Should().NotBeNull();
        capturedPath!.ToString().Should().Contain("105",
            because: "holder binding key uses index 105 in the derivation path");
    }

    [Fact]
    public async Task GetPublicJwkAsync_WalletNotFound_ThrowsKeyNotFoundException()
    {
        _repoMock.Setup(r => r.GetByAddressAsync("nonexistent", false, false, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WalletEntity?)null);

        var act = () => _service.GetPublicJwkAsync("nonexistent");

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task SignKbJwtAsync_SignatureVerifies_AgainstPublicJwk()
    {
        // Arrange — use real P-256 keys for round-trip verification
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privateKey = ecdsa.ExportECPrivateKey();
        var publicKey = ecdsa.ExportSubjectPublicKeyInfo();

        var wallet = CreateTestWallet();
        var masterKey = new byte[64];

        _repoMock.Setup(r => r.GetByAddressAsync("ws1qtest123", false, false, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(wallet);
        _keyMgmtMock.Setup(k => k.DecryptPrivateKeyAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(masterKey);
        _keyMgmtMock.Setup(k => k.DeriveKeyAtPathAsync(masterKey, It.IsAny<DerivationPath>(), "ES256"))
            .ReturnsAsync((privateKey, publicKey));

        var signingInput = System.Text.Encoding.UTF8.GetBytes("test-kb-jwt-payload");

        // Act
        var (signature, algorithm) = await _service.SignKbJwtAsync("ws1qtest123", signingInput);

        // Assert — verify signature with the public key
        algorithm.Should().Be("ES256");
        signature.Should().NotBeEmpty();

        using var verifier = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        verifier.ImportSubjectPublicKeyInfo(publicKey, out _);
        verifier.VerifyData(signingInput, signature, HashAlgorithmName.SHA256).Should().BeTrue();
    }

    private static byte[] GenerateP256PublicKeyBytes()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return ecdsa.ExportSubjectPublicKeyInfo();
    }
}
