// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Security.Cryptography.Cose;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

using FluentAssertions;

using Sorcha.Mdoc;
using Sorcha.Mdoc.Cbor;
using Sorcha.Haip.Service.Endpoints;
using Xunit;

namespace Sorcha.Haip.Service.Tests;

/// <summary>
/// Feature 135 / T058-T059 — the OpenID4VCI credential endpoint's mdoc issuance binds the holder's
/// proof JWK to the MSO device key. This exercises BuildEc2CoseKeyFromJwk end-to-end: the COSE key
/// it derives works as the MSO device key so the issued mdoc verifies with that holder's signature.
/// </summary>
public class CredentialEndpointsMdocIssuanceTests
{
    private const string DocType = "eu.europa.ec.eudi.pid.1";
    private const string ClientId = "x509_san_dns:verifier.example";
    private const string Nonce = "vci-mdoc-nonce";
    private const string ResponseUri = "https://verifier.example/cb";

    private static JsonElement Ec2Jwk(ECDsa key)
    {
        var p = key.ExportParameters(false);
        string B64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["kty"] = "EC", ["crv"] = "P-256", ["x"] = B64Url(p.Q.X!), ["y"] = B64Url(p.Q.Y!)
        }));
    }

    [Fact]
    public void BuildEc2CoseKeyFromJwk_BindsHolderKey_IssuedMdocVerifies()
    {
        // Issuer + holder keys; holder's proof JWK → COSE device key via the endpoint helper.
        using var issuerKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var issuerCert = new CertificateRequest("CN=Issuer", issuerKey, HashAlgorithmName.SHA256)
            .CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        var issuerCertDer = issuerCert.Export(X509ContentType.Cert);

        using var holderKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var holderCose = CredentialEndpoints.BuildEc2CoseKeyFromJwk(Ec2Jwk(holderKey));

        var now = DateTimeOffset.UtcNow;
        var issued = MdocIssuer.IssueIssuerSigned(
            DocType, new Dictionary<string, object> { ["family_name"] = "Andersson" },
            issuerKey.ExportECPrivateKey(), "ES256", holderCose, now, now.AddYears(1), x5cChain: [issuerCertDer]);

        // Present with the holder key the JWK described.
        var deviceNameSpaces = MdocCbor.WrapTag24(MdocCbor.Encode(w => { w.WriteStartMap(0); w.WriteEndMap(); }));
        var transcript = MdocCodec.BuildOpenId4VpSessionTranscript(ClientId, Nonce, null, ResponseUri);
        var deviceAuthn = MdocCodec.BuildDeviceAuthentication(transcript, DocType, deviceNameSpaces);
        var deviceSig = CoseMessage.DecodeSign1(CoseSign1Message.SignDetached(deviceAuthn, new CoseSigner(holderKey, HashAlgorithmName.SHA256)));

        var response = new DeviceResponse
        {
            Documents =
            [
                new Document
                {
                    DocType = DocType,
                    IssuerSigned = issued,
                    DeviceSigned = new DeviceSigned { NameSpacesBytes = deviceNameSpaces, DeviceAuth = new DeviceAuth { DeviceSignature = deviceSig } }
                }
            ]
        };

        var result = new MdocService().Verify(
            MdocCodec.EncodeDeviceResponse(response),
            new MdocSessionTranscript { ClientId = ClientId, Nonce = Nonce, ResponseUri = ResponseUri });

        result.IsValid.Should().BeTrue();
        result.DeviceBindingValid.Should().BeTrue();
        result.Claims.Should().ContainKey("family_name");
    }

    [Fact]
    public void BuildEc2CoseKeyFromJwk_NonEcKey_Throws()
    {
        var ed25519 = JsonSerializer.Deserialize<JsonElement>(
            JsonSerializer.Serialize(new Dictionary<string, string> { ["kty"] = "OKP", ["crv"] = "Ed25519", ["x"] = "AAAA" }));
        var act = () => CredentialEndpoints.BuildEc2CoseKeyFromJwk(ed25519);
        act.Should().Throw<NotSupportedException>();
    }
}
