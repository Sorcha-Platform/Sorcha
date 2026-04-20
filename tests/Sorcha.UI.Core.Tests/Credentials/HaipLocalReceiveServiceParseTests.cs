// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Sorcha.UI.Core.Services.Credentials;
using Xunit;

namespace Sorcha.UI.Core.Tests.Credentials;

/// <summary>
/// Pure-parser tests for <see cref="HaipLocalReceiveService"/>. The full
/// receive flow is exercised by the wave 13 E2E walkthrough; these unit
/// tests pin the URI + offer parsing helpers so they can't silently
/// regress when refactored.
/// </summary>
public class HaipLocalReceiveServiceParseTests
{
    [Fact]
    public void ExtractOfferJson_ValidUri_ReturnsDecodedJson()
    {
        const string offerJson = """{"credential_issuer":"https://issuer.example","credentials":["X"]}""";
        var encoded = Uri.EscapeDataString(offerJson);
        var uri = $"openid-credential-offer://?credential_offer={encoded}";

        var result = HaipLocalReceiveService.ExtractOfferJson(uri);

        result.Should().Be(offerJson);
    }

    [Fact]
    public void ExtractOfferJson_TrailingQueryParam_StopsAtAmpersand()
    {
        const string offerJson = """{"credential_issuer":"https://issuer.example"}""";
        var encoded = Uri.EscapeDataString(offerJson);
        var uri = $"openid-credential-offer://?credential_offer={encoded}&extra=foo";

        var result = HaipLocalReceiveService.ExtractOfferJson(uri);

        result.Should().Be(offerJson);
    }

    [Fact]
    public void ExtractOfferJson_MissingMarker_ReturnsNull()
    {
        const string uri = "openid-credential-offer://?credential_offer_uri=https://x.example/offer/123";
        var result = HaipLocalReceiveService.ExtractOfferJson(uri);
        result.Should().BeNull();
    }

    [Fact]
    public void ExtractPreAuthorizedCode_ValidGrants_ReturnsCode()
    {
        var offer = JsonDocument.Parse("""
            {
                "credential_issuer": "https://issuer.example",
                "credentials": ["AssuredIdentityCredential"],
                "grants": {
                    "urn:ietf:params:oauth:grant-type:pre-authorized_code": {
                        "pre-authorized_code": "abc123-secret"
                    }
                }
            }
            """);

        var code = HaipLocalReceiveService.ExtractPreAuthorizedCode(offer.RootElement);

        code.Should().Be("abc123-secret");
    }

    [Fact]
    public void ExtractPreAuthorizedCode_NoGrants_ReturnsNull()
    {
        var offer = JsonDocument.Parse("""
            {
                "credential_issuer": "https://issuer.example",
                "credentials": ["X"]
            }
            """);

        var code = HaipLocalReceiveService.ExtractPreAuthorizedCode(offer.RootElement);

        code.Should().BeNull();
    }

    [Fact]
    public void ExtractPreAuthorizedCode_GrantsWithoutPreAuthBranch_ReturnsNull()
    {
        var offer = JsonDocument.Parse("""
            {
                "grants": {
                    "authorization_code": { "issuer_state": "x" }
                }
            }
            """);

        var code = HaipLocalReceiveService.ExtractPreAuthorizedCode(offer.RootElement);

        code.Should().BeNull();
    }
}
