// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Buffers.Text;
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
    private static string Disclosure(string salt, string name, object value) =>
        Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(new[] { salt, name, value?.ToString() ?? "" }));

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
}
