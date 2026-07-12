// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Sorcha.Verifier.Engine;
using Xunit;

namespace Sorcha.Verifier.Tests;

/// <summary>
/// Feature 181 US6 (T055) — the WASM-safe request-object validator. Tamper and SAN-mismatch are HARD
/// refusals; a valid request renders Trusted (chains to an anchor) / AuthenticUntrusted (no anchor) /
/// Unverifiable (no cert / bad client_id). Absent anchors never block.
/// </summary>
public class RequestObjectValidatorTests
{
    private const string Host = "verifier.sorcha.test";
    private readonly RequestObjectValidator _validator = new();

    [Fact]
    public void Valid_NoAnchors_RendersAuthenticUntrusted()
    {
        var (jwt, _) = SelfSignedRequest(Host);
        var result = _validator.Validate(jwt, $"x509_san_dns:{Host}", anchors: null);

        result.Refused.Should().BeFalse();
        result.AuthState!.Status.Should().Be(VerifierAuthStatus.AuthenticUntrusted);
        result.AuthState.VerifierHost.Should().Be(Host);
    }

    [Fact]
    public void Valid_LeafIsTheAnchor_RendersTrustedListVerified()
    {
        var (jwt, leafDer) = SelfSignedRequest(Host);
        var anchors = new VerifierTrustAnchors([leafDer], "eu-lotl#3");

        var result = _validator.Validate(jwt, $"x509_san_dns:{Host}", anchors);

        result.Refused.Should().BeFalse();
        result.AuthState!.Status.Should().Be(VerifierAuthStatus.TrustedListVerified);
        result.AuthState.AnchorSetId.Should().Be("eu-lotl#3");
    }

    [Fact]
    public void Valid_CaIssuedLeaf_WithCaRootAnchor_RendersTrusted()
    {
        var (jwt, rootDer) = CaIssuedRequest(Host);
        var anchors = new VerifierTrustAnchors([rootDer], "test-ca#1");

        var result = _validator.Validate(jwt, $"x509_san_dns:{Host}", anchors);

        result.AuthState!.Status.Should().Be(VerifierAuthStatus.TrustedListVerified);
    }

    [Fact]
    public void Valid_CaIssuedLeaf_WithUnrelatedAnchor_RendersAuthenticUntrusted()
    {
        var (jwt, _) = CaIssuedRequest(Host);
        using var unrelated = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var unrelatedRoot = new CertificateRequest("CN=Other CA", unrelated, HashAlgorithmName.SHA256)
            .CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));

        var result = _validator.Validate(jwt, $"x509_san_dns:{Host}",
            new VerifierTrustAnchors([unrelatedRoot.RawData], "other#1"));

        result.AuthState!.Status.Should().Be(VerifierAuthStatus.AuthenticUntrusted);
    }

    [Fact]
    public void TamperedSignature_IsRefused()
    {
        var (jwt, _) = SelfSignedRequest(Host);
        // Flip the last char of the signature segment.
        var parts = jwt.Split('.');
        var sig = parts[2].ToCharArray();
        sig[^1] = sig[^1] == 'A' ? 'B' : 'A';
        var tampered = $"{parts[0]}.{parts[1]}.{new string(sig)}";

        var result = _validator.Validate(tampered, $"x509_san_dns:{Host}", anchors: null);

        result.Refused.Should().BeTrue();
        result.RefusalCode.Should().Be(RequestObjectErrorCodes.RequestObjectInvalid);
    }

    [Fact]
    public void SanMismatch_IsRefused()
    {
        var (jwt, _) = SelfSignedRequest(Host);
        var result = _validator.Validate(jwt, "x509_san_dns:attacker.example", anchors: null);

        result.Refused.Should().BeTrue();
        result.RefusalCode.Should().Be(RequestObjectErrorCodes.RequestHostMismatch);
    }

    [Fact]
    public void NoX5c_RendersUnverifiable()
    {
        // Hand-build a JWS with a jwk header (no x5c).
        var header = B64Url("""{"alg":"ES256","typ":"oauth-authz-req+jwt","jwk":{}}"""u8.ToArray());
        var payload = B64Url("""{"client_id":"x509_san_dns:verifier.sorcha.test"}"""u8.ToArray());
        var jwt = $"{header}.{payload}.{B64Url(new byte[64])}";

        var result = _validator.Validate(jwt, $"x509_san_dns:{Host}", anchors: null);
        result.Refused.Should().BeFalse();
        result.AuthState!.Status.Should().Be(VerifierAuthStatus.Unverifiable);
    }

    [Fact]
    public void ClientIdNotX509SanDns_RendersUnverifiable()
    {
        var (jwt, _) = SelfSignedRequest(Host);
        var result = _validator.Validate(jwt, "https://verifier.example/haip", anchors: null);
        result.AuthState!.Status.Should().Be(VerifierAuthStatus.Unverifiable);
    }

    [Fact]
    public void Malformed_IsRefused()
    {
        _validator.Validate("not-a-jwt", "x509_san_dns:x", null).RefusalCode
            .Should().Be(RequestObjectErrorCodes.RequestObjectInvalid);
    }

    // ---- helpers ----

    private static (string Jwt, byte[] LeafDer) SelfSignedRequest(string host)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var req = new CertificateRequest($"CN={host}", key, HashAlgorithmName.SHA256);
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName(host);
        req.CertificateExtensions.Add(san.Build());
        using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(1));
        return (SignRequestObject(host, key, [cert.RawData]), cert.RawData);
    }

    private static (string Jwt, byte[] RootDer) CaIssuedRequest(string host)
    {
        using var caKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var caReq = new CertificateRequest("CN=Test Verifier CA", caKey, HashAlgorithmName.SHA256);
        caReq.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        using var root = caReq.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(10));

        using var leafKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var leafReq = new CertificateRequest($"CN={host}", leafKey, HashAlgorithmName.SHA256);
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName(host);
        leafReq.CertificateExtensions.Add(san.Build());
        var serial = RandomNumberGenerator.GetBytes(16);
        serial[0] &= 0x7F;
        using var leaf = leafReq.Create(root.SubjectName, X509SignatureGenerator.CreateForECDsa(caKey),
            DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(1), serial);

        return (SignRequestObject(host, leafKey, [leaf.RawData]), root.RawData);
    }

    private static string SignRequestObject(string host, ECDsa signingKey, byte[][] x5cDer)
    {
        var header = new Dictionary<string, object>
        {
            ["alg"] = "ES256",
            ["typ"] = "oauth-authz-req+jwt",
            ["x5c"] = x5cDer.Select(Convert.ToBase64String).ToList(),
        };
        var payload = new Dictionary<string, object>
        {
            ["client_id"] = $"x509_san_dns:{host}",
            ["nonce"] = "n-test",
            ["response_type"] = "vp_token",
        };
        var h = B64Url(JsonSerializer.SerializeToUtf8Bytes(header));
        var p = B64Url(JsonSerializer.SerializeToUtf8Bytes(payload));
        var signingInput = Encoding.ASCII.GetBytes($"{h}.{p}");
        var sig = signingKey.SignData(signingInput, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return $"{h}.{p}.{B64Url(sig)}";
    }

    private static string B64Url(byte[] data) => Base64Url.EncodeToString(data);
}
