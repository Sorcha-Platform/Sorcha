// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace Sorcha.Verifier.Tests.Services;

/// <summary>
/// #1195 Phase 2 (Task 6 fix round) — RFC 9901 disclosure anchoring. Every disclosure
/// segment in a presented SD-JWT MUST be committed by the credential: its SHA-256 digest
/// must appear in an <c>_sd</c> array of the (issuer-signed) payload, or of an
/// already-accepted disclosure's value (nested SD). Without this check a presenter could
/// append fabricated <c>[salt, name, value]</c> segments — or OVERRIDE a legitimate
/// claim's value with a forged duplicate — and have them emitted as verified claims.
/// The KB-JWT does not protect against this (its signer IS the presenter).
/// </summary>
public sealed class DisclosureAnchoringTests
{
    private const string Vct = VpValidatorTestHarness.Vct;

    private static string ForgedDisclosure(string name, string value) =>
        Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(
            new object[] { "forged-salt", name, value }));

    /// <summary>Insert a disclosure segment immediately before the trailing KB-JWT.</summary>
    private static string InjectDisclosure(string vpToken, string segment)
    {
        var lastTilde = vpToken.LastIndexOf('~');
        return vpToken[..(lastTilde + 1)] + segment + "~" + vpToken[(lastTilde + 1)..];
    }

    [Fact]
    public async Task ValidateAsync_ForgedAppendedDisclosure_IsRejected_WithNamedError()
    {
        // A legitimate presentation with a fabricated extra claim appended: the forged
        // segment's digest appears in no _sd array, so the presentation must be REJECTED
        // (RFC 9901 — an unanchored disclosure invalidates the SD-JWT), never silently
        // accepted with the forged claim in the disclosed set.
        var bundle = TestVpFactory.Mint(Vct,
            VpValidatorTestHarness.Claims(("givenName", "Stuart")),
            VpValidatorTestHarness.ClientId, VpValidatorTestHarness.Nonce);
        var tampered = InjectDisclosure(bundle.VpToken, ForgedDisclosure("email", "attacker@evil.example"));

        var validator = VpValidatorTestHarness.BuildValidator();
        var outcome = await validator.ValidateAsync(
            VpValidatorTestHarness.Session(), tampered, bundle.Delegation);

        outcome.Accepted.Should().BeFalse("a disclosure the issuer never committed must invalidate the presentation");
        outcome.Errors.Should().Contain(e => e.Contains("email") && e.Contains("not committed", StringComparison.OrdinalIgnoreCase),
            "the refusal must NAME the forged claim — never silent");
        outcome.DisclosedClaims.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_ForgedDuplicateOfLegitimateClaim_IsRejected_NotOverridden()
    {
        // The value-override attack: the credential legitimately discloses givenName, and the
        // presenter appends a SECOND, forged givenName segment. Dictionary parse order would let
        // the forged value win — anchoring must reject the presentation outright instead.
        var bundle = TestVpFactory.Mint(Vct,
            VpValidatorTestHarness.Claims(("givenName", "Stuart")),
            VpValidatorTestHarness.ClientId, VpValidatorTestHarness.Nonce);
        var tampered = InjectDisclosure(bundle.VpToken, ForgedDisclosure("givenName", "Mallory"));

        var validator = VpValidatorTestHarness.BuildValidator();
        var outcome = await validator.ValidateAsync(
            VpValidatorTestHarness.Session(), tampered, bundle.Delegation);

        outcome.Accepted.Should().BeFalse("a forged duplicate must never override the issuer-committed value");
        outcome.Errors.Should().Contain(e => e.Contains("givenName") && e.Contains("not committed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ValidateAsync_AllDisclosuresAnchored_StillAccepts()
    {
        // Regression guard: the untampered fixture (digest-anchored per RFC 9901 by
        // TestVpFactory) continues to verify after the anchoring check lands.
        var bundle = TestVpFactory.Mint(Vct,
            VpValidatorTestHarness.Claims(("givenName", "Stuart"), ("familyName", "Fraser")),
            VpValidatorTestHarness.ClientId, VpValidatorTestHarness.Nonce);

        var validator = VpValidatorTestHarness.BuildValidator();
        var outcome = await validator.ValidateAsync(
            VpValidatorTestHarness.Session(), bundle.VpToken, bundle.Delegation);

        outcome.Accepted.Should().BeTrue(string.Join(", ", outcome.Errors));
        outcome.DisclosedClaims.Should().ContainKeys("givenName", "familyName");
    }

    // ── Fix round 2: RFC 9901 anchoring completeness ─────────────────────────────

    /// <summary>Mint an RFC 9901 array-element disclosure: base64url(JSON [salt, value]). Returns (segment, digest).</summary>
    private static (string Segment, string Digest) MintArrayElementDisclosure(string salt, string value)
    {
        var segment = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(
            new object[] { salt, value }));
        var digest = Base64Url.EncodeToString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(segment)));
        return (segment, digest);
    }

    [Fact]
    public async Task ValidateAsync_LegitimateArrayElementDisclosure_AnchorsAndVerifies()
    {
        // RFC 9901 array SD: the payload carries an array whose selectively-disclosable
        // element is the {"...": "<digest>"} marker; the matching 2-element [salt, value]
        // disclosure rides in the vp_token. A legitimate credential using this shape must
        // anchor cleanly — reporting it as "unanchored" (forgery) was the fix-round-2 gap.
        var (segment, digest) = MintArrayElementDisclosure("elem-salt-1", "GB");
        var bundle = TestVpFactory.Mint(Vct,
            VpValidatorTestHarness.Claims(("givenName", "Stuart")),
            VpValidatorTestHarness.ClientId, VpValidatorTestHarness.Nonce,
            extraPayloadClaims: new Dictionary<string, object>
            {
                ["nationalities"] = new object[]
                {
                    new Dictionary<string, object> { ["..."] = digest },
                    "IE", // non-disclosable sibling element stays inline
                },
            },
            extraDisclosureSegments: [segment]);

        var validator = VpValidatorTestHarness.BuildValidator();
        var outcome = await validator.ValidateAsync(
            VpValidatorTestHarness.Session(), bundle.VpToken, bundle.Delegation);

        outcome.Accepted.Should().BeTrue(
            "an issuer-committed array-element disclosure is legitimate, not forgery: " +
            string.Join(", ", outcome.Errors));
        outcome.DisclosedClaims.Should().ContainKey("givenName");
    }

    [Fact]
    public async Task ValidateAsync_ForgedArrayElementDisclosure_IsRejected_WithNamedError()
    {
        // A 2-element [salt, value] disclosure whose digest appears in NO marker and no _sd
        // array is still a forgery and must reject, naming the segment.
        var (forgedSegment, _) = MintArrayElementDisclosure("forged-elem-salt", "attacker-value");
        var bundle = TestVpFactory.Mint(Vct,
            VpValidatorTestHarness.Claims(("givenName", "Stuart")),
            VpValidatorTestHarness.ClientId, VpValidatorTestHarness.Nonce,
            extraDisclosureSegments: [forgedSegment]);

        var validator = VpValidatorTestHarness.BuildValidator();
        var outcome = await validator.ValidateAsync(
            VpValidatorTestHarness.Session(), bundle.VpToken, bundle.Delegation);

        outcome.Accepted.Should().BeFalse("an uncommitted array-element disclosure is a forgery");
        outcome.Errors.Should().Contain(e =>
                e.Contains("not committed", StringComparison.OrdinalIgnoreCase) &&
                e.Contains("forged-elem-salt"),
            "the refusal must identify the forged segment — never silent");
    }

    [Fact]
    public async Task ValidateAsync_UnsupportedSdAlg_RejectsWithDistinctNamedError_NotUnanchored()
    {
        // _sd_alg other than sha-256: digests computed under an unknown algorithm can never
        // anchor here — but calling that "unanchored disclosure" would accuse a legitimate
        // credential of forgery. The rejection must be its own named reason.
        var bundle = TestVpFactory.Mint(Vct,
            VpValidatorTestHarness.Claims(("givenName", "Stuart")),
            VpValidatorTestHarness.ClientId, VpValidatorTestHarness.Nonce,
            extraPayloadClaims: new Dictionary<string, object> { ["_sd_alg"] = "sha-384" });

        var validator = VpValidatorTestHarness.BuildValidator();
        var outcome = await validator.ValidateAsync(
            VpValidatorTestHarness.Session(), bundle.VpToken, bundle.Delegation);

        outcome.Accepted.Should().BeFalse("sha-256 is the only supported _sd_alg — fail closed");
        outcome.Errors.Should().Contain(e =>
                e.Contains("_sd_alg", StringComparison.OrdinalIgnoreCase) &&
                e.Contains("sha-384"),
            "the error must name the unsupported algorithm");
        outcome.Errors.Should().NotContain(e => e.Contains("not committed", StringComparison.OrdinalIgnoreCase),
            "an unsupported algorithm must NOT be misclassified as a forged/unanchored disclosure");
    }
}
