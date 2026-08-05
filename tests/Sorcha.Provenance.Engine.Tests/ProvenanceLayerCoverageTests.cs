// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Reflection;
using FluentAssertions;
using Sorcha.Provenance.Engine;
using Sorcha.Verification.Abstractions;

namespace Sorcha.Provenance.Engine.Tests;

/// <summary>
/// Stops a provenance layer being added and then silently left unchecked.
/// </summary>
/// <remarks>
/// A layer that exists but is never verified is the worst of both worlds: it appears in the trail,
/// occupies a row in the administrator's view, and reports something — while nothing has ever
/// demonstrated that what it reports is true. Adding a member to <see cref="ProvenanceLayer"/>
/// should cost the author a test, and this is what makes it cost one.
/// </remarks>
public class ProvenanceLayerCoverageTests
{
    private static IEnumerable<ProvenanceLayer> AllLayers => Enum.GetValues<ProvenanceLayer>();

    /// <summary>
    /// Every layer must be exercised by at least one test class — matched either by name convention
    /// (<c>SealCheckTests</c> ⇒ <see cref="ProvenanceLayer.Seal"/>) or by an explicit
    /// <see cref="CoversLayerAttribute"/>.
    /// </summary>
    [Fact]
    public void EveryLayer_IsExercisedByAtLeastOneTestClass()
    {
        var testClasses = Assembly.GetExecutingAssembly()
            .GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract)
            .Where(t => t.GetMethods().Any(m => m.GetCustomAttributes<FactAttribute>().Any()))
            .ToList();

        testClasses.Should().NotBeEmpty("this guard is worthless if it cannot find the tests");

        var uncovered = new List<ProvenanceLayer>();

        foreach (var layer in AllLayers)
        {
            var covered = testClasses.Any(t =>
                t.Name.Contains(layer.ToString(), StringComparison.Ordinal) ||
                t.GetCustomAttributes<CoversLayerAttribute>().Any(a => a.Layer == layer));

            if (!covered)
            {
                uncovered.Add(layer);
            }
        }

        uncovered.Should().BeEmpty(
            "every ProvenanceLayer must be exercised by a test. Uncovered: {0}. Add a " +
            "<Layer>CheckTests class, or mark an existing test class with [CoversLayer].",
            string.Join(", ", uncovered));
    }

    /// <summary>
    /// The trail must carry exactly one check per layer — no layer dropped, none duplicated. A
    /// dropped layer would leave a question silently unasked; a duplicated one would let two
    /// different verdicts for the same question sit side by side.
    /// </summary>
    [Fact]
    public void Trail_ContainsExactlyOneCheckPerLayer()
    {
        var trail = new DocketProvenanceVerifier(new StubMerkle()).Verify(
            TestEvidence.RegisterId,
            TestEvidence.HealthyDocket(),
            TestEvidence.Roster(1, TestEvidence.Validator("validator-a", "key-a")),
            TestEvidence.TraceableAnchor());

        trail.Checks.Select(c => c.Layer).Should().BeEquivalentTo(AllLayers);
        trail.Checks.Should().HaveCount(AllLayers.Count());
    }

    /// <summary>
    /// FR-004, stated as a property rather than trusted: no layer may report
    /// <see cref="VerificationStatus.Verified"/> when the evidence it needs is absent. Every layer is
    /// driven with evidence stripped to nothing, and none may claim success.
    /// </summary>
    [Fact]
    public void NoLayer_ReportsVerified_WhenAllEvidenceIsAbsent()
    {
        var trail = new DocketProvenanceVerifier(new StubMerkle()).Verify(
            TestEvidence.RegisterId,
            new Evidence.DocketEvidence
            {
                DocketNumber = 7,
                Hash = "hash-7",
                ClaimedPreviousHash = null,
                PredecessorHash = null,
                TransactionIds = [],
                SealedMerkleRoot = null,
                ProposerValidatorId = null,
                Votes = [],
            },
            rosterAsOf: null,
            anchor: null);

        trail.Checks.Should().AllSatisfy(check =>
        {
            check.Status.Should().NotBe(VerificationStatus.Verified,
                "layer {0} claimed success with no evidence to support it (FR-004)", check.Layer);
            check.Reason.Should().NotBeNullOrWhiteSpace(
                "layer {0} must say which link could not be established (SC-009)", check.Layer);
        });
    }
}
