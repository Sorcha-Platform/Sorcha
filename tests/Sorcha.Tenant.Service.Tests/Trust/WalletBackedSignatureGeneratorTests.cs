// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FluentAssertions;
using Moq;
using Sorcha.ServiceClients.Wallet;
using Sorcha.Tenant.Service.Trust;
using Xunit;

namespace Sorcha.Tenant.Service.Tests.Trust;

/// <summary>
/// Feature 181 US4 (T041) — the remote-signing CSR generator. Proves raw <c>r‖s</c> → DER conversion and a
/// full CSR round-trip where the "wallet" signs with a P-256 key that never leaves the (mock) client.
/// </summary>
public class WalletBackedSignatureGeneratorTests
{
    [Fact]
    public void ConvertP1363ToDer_ProducesValidSequence_ThatBclVerifies()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var digest = SHA256.HashData("payload"u8.ToArray());
        var raw = ecdsa.SignHash(digest); // IEEE P1363 r‖s

        var der = WalletBackedSignatureGenerator.ConvertP1363ToDer(raw);

        // Must be a DER SEQUENCE of two INTEGERs...
        var reader = new AsnReader(der, AsnEncodingRules.DER);
        var seq = reader.ReadSequence();
        seq.ReadInteger();
        seq.ReadInteger();
        seq.HasData.Should().BeFalse();

        // ...and verify as a real ECDSA signature in DER form.
        ecdsa.VerifyHash(digest, der, DSASignatureFormat.Rfc3279DerSequence).Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(63)]
    public void ConvertP1363ToDer_RejectsOddOrEmptyLength(int length)
    {
        var act = () => WalletBackedSignatureGenerator.ConvertP1363ToDer(new byte[length]);
        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void CreateSigningRequest_WithWalletBackedGenerator_ProducesAValidCsr()
    {
        // The org key that "lives in the wallet".
        using var orgKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var spki = orgKey.ExportSubjectPublicKeyInfo();
        var orgD = orgKey.ExportParameters(true).D!;

        var walletClient = new Mock<IWalletServiceClient>();
        walletClient
            .Setup(w => w.SignIssuerCertPreHashedAsync("ws1qorg", It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, byte[] digest, CancellationToken _) =>
            {
                using var signer = ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, D = orgD });
                return signer.SignHash(digest); // raw r‖s — the wallet's wire shape
            });

        using var publicOnly = ECDsa.Create();
        publicOnly.ImportSubjectPublicKeyInfo(spki, out _);
        var request = new CertificateRequest(new X500DistinguishedName("CN=Test Org"), publicOnly, HashAlgorithmName.SHA256);

        var generator = new WalletBackedSignatureGenerator(walletClient.Object, "ws1qorg", spki);
        var csrDer = request.CreateSigningRequest(generator);

        // The BCL loader validates the CSR's self-signature by default — a bad r‖s→DER conversion throws here.
        var loaded = CertificateRequest.LoadSigningRequest(
            csrDer, HashAlgorithmName.SHA256, CertificateRequestLoadOptions.Default);
        loaded.PublicKey.ExportSubjectPublicKeyInfo().Should().Equal(spki);
    }
}
