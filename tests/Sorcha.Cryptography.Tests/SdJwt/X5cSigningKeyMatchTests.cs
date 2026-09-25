// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

using FluentAssertions;

using Xunit;

using Sorcha.Cryptography.SdJwt;

namespace Sorcha.Cryptography.Tests.SdJwt;

/// <summary>
/// #1699 — an x5c chain may only ride on a credential that verifies under it. No mocked crypto:
/// real tokens, real certificates, real signatures.
/// </summary>
public class X5cSigningKeyMatchTests
{
    private readonly SdJwtService _sdJwt = new();

    private Task<SdJwtToken> MintAsync(byte[] signingKey, string algorithm, IReadOnlyList<byte[]>? chain = null) =>
        _sdJwt.CreateTokenAsync(
            claims: new Dictionary<string, object> { ["compliant"] = true },
            disclosableClaims: null,
            issuer: "did:sorcha:org:ws1qtest",
            subject: "did:sorcha:w:holder",
            signingKey: signingKey,
            algorithm: algorithm,
            expiresAt: DateTimeOffset.UtcNow.AddDays(30),
            cancellationToken: default,
            x5cChain: chain);

    [Fact]
    public async Task Es256TokenSignedByTheLeafKey_Verifies()
    {
        using var leafKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var chain = BuildChain(leafKey);
        var token = await MintAsync(leafKey.ExportECPrivateKey(), "ES256", chain);

        (await X5cSigningKeyMatch.TokenVerifiesUnderLeafAsync(_sdJwt, token.RawToken, chain))
            .Should().BeTrue();
    }

    /// <summary>
    /// The live #1699 shape: the org's certificate is P-256, the VC-issuance key is Ed25519. The
    /// signature is perfectly valid — just not for the key the header points at.
    /// </summary>
    [Fact]
    public async Task Ed25519TokenCarryingAP256Chain_DoesNotVerify()
    {
        using var certKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var chain = BuildChain(certKey);
        var ed25519 = Sodium.PublicKeyAuth.GenerateKeyPair();
        var token = await MintAsync(ed25519.PrivateKey, "ED25519", chain);

        // Sanity: the token itself is sound under its real key — this is not a broken signature.
        (await _sdJwt.VerifyPresentationAsync(token.RawToken, ed25519.PublicKey, "EdDSA")).IsValid
            .Should().BeTrue();

        (await X5cSigningKeyMatch.TokenVerifiesUnderLeafAsync(_sdJwt, token.RawToken, chain))
            .Should().BeFalse();
    }

    [Fact]
    public async Task Es256TokenSignedByADifferentP256Key_DoesNotVerify()
    {
        using var certKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var chain = BuildChain(certKey);
        var token = await MintAsync(otherKey.ExportECPrivateKey(), "ES256", chain);

        (await X5cSigningKeyMatch.TokenVerifiesUnderLeafAsync(_sdJwt, token.RawToken, chain))
            .Should().BeFalse();
    }

    [Fact]
    public async Task EmptyOrUnreadableChain_DoesNotVerify()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var token = await MintAsync(key.ExportECPrivateKey(), "ES256");

        (await X5cSigningKeyMatch.TokenVerifiesUnderLeafAsync(_sdJwt, token.RawToken, []))
            .Should().BeFalse();
        (await X5cSigningKeyMatch.TokenVerifiesUnderLeafAsync(_sdJwt, token.RawToken, [new byte[] { 1, 2, 3 }]))
            .Should().BeFalse();
    }

    /// <summary>Root CA + leaf issued to <paramref name="leafKey"/>, leaf first (x5c order).</summary>
    private static IReadOnlyList<byte[]> BuildChain(ECDsa leafKey)
    {
        using var rootKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var rootRequest = new CertificateRequest("CN=Test Root CA", rootKey, HashAlgorithmName.SHA256);
        rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 1, true));
        var now = DateTimeOffset.UtcNow;
        using var root = rootRequest.CreateSelfSigned(now, now.AddYears(5));

        var leafRequest = new CertificateRequest("CN=Test Org", leafKey, HashAlgorithmName.SHA256);
        leafRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        var serial = RandomNumberGenerator.GetBytes(16);
        serial[0] &= 0x7F;
        using var leaf = leafRequest.Create(
            root.SubjectName, X509SignatureGenerator.CreateForECDsa(rootKey), now, now.AddYears(2), serial);

        return [leaf.RawData, root.RawData];
    }
}
