// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Buffers.Text;
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
}
