// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Provenance.Engine;
using Sorcha.Verification.Abstractions;

namespace Sorcha.Provenance.Engine.Tests;

/// <summary>
/// The trail's shape, independent of what any individual check concludes.
/// </summary>
public class DocketProvenanceVerifierOrderingTests
{
    private static ProvenanceTrail Verify() =>
        new DocketProvenanceVerifier(new StubMerkle()).Verify(
            TestEvidence.RegisterId,
            TestEvidence.HealthyDocket(),
            TestEvidence.Roster(1, TestEvidence.Validator("validator-a", "key-a")),
            TestEvidence.TraceableAnchor());

    [Fact]
    public void Verify_ReturnsOneCheckPerLayer_BroadestFirst()
    {
        var trail = Verify();

        trail.Checks.Select(c => c.Layer).Should().Equal(
            ProvenanceLayer.Anchor,
            ProvenanceLayer.Chain,
            ProvenanceLayer.Seal,
            ProvenanceLayer.Signers,
            ProvenanceLayer.Proposer);
    }

    [Fact]
    public void Verify_CarriesTheSubjectIdentity()
    {
        var trail = Verify();

        trail.RegisterId.Should().Be(TestEvidence.RegisterId);
        trail.DocketNumber.Should().Be(10);
    }

    /// <summary>
    /// FR-002: a result whose basis the reader cannot restate is an assertion, not evidence. This
    /// holds for every status — an <see cref="VerificationStatus.Unverified"/> row still has to say
    /// what it would have compared against.
    /// </summary>
    [Fact]
    public void Verify_EveryCheckStatesWhatItWasComparedAgainst()
    {
        var trail = Verify();

        trail.Checks.Should().AllSatisfy(check =>
        {
            check.CheckedAgainst.Should().NotBeNullOrWhiteSpace(
                "check {0} must state its basis (FR-002)", check.Layer);
            check.Headline.Should().NotBeNullOrWhiteSpace(
                "check {0} must give the reader one line they can act on", check.Layer);
        });
    }

    /// <summary>
    /// FR-003: an <see cref="VerificationStatus.Unverified"/> result is only useful if it says which
    /// link could not be established (SC-009). A bare "unverified" tells an auditor nothing.
    /// </summary>
    [Fact]
    public void Verify_EveryUnverifiedCheckCarriesAReason()
    {
        var trail = Verify();

        trail.Checks
            .Where(c => c.Status == VerificationStatus.Unverified)
            .Should().AllSatisfy(check => check.Reason.Should().NotBeNullOrWhiteSpace(
                "unverified check {0} must name what stopped it (FR-003, SC-009)", check.Layer));
    }
}
