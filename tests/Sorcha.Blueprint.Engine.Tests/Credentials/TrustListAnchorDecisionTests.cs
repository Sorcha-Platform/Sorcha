// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;

using Sorcha.Blueprint.Engine.Credentials;

namespace Sorcha.Blueprint.Engine.Tests.Credentials;

/// <summary>
/// Covers the trusted-list anchor decision (Feature 181 US3) now that Blueprint Service and HAIP
/// share one implementation instead of a copy each.
/// </summary>
/// <remarks>
/// These are fail-closed rules, which is why the duplication mattered: two copies of a security
/// rule is one copy that can be fixed and one that cannot. Neither service-side adapter had direct
/// coverage of the strict-freshness branch before.
/// </remarks>
public sealed class TrustListAnchorDecisionTests
{
    private static readonly IReadOnlyList<byte[]> Roots = [[0x30, 0x82], [0x30, 0x83]];

    [Fact]
    public void FreshSnapshot_VouchesWithTheSnapshotIdentityAsEvidence()
    {
        var freshness = DateTimeOffset.Parse("2026-07-01T00:00:00Z");

        var result = TrustListAnchorDecision.Evaluate(
            Roots, "eu-lotl", 3, freshness, isStale: false, strictFreshness: false);

        result.Should().NotBeNull();
        // FR-015: this exact shape flows into TrustEvidence.TrustListId.
        result!.AnchorSetId.Should().Be("eu-lotl#3");
        result.Roots.Should().BeEquivalentTo(Roots);
        result.Freshness.Should().Be(freshness);
        result.CheckRevocation.Should().BeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EmptyRoots_FailClosed_RegardlessOfFreshnessMode(bool strict)
    {
        // FR-014 — an imported-but-empty snapshot must vouch for nothing.
        TrustListAnchorDecision.Evaluate([], "eu-lotl", 3, null, isStale: false, strictFreshness: strict)
            .Should().BeNull();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NullRoots_FailClosed_RegardlessOfFreshnessMode(bool strict)
    {
        TrustListAnchorDecision.Evaluate(null, "eu-lotl", 3, null, isStale: false, strictFreshness: strict)
            .Should().BeNull();
    }

    [Fact]
    public void StaleSnapshot_UnderStrictFreshness_FailsClosed()
    {
        // FR-016 strict mode: TRUSTLIST_STALE.
        TrustListAnchorDecision.Evaluate(
            Roots, "eu-lotl", 3, null, isStale: true, strictFreshness: true)
            .Should().BeNull();
    }

    [Fact]
    public void StaleSnapshot_UnderWarnMode_StillVouches_WithEvidenceIntact()
    {
        // FR-016 default mode: flag it, but keep verifying — an expired trusted list is a
        // freshness signal, not grounds to stop trusting every credential it anchors.
        var result = TrustListAnchorDecision.Evaluate(
            Roots, "eu-lotl", 7, null, isStale: true, strictFreshness: false);

        result.Should().NotBeNull();
        result!.AnchorSetId.Should().Be("eu-lotl#7");
    }

    [Fact]
    public void AnchorSetId_UsesTheSequenceNumber_SoTwoImportsAreDistinguishable()
    {
        // Evidence must identify WHICH import vouched, not merely which list.
        var first = TrustListAnchorDecision.Evaluate(Roots, "eu-lotl", 3, null, false, false);
        var second = TrustListAnchorDecision.Evaluate(Roots, "eu-lotl", 4, null, false, false);

        first!.AnchorSetId.Should().NotBe(second!.AnchorSetId);
    }
}
