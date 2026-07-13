// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Security.Cryptography.Cose;
using System.Security.Cryptography.X509Certificates;

using FluentAssertions;

using Sorcha.Mdoc;
using Sorcha.Mdoc.Cbor;

using Xunit;

namespace Sorcha.Cryptography.Tests.Mdoc;

/// <summary>
/// Feature 135 / T052 — MdocIssuer produces a valid mdoc (signed MSO + x5chain) that round-trips
/// through the US2 MdocService verification once the holder wraps it in a presentation.
/// </summary>
public class MdocIssuanceTests
{
    private const string DocType = "eu.europa.ec.eudi.pid.1";
    private const string ClientId = "x509_san_dns:verifier.example";
    private const string Nonce = "issue-roundtrip-nonce";
    private const string ResponseUri = "https://verifier.example/cb";

    private static (byte[] privateKey, byte[] certDer, ECDsa key) NewIssuer()
    {
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var req = new CertificateRequest("CN=Test PID Issuer", key, HashAlgorithmName.SHA256);
        using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        return (key.ExportECPrivateKey(), cert.Export(X509ContentType.Cert), key);
    }

    /// <summary>Builds a holder-presented DeviceResponse from an issued IssuerSigned + holder key.</summary>
    private static byte[] PresentAsDeviceResponse(IssuerSigned issued, string docType, ECDsa holderKey)
    {
        var deviceNameSpacesBytes = MdocCbor.WrapTag24(MdocCbor.Encode(w => { w.WriteStartMap(0); w.WriteEndMap(); }));
        var sessionTranscript = MdocCodec.BuildOpenId4VpSessionTranscript(ClientId, Nonce, null, ResponseUri);
        var deviceAuthentication = MdocCodec.BuildDeviceAuthentication(sessionTranscript, docType, deviceNameSpacesBytes);
        var deviceSigner = new CoseSigner(holderKey, HashAlgorithmName.SHA256);
        var deviceSig = CoseMessage.DecodeSign1(CoseSign1Message.SignDetached(deviceAuthentication, deviceSigner));

        var response = new DeviceResponse
        {
            Documents =
            [
                new Document
                {
                    DocType = docType,
                    IssuerSigned = issued,
                    DeviceSigned = new DeviceSigned { NameSpacesBytes = deviceNameSpacesBytes, DeviceAuth = new DeviceAuth { DeviceSignature = deviceSig } }
                }
            ]
        };
        return MdocCodec.EncodeDeviceResponse(response);
    }

    [Fact]
    public void Issue_WithX5cChain_RoundTripsThroughVerify()
    {
        var (issuerPriv, issuerCertDer, _) = NewIssuer();
        using var holderKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var holderCose = MdocTestVectors.EncodeEc2CoseKey(holderKey);
        var now = DateTimeOffset.UtcNow;

        var issued = MdocIssuer.IssueIssuerSigned(
            DocType,
            new Dictionary<string, object> { ["family_name"] = "Andersson", ["given_name"] = "Anna", ["birth_date"] = "1985-03-30" },
            issuerPriv, "ES256", holderCose, now, now.AddYears(1), x5cChain: [issuerCertDer]);

        var bytes = PresentAsDeviceResponse(issued, DocType, holderKey);
        var result = new MdocService().Verify(bytes, new MdocSessionTranscript { ClientId = ClientId, Nonce = Nonce, ResponseUri = ResponseUri });

        result.IsValid.Should().BeTrue();
        result.IssuerSignatureValid.Should().BeTrue();
        result.DigestsValid.Should().BeTrue();
        result.DeviceBindingValid.Should().BeTrue();
        result.Claims.Should().ContainKey("family_name").WhoseValue.Should().Be("Andersson");
        result.Claims.Should().ContainKey("birth_date");
    }

    [Fact]
    public void Issue_EncodedIssuerSigned_RoundTripsThroughCodec()
    {
        var (issuerPriv, issuerCertDer, _) = NewIssuer();
        using var holderKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var now = DateTimeOffset.UtcNow;

        var issued = MdocIssuer.IssueIssuerSigned(
            DocType, new Dictionary<string, object> { ["family_name"] = "Andersson" },
            issuerPriv, "ES256", MdocTestVectors.EncodeEc2CoseKey(holderKey), now, now.AddYears(1), x5cChain: [issuerCertDer]);

        var decoded = MdocCodec.DecodeIssuerSigned(MdocCodec.EncodeIssuerSigned(issued));

        decoded.NameSpaces.Should().ContainKey(DocType);
        decoded.NameSpaces[DocType].Single().Item.ElementIdentifier.Should().Be("family_name");
        // The signature survives the encode→decode cycle and verifies against the issuer cert key.
        using var issuerPublic = IssuerPublicFrom(issuerCertDer);
        decoded.IssuerAuth.VerifyEmbedded(issuerPublic).Should().BeTrue();
    }

    [Fact]
    public void Issue_WithoutX5cChain_NotVerifiable_ByX5cVerifier()
    {
        // A register-anchored mdoc carries no x5chain; MdocService (x5chain-only key resolution)
        // cannot resolve the issuer key, so it cannot verify the issuer signature. This is why the
        // format handler requires an X.509 anchor for mdoc issuance.
        var (issuerPriv, _, _) = NewIssuer();
        using var holderKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var now = DateTimeOffset.UtcNow;

        var issued = MdocIssuer.IssueIssuerSigned(
            DocType, new Dictionary<string, object> { ["family_name"] = "Andersson" },
            issuerPriv, "ES256", MdocTestVectors.EncodeEc2CoseKey(holderKey), now, now.AddYears(1), x5cChain: null);

        var bytes = PresentAsDeviceResponse(issued, DocType, holderKey);
        var result = new MdocService().Verify(bytes, new MdocSessionTranscript { ClientId = ClientId, Nonce = Nonce, ResponseUri = ResponseUri });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("x5chain", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Issue_NonEs256Algorithm_Throws()
    {
        using var holderKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var now = DateTimeOffset.UtcNow;
        var act = () => MdocIssuer.IssueIssuerSigned(
            DocType, new Dictionary<string, object> { ["x"] = "y" },
            [1, 2, 3], "EdDSA", MdocTestVectors.EncodeEc2CoseKey(holderKey), now, now.AddYears(1));
        act.Should().Throw<NotSupportedException>();
    }

    private static ECDsa IssuerPublicFrom(byte[] certDer)
    {
        using var cert = X509CertificateLoader.LoadCertificate(certDer);
        return cert.GetECDsaPublicKey()!;
    }
}
