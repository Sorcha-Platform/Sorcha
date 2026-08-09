// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Services.Implementation;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Enums;

namespace Sorcha.Blueprint.Service.Tests.Projection;

/// <summary>
/// Feature 189 / T055 — governance transactions are deliberately <b>not</b> instance-scoped, and
/// this is the executable record of why, because the obvious "fix" is a one-line change that looks
/// right and is wrong.
/// </summary>
/// <remarks>
/// <para>
/// T055 as written asks for the governance workflow to fold under F145's projector, on the basis
/// that the resolver only lacks an instance id. It does lack one — but supplying it does not produce
/// a correct instance, it produces a confidently wrong one, and the failure is silent.
/// </para>
/// <para>
/// <b>Governance is a star; the instance model is a line.</b> Quorum is many organisations acting on
/// one step, and every approval chains off the same proposal. The platform's instance model refuses
/// that shape in three independent places, none of which is an oversight:
/// </para>
/// <list type="number">
/// <item><description>
/// <c>ValidationEngine</c>'s fork check permits multiple children only when the predecessor is a
/// Control transaction — which is the sole reason sibling approvals seal at all today.
/// </description></item>
/// <item><description>
/// <c>VAL_BP_002</c>'s Tier 3 binding treats the earliest in-instance transaction for a participant
/// role as an immutable binding, so the second organisation approving as <c>voter</c> is a binding
/// violation.
/// </description></item>
/// <item><description>
/// <c>InstanceProjection.OrderByChain</c> builds a <c>Dictionary</c> keyed by predecessor id — one
/// successor per predecessor. Siblings overwrite one another and the losers fall out to the
/// straggler path, so a star cannot even be represented, let alone folded deterministically.
/// </description></item>
/// </list>
/// <para>
/// So R-009's requirement — quorum being a pure function of sealed content that every node agrees on
/// — is met by the tally being pure (<c>GovernanceApprovalTallyTests</c> pins that directly), not by
/// routing governance through a projection whose ordering guarantee it would break.
/// </para>
/// </remarks>
public class GovernanceIsNotInstanceScopedTests
{
    private static readonly IActionResolverService NoActionResolver =
        new Mock<IActionResolverService>().Object;

    /// <summary>
    /// A governance transaction exactly as the Register Service submits one: the governance
    /// blueprint, a numbered action, Control type — and no instance id.
    /// </summary>
    private static TransactionModel GovernanceTx(string? instanceId, RoutingDecision? decision = null) => new()
    {
        TxId = "tx-proposal",
        PrevTxId = "tx-roster-head",
        SenderWallet = "ws-proposer",
        MetaData = new TransactionMetaData
        {
            BlueprintId = GovernanceBlueprint.BlueprintId,
            InstanceId = instanceId,
            ActionId = (uint)GovernanceBlueprint.ProposeChangeActionId,
            TransactionType = TransactionType.Control,
            RoutingDecision = decision,
        },
    };

    [Fact]
    public async Task AGovernanceTransaction_IsNotInstanceScoped_AndDoesNotFold()
    {
        // The current, correct behaviour. Governance state is reconstructed from sealed control
        // transactions by GovernanceRosterService and GovernanceProposalStatus.Derive — not by the
        // instance projection.
        var resolved = await InstanceProjectionResolver.ResolveAsync(
            GovernanceTx(instanceId: null),
            NoActionResolver, NullLogger.Instance, CancellationToken.None);

        resolved.Should().BeNull(
            "governance transactions carry no instance id and are not folded into an instance");
    }

    [Fact]
    public async Task GivingAGovernanceTransactionAnInstanceId_WouldFoldItAsTerminal()
    {
        // This is the trap, executed rather than described. Governance transactions are exempt from
        // VAL_ROUTING_*, so they carry no RoutingDecision — and the resolver documents that a
        // transaction with no decision "contributes a terminal (empty) next-action set".
        //
        // The consequence of "just add an instance id": the PROPOSAL folds as a terminal, so the
        // instance reports Completed at the moment a governance change is raised — before a single
        // approval, and before the enactment that actually changes the roster. Nothing errors. It is
        // the same latent defect F145 US6 found for presentation lifecycle transactions, which is
        // why the resolver skips those when they carry no decision.
        var resolved = await InstanceProjectionResolver.ResolveAsync(
            GovernanceTx(instanceId: "would-be-governance-instance"),
            NoActionResolver, NullLogger.Instance, CancellationToken.None);

        resolved.Should().NotBeNull("the resolver keys only on the three metadata fields being present");
        resolved!.Tx.NextActionIds.Should().BeEmpty(
            "no RoutingDecision means an empty next-action set");

        var instance = InstanceProjection.Project(
            resolved.InstanceId, "reg-1", GovernanceBlueprint.BlueprintId,
            GovernanceBlueprint.ProposeChangeActionId, "tenant", [resolved.Tx]);

        instance.Should().NotBeNull();
        instance!.State.Should().Be(InstanceState.Completed,
            "a proposal would be reported as a COMPLETED workflow the moment it was raised — which "
            + "is why T055 is not delivered by supplying an instance id");
    }

    [Fact]
    public void TheProjectionCannotRepresentSiblings_WhichIsWhatQuorumIs()
    {
        // The structural reason, independent of routing decisions. Three approvals, all chaining off
        // the same proposal, exactly as GovernanceApprovalActionSubmitter builds them.
        var siblings = new[] { "tx-approval-a", "tx-approval-b", "tx-approval-c" }
            .Select(id => new ProjectedTransaction(
                TxId: id,
                PreviousTransactionId: "tx-proposal",
                CompletedActionId: GovernanceBlueprint.CollectQuorumActionId,
                NextActionIds: [],
                ParticipantBindings: new Dictionary<string, string>()))
            .ToList();

        var root = new ProjectedTransaction(
            TxId: "tx-proposal",
            PreviousTransactionId: null,
            CompletedActionId: GovernanceBlueprint.ProposeChangeActionId,
            NextActionIds: [GovernanceBlueprint.CollectQuorumActionId],
            ParticipantBindings: new Dictionary<string, string>());

        // Fold the same set twice, presented in opposite orders. A projection that could represent a
        // star would reach the same LastAppliedTxId either way — that is precisely the determinism
        // guarantee F145 makes and every node relies on.
        var forwards = InstanceProjection.Project(
            "inst", "reg-1", GovernanceBlueprint.BlueprintId, 1, "tenant",
            [root, .. siblings]);

        var backwards = InstanceProjection.Project(
            "inst", "reg-1", GovernanceBlueprint.BlueprintId, 1, "tenant",
            [root, .. siblings.AsEnumerable().Reverse()]);

        forwards.Should().NotBeNull();
        backwards.Should().NotBeNull();

        forwards!.LastAppliedTxId.Should().NotBe(backwards!.LastAppliedTxId,
            "OrderByChain keys successors by predecessor id in a dictionary, so only one sibling is "
            + "reachable through the chain and the rest are appended as stragglers — the fold order, "
            + "and so the final watermark, depends on input order. This assertion is deliberately "
            + "pinning the LIMITATION: if it ever fails because the projection learned to represent "
            + "branching, revisit T055, because the reason it was not delivered has gone away.");
    }
}
