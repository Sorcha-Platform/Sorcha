// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Register.Models;
using Xunit;

namespace Sorcha.Register.Models.Tests.Governance;

/// <summary>Feature 197 — the resolution table in contracts/governance-definition-pin.md.</summary>
public class GovernanceDefinitionPinTests
{
    private const int Propose = GovernanceBlueprint.ProposeChangeActionId;
    private const int Approve = GovernanceBlueprint.CollectQuorumActionId;
    private const int Enact = GovernanceBlueprint.RecordControlTransactionActionId;

    private static readonly string PinA = new('a', 64);
    private static readonly string PinB = new('b', 64);

    private static ControlTransactionPayload Payload(string? pin = null, string? enacts = null) =>
        new() { GovernanceDefinitionTxId = pin, EnactsProposalId = enacts };

    [Fact]
    public void Resolve_ProposalWithOwnPin_PinnedAsRaise()
    {
        GovernanceDefinitionPin.Resolve(Propose, Payload(PinA), null, true)
            .Should().Be(new PinResolution.Pinned(PinA, true));
    }

    [Fact]
    public void Resolve_OwnerOverrideWithOwnPinAndRoster_PinnedAsRaise()
    {
        var own = new ControlTransactionPayload
        {
            GovernanceDefinitionTxId = PinA,
            EnactsProposalId = null,
            Roster = new RegisterControlRecord(),
        };

        // Even on the enactment action id, and with an unrelated proposal supplied.
        GovernanceDefinitionPin.Resolve(Enact, own, Payload(PinB), true)
            .Should().Be(new PinResolution.Pinned(PinA, true));
    }

    [Fact]
    public void Resolve_OwnPinAndReference_OwnPinWins()
    {
        GovernanceDefinitionPin.Resolve(Propose, Payload(PinA), Payload(PinB), true)
            .Should().Be(new PinResolution.Pinned(PinA, true));
    }

    [Fact]
    public void Resolve_ProposalWithoutPin_LegacyProposal()
    {
        GovernanceDefinitionPin.Resolve(Propose, Payload(), null, true)
            .Should().Be(new PinResolution.Legacy("proposal"));
    }

    [Fact]
    public void Resolve_ApprovalOfPinnedProposal_PinnedNotRaise()
    {
        GovernanceDefinitionPin.Resolve(Approve, null, Payload(PinA), true)
            .Should().Be(new PinResolution.Pinned(PinA, false));
    }

    [Fact]
    public void Resolve_ApprovalOfUnpinnedProposal_LegacyApproval()
    {
        GovernanceDefinitionPin.Resolve(Approve, null, Payload(), true)
            .Should().Be(new PinResolution.Legacy("approval"));
    }

    [Fact]
    public void Resolve_ApprovalProposalUnreadable_Unresolvable()
    {
        GovernanceDefinitionPin.Resolve(Approve, null, null, false)
            .Should().BeOfType<PinResolution.Unresolvable>();
    }

    [Fact]
    public void Resolve_ApprovalUnreadableFlagEvenWithProposalSupplied_Unresolvable()
    {
        GovernanceDefinitionPin.Resolve(Approve, null, Payload(), false)
            .Should().BeOfType<PinResolution.Unresolvable>();
    }

    [Fact]
    public void Resolve_ApprovalReadableButNull_Unresolvable()
    {
        GovernanceDefinitionPin.Resolve(Approve, null, null, true)
            .Should().BeOfType<PinResolution.Unresolvable>();
    }

    [Fact]
    public void Resolve_EnactmentOfPinnedProposal_PinnedNotRaise()
    {
        GovernanceDefinitionPin.Resolve(Enact, Payload(enacts: "p1"), Payload(PinA), true)
            .Should().Be(new PinResolution.Pinned(PinA, false));
    }

    [Fact]
    public void Resolve_EnactmentOfUnpinnedProposal_LegacyEnactment()
    {
        GovernanceDefinitionPin.Resolve(Enact, Payload(enacts: "p1"), Payload(), true)
            .Should().Be(new PinResolution.Legacy("enactment"));
    }

    [Fact]
    public void Resolve_EnactmentProposalUnreadable_Unresolvable()
    {
        GovernanceDefinitionPin.Resolve(Enact, Payload(enacts: "p1"), null, false)
            .Should().BeOfType<PinResolution.Unresolvable>();
    }

    [Fact]
    public void Resolve_EnactsProposalIdOnOtherAction_TreatedAsEnactment()
    {
        GovernanceDefinitionPin.Resolve(99, Payload(enacts: "p1"), Payload(PinA), true)
            .Should().Be(new PinResolution.Pinned(PinA, false));
    }

    [Fact]
    public void Resolve_UnknownAction_UnresolvableFailsClosed()
    {
        GovernanceDefinitionPin.Resolve(99, null, null, true)
            .Should().Be(new PinResolution.Unresolvable("not a governance step"));
    }
}
