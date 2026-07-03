// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Sorcha.Agent.Commands;
using Xunit;

namespace Sorcha.Agent.Tests.Commands;

/// <summary>
/// Tests for HaipPresentCommand.ParseRequestObjectPayload, the response detector + verifier for OpenID4VP
/// request objects fetched from a verifier. Issue #346 tightened detection so non-JSON, non-JWT bodies
/// produce a clear error with a quoted preview. Issue #344 added RFC 9101 §4 signature verification: a
/// signed request object's JWS is verified against its embedded jwk (rejecting alg:none / tampering) before
/// any claim is used, and the signing key can be pinned to a trusted RFC 7638 thumbprint.
/// </summary>
public class HaipPresentCommandTests
{
    private static string Base64UrlEncode(string s) =>
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes(s));

    /// <summary>
    /// Signs a request-object payload with a fresh ES256 (P-256) key, embedding the public key as a
    /// JOSE-header jwk exactly as the verifier's RequestObjectSigner does. Returns the compact JWS and the
    /// RFC 7638 thumbprint of the embedded jwk.
    /// </summary>
    private static (string Jwt, string Thumbprint) SignEs256(string payloadJson)
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var p = ecdsa.ExportParameters(includePrivateParameters: false);
        var x = Base64Url.EncodeToString(p.Q.X!);
        var y = Base64Url.EncodeToString(p.Q.Y!);

        var headerJson = $"{{\"alg\":\"ES256\",\"typ\":\"oauth-authz-req+jwt\",\"jwk\":{{\"kty\":\"EC\",\"crv\":\"P-256\",\"x\":\"{x}\",\"y\":\"{y}\"}}}}";
        var h = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(headerJson));
        var pl = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(payloadJson));
        var signingInput = Encoding.ASCII.GetBytes($"{h}.{pl}");
        var sig = ecdsa.SignData(signingInput, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        var jwt = $"{h}.{pl}.{Base64Url.EncodeToString(sig)}";

        // RFC 7638 thumbprint for an EC key: canonical members {crv, kty, x, y} in lexicographic order.
        var canonical = $"{{\"crv\":\"P-256\",\"kty\":\"EC\",\"x\":\"{x}\",\"y\":\"{y}\"}}";
        var thumbprint = Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        return (jwt, thumbprint);
    }

    [Fact]
    public void ParseRequestObjectPayload_BareJsonObject_DeserialisesPayload()
    {
        var json = """{ "client_id": "demo-verifier", "nonce": "abc" }""";

        var payload = HaipPresentCommand.ParseRequestObjectPayload(json);

        payload.GetProperty("client_id").GetString().Should().Be("demo-verifier");
        payload.GetProperty("nonce").GetString().Should().Be("abc");
    }

    [Fact]
    public void ParseRequestObjectPayload_BareJsonObject_TolerantOfLeadingWhitespace()
    {
        var json = "  \r\n  { \"client_id\": \"demo\" }";

        var payload = HaipPresentCommand.ParseRequestObjectPayload(json);

        payload.GetProperty("client_id").GetString().Should().Be("demo");
    }

    [Fact]
    public void ParseRequestObjectPayload_ValidSignedJwt_ReturnsVerifiedPayload()
    {
        var (jwt, _) = SignEs256("""{"client_id":"demo","nonce":"xyz"}""");

        var payload = HaipPresentCommand.ParseRequestObjectPayload(jwt);

        payload.GetProperty("client_id").GetString().Should().Be("demo");
        payload.GetProperty("nonce").GetString().Should().Be("xyz");
    }

    [Fact]
    public void ParseRequestObjectPayload_PinnedThumbprintMatches_ReturnsPayload()
    {
        var (jwt, thumbprint) = SignEs256("""{"client_id":"demo","nonce":"xyz"}""");

        var payload = HaipPresentCommand.ParseRequestObjectPayload(jwt, thumbprint);

        payload.GetProperty("client_id").GetString().Should().Be("demo");
    }

    [Fact]
    public void ParseRequestObjectPayload_PinnedThumbprintMismatch_Throws()
    {
        var (jwt, _) = SignEs256("""{"client_id":"demo","nonce":"xyz"}""");

        var act = () => HaipPresentCommand.ParseRequestObjectPayload(jwt, "NOT_THE_REAL_THUMBPRINT");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*not the pinned verifier key*");
    }

    [Fact]
    public void ParseRequestObjectPayload_TamperedPayload_Throws()
    {
        var (jwt, _) = SignEs256("""{"client_id":"demo","nonce":"xyz"}""");
        var parts = jwt.Split('.');
        // Swap the payload for a different one but keep the original signature — verification must fail.
        var tampered = $"{parts[0]}.{Base64UrlEncode("""{"client_id":"attacker","nonce":"xyz"}""")}.{parts[2]}";

        var act = () => HaipPresentCommand.ParseRequestObjectPayload(tampered);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*signature verification failed*");
    }

    [Fact]
    public void ParseRequestObjectPayload_AlgNoneJwt_Rejected()
    {
        // An unsigned "alg:none" token must never be trusted (the classic JWT downgrade bypass).
        var header = Base64UrlEncode("""{"alg":"none","typ":"oauth-authz-req+jwt"}""");
        var body = Base64UrlEncode("""{"client_id":"demo","nonce":"xyz"}""");
        var jwt = $"{header}.{body}.";

        var act = () => HaipPresentCommand.ParseRequestObjectPayload(jwt);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*verification failed*");
    }

    [Fact]
    public void ParseRequestObjectPayload_HtmlErrorPage_ThrowsWithPreview()
    {
        var html = "<!DOCTYPE html><html><body>Internal Server Error</body></html>";
        var firstTwenty = html[..20]; // "<!DOCTYPE html><html"

        var act = () => HaipPresentCommand.ParseRequestObjectPayload(html);

        var ex = act.Should().Throw<InvalidOperationException>().Which;
        ex.Message.Should().Contain("neither a JSON body");
        ex.Message.Should().Contain("nor a compact JWT");
        ex.Message.Should().Contain($"\"{firstTwenty}\"",
            "the first 20 chars must be quoted in the message so the operator " +
            "can see what the verifier returned");
    }

    [Fact]
    public void ParseRequestObjectPayload_PlainTextError_ThrowsWithPreview()
    {
        var text = "not authorised";

        var act = () => HaipPresentCommand.ParseRequestObjectPayload(text);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*\"not authorised\"*");
    }

    [Fact]
    public void ParseRequestObjectPayload_EmptyResponse_Throws()
    {
        var act = () => HaipPresentCommand.ParseRequestObjectPayload("");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*neither a JSON body*nor a compact JWT*");
    }

    [Fact]
    public void ParseRequestObjectPayload_LeadingEyJButNotThreeSegments_Throws()
    {
        // Something starts with "eyJ" but isn't a real 3-segment JWS — verification rejects it.
        var act = () => HaipPresentCommand.ParseRequestObjectPayload("eyJalgIsNoneAndNoDot");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*verification failed*3-part*");
    }
}
