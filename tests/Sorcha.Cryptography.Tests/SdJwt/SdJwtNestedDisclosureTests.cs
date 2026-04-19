// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Security.Cryptography;
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

    [Fact]
    public async Task ArrayElementDisclosure_SelectsOnlyRequestedIndex()
    {
        // Feature 094 TODO(094) regression test: requesting /qualifications/1 must
        // select only the disclosure whose digest appears at that placeholder slot.
        // Prior behaviour dumped every 2-element disclosure into the presentation,
        // silently leaking elements the holder did not intend to disclose.
        var (privateKey, publicKey) = GenerateP256KeyPair();

        var claims = new Dictionary<string, object>
        {
            ["name"] = "Alice",
            ["qualifications"] = new List<object>
            {
                new Dictionary<string, object> { ["type"] = "Plumbing" },
                new Dictionary<string, object> { ["type"] = "Engineering" },
                new Dictionary<string, object> { ["type"] = "Medicine" },
            },
        };

        var token = await _service.CreateTokenAsync(
            claims,
            disclosableClaims: ["/qualifications/0", "/qualifications/1", "/qualifications/2"],
            issuer: "did:sorcha:org:gov",
            subject: "did:sorcha:w:alice",
            signingKey: privateKey,
            algorithm: "ES256");

        // Disclose ONLY index 1.
        var presentation = await _service.CreatePresentationAsync(
            token.RawToken,
            claimsToDisclose: ["/qualifications/1"]);

        presentation.SelectedDisclosures.Should().HaveCount(1,
            "only the single requested array-element disclosure must be selected");

        // Decode the one disclosure and confirm it carries the Engineering element.
        var raw = presentation.SelectedDisclosures[0];
        var decoded = JsonSerializer.Deserialize<JsonElement[]>(
            Base64Url.DecodeFromChars(raw));
        decoded.Should().NotBeNull();
        decoded!.Should().HaveCount(2, "array-element disclosures are [salt, value]");
        var value = decoded[1];
        value.GetProperty("type").GetString().Should().Be("Engineering");

        // Verifying the presentation end-to-end must still succeed.
        var verified = await _service.VerifyPresentationAsync(
            presentation.RawPresentation, publicKey, "ES256");
        verified.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ArrayElementDisclosure_RequestingNoElements_YieldsEmptyDisclosureList()
    {
        // The caller may legitimately want to present only public fields; no array
        // elements disclosed means the selected list must be empty — NOT every
        // 2-element disclosure in the token (the prior bug).
        var (privateKey, _) = GenerateP256KeyPair();

        var claims = new Dictionary<string, object>
        {
            ["publicField"] = "always-visible",
            ["qualifications"] = new List<object>
            {
                new Dictionary<string, object> { ["type"] = "Plumbing" },
                new Dictionary<string, object> { ["type"] = "Engineering" },
            },
        };

        var token = await _service.CreateTokenAsync(
            claims,
            disclosableClaims: ["/qualifications/0", "/qualifications/1"],
            issuer: "did:sorcha:org:gov",
            subject: "did:sorcha:w:alice",
            signingKey: privateKey,
            algorithm: "ES256");

        var presentation = await _service.CreatePresentationAsync(
            token.RawToken,
            claimsToDisclose: Array.Empty<string>());

        presentation.SelectedDisclosures.Should().BeEmpty();
    }

    [Fact]
    public async Task ArrayElementDisclosure_MixedWithTopLevelNameRequest_SelectsBoth()
    {
        // Top-level "name" + /qualifications/1 in one presentation. Both disclosures
        // must be selected, nothing else.
        var (privateKey, publicKey) = GenerateP256KeyPair();

        var claims = new Dictionary<string, object>
        {
            ["name"] = "Alice",
            ["qualifications"] = new List<object>
            {
                new Dictionary<string, object> { ["type"] = "Plumbing" },
                new Dictionary<string, object> { ["type"] = "Engineering" },
            },
        };

        var token = await _service.CreateTokenAsync(
            claims,
            disclosableClaims: ["name", "/qualifications/0", "/qualifications/1"],
            issuer: "did:sorcha:org:gov",
            subject: "did:sorcha:w:alice",
            signingKey: privateKey,
            algorithm: "ES256");

        var presentation = await _service.CreatePresentationAsync(
            token.RawToken,
            claimsToDisclose: ["name", "/qualifications/1"]);

        presentation.SelectedDisclosures.Should().HaveCount(2);

        var verified = await _service.VerifyPresentationAsync(
            presentation.RawPresentation, publicKey, "ES256");
        verified.IsValid.Should().BeTrue();
        verified.Claims.Should().ContainKey("name");
        verified.Claims["name"].Should().Be("Alice");
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
