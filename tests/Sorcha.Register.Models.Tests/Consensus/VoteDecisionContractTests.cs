// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Register.Models;

namespace Sorcha.Register.Models.Tests.Consensus;

/// <summary>
/// Pins <see cref="VoteDecision"/>'s wire values. These are a persisted ledger contract: consensus
/// votes are written to the register, so a renumbering silently reinterprets already-sealed dockets.
/// </summary>
/// <remarks>
/// <para>
/// This enum was previously declared twice with <b>incompatible</b> values —
/// <c>Validator.Core</c> had <c>Approve=1, Reject=2, Abstain=3</c> while <c>Validator.Service</c> had
/// <c>Reject=0, Approve=1</c> and no Abstain — in two assemblies that reference each other. A value
/// crossing between them numerically turned a Reject into something else, silently. Feature 187
/// (#1371) consolidated them here.
/// </para>
/// <para>
/// Re-declaration elsewhere is blocked by <c>scripts/check-consensus-vote-contract.ps1</c>; this test
/// blocks the other half of the problem — someone changing a value in the one remaining declaration.
/// </para>
/// </remarks>
public class VoteDecisionContractTests
{
    [Theory]
    [InlineData(VoteDecision.Unspecified, 0)]
    [InlineData(VoteDecision.Approve, 1)]
    [InlineData(VoteDecision.Reject, 2)]
    [InlineData(VoteDecision.Abstain, 3)]
    public void VoteDecision_WireValue_IsPinned(VoteDecision member, int expected)
    {
        ((int)member).Should().Be(expected,
            "consensus votes are persisted to the register — renumbering this member silently " +
            "reinterprets every already-sealed docket that carries a vote");
    }

    [Fact]
    public void VoteDecision_ZeroIsUnspecified_SoAnUninitialisedVoteIsNeverARealDecision()
    {
        // The load-bearing property: default(VoteDecision) must not be a castable-to-meaning vote.
        // Under the retired Validator.Service numbering, default was Reject — so an uninitialised or
        // malformed vote read as a real rejection.
        default(VoteDecision).Should().Be(VoteDecision.Unspecified);
        ((int)VoteDecision.Unspecified).Should().Be(0);
    }

    [Fact]
    public void VoteDecision_DeclaresExactlyTheExpectedMembers()
    {
        // A new member is additive and safe; this fails on an unexpected addition purely so the wire
        // contract above is extended deliberately rather than by accident.
        Enum.GetNames<VoteDecision>().Should().BeEquivalentTo(
            [nameof(VoteDecision.Unspecified),
             nameof(VoteDecision.Approve),
             nameof(VoteDecision.Reject),
             nameof(VoteDecision.Abstain)]);
    }
}
