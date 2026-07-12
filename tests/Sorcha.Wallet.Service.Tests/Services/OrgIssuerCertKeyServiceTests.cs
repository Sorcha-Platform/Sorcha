// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Wallet.Core.Domain.ValueObjects;
using Sorcha.Wallet.Core.Repositories.Interfaces;
using Sorcha.Wallet.Core.Services.Interfaces;
using Sorcha.Wallet.Service.Services.Implementation;
using Sorcha.Wallet.Service.Services.Interfaces;
using Xunit;
using WalletEntity = Sorcha.Wallet.Core.Domain.Entities.Wallet;

namespace Sorcha.Wallet.Service.Tests.Services;

/// <summary>
/// Feature 181 US4 — the org P-256 cert-issuing key seam. Proves the resolve→sign round-trip is
/// cryptographically consistent for both an ES256-primary org (Primary) and an Ed25519-primary org that
/// derives the HAIP co-key (HaipCoKey), and that ineligible orgs are reported/thrown cleanly.
/// </summary>
public class OrgIssuerCertKeyServiceTests
{
    private readonly Mock<IWalletRepository> _repo = new();
    private readonly Mock<IKeyManagementService> _keyMgmt = new();
    private readonly OrgIssuerCertKeyService _service;

    public OrgIssuerCertKeyServiceTests()
        => _service = new OrgIssuerCertKeyService(_repo.Object, _keyMgmt.Object, Mock.Of<ILogger<OrgIssuerCertKeyService>>());

    private static (byte[] D, byte[] Point64, byte[] Spki) NewP256Key()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var p = ecdsa.ExportParameters(true);
        var point = new byte[64];
        Array.Copy(p.Q.X!, 0, point, 0, 32);
        Array.Copy(p.Q.Y!, 0, point, 32, 32);
        return (p.D!, point, ecdsa.ExportSubjectPublicKeyInfo());
    }

    private static WalletEntity Wallet(string algorithm, string? publicKeyBase64 = null) => new()
    {
        Address = "ws1qorg",
        EncryptedPrivateKey = "enc",
        EncryptionKeyId = "k1",
        Algorithm = algorithm,
        Owner = "org-1",
        Tenant = "tenant-1",
        Name = "Org Wallet",
        PublicKey = publicKeyBase64 ?? Convert.ToBase64String(new byte[64]),
        HaipIssuer = false,
    };

    [Fact]
    public async Task Resolve_Es256Primary_BindsPrimaryKey_And_SignVerifies()
    {
        var (d, point, spki) = NewP256Key();
        _repo.Setup(r => r.GetByAddressAsync("ws1qorg", false, false, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Wallet("ES256", Convert.ToBase64String(point)));
        // Fresh clone per call — the service zeroises the returned key after use (shared-array mock gotcha).
        _keyMgmt.Setup(k => k.DecryptPrivateKeyAsync("enc", "k1")).ReturnsAsync(() => (byte[])d.Clone());

        var resolution = await _service.ResolveAsync("ws1qorg");
        resolution.Eligible.Should().BeTrue();
        resolution.BoundKeySource.Should().Be("Primary");
        resolution.PublicKeySpki.Should().Equal(spki);

        await AssertSignRoundTripAsync(spki);
    }

    [Fact]
    public async Task Resolve_Ed25519Primary_DerivesHaipCoKey_And_SignVerifies()
    {
        var (d, point, _) = NewP256Key();
        _repo.Setup(r => r.GetByAddressAsync("ws1qorg", false, false, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Wallet("ED25519"));
        _keyMgmt.Setup(k => k.DecryptPrivateKeyAsync("enc", "k1")).ReturnsAsync(() => new byte[64]);
        // Fresh clones per call — the service zeroises the derived private key after use.
        _keyMgmt.Setup(k => k.DeriveKeyAtPathAsync(It.IsAny<byte[]>(), It.IsAny<DerivationPath>(), "ES256"))
            .ReturnsAsync(() => ((byte[])d.Clone(), (byte[])point.Clone()));

        var resolution = await _service.ResolveAsync("ws1qorg");
        resolution.Eligible.Should().BeTrue();
        resolution.BoundKeySource.Should().Be("HaipCoKey");
        resolution.PublicKeySpki.Should().NotBeNull();

        await AssertSignRoundTripAsync(resolution.PublicKeySpki!);
    }

    [Fact]
    public async Task Resolve_WalletNotFound_ReportsNotEligible()
    {
        _repo.Setup(r => r.GetByAddressAsync("ws1qorg", false, false, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WalletEntity?)null);

        var resolution = await _service.ResolveAsync("ws1qorg");
        resolution.Eligible.Should().BeFalse();
        resolution.Reason.Should().Be("CERT_KEY_NOT_ELIGIBLE");
        resolution.PublicKeySpki.Should().BeNull();
    }

    [Fact]
    public async Task Sign_NotEligible_Throws()
    {
        _repo.Setup(r => r.GetByAddressAsync("ws1qorg", false, false, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WalletEntity?)null);

        var act = () => _service.SignPreHashedAsync("ws1qorg", SHA256.HashData([1, 2, 3]));
        await act.Should().ThrowAsync<OrgIssuerCertKeyNotEligibleException>();
    }

    private async Task AssertSignRoundTripAsync(byte[] spki)
    {
        var digest = SHA256.HashData("to-be-signed"u8.ToArray());
        var signature = await _service.SignPreHashedAsync("ws1qorg", digest);

        using var verifier = ECDsa.Create();
        verifier.ImportSubjectPublicKeyInfo(spki, out _);
        verifier.VerifyHash(digest, signature).Should().BeTrue("the signature must verify against the resolved SPKI");
    }
}
