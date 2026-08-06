// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Register.Models;
using Xunit;

namespace Sorcha.Register.Models.Tests;

/// <summary>
/// Feature 189 / R-003 — governance keys are compared by decoded bytes, never as encoded strings.
/// </summary>
/// <remarks>
/// The motivating defect: roster attestations store standard padded base64, while the Validator
/// compared against unpadded base64url. The two can never be equal for a key requiring padding or
/// containing '+' or '/', so a correct key failed to match and the transaction was rejected
/// "submitter not found in roster" — indistinguishable from the separate signer defect.
/// </remarks>
public class GovernanceKeyMatcherTests
{
    /// <summary>
    /// An Owner attestation public key read from a live n1 register on 2026-08-06. Standard base64:
    /// note the trailing '=' padding. This exact value is why string comparison could not work.
    /// </summary>
    private const string LiveRosterKeyBase64 = "uS780HTirYETFCiao2cn5mWe2bQ7EQCkuPxNfpkpyYc=";

    private static byte[] LiveRosterKeyBytes => Convert.FromBase64String(LiveRosterKeyBase64);

    [Fact]
    public void Matches_PaddedBase64_AgainstUnpaddedBase64Url_SameKey_Matches()
    {
        // The precise failure that shipped: the roster's padded base64 vs the encoding the
        // Validator produced. These are the same key and MUST match.
        var base64Url = Convert.ToBase64String(LiveRosterKeyBytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        GovernanceKeyMatcher.Matches(LiveRosterKeyBase64, base64Url).Should().BeTrue();
    }

    [Fact]
    public void Matches_EncodedAgainstRawBytes_SameKey_Matches()
    {
        // A transaction signature carries raw bytes; the roster carries an encoded string.
        GovernanceKeyMatcher.Matches(LiveRosterKeyBase64, LiveRosterKeyBytes).Should().BeTrue();
    }

    [Theory]
    [InlineData("uS780HTirYETFCiao2cn5mWe2bQ7EQCkuPxNfpkpyYc=")]   // base64, padded
    [InlineData("uS780HTirYETFCiao2cn5mWe2bQ7EQCkuPxNfpkpyYc")]    // base64, unpadded
    public void TryDecode_AcceptsEveryEncodingTheplatformStores(string encoded)
    {
        GovernanceKeyMatcher.TryDecode(encoded, out var bytes).Should().BeTrue();
        bytes.Should().Equal(LiveRosterKeyBytes);
    }

    [Fact]
    public void TryDecode_Base64UrlAlphabet_DecodesToTheSameBytes()
    {
        var base64Url = Convert.ToBase64String(LiveRosterKeyBytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        GovernanceKeyMatcher.TryDecode(base64Url, out var bytes).Should().BeTrue();
        bytes.Should().Equal(LiveRosterKeyBytes);
    }

    [Fact]
    public void Matches_DifferentKeys_DoesNotMatch()
    {
        var other = new byte[LiveRosterKeyBytes.Length];
        Array.Copy(LiveRosterKeyBytes, other, other.Length);
        other[0] ^= 0xFF;

        GovernanceKeyMatcher.Matches(LiveRosterKeyBase64, other).Should().BeFalse();
    }

    [Fact]
    public void Matches_KeysOfDifferentLength_DoNotMatch()
    {
        // A prefix of a valid key must never authorise.
        var truncated = LiveRosterKeyBytes[..16];

        GovernanceKeyMatcher.Matches(LiveRosterKeyBase64, truncated).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not base64 at all !!")]
    [InlineData("AAAAA")]   // length % 4 == 1 — impossible under any padding rule
    public void TryDecode_Undecodable_ReturnsFalse(string? encoded)
    {
        GovernanceKeyMatcher.TryDecode(encoded, out var bytes).Should().BeFalse();
        bytes.Should().BeEmpty();
    }

    [Fact]
    public void Matches_UndecodableInput_IsNeverAMatch()
    {
        // Fail closed: an unparseable key must not authorise anything, and must not throw.
        // Casts are required: a bare `null` binds ambiguously between the string and span overloads.
        GovernanceKeyMatcher.Matches("not base64 at all !!", LiveRosterKeyBytes).Should().BeFalse();
        GovernanceKeyMatcher.Matches((string?)null, LiveRosterKeyBytes).Should().BeFalse();
        GovernanceKeyMatcher.Matches((string?)null, (string?)null).Should().BeFalse();
    }
}
