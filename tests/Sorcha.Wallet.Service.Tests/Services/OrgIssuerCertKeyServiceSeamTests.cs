// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;

using FluentAssertions;

using Microsoft.Extensions.Logging;

using Moq;

using Sorcha.Cryptography.Core;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Wallet.Core.Domain.ValueObjects;
using Sorcha.Wallet.Core.Encryption.Interfaces;
using Sorcha.Wallet.Core.Encryption.Providers;
using Sorcha.Wallet.Core.Repositories.Interfaces;
using Sorcha.Wallet.Core.Services.Implementation;
using Sorcha.Wallet.Core.Services.Interfaces;
using Sorcha.Wallet.Service.Services.Implementation;
using Sorcha.Wallet.Service.Services.Interfaces;

using Xunit;

using WalletEntity = Sorcha.Wallet.Core.Domain.Entities.Wallet;

namespace Sorcha.Wallet.Service.Tests.Services;

/// <summary>
/// Seam test for the HaipCoKey derivation path in <see cref="OrgIssuerCertKeyService"/> (Feature 181
/// US4/US5). Unlike <see cref="OrgIssuerCertKeyServiceTests"/> — which mocks
/// <c>IKeyManagementService.DeriveKeyAtPathAsync</c> directly and therefore cannot see a defect at the
/// join between <see cref="OrgIssuerCertKeyService"/> and the real key-derivation path — this test wires
/// up a REAL <see cref="KeyManagementService"/> (with a real <see cref="CryptoModule"/> and real
/// <see cref="LocalEncryptionProvider"/>) so the real
/// <see cref="Sorcha.Cryptography.Utilities.AlgorithmMapper"/> call inside
/// <c>DeriveKeyAtPathAsync</c> actually executes. A mismatch between the "ES256" literal
/// <see cref="OrgIssuerCertKeyService"/> passes and the aliases <c>AlgorithmMapper</c> accepts throws
/// there and is swallowed by <see cref="OrgIssuerCertKeyService"/>'s catch-and-log, so the endpoint
/// silently reports "not eligible" for every ED25519-primary org instead of failing loudly.
/// </summary>
public class OrgIssuerCertKeyServiceSeamTests
{
    private readonly Mock<IWalletRepository> _repo = new();
    private readonly KeyManagementService _realKeyManagement;
    private readonly OrgIssuerCertKeyService _service;

    public OrgIssuerCertKeyServiceSeamTests()
    {
        var encryptionProvider = new LocalEncryptionProvider(Mock.Of<ILogger<LocalEncryptionProvider>>());
        _realKeyManagement = new KeyManagementService(
            (IKeyProtectionProvider)encryptionProvider,
            new CryptoModule(),
            Mock.Of<IWalletUtilities>(),
            Mock.Of<ILogger<KeyManagementService>>());

        _service = new OrgIssuerCertKeyService(_repo.Object, _realKeyManagement, Mock.Of<ILogger<OrgIssuerCertKeyService>>());
    }

    [Fact]
    public async Task Resolve_Ed25519PrimaryOrgWallet_DerivesRealHaipCoKey()
    {
        // Arrange — an ED25519-primary org wallet, with its 32-byte master key encrypted through the
        // SAME real KeyManagementService instance the service under test will decrypt it with, exactly
        // mirroring what is stored at rest in production.
        var masterKey = RandomNumberGenerator.GetBytes(32);
        var (encryptedPrivateKey, keyId) = await _realKeyManagement.EncryptPrivateKeyAsync(masterKey, "seam-test-key");

        _repo.Setup(r => r.GetByAddressAsync("ws1qseam", false, false, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WalletEntity
            {
                Address = "ws1qseam",
                EncryptedPrivateKey = encryptedPrivateKey,
                EncryptionKeyId = keyId,
                Algorithm = "ED25519",
                Owner = "org-seam",
                Tenant = "tenant-seam",
                Name = "Seam Org Wallet",
                PublicKey = Convert.ToBase64String(new byte[32]), // ED25519 primary public key — irrelevant to the co-key path
                HaipIssuer = false,
            });

        // Act — go through the public IOrgIssuerCertKeyService surface only.
        IOrgIssuerCertKeyService service = _service;
        var resolution = await service.ResolveAsync("ws1qseam");

        // Assert — the real derivation actually completed and produced a real, usable P-256 key.
        resolution.Eligible.Should().BeTrue(
            "an ED25519-primary org wallet is eligible via the HaipCoKey derivation path — {0}", resolution.Reason);
        resolution.BoundKeySource.Should().Be("HaipCoKey");
        resolution.PublicKeySpki.Should().NotBeNull();

        // Decode the SPKI back to its raw EC point to prove it is a genuine 64-byte (32+32) P-256 point,
        // not a mock echoing whatever shape a test handed it.
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportSubjectPublicKeyInfo(resolution.PublicKeySpki!, out _);
        var parameters = ecdsa.ExportParameters(false);
        var rawPoint = parameters.Q.X!.Concat(parameters.Q.Y!).ToArray();
        rawPoint.Should().HaveCount(64, "a P-256 public point is 32-byte X concatenated with 32-byte Y");

        // And prove the derived key is actually usable for signing — the co-key derivation, not just its
        // shape, must be real.
        // Separately exercise the OTHER branch of TryResolveAsync (needPrivate: true) end-to-end — it
        // re-derives independently of ResolveAsync's derivation, so this proves the sign path completes
        // the real ES256 derivation and produces a well-formed P-256 signature, without assuming the two
        // derivations yield the same keypair.
        //
        // NOTE: they do NOT currently yield the same keypair — see the "found but out of scope" defect
        // recorded in the PR description: CryptoModule.GenerateNISTP256KeySetAsync ignores its seed
        // parameter entirely and always generates a fresh random P-256 key, unlike GenerateED25519KeySetAsync
        // which genuinely derives from the seed. That is a second, separate defect this seam test surfaced;
        // fixing it is out of scope for the ES256 alias fix.
        var digest = SHA256.HashData("seam-test-payload"u8.ToArray());
        var signature = await service.SignPreHashedAsync("ws1qseam", digest);
        signature.Should().HaveCount(64, "a P-256 IEEE P1363 signature is a 32-byte r concatenated with a 32-byte s");
    }
}
