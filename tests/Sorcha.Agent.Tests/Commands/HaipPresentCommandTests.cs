// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Sorcha.Agent.Commands;
using Sorcha.Verifier.Engine;
using Xunit;

namespace Sorcha.Agent.Tests.Commands;

/// <summary>
/// Tests for <c>HaipPresentCommand.ParseRequestObjectPayload</c> — the response detector and verifier
/// authentication gate for OpenID4VP request objects fetched from a verifier.
///
/// <para>Issue #346 tightened detection so non-JSON, non-JWT bodies produce a clear error with a quoted
/// preview. Issue #344 added RFC 9101 §4 signature verification. <b>Issue #1538</b> replaced the
/// embedded-<c>jwk</c> check with X.509 verifier authentication: since Feature 181 US6 the verifier signs
/// with a certificate and an <c>x5c</c> chain and emits no <c>jwk</c>, so the old check could only ever
/// refuse — the agent fail-closed against a correctly-behaving verifier and its OID4VP leg was unusable.</para>
///
/// <para>The authentication itself lives in <see cref="RequestObjectValidator"/> (shared with the citizen
/// wallet); what is tested here is the agent's <b>unattended policy</b> over its three-state verdict, since
/// an agent has no human to render a consent decision to.</para>
/// </summary>
public class HaipPresentCommandTests
{
    private const string VerifierHost = "verifier.sorcha.test";
    private const string ClientId = $"x509_san_dns:{VerifierHost}";

    private static readonly RequestObjectTrustPolicy Default = new();

    private static string SamplePayload(string clientId = ClientId) =>
        $"{{\"client_id\":\"{clientId}\",\"nonce\":\"abc123\",\"state\":\"s1\",\"response_uri\":\"https://x/y\"}}";

    /// <summary>
    /// Mints a self-signed P-256 certificate carrying <paramref name="sanDnsName"/> as a SAN dNSName —
    /// the shape the HAIP verifier's own certificate has (Feature 181 US6).
    /// </summary>
    private static X509Certificate2 MintCert(string sanDnsName, ECDsa key)
    {
        var request = new CertificateRequest(
            $"CN={sanDnsName}", key, HashAlgorithmName.SHA256);

        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName(sanDnsName);
        request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));

        return request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(1));
    }

    /// <summary>
    /// Signs a request-object payload ES256 and embeds the signing certificate as an <c>x5c</c> chain,
    /// exactly as the verifier's <c>RequestObjectSigner</c> does.
    /// </summary>
    private static string SignWithX5c(string payloadJson, X509Certificate2 cert, ECDsa key)
    {
        var x5c = Convert.ToBase64String(cert.RawData);
        var headerJson = $"{{\"alg\":\"ES256\",\"typ\":\"oauth-authz-req+jwt\",\"x5c\":[\"{x5c}\"]}}";

        var h = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(headerJson));
        var pl = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(payloadJson));
        var signingInput = Encoding.ASCII.GetBytes($"{h}.{pl}");
        var sig = key.SignData(signingInput, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return $"{h}.{pl}.{Base64Url.EncodeToString(sig)}";
    }

    /// <summary>Signs with an embedded jwk and NO x5c — the pre-#1538 shape the platform no longer emits.</summary>
    private static string SignWithEmbeddedJwk(string payloadJson)
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var p = ecdsa.ExportParameters(includePrivateParameters: false);
        var x = Base64Url.EncodeToString(p.Q.X!);
        var y = Base64Url.EncodeToString(p.Q.Y!);

        var headerJson = $"{{\"alg\":\"ES256\",\"typ\":\"oauth-authz-req+jwt\",\"jwk\":{{\"kty\":\"EC\",\"crv\":\"P-256\",\"x\":\"{x}\",\"y\":\"{y}\"}}}}";
        var h = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(headerJson));
        var pl = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(payloadJson));
        var sig = ecdsa.SignData(Encoding.ASCII.GetBytes($"{h}.{pl}"), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return $"{h}.{pl}.{Base64Url.EncodeToString(sig)}";
    }

    // ---------------------------------------------------------------------
    // The regression #1538 is about: a correctly-signed x5c request object.
    // ---------------------------------------------------------------------

    [Fact]
    public void ParseRequestObjectPayload_X5cSignedRequestObject_ReturnsPayload()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var cert = MintCert(VerifierHost, key);
        var jwt = SignWithX5c(SamplePayload(), cert, key);

        var payload = HaipPresentCommand.ParseRequestObjectPayload(jwt, Default);

        payload.GetProperty("nonce").GetString().Should().Be("abc123");
        payload.GetProperty("client_id").GetString().Should().Be(ClientId);
    }

    [Fact]
    public void ParseRequestObjectPayload_X5cSignedWithNoAnchors_ProceedsAsAuthenticButUntrusted()
    {
        // FR-027: absent anchors must NEVER block. A dev verifier self-signs, so this is the
        // ordinary local path — refusing here would break every local HAIP run.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var cert = MintCert(VerifierHost, key);
        var jwt = SignWithX5c(SamplePayload(), cert, key);

        var act = () => HaipPresentCommand.ParseRequestObjectPayload(jwt, new RequestObjectTrustPolicy());

        act.Should().NotThrow();
    }

    [Fact]
    public void ParseRequestObjectPayload_ChainsToSuppliedAnchor_SatisfiesRequireTrusted()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var cert = MintCert(VerifierHost, key);
        var jwt = SignWithX5c(SamplePayload(), cert, key);

        // The self-signed leaf IS its own root, so supplying it as an anchor must reach TrustedListVerified
        // — and therefore must satisfy --require-trusted-verifier.
        var policy = new RequestObjectTrustPolicy(
            Anchors: new VerifierTrustAnchors([cert.RawData], "test-anchors"),
            RequireTrusted: true);

        var act = () => HaipPresentCommand.ParseRequestObjectPayload(jwt, policy);

        act.Should().NotThrow();
    }

    // ---------------------------------------------------------------------
    // Hard refusals — these must throw regardless of any permissive flag.
    // ---------------------------------------------------------------------

    [Fact]
    public void ParseRequestObjectPayload_TamperedPayload_IsRefusedDespiteEveryPermissiveFlag()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var cert = MintCert(VerifierHost, key);
        var jwt = SignWithX5c(SamplePayload(), cert, key);

        var parts = jwt.Split('.');
        var tamperedPayload = Base64Url.EncodeToString(
            Encoding.UTF8.GetBytes(SamplePayload().Replace("abc123", "evil99")));
        var tampered = $"{parts[0]}.{tamperedPayload}.{parts[2]}";

        var act = () => HaipPresentCommand.ParseRequestObjectPayload(
            tampered, new RequestObjectTrustPolicy(AllowUnverified: true));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*REQUEST_OBJECT_INVALID*");
    }

    [Fact]
    public void ParseRequestObjectPayload_SanNotMatchingClientIdHost_IsRefused()
    {
        // The certificate is for a DIFFERENT host than the client_id claims — what a substituted
        // verifier looks like. Must be a hard refusal, not a warning.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var cert = MintCert("attacker.example", key);
        var jwt = SignWithX5c(SamplePayload(), cert, key);

        var act = () => HaipPresentCommand.ParseRequestObjectPayload(
            jwt, new RequestObjectTrustPolicy(ExpectedClientId: ClientId, AllowUnverified: true));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*REQUEST_HOST_MISMATCH*");
    }

    [Fact]
    public void ParseRequestObjectPayload_PinnedClientIdMismatch_IsRefused()
    {
        // Pinning out-of-band is what turns internal consistency into identity: the cert is genuinely
        // for verifier.sorcha.test, but the caller expected someone else.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var cert = MintCert(VerifierHost, key);
        var jwt = SignWithX5c(SamplePayload(), cert, key);

        var act = () => HaipPresentCommand.ParseRequestObjectPayload(
            jwt, new RequestObjectTrustPolicy(ExpectedClientId: "x509_san_dns:someone.else"));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*REQUEST_HOST_MISMATCH*");
    }

    // ---------------------------------------------------------------------
    // Unverifiable — refused by default, permitted only on explicit opt-in.
    // ---------------------------------------------------------------------

    [Fact]
    public void ParseRequestObjectPayload_EmbeddedJwkWithoutX5c_IsRefusedByDefault()
    {
        // The pre-#1538 shape. A key the signer puts in its own header is self-asserted, so it
        // authenticates nobody — an unattended agent must not present against it silently.
        var jwt = SignWithEmbeddedJwk(SamplePayload());

        var act = () => HaipPresentCommand.ParseRequestObjectPayload(jwt, Default);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*could not be authenticated*");
    }

    [Fact]
    public void ParseRequestObjectPayload_EmbeddedJwkWithoutX5c_IsPermittedWithOptIn()
    {
        var jwt = SignWithEmbeddedJwk(SamplePayload());

        var payload = HaipPresentCommand.ParseRequestObjectPayload(
            jwt, new RequestObjectTrustPolicy(AllowUnverified: true));

        payload.GetProperty("nonce").GetString().Should().Be("abc123");
    }

    [Fact]
    public void ParseRequestObjectPayload_UnsignedJsonBody_IsRefusedByDefault()
    {
        var act = () => HaipPresentCommand.ParseRequestObjectPayload(SamplePayload(), Default);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*unsigned JSON body*");
    }

    [Fact]
    public void ParseRequestObjectPayload_UnsignedJsonBody_IsPermittedWithOptIn()
    {
        var payload = HaipPresentCommand.ParseRequestObjectPayload(
            "  " + SamplePayload(), new RequestObjectTrustPolicy(AllowUnverified: true));

        payload.GetProperty("state").GetString().Should().Be("s1");
    }

    [Fact]
    public void ParseRequestObjectPayload_AuthenticButUntrusted_IsRefusedWhenTrustRequired()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var cert = MintCert(VerifierHost, key);
        var jwt = SignWithX5c(SamplePayload(), cert, key);

        var act = () => HaipPresentCommand.ParseRequestObjectPayload(
            jwt, new RequestObjectTrustPolicy(RequireTrusted: true));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*does not chain to any supplied anchor*");
    }

    // ---------------------------------------------------------------------
    // Response-shape detection (issue #346) — unchanged behaviour.
    // ---------------------------------------------------------------------

    [Fact]
    public void ParseRequestObjectPayload_HtmlErrorPage_ThrowsWithPreview()
    {
        var html = "<!DOCTYPE html><html><body>502 Bad Gateway</body></html>";

        var act = () => HaipPresentCommand.ParseRequestObjectPayload(html, Default);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*neither a JSON body*")
            .WithMessage("*<!DOCTYPE html><html*", "the preview is truncated to the first 20 chars");
    }

    [Fact]
    public void ParseRequestObjectPayload_PlainTextError_ThrowsWithPreview()
    {
        var act = () => HaipPresentCommand.ParseRequestObjectPayload("Request not found", Default);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Request not found*");
    }

    [Fact]
    public void ParseRequestObjectPayload_EmptyResponse_Throws()
    {
        var act = () => HaipPresentCommand.ParseRequestObjectPayload("   ", Default);

        act.Should().Throw<InvalidOperationException>();
    }

    // ---------------------------------------------------------------------
    // Anchor loading.
    // ---------------------------------------------------------------------

    [Fact]
    public void LoadAnchors_WithNoPaths_ReturnsNull()
    {
        HaipPresentCommand.LoadAnchors(null).Should().BeNull();
        HaipPresentCommand.LoadAnchors([]).Should().BeNull();
    }

    [Fact]
    public void LoadAnchors_ReadsBothPemAndDer()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var cert = MintCert(VerifierHost, key);

        var der = Path.GetTempFileName();
        var pem = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(der, cert.RawData);
            File.WriteAllText(pem,
                "-----BEGIN CERTIFICATE-----\n" +
                Convert.ToBase64String(cert.RawData, Base64FormattingOptions.InsertLineBreaks) +
                "\n-----END CERTIFICATE-----\n");

            var anchors = HaipPresentCommand.LoadAnchors([der, pem]);

            anchors.Should().NotBeNull();
            anchors!.Roots.Should().HaveCount(2);
            anchors.Roots[0].Should().Equal(cert.RawData);
            anchors.Roots[1].Should().Equal(cert.RawData, "the PEM decodes to the same DER bytes");
        }
        finally
        {
            File.Delete(der);
            File.Delete(pem);
        }
    }

    [Fact]
    public void LoadAnchors_MissingFile_Throws()
    {
        var act = () => HaipPresentCommand.LoadAnchors([Path.Combine(Path.GetTempPath(), "no-such-anchor.pem")]);

        act.Should().Throw<FileNotFoundException>();
    }
}
