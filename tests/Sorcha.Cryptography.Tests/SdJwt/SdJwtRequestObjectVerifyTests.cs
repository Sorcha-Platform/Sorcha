// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Sorcha.Cryptography.SdJwt;
using Xunit;

namespace Sorcha.Cryptography.Tests.SdJwt;

/// <summary>
/// Tests for <see cref="SdJwtService.TryVerifyCompactJwsWithEmbeddedJwk"/> (issue #344) — the RFC 9101 §4
/// request-object verifier that validates a compact JWS against its JOSE-header-embedded jwk, for both
/// algorithms the verifier's RequestObjectSigner emits (ES256 P-256 and EdDSA Ed25519), and rejects
/// alg:none, no-jwk, and tampered signatures.
/// </summary>
public class SdJwtRequestObjectVerifyTests
{
    private static string B64(byte[] b) => Base64Url.EncodeToString(b);
    private static string B64(string s) => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(s));

    private static string SignEs256(string payloadJson, out string thumbprint)
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var p = ecdsa.ExportParameters(includePrivateParameters: false);
        var x = B64(p.Q.X!);
        var y = B64(p.Q.Y!);
        var header = $"{{\"alg\":\"ES256\",\"typ\":\"oauth-authz-req+jwt\",\"jwk\":{{\"kty\":\"EC\",\"crv\":\"P-256\",\"x\":\"{x}\",\"y\":\"{y}\"}}}}";
        var h = B64(header);
        var pl = B64(payloadJson);
        var sig = ecdsa.SignData(Encoding.ASCII.GetBytes($"{h}.{pl}"), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        thumbprint = B64(SHA256.HashData(Encoding.UTF8.GetBytes($"{{\"crv\":\"P-256\",\"kty\":\"EC\",\"x\":\"{x}\",\"y\":\"{y}\"}}")));
        return $"{h}.{pl}.{B64(sig)}";
    }

    private static string SignEdDsa(string payloadJson, out string thumbprint)
    {
        var kp = Sodium.PublicKeyAuth.GenerateKeyPair();
        var x = B64(kp.PublicKey);
        var header = $"{{\"alg\":\"EdDSA\",\"typ\":\"oauth-authz-req+jwt\",\"jwk\":{{\"kty\":\"OKP\",\"crv\":\"Ed25519\",\"x\":\"{x}\"}}}}";
        var h = B64(header);
        var pl = B64(payloadJson);
        var sig = Sodium.PublicKeyAuth.SignDetached(Encoding.ASCII.GetBytes($"{h}.{pl}"), kp.PrivateKey);
        thumbprint = B64(SHA256.HashData(Encoding.UTF8.GetBytes($"{{\"crv\":\"Ed25519\",\"kty\":\"OKP\",\"x\":\"{x}\"}}")));
        return $"{h}.{pl}.{B64(sig)}";
    }

    [Fact]
    public void Es256_ValidSignature_VerifiesAndReturnsPayloadAndThumbprint()
    {
        var jwt = SignEs256("""{"client_id":"demo","nonce":"n1"}""", out var expectedThumbprint);

        var ok = SdJwtService.TryVerifyCompactJwsWithEmbeddedJwk(jwt, out var payload, out var thumbprint, out var error);

        ok.Should().BeTrue(error);
        payload.GetProperty("client_id").GetString().Should().Be("demo");
        thumbprint.Should().Be(expectedThumbprint);
    }

    [Fact]
    public void EdDsa_ValidSignature_VerifiesAndReturnsPayloadAndThumbprint()
    {
        var jwt = SignEdDsa("""{"client_id":"demo","nonce":"n2"}""", out var expectedThumbprint);

        var ok = SdJwtService.TryVerifyCompactJwsWithEmbeddedJwk(jwt, out var payload, out var thumbprint, out var error);

        ok.Should().BeTrue(error);
        payload.GetProperty("nonce").GetString().Should().Be("n2");
        thumbprint.Should().Be(expectedThumbprint);
    }

    [Fact]
    public void AlgNone_IsRejected()
    {
        var header = B64("""{"alg":"none","typ":"oauth-authz-req+jwt","jwk":{"kty":"OKP","crv":"Ed25519","x":"AAAA"}}""");
        var body = B64("""{"client_id":"demo"}""");
        var jwt = $"{header}.{body}.";

        var ok = SdJwtService.TryVerifyCompactJwsWithEmbeddedJwk(jwt, out _, out _, out var error);

        ok.Should().BeFalse();
        error.Should().Contain("alg:none");
    }

    [Fact]
    public void NoEmbeddedJwk_IsRejected()
    {
        var jwt = SignEs256("""{"client_id":"demo"}""", out _);
        // Replace the header with one that has no jwk (breaks the signature too, but the missing-jwk check fires first).
        var noJwkHeader = B64("""{"alg":"ES256","typ":"oauth-authz-req+jwt"}""");
        var body = jwt.Split('.')[1];
        var tampered = $"{noJwkHeader}.{body}.{jwt.Split('.')[2]}";

        var ok = SdJwtService.TryVerifyCompactJwsWithEmbeddedJwk(tampered, out _, out _, out var error);

        ok.Should().BeFalse();
        error.Should().Contain("no embedded 'jwk'");
    }

    [Fact]
    public void TamperedPayload_IsRejected()
    {
        var jwt = SignEs256("""{"client_id":"demo","nonce":"n1"}""", out _);
        var parts = jwt.Split('.');
        var tampered = $"{parts[0]}.{B64("""{"client_id":"attacker","nonce":"n1"}""")}.{parts[2]}";

        var ok = SdJwtService.TryVerifyCompactJwsWithEmbeddedJwk(tampered, out _, out _, out var error);

        ok.Should().BeFalse();
        error.Should().Contain("signature is invalid");
    }
}
