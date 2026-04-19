// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Sorcha.Cryptography.SdJwt;
using Xunit;

namespace Sorcha.Cryptography.Tests.SdJwt;

/// <summary>
/// Tests for nested and array-element selective disclosure via JSON Pointer paths
/// (FR-015 through FR-021).
/// </summary>
public class SdJwtNestedDisclosureTests
{
    private readonly SdJwtService _service = new();

    private static (byte[] privateKey, byte[] publicKey) GenerateP256KeyPair()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (ecdsa.ExportECPrivateKey(), ecdsa.ExportSubjectPublicKeyInfo());
    }

    [Fact]
    public void Translate_NestedObjectField_ProducesScopedSdArray()
    {
        var claims = new Dictionary<string, object>
        {
            ["name"] = "Alice",
            ["address"] = new Dictionary<string, object>
            {
                ["street"] = "123 Main St",
                ["locality"] = "Dublin",
                ["country"] = "Ireland"
            }
        };

        var (payload, disclosures, topSd) = NestedDisclosure.Translate(
            claims, ["/address/locality", "/address/country"]);

        // The address object should have _sd digests for locality and country
        payload.Should().ContainKey("address");
        var address = payload["address"] as Dictionary<string, object>;
        address.Should().NotBeNull();
        address!.Should().ContainKey("_sd");
        var sdArray = address["_sd"] as List<string>;
        sdArray.Should().HaveCount(2);

        // street should still be visible (not disclosable)
        address.Should().ContainKey("street");

        // locality and country should be removed from the address dict (they're in disclosures)
        address.Should().NotContainKey("locality");
        address.Should().NotContainKey("country");

        // Two disclosures should exist
        disclosures.Should().HaveCount(2);

        // No top-level SD digests (name is not disclosable in this test)
        topSd.Should().BeEmpty();
    }

    [Fact]
    public void Translate_MixedTopLevelAndNested_BothWork()
    {
        var claims = new Dictionary<string, object>
        {
            ["name"] = "Alice",
            ["age"] = 30,
            ["address"] = new Dictionary<string, object>
            {
                ["locality"] = "Dublin",
                ["country"] = "Ireland"
            }
        };

        var (payload, disclosures, topSd) = NestedDisclosure.Translate(
            claims, ["name", "/address/locality"]);

        // "name" should be top-level disclosed
        payload.Should().NotContainKey("name");
        topSd.Should().HaveCount(1);

        // address.locality should be nested-disclosed
        var address = payload["address"] as Dictionary<string, object>;
        address.Should().NotBeNull();
        address!.Should().NotContainKey("locality");
        address.Should().ContainKey("country"); // not disclosable, stays
        address.Should().ContainKey("_sd");

        // Total: 1 top-level + 1 nested
        disclosures.Should().HaveCount(2);
    }

    [Fact]
    public void Translate_UnknownPath_IsIgnored()
    {
        // When a disclosable path references a field that doesn't exist in the claims,
        // it is silently skipped (no error at translation time — error happens at
        // presentation time if the holder tries to disclose it)
        var claims = new Dictionary<string, object>
        {
            ["name"] = "Alice"
        };

        var (_, disclosures, _) = NestedDisclosure.Translate(
            claims, ["/address/locality"]);

        disclosures.Should().BeEmpty();
    }

    [Fact]
    public void HasNestedPaths_WithPointers_ReturnsTrue()
    {
        NestedDisclosure.HasNestedPaths(["name", "/address/locality"]).Should().BeTrue();
    }

    [Fact]
    public void HasNestedPaths_WithoutPointers_ReturnsFalse()
    {
        NestedDisclosure.HasNestedPaths(["name", "age"]).Should().BeFalse();
    }

    [Fact]
    public async Task RoundTrip_NestedDisclosure_IssueAndVerify()
    {
        // End-to-end: issue credential with nested disclosable fields,
        // create presentation disclosing a subset, verify
        var (privateKey, publicKey) = GenerateP256KeyPair();

        var claims = new Dictionary<string, object>
        {
            ["name"] = "Alice O'Brien",
            ["address"] = new Dictionary<string, object>
            {
                ["street"] = "123 Main St",
                ["locality"] = "Dublin",
                ["region"] = "Leinster",
                ["postcode"] = "D01 F5P2",
                ["country"] = "Ireland"
            }
        };

        // Issue with nested disclosable paths
        var token = await _service.CreateTokenAsync(
            claims,
            disclosableClaims: ["name", "/address/locality", "/address/country"],
            issuer: "did:sorcha:org:gov",
            subject: "did:sorcha:w:alice",
            signingKey: privateKey,
            algorithm: "ES256");

        token.Should().NotBeNull();
        token.RawToken.Should().NotBeNullOrWhiteSpace();

        // Verify the full token — all disclosures present
        var fullResult = await _service.VerifyTokenAsync(token.RawToken, publicKey, "ES256");
        fullResult.IsValid.Should().BeTrue();
        fullResult.Claims.Should().ContainKey("name");
    }

    // -----------------------------------------------------------------------
    // Reconstruct tests — verifier-side tree walk per SD-JWT §7.1.
    // These tests lock in the fix for PR #226 review item #2: nested disclosures
    // must land at the correct tree depth, not at the root.
    // -----------------------------------------------------------------------

    [Fact]
    public void Reconstruct_NestedObjectField_PlacesAtCorrectDepth()
    {
        // Build a payload where /address/locality is disclosable via a nested _sd
        // array inside the address object. The expected result has address.locality
        // INSIDE address — NOT a flat "locality" at the root.
        var localityDisclosure = EncodeDisclosure("salt-locality", "locality", "Edinburgh");
        var localityDigest = ComputeDigest(localityDisclosure);

        var payloadJson = $$"""
        {
          "iss": "did:sorcha:org:gov",
          "sub": "did:sorcha:w:alice",
          "iat": 1700000000,
          "address": {
            "street": "123 Main St",
            "country": "UK",
            "_sd": ["{{localityDigest}}"]
          },
          "_sd_alg": "sha-256"
        }
        """;
        var payload = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(payloadJson)!;

        var result = NestedDisclosure.Reconstruct(payload, [localityDisclosure]);

        result.Should().ContainKey("address");
        result.Should().NotContainKey("locality",
            "the disclosed value MUST land inside its parent object, not at the root");

        var address = result["address"].Should().BeAssignableTo<Dictionary<string, object>>().Subject;
        address["locality"].Should().Be("Edinburgh");
        address["street"].Should().Be("123 Main St");
        address["country"].Should().Be("UK");
        address.Should().NotContainKey("_sd", "_sd slots must be stripped from the output");
    }

    [Fact]
    public void Reconstruct_NestedDisclosureNotPresented_StaysHidden()
    {
        // Same payload, but the holder does NOT disclose locality. The verifier
        // must not see any trace of the undisclosed field.
        var localityDisclosure = EncodeDisclosure("salt-locality", "locality", "Edinburgh");
        var localityDigest = ComputeDigest(localityDisclosure);

        var payloadJson = $$"""
        {
          "address": {
            "street": "123 Main St",
            "_sd": ["{{localityDigest}}"]
          },
          "_sd_alg": "sha-256"
        }
        """;
        var payload = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(payloadJson)!;

        var result = NestedDisclosure.Reconstruct(payload, []);

        var address = result["address"].Should().BeAssignableTo<Dictionary<string, object>>().Subject;
        address.Should().NotContainKey("locality");
        address["street"].Should().Be("123 Main St");
    }

    [Fact]
    public void Reconstruct_MixedTopLevelAndNested_BothResolveCorrectly()
    {
        // Top-level "name" disclosure + nested /address/locality disclosure in one
        // presentation. Both must land at their correct depth.
        var nameDisclosure = EncodeDisclosure("salt-name", "name", "Alice");
        var nameDigest = ComputeDigest(nameDisclosure);
        var localityDisclosure = EncodeDisclosure("salt-locality", "locality", "Edinburgh");
        var localityDigest = ComputeDigest(localityDisclosure);

        var payloadJson = $$"""
        {
          "_sd": ["{{nameDigest}}"],
          "address": {
            "country": "UK",
            "_sd": ["{{localityDigest}}"]
          }
        }
        """;
        var payload = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(payloadJson)!;

        var result = NestedDisclosure.Reconstruct(payload, [nameDisclosure, localityDisclosure]);

        result["name"].Should().Be("Alice");
        var address = result["address"].Should().BeAssignableTo<Dictionary<string, object>>().Subject;
        address["locality"].Should().Be("Edinburgh");
        address["country"].Should().Be("UK");
    }

    [Fact]
    public void Reconstruct_ArrayElementDisclosure_RestoresAtIndex()
    {
        // /qualifications/1 disclosable — only element at index 1 is revealed.
        // Element 0 is plaintext (non-disclosable), elements 2 is a placeholder
        // whose disclosure is absent from the presentation → must stay hidden.
        var qualOne = EncodeArrayDisclosure("salt-q1", new { type = "Engineering" });
        var qualOneDigest = ComputeDigest(qualOne);
        var qualTwo = EncodeArrayDisclosure("salt-q2", new { type = "Medicine" });
        var qualTwoDigest = ComputeDigest(qualTwo);

        var payloadJson = $$"""
        {
          "qualifications": [
            { "type": "Plumbing" },
            { "...": "{{qualOneDigest}}" },
            { "...": "{{qualTwoDigest}}" }
          ]
        }
        """;
        var payload = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(payloadJson)!;

        // Only disclose element 1.
        var result = NestedDisclosure.Reconstruct(payload, [qualOne]);

        var quals = result["qualifications"].Should().BeAssignableTo<List<object?>>().Subject;
        quals.Should().HaveCount(2, "plumbing stays (plaintext), q1 is disclosed, q2 is hidden and dropped");

        // Plumbing still at index 0.
        var q0 = quals[0].Should().BeAssignableTo<Dictionary<string, object>>().Subject;
        q0["type"].Should().Be("Plumbing");

        // Engineering was at placeholder index 1 → now at index 1 in output.
        var q1 = quals[1].Should().BeAssignableTo<Dictionary<string, object>>().Subject;
        q1["type"].Should().Be("Engineering");
    }

    [Fact]
    public void Reconstruct_EnvelopeClaims_AreStripped()
    {
        // iss/sub/iat/exp/cnf belong to the JWT envelope, not the application
        // claims surface. Callers pull those off basePayload directly.
        var payloadJson = """
        {
          "iss": "did:sorcha:org:gov",
          "sub": "did:sorcha:w:alice",
          "iat": 1700000000,
          "exp": 1800000000,
          "cnf": { "jwk": { "kty": "EC" } },
          "publicField": "visible"
        }
        """;
        var payload = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(payloadJson)!;

        var result = NestedDisclosure.Reconstruct(payload, []);

        result.Should().ContainKey("publicField");
        result.Keys.Should().NotContain(["iss", "sub", "iat", "exp", "cnf", "_sd", "_sd_alg"]);
    }

    [Fact]
    public void Reconstruct_MalformedDisclosure_SilentlySkipped()
    {
        // Verifier MUST NOT throw on attacker-controlled disclosure strings.
        // Malformed entries are simply ignored (their digests won't match anything).
        var validDisclosure = EncodeDisclosure("salt-name", "name", "Alice");
        var validDigest = ComputeDigest(validDisclosure);

        var payloadJson = $$"""
        {
          "_sd": ["{{validDigest}}"]
        }
        """;
        var payload = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(payloadJson)!;

        var act = () => NestedDisclosure.Reconstruct(payload, [
            validDisclosure,
            "!!not-base64!!",
            "",
            "   ",
            Base64Url.EncodeToString(Encoding.UTF8.GetBytes("not-an-array")),
        ]);

        var result = act.Should().NotThrow().Which;
        result["name"].Should().Be("Alice");
    }

    [Fact]
    public void Reconstruct_DisclosedValueWithNestedSd_ResolvesTransitively()
    {
        // An entire "address" object is disclosable, and the disclosed value
        // itself contains a further _sd with "locality" hidden inside. When both
        // disclosures are presented, the verifier must recurse into the disclosed
        // value and resolve the inner _sd digest too.
        var localityDisclosure = EncodeDisclosure("salt-locality", "locality", "Edinburgh");
        var localityDigest = ComputeDigest(localityDisclosure);

        // The address value carries its own _sd array.
        var addressValueJson = $$"""
        {
          "country": "UK",
          "_sd": ["{{localityDigest}}"]
        }
        """;
        var addressValue = JsonSerializer.Deserialize<JsonElement>(addressValueJson);

        // Build the address disclosure with that nested structure as the value.
        var addressDisclosureRaw = $"[\"salt-addr\",\"address\",{addressValue.GetRawText()}]";
        var addressDisclosure = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(addressDisclosureRaw));
        var addressDigest = ComputeDigest(addressDisclosure);

        var payloadJson = $$"""
        {
          "_sd": ["{{addressDigest}}"]
        }
        """;
        var payload = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(payloadJson)!;

        var result = NestedDisclosure.Reconstruct(payload, [addressDisclosure, localityDisclosure]);

        var address = result["address"].Should().BeAssignableTo<Dictionary<string, object>>().Subject;
        address["country"].Should().Be("UK");
        address["locality"].Should().Be("Edinburgh",
            "a disclosed value carrying its own _sd must recurse and resolve inner digests");
        address.Should().NotContainKey("_sd");
    }

    [Fact]
    public void Reconstruct_DigestPresentNoMatchingDisclosure_IsDropped()
    {
        // The _sd array references a digest we don't have the disclosure for
        // (holder didn't present it). The digest must NOT appear in the output.
        var payloadJson = """
        {
          "address": {
            "street": "123 Main St",
            "_sd": ["ZF53bOHwmAdEWLmBh4QItUv6tQZKRw6iYxdqjOErXt8"]
          }
        }
        """;
        var payload = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(payloadJson)!;

        var result = NestedDisclosure.Reconstruct(payload, []);

        var address = result["address"].Should().BeAssignableTo<Dictionary<string, object>>().Subject;
        address.Keys.Should().OnlyContain(k => k == "street");
    }

    // --- Test helpers (mirror the encoding used by Translate / the SD-JWT spec) ---

    private static string EncodeDisclosure(string salt, string name, object value)
    {
        var json = JsonSerializer.Serialize(new object[] { salt, name, value });
        return Base64Url.EncodeToString(Encoding.UTF8.GetBytes(json));
    }

    private static string EncodeArrayDisclosure(string salt, object value)
    {
        var json = JsonSerializer.Serialize(new object[] { salt, value });
        return Base64Url.EncodeToString(Encoding.UTF8.GetBytes(json));
    }

    private static string ComputeDigest(string disclosure)
    {
        var bytes = Encoding.ASCII.GetBytes(disclosure);
        var hash = SHA256.HashData(bytes);
        return Base64Url.EncodeToString(hash);
    }

    [Fact]
    public async Task ExistingBlueprints_TopLevelNameKeyed_WorkIdentically()
    {
        // FR-021: Existing blueprints with top-level name-keyed disclosables
        // must continue to produce identical output
        var (privateKey, publicKey) = GenerateP256KeyPair();

        var claims = new Dictionary<string, object>
        {
            ["name"] = "Alice",
            ["licenseType"] = "ClassA",
            ["publicField"] = "visible"
        };

        // Use only top-level names (no JSON Pointers) — legacy path
        var token = await _service.CreateTokenAsync(
            claims,
            disclosableClaims: ["name", "licenseType"],
            issuer: "did:sorcha:org:gov",
            subject: "did:sorcha:w:alice",
            signingKey: privateKey,
            algorithm: "ES256");

        var result = await _service.VerifyTokenAsync(token.RawToken, publicKey, "ES256");
        result.IsValid.Should().BeTrue();
        result.Claims.Should().ContainKey("name");
        result.Claims.Should().ContainKey("licenseType");
        result.Claims.Should().ContainKey("publicField");

        // Create selective presentation — only name
        var presentation = await _service.CreatePresentationAsync(
            token.RawToken,
            claimsToDisclose: ["name"]);

        var presResult = await _service.VerifyPresentationAsync(
            presentation.RawPresentation, publicKey, "ES256");

        presResult.IsValid.Should().BeTrue();
        presResult.Claims.Should().ContainKey("name");
        presResult.Claims.Should().ContainKey("publicField"); // non-disclosable, always visible
        presResult.Claims.Should().NotContainKey("licenseType"); // not disclosed
    }
}
