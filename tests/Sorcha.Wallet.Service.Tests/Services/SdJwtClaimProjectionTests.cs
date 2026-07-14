// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Sorcha.Wallet.Service.Services.Implementation;

namespace Sorcha.Wallet.Service.Tests.Services;

/// <summary>
/// Guards the defect found live on n1 (2026-07-14): a NESTED selective disclosure
/// left <c>address</c> rendering as its raw <c>{"_sd":[…]}</c> digest array on the
/// credential card, while its children leaked out as flat top-level claims.
/// Every pre-existing decoder test used a FLAT SD-JWT, which is why it shipped.
/// </summary>
public class SdJwtClaimProjectionTests
{
    // --- SD-JWT construction helpers (RFC 9901 §4.2.1) ---

    private static string B64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>A disclosure is base64url(JSON([salt, name, value])).</summary>
    private static string Disclosure(string salt, string name, object value)
    {
        var json = JsonSerializer.Serialize(new object[] { salt, name, value });
        return B64Url(Encoding.UTF8.GetBytes(json));
    }

    /// <summary>The digest that appears in an _sd array is base64url(SHA-256(ascii(disclosure))).</summary>
    private static string Digest(string disclosure) =>
        B64Url(SHA256.HashData(Encoding.ASCII.GetBytes(disclosure)));

    /// <summary>
    /// An array-element disclosure is base64url(JSON([salt, value])) — RFC 9901 §5.2.4.
    /// Two elements, no claim name (the name is implicit in the array's position).
    /// </summary>
    private static string ArrayElementDisclosure(string salt, object value)
    {
        var json = JsonSerializer.Serialize(new object[] { salt, value });
        return B64Url(Encoding.UTF8.GetBytes(json));
    }

    private static string Token(object body, params string[] disclosures)
    {
        var header = B64Url(Encoding.UTF8.GetBytes("""{"alg":"ES256","typ":"dc+sd-jwt"}"""));
        var payload = B64Url(JsonSerializer.SerializeToUtf8Bytes(body));
        var jwt = $"{header}.{payload}.c2ln";   // signature is never verified on this path
        return disclosures.Length == 0 ? jwt : jwt + "~" + string.Join("~", disclosures);
    }

    /// <summary>
    /// The n1 shape: address is an OBJECT whose town/line1 are individually
    /// disclosable, so the body carries address:{_sd:[…]} and the disclosures
    /// name the CHILDREN.
    /// </summary>
    private static string NestedToken()
    {
        var town = Disclosure("s1", "town", "Edinburgh");
        var line1 = Disclosure("s2", "line1", "6/2 Warrender Park Terrace");
        var body = new Dictionary<string, object>
        {
            ["vct"] = "https://sorcha.dev/vc/assured-identity/v1",
            ["iss"] = "did:sorcha:org:ws11q",
            ["email"] = "stuart@stuartfraser.net",       // always disclosed — in the body, no _sd
            ["address"] = new Dictionary<string, object>
            {
                ["_sd"] = new[] { Digest(town), Digest(line1) }
            }
        };
        return Token(body, town, line1);
    }

    [Fact]
    public void Project_NestedDisclosure_ReconstructsAddressObject()
    {
        var result = SdJwtClaimProjection.Project(NestedToken());

        using var doc = JsonDocument.Parse(result.ClaimsJson);
        var address = doc.RootElement.GetProperty("address");

        address.ValueKind.Should().Be(JsonValueKind.Object);
        address.GetProperty("town").GetString().Should().Be("Edinburgh");
        address.GetProperty("line1").GetString().Should().Be("6/2 Warrender Park Terrace");
    }

    [Fact]
    public void Project_NestedDisclosure_LeaksNoSdDigestsAtAnyDepth()
    {
        var result = SdJwtClaimProjection.Project(NestedToken());

        // The bug in one assertion: no _sd / _sd_alg key may survive, at any depth.
        result.ClaimsJson.Should().NotContain("_sd");
    }

    [Fact]
    public void Project_NestedDisclosure_DoesNotFlattenChildrenToTopLevel()
    {
        var result = SdJwtClaimProjection.Project(NestedToken());

        using var doc = JsonDocument.Parse(result.ClaimsJson);
        doc.RootElement.TryGetProperty("town", out _).Should().BeFalse(
            "town belongs inside address, not beside it");
        doc.RootElement.TryGetProperty("line1", out _).Should().BeFalse();
    }

    [Fact]
    public void Project_NestedDisclosure_MarksOnlySelectivelyDisclosableClaims()
    {
        var result = SdJwtClaimProjection.Project(NestedToken());

        // address carries an _sd → the holder controls what of it is revealed.
        result.DisclosableClaims.Should().Contain("address");
        // email sits in the body with no _sd → it always travels.
        result.DisclosableClaims.Should().NotContain("email");
    }

    [Fact]
    public void Project_StripsProtocolFields()
    {
        var result = SdJwtClaimProjection.Project(NestedToken());

        using var doc = JsonDocument.Parse(result.ClaimsJson);
        foreach (var field in new[] { "iss", "vct", "sub", "iat", "exp", "cnf" })
            doc.RootElement.TryGetProperty(field, out _).Should().BeFalse($"{field} is a protocol field, not a claim");
    }

    [Fact]
    public void Project_FlatDisclosure_MarksClaimDisclosable()
    {
        var name = Disclosure("s1", "name", "Jane Doe");
        var body = new Dictionary<string, object>
        {
            ["vct"] = "https://sorcha.dev/vc/x/v1",
            ["licenceNumber"] = "BI-2026-0042",           // always disclosed
            ["_sd"] = new[] { Digest(name) }
        };
        var result = SdJwtClaimProjection.Project(Token(body, name));

        using var doc = JsonDocument.Parse(result.ClaimsJson);
        doc.RootElement.GetProperty("name").GetString().Should().Be("Jane Doe");
        doc.RootElement.GetProperty("licenceNumber").GetString().Should().Be("BI-2026-0042");

        result.DisclosableClaims.Should().BeEquivalentTo(["name"]);
    }

    /// <summary>
    /// RFC 9901's OTHER disclosure shape: an array element replaced with a
    /// <c>{"...": digest}</c> placeholder — no <c>_sd</c> key anywhere in the subtree.
    /// </summary>
    private static string ArrayElementDisclosureToken()
    {
        var element = ArrayElementDisclosure("s3", "DE");
        var body = new Dictionary<string, object>
        {
            ["vct"] = "https://sorcha.dev/vc/assured-identity/v1",
            ["iss"] = "did:sorcha:org:ws11q",
            ["nationalities"] = new object[]
            {
                new Dictionary<string, object> { ["..."] = Digest(element) }
            }
        };
        return Token(body, element);
    }

    [Fact]
    public void Project_ArrayElementDisclosure_MarksClaimDisclosable()
    {
        var result = SdJwtClaimProjection.Project(ArrayElementDisclosureToken());

        // nationalities has an array-element {"...": digest} placeholder — no _sd
        // anywhere in its subtree — so the naive _sd-only check wrongly marks it
        // always-disclosed. The holder actually controls whether it's revealed.
        result.DisclosableClaims.Should().Contain("nationalities");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-jwt")]
    public void Project_MalformedInput_ReturnsEmptyProjection(string? input)
    {
        var result = SdJwtClaimProjection.Project(input);

        result.ClaimsJson.Should().Be("{}");
        result.DisclosableClaims.Should().BeEmpty();
    }

    [Fact]
    public void Project_BadDisclosure_KeepsTheRest()
    {
        var good = Disclosure("s1", "name", "Jane Doe");
        var body = new Dictionary<string, object>
        {
            ["vct"] = "https://sorcha.dev/vc/x/v1",
            ["_sd"] = new[] { Digest(good) }
        };
        var token = Token(body, good) + "~!!!not-base64!!!";

        var result = SdJwtClaimProjection.Project(token);

        using var doc = JsonDocument.Parse(result.ClaimsJson);
        doc.RootElement.GetProperty("name").GetString().Should().Be("Jane Doe");
    }
}
