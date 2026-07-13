// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Sorcha.Mdoc;
using Sorcha.Mdoc.Cose;
using Sorcha.Mdoc.Proximity;

namespace Sorcha.Proximity.Tests.Support;

/// <summary>
/// Issues a real, fully-signed mdoc for the tests.
/// </summary>
/// <remarks>
/// Deliberately a genuine credential — issued by a real (self-signed) certificate, with real digests over
/// real tag-24 item bytes — rather than a stub. A stubbed credential would let the exchange "pass" while the
/// digests, the issuer signature, or the tag-24 splicing were all wrong, which is exactly the class of bug
/// these tests exist to catch.
/// </remarks>
public static class TestMdoc
{
    /// <summary>The ISO mDL doc type.</summary>
    public const string DocType = "org.iso.18013.5.1.mDL";

    /// <summary>
    /// The namespace elements are stored under.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>This should be <c>org.iso.18013.5.1</c>, and in a real mDL it is</b> — the namespace and the
    /// docType are different strings. But <c>MdocIssuer.IssueIssuerSigned</c> (feature 135) issues with a
    /// <b>flat namespace equal to the docType</b>, so that is what a Sorcha-issued mdoc actually contains and
    /// what these tests must therefore ask for.
    /// <para>
    /// Tracked as a follow-up: <c>MdocIssuer</c> needs a real namespace parameter before we can issue a
    /// credential any third-party reader would understand. It does not affect the proximity protocol — which
    /// is namespace-agnostic — so it is not fixed here, but it is a genuine interop blocker for issuance.
    /// </para>
    /// </remarks>
    public const string Namespace = DocType;

    /// <summary>
    /// Issues a credential bound to <paramref name="deviceKey"/>'s public half.
    /// </summary>
    /// <param name="deviceKey">
    /// The holder's <b>static, ECDH-capable</b> device key. Its public half becomes <c>MSO.DeviceKey</c>, which
    /// is what the reader agrees with to derive <c>EMacKey</c>.
    /// </param>
    public static HeldMdoc Issue(ECPublicKeyParameters deviceKey)
    {
        using var issuerKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var request = new CertificateRequest(
            "CN=Sorcha Test mDL Issuer", issuerKey, HashAlgorithmName.SHA256);

        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));

        var elements = new Dictionary<string, object>
        {
            ["family_name"] = "MacLeod",
            ["age_over_18"] = true,
            ["birth_date"] = "1985-03-14"
        };

        var issuerSigned = MdocIssuer.IssueIssuerSigned(
            docType: DocType,
            elements: elements,
            // SEC1, not PKCS#8 — MdocIssuer calls ECDsa.ImportECPrivateKey.
            issuerPrivateKey: issuerKey.ExportECPrivateKey(),
            algorithm: "ES256",
            holderDeviceKeyCose: CoseKey.EncodeEc2PublicKey(deviceKey),
            validFrom: DateTimeOffset.UtcNow.AddDays(-1),
            validUntil: DateTimeOffset.UtcNow.AddYears(1),
            x5cChain: [certificate.RawData]);

        return new HeldMdoc(DocType, issuerSigned);
    }

    /// <summary>Generates a static, ECDH-capable device key pair.</summary>
    /// <remarks>
    /// ⚠ ECDH, not ECDSA. On a real phone this is the <em>second</em> device key: the wallet's existing
    /// WebCrypto key is ECDSA-only and <b>structurally cannot</b> do the ECDH that <c>EMacKey</c> requires,
    /// because WebCrypto fixes a key's usages at generation (feature 185, design §3).
    /// </remarks>
    public static (ECPrivateKeyParameters Private, ECPublicKeyParameters Public) GenerateDeviceKey()
    {
        var pair = MdocSessionCrypto.GenerateEphemeralKeyPair();
        return ((ECPrivateKeyParameters)pair.Private, (ECPublicKeyParameters)pair.Public);
    }
}
