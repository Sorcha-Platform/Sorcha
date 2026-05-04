// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Sorcha.Agent.Commands;
using Xunit;

namespace Sorcha.Agent.Tests.Commands;

/// <summary>
/// Tests for HaipPresentCommand.ParseRequestObjectPayload, the response-format
/// detector for OpenID4VP request objects fetched from a verifier. Issue #346
/// tightened the detection so non-JSON, non-JWT bodies (HTML error pages,
/// redirect bodies, plain text) produce a clear error with a quoted preview
/// rather than falling into the JWT-decode branch and surfacing a cryptic
/// base64 error.
/// </summary>
public class HaipPresentCommandTests
{
    private static string Base64UrlEncode(string s) =>
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes(s));

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
    public void ParseRequestObjectPayload_CompactJwt_ReturnsPayloadSegment()
    {
        // A real JWS-compact: header.payload.signature, all base64url. We don't
        // verify the signature here (issue #344 tracks that); we just need the
        // payload segment to decode to JSON.
        var header = Base64UrlEncode("""{"alg":"none","typ":"oauth-authz-req+jwt"}""");
        var body = Base64UrlEncode("""{"client_id":"demo","nonce":"xyz"}""");
        var jwt = $"{header}.{body}.";

        var payload = HaipPresentCommand.ParseRequestObjectPayload(jwt);

        payload.GetProperty("client_id").GetString().Should().Be("demo");
        payload.GetProperty("nonce").GetString().Should().Be("xyz");
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
    public void ParseRequestObjectPayload_LeadingEyJButOnlyOneSegment_Throws()
    {
        // Defensive: something starts with "eyJ" but isn't a real JWT. The detector
        // routes it down the JWT branch but the missing-segments check catches it.
        var act = () => HaipPresentCommand.ParseRequestObjectPayload("eyJalgIsNoneAndNoDot");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*JWT*at least two dot-separated segments*");
    }
}
