// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using Sorcha.Wallet.Pwa.Services;
using Xunit;

namespace Sorcha.Wallet.Pwa.Tests.Services;

/// <summary>
/// Unit tests for <see cref="SdJwtReader"/> — pure-.NET SD-JWT disclosure +
/// expiry decoding (the credential detail page reuses this; no libsodium).
/// </summary>
public sealed class SdJwtReaderTests
{
    private static string JwtBody(object payload) =>
        "eyJhbGciOiJFUzI1NiJ9." +
        Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(payload)) +
        ".c2ln";

    [Fact]
    public void ReadDisclosedClaims_ReturnsNameValuePairs_SkippingKbJwtAndEmpties()
    {
        var givenName = Disclosure("s1", "given_name", "Ada");
        var familyName = Disclosure("s2", "family_name", "Lovelace");
        var sd = JwtBody(new { vct = "AssuredIdentityCredential", _sd = new[] { Digest(givenName), Digest(familyName) } })
            + "~" + givenName
            + "~" + familyName
            + "~";

        var claims = SdJwtReader.ReadDisclosedClaims(sd);

        claims.Should().HaveCount(2);
        claims.Should().ContainEquivalentOf(new DisclosedClaim("given_name", "Ada"));
        claims.Should().ContainEquivalentOf(new DisclosedClaim("family_name", "Lovelace"));
    }

    [Fact]
    public void ReadDisclosedClaims_EmptyOrNull_ReturnsEmpty()
    {
        SdJwtReader.ReadDisclosedClaims(null).Should().BeEmpty();
        SdJwtReader.ReadDisclosedClaims("").Should().BeEmpty();
        SdJwtReader.ReadDisclosedClaims("just.a.jwt").Should().BeEmpty();
    }

    [Fact]
    public void ReadExpiry_ReadsExpClaim()
    {
        var exp = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds();
        var sd = JwtBody(new { exp }) + "~";

        SdJwtReader.ReadExpiry(sd)!.Value.ToUnixTimeSeconds().Should().Be(exp);
    }

    [Fact]
    public void ReadExpiry_NoExp_ReturnsNull()
    {
        var sd = JwtBody(new { vct = "X" }) + "~";
        SdJwtReader.ReadExpiry(sd).Should().BeNull();
    }

    [Fact]
    public void ReadVct_ReadsCanonicalUriVct()
    {
        var sd = JwtBody(new { vct = "https://sorcha.dev/vc/assured-identity/v1" }) + "~";
        SdJwtReader.ReadVct(sd).Should().Be("https://sorcha.dev/vc/assured-identity/v1");
    }

    [Fact]
    public void ReadVct_NoVctOrNull_ReturnsEmpty()
    {
        SdJwtReader.ReadVct(JwtBody(new { exp = 1L }) + "~").Should().BeEmpty();
        SdJwtReader.ReadVct(null).Should().BeEmpty();
        SdJwtReader.ReadVct("just.a.jwt").Should().BeEmpty();
    }

    [Fact]
    public void ReadDisclosedClaims_ObjectValue_RendersFieldNamesNotRawJson()
    {
        // A disclosure whose VALUE is an object must not print as {"town":"Edinburgh"}.
        // The digest must appear in the body's _sd array — that is what makes this a
        // valid (resolvable) disclosure rather than an orphan segment a real verifier
        // would ignore.
        var disclosure = Disclosure("s1", "address", new Dictionary<string, object>
        {
            ["town"] = "Edinburgh",
            ["line1"] = "6/2 Warrender Park Terrace"
        });
        var token = $"{JwtBody(new { vct = "x", _sd = new[] { Digest(disclosure) } })}~{disclosure}";

        var claims = SdJwtReader.ReadDisclosedClaims(token);

        var address = claims.Single(c => c.Name == "address");
        address.Value.Should().NotContain("{");
        address.Value.Should().NotContain("\"town\"");
        address.Value.Should().Contain("Edinburgh");
    }

    [Fact]
    public void ReadDisclosedClaims_NeverLeaksSdDigests()
    {
        var disclosure = Disclosure("s1", "address", new Dictionary<string, object>
        {
            ["_sd"] = new[] { "zSH_kfTeW2Mlc" }
        });
        var token = $"{JwtBody(new { vct = "x", _sd = new[] { Digest(disclosure) } })}~{disclosure}";

        var claims = SdJwtReader.ReadDisclosedClaims(token);

        claims.Single(c => c.Name == "address").Value.Should().NotContain("_sd");
    }

    [Fact]
    public void ReadDisclosedClaims_NestedAddressDisclosure_ReconstructsAtCorrectDepth()
    {
        // The real credential shape: "address" carries its OWN _sd array (town, line1
        // disclosed independently), and "email" is always-disclosed (present verbatim
        // in the body, no digest indirection). Before the fix, ReadDisclosedClaims only
        // read the ~disclosure~ segments — never the JWT body — so it emitted "town"
        // and "line1" as flat top-level claims, never showed "address" at all, and
        // never showed "email".
        var town = Disclosure("s1", "town", "Edinburgh");
        var line1 = Disclosure("s2", "line1", "6/2 Warrender Park Terrace");

        var body = JwtBody(new
        {
            vct = "AssuredIdentityCredential",
            email = "ada@example.com",
            address = new Dictionary<string, object>
            {
                ["_sd"] = new[] { Digest(town), Digest(line1) }
            }
        });
        var token = $"{body}~{town}~{line1}~";

        var claims = SdJwtReader.ReadDisclosedClaims(token);

        claims.Should().Contain(c => c.Name == "email" && c.Value == "ada@example.com");

        var address = claims.Single(c => c.Name == "address");
        address.Value.Should().Contain("Edinburgh");
        address.Value.Should().Contain("6/2 Warrender Park Terrace");

        claims.Should().NotContain(c => c.Name == "town");
        claims.Should().NotContain(c => c.Name == "line1");
        claims.Should().NotContain(c => c.Value.Contains("_sd"));
    }

    private static string B64Url(byte[] b) =>
        Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Disclosure(string salt, string name, object value) =>
        B64Url(System.Text.Encoding.UTF8.GetBytes(
            System.Text.Json.JsonSerializer.Serialize(new object[] { salt, name, value })));

    /// <summary>The digest of a disclosure as it appears in an <c>_sd</c> array: base64url(SHA256(ascii(disclosure))).</summary>
    private static string Digest(string disclosure) =>
        Base64Url.EncodeToString(SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(disclosure)));
}
