// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sorcha.Wallet.Core.Domain.ValueObjects;
using Sorcha.Wallet.Core.Repositories.Interfaces;
using Sorcha.Wallet.Core.Services.Interfaces;
using Sorcha.Wallet.Service.Services.Implementation;
using Xunit;
using WalletEntity = Sorcha.Wallet.Core.Domain.Entities.Wallet;

namespace Sorcha.Wallet.Service.Tests.Services;

/// <summary>
/// Tests for <see cref="HolderKeyService"/> (Feature 114). Mirrors the
/// HolderBindingKeyService test pattern with citizen-holder-specific assertions
/// (slot 108 derivation path, JWK thumbprint helper, Redis cache integration).
/// </summary>
public sealed class HolderKeyServiceTests
{
    private readonly Mock<IWalletRepository> _repoMock = new();
    private readonly Mock<IKeyManagementService> _keyMgmtMock = new();
    private readonly IDistributedCache _cache = new MemoryDistributedCache(
        Options.Create(new MemoryDistributedCacheOptions()));
    private readonly HolderKeyService _service;

    public HolderKeyServiceTests()
    {
        _service = new HolderKeyService(
            _repoMock.Object,
            _keyMgmtMock.Object,
            _cache,
            Mock.Of<ILogger<HolderKeyService>>());
    }

    private static WalletEntity CreateTestWallet(string algorithm = "ES256") => new()
    {
        Address = "ws1qcitizen1",
        EncryptedPrivateKey = "encrypted-key-data",
        EncryptionKeyId = "key-001",
        Algorithm = algorithm,
        Owner = "citizen-1",
        Tenant = "tenant-1",
        Name = "Citizen Wallet",
        PublicKey = Convert.ToBase64String(new byte[32])
    };

    [Fact]
    public async Task GetHolderPublicJwkAsync_DerivesAtSlot108()
    {
        var wallet = CreateTestWallet();
        var masterKey = new byte[64];
        DerivationPath? capturedPath = null;

        _repoMock.Setup(r => r.GetByAddressAsync("ws1qcitizen1", false, false, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(wallet);
        _keyMgmtMock.Setup(k => k.DecryptPrivateKeyAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(masterKey);
        _keyMgmtMock.Setup(k => k.DeriveKeyAtPathAsync(masterKey, It.IsAny<DerivationPath>(), "ES256"))
            .Callback<byte[], DerivationPath, string>((_, path, _) => capturedPath = path)
            .ReturnsAsync((new byte[32], GenerateP256PublicKeyBytes()));

        await _service.GetHolderPublicJwkAsync("ws1qcitizen1");

        capturedPath.Should().NotBeNull();
        capturedPath!.ToString().Should().Contain("108",
            because: "citizen holder key uses index 108 in the derivation path");
    }

    [Fact]
    public async Task GetHolderPublicJwkAsync_ReturnsP256Jwk_ForEs256Wallet()
    {
        SetupHappyPath(out _);

        var jwk = await _service.GetHolderPublicJwkAsync("ws1qcitizen1");

        jwk.GetProperty("kty").GetString().Should().Be("EC");
        jwk.GetProperty("crv").GetString().Should().Be("P-256");
        jwk.GetProperty("x").GetString().Should().NotBeNullOrEmpty();
        jwk.GetProperty("y").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetHolderPublicJwkAsync_DeterministicAcrossCalls_FromCache()
    {
        SetupHappyPath(out _);

        var jwk1 = await _service.GetHolderPublicJwkAsync("ws1qcitizen1");
        var jwk2 = await _service.GetHolderPublicJwkAsync("ws1qcitizen1");

        jwk1.GetRawText().Should().Be(jwk2.GetRawText());

        // Repository hit only once due to cache
        _repoMock.Verify(
            r => r.GetByAddressAsync("ws1qcitizen1", false, false, false, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetHolderPublicJwkAsync_WalletNotFound_ThrowsKeyNotFoundException()
    {
        _repoMock.Setup(r => r.GetByAddressAsync("ghost", false, false, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WalletEntity?)null);

        var act = () => _service.GetHolderPublicJwkAsync("ghost");

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task SignAsync_SignatureVerifiesAgainstPublicJwk_RoundTrip()
    {
        // Use real P-256 keys for an end-to-end round-trip
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privateKey = ecdsa.ExportECPrivateKey();
        var publicKey = ecdsa.ExportSubjectPublicKeyInfo();

        var wallet = CreateTestWallet();
        var masterKey = new byte[64];

        _repoMock.Setup(r => r.GetByAddressAsync("ws1qcitizen1", false, false, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(wallet);
        _keyMgmtMock.Setup(k => k.DecryptPrivateKeyAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(masterKey);
        _keyMgmtMock.Setup(k => k.DeriveKeyAtPathAsync(masterKey, It.IsAny<DerivationPath>(), "ES256"))
            .ReturnsAsync((privateKey, publicKey));

        var signingInput = System.Text.Encoding.UTF8.GetBytes("delegation-credential-payload");

        var (signature, algorithm) = await _service.SignAsync("ws1qcitizen1", signingInput);

        algorithm.Should().Be("ES256");
        signature.Should().NotBeEmpty();

        using var verifier = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        verifier.ImportSubjectPublicKeyInfo(publicKey, out _);
        verifier.VerifyData(signingInput, signature, HashAlgorithmName.SHA256).Should().BeTrue();
    }

    [Fact]
    public async Task GetHolderJwkThumbprintAsync_Returns43CharBase64Url()
    {
        SetupHappyPath(out _);

        var thumbprint = await _service.GetHolderJwkThumbprintAsync("ws1qcitizen1");

        thumbprint.Should().HaveLength(43, because: "SHA-256 → 32 bytes → 43 base64url chars");
        thumbprint.Should().MatchRegex("^[A-Za-z0-9_-]+$",
            because: "thumbprint must be base64url-encoded with no padding");
    }

    [Fact]
    public async Task GetHolderJwkThumbprintAsync_DeterministicAcrossCalls()
    {
        SetupHappyPath(out _);

        var t1 = await _service.GetHolderJwkThumbprintAsync("ws1qcitizen1");
        var t2 = await _service.GetHolderJwkThumbprintAsync("ws1qcitizen1");

        t1.Should().Be(t2);
    }

    [Fact]
    public async Task SignAsync_NullSigningInput_Throws()
    {
        var act = () => _service.SignAsync("ws1qcitizen1", null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task GetHolderPublicJwkAsync_EmptyWalletAddress_Throws()
    {
        var act = () => _service.GetHolderPublicJwkAsync("");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    private void SetupHappyPath(out byte[] derivedPublic)
    {
        var wallet = CreateTestWallet();
        var masterKey = new byte[64];
        derivedPublic = GenerateP256PublicKeyBytes();

        _repoMock.Setup(r => r.GetByAddressAsync("ws1qcitizen1", false, false, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(wallet);
        _keyMgmtMock.Setup(k => k.DecryptPrivateKeyAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(masterKey);
        _keyMgmtMock.Setup(k => k.DeriveKeyAtPathAsync(masterKey, It.IsAny<DerivationPath>(), "ES256"))
            .ReturnsAsync((new byte[32], derivedPublic));
    }

    private static byte[] GenerateP256PublicKeyBytes()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return ecdsa.ExportSubjectPublicKeyInfo();
    }
}
