// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

using FluentAssertions;

using Sorcha.Haip.Service.Services;

using Xunit;

namespace Sorcha.Haip.Service.Tests.Services;

/// <summary>
/// Feature 181 US6 (T053) — <see cref="RequestObjectSigner"/> produces an RFC 9101 JWT-Secured
/// Authorization Request signed with the verifier certificate (ES256), carrying its <c>x5c</c> chain (not
/// the retired self-certifying <c>jwk</c>), so a wallet can authenticate the verifier.
/// </summary>
public class RequestObjectSignerTests
{
    private static RequestObjectSigner BuildSigner(string host = "verifier.test")
        => new(VerifierCertificate.CreateSelfSigned(host));

    [Fact]
    public void Sign_ProducesEs256JwtWith_x5c_AndNo_jwk()
    {
        var signer = BuildSigner();
        var jwt = signer.Sign(new Dictionary<string, object>
        {
            ["client_id"] = signer.ClientId,
            ["nonce"] = "n-0S6_WzA2Mj",
            ["response_type"] = "vp_token",
        });

        var parts = jwt.Split('.');
        parts.Should().HaveCount(3);

        using var header = JsonDocument.Parse(Base64Url.DecodeFromChars(parts[0]));
        header.RootElement.GetProperty("typ").GetString().Should().Be("oauth-authz-req+jwt");
        header.RootElement.GetProperty("alg").GetString().Should().Be("ES256");
        header.RootElement.TryGetProperty("x5c", out var x5c).Should().BeTrue("the verifier cert chain must be embedded");
        x5c.GetArrayLength().Should().BeGreaterThan(0);
        header.RootElement.TryGetProperty("jwk", out _).Should().BeFalse("the self-certifying jwk header is retired");
    }

    [Fact]
    public void Sign_SignatureVerifiesUnderThe_x5c_LeafKey()
    {
        var signer = BuildSigner();
        var jwt = signer.Sign(new Dictionary<string, object> { ["nonce"] = "verify-me" });
        var parts = jwt.Split('.');

        using var header = JsonDocument.Parse(Base64Url.DecodeFromChars(parts[0]));
        var leafDer = Convert.FromBase64String(header.RootElement.GetProperty("x5c")[0].GetString()!);
        using var leaf = X509CertificateLoader.LoadCertificate(leafDer);
        using var leafKey = leaf.GetECDsaPublicKey()!;

        var signingInput = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");
        var signature = Base64Url.DecodeFromChars(parts[2]);
        leafKey.VerifyData(signingInput, signature, HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation)
            .Should().BeTrue("the request object must verify under its x5c leaf key");
    }

    [Fact]
    public void ClientId_IsPrefixed_x509_san_dns()
    {
        BuildSigner("sorcha.example").ClientId.Should().Be("x509_san_dns:sorcha.example");
    }

    [Fact]
    public void Sign_PayloadRoundTripsThroughBase64UrlDecoding()
    {
        var jwt = BuildSigner().Sign(new Dictionary<string, object>
        {
            ["nonce"] = "roundtrip-payload",
            ["response_type"] = "vp_token",
            ["state"] = "abc-123",
        });
        var parts = jwt.Split('.');

        using var doc = JsonDocument.Parse(Base64Url.DecodeFromChars(parts[1]));
        doc.RootElement.GetProperty("nonce").GetString().Should().Be("roundtrip-payload");
        doc.RootElement.GetProperty("state").GetString().Should().Be("abc-123");
    }
}
