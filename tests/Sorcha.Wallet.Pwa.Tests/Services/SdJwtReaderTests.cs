// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Linq;
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
        var sd = JwtBody(new { vct = "AssuredIdentityCredential" })
            + "~" + Disclosure("s1", "given_name", "Ada")
            + "~" + Disclosure("s2", "family_name", "Lovelace")
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
    public void ReadDisclosedClaims_ObjectValue_RendersFieldNamesNotRawJson()
    {
        // A disclosure whose VALUE is an object must not print as {"town":"Edinburgh"}.
        var disclosure = Disclosure("s1", "address", new Dictionary<string, object>
        {
            ["town"] = "Edinburgh",
            ["line1"] = "6/2 Warrender Park Terrace"
        });
        var token = $"{Jwt()}~{disclosure}";

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
        var token = $"{Jwt()}~{disclosure}";

        var claims = SdJwtReader.ReadDisclosedClaims(token);

        claims.Single(c => c.Name == "address").Value.Should().NotContain("_sd");
    }

    private static string B64Url(byte[] b) =>
        Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Disclosure(string salt, string name, object value) =>
        B64Url(System.Text.Encoding.UTF8.GetBytes(
            System.Text.Json.JsonSerializer.Serialize(new object[] { salt, name, value })));

    private static string Jwt() =>
        $"{B64Url(System.Text.Encoding.UTF8.GetBytes("""{"alg":"ES256"}"""))}." +
        $"{B64Url(System.Text.Encoding.UTF8.GetBytes("""{"vct":"x"}"""))}.c2ln";
}
