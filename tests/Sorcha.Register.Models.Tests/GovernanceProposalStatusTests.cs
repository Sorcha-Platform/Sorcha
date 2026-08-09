// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using FluentAssertions;
using Sorcha.Register.Models;
using Xunit;

namespace Sorcha.Register.Models.Tests;

/// <summary>
/// A proposal's status is DERIVED from sealed content, never stored (T043/T046).
/// </summary>
/// <remarks>
/// <para>
/// A stored status would be a second source of truth about a fact the ledger already carries, and it
/// would drift the moment one node folds a docket the writer never saw — which is the normal case in
/// a federated deployment, not an edge one. Everything below is a pure function of the proposal's own
/// operation, the register's current roster head, whether an enactment exists, and the clock.
/// </para>
/// <para>
/// Which is why this lives in the zero-dependency leaf: the Validator, the Register Service, the CLI
/// and a console all have to agree on what a proposal's status is, and the way they disagree is by
/// each deriving it themselves.
/// </para>
/// </remarks>
public sealed class GovernanceProposalStatusTests
{
    private const string ProposalTx = "proposal-tx";
    private const string GenesisHead = "genesis-tx";
    private const string EnactmentTx = "enactment-tx";

    private static readonly DateTimeOffset Now = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

    private static GovernanceOperation Pending(DateTimeOffset? expiresAt = null) => new()
    {
        OperationType = GovernanceOperationType.Add,
        ProposerDid = "did:sorcha:w:ws11qadmin",
        TargetDid = "did:sorcha:w:ws11qnew",
        TargetRole = RegisterRole.Admin,
        Status = ProposalStatus.Pending,
        ProposedAt = Now.AddHours(-2),
        ExpiresAt = expiresAt ?? Now.AddDays(7),
        RosterSnapshotId = GenesisHead,
    };

    [Fact]
    public void APendingProposalOnTheCurrentRoster_IsOpen()
    {
        var outcome = GovernanceProposalStatus.Derive(
            Pending(), ProposalTx, GenesisHead, enactmentTxId: null, Now);

        outcome.State.Should().Be(GovernanceProposalState.Open);
        outcome.Reason.Should().Be(GovernanceProposalStateReason.None);
        outcome.OutcomeTxId.Should().BeNull("nothing has happened to it yet");
    }

    [Fact]
    public void AProposalWithAnEnactment_IsEnacted()
    {
        var outcome = GovernanceProposalStatus.Derive(
            Pending(), ProposalTx, GenesisHead, EnactmentTx, Now);

        outcome.State.Should().Be(GovernanceProposalState.Enacted);
        outcome.Reason.Should().Be(GovernanceProposalStateReason.QuorumMet);
        outcome.OutcomeTxId.Should().Be(EnactmentTx);
    }

    /// <summary>
    /// The Owner override writes ONE propose-and-enact transaction, so the proposal IS its own
    /// enactment and there is no separate transaction to find.
    /// </summary>
    [Fact]
    public void AnOwnerOverrideProposeAndEnact_IsEnactedByItself()
    {
        var recorded = Pending();
        recorded.Status = ProposalStatus.Recorded;

        var outcome = GovernanceProposalStatus.Derive(
            recorded, ProposalTx, GenesisHead, enactmentTxId: null, Now);

        outcome.State.Should().Be(GovernanceProposalState.Enacted);
        outcome.OutcomeTxId.Should().Be(ProposalTx,
            "the propose-and-enact transaction is its own outcome");
    }

    [Fact]
    public void AProposalWhoseRosterMovedUnderIt_IsInvalidated()
    {
        var outcome = GovernanceProposalStatus.Derive(
            Pending(), ProposalTx, "some-later-control-tx", enactmentTxId: null, Now);

        outcome.State.Should().Be(GovernanceProposalState.Invalidated);
        outcome.Reason.Should().Be(GovernanceProposalStateReason.RosterChanged);
    }

    [Fact]
    public void AProposalPastItsWindow_IsExpired()
    {
        var outcome = GovernanceProposalStatus.Derive(
            Pending(expiresAt: Now.AddSeconds(-1)), ProposalTx, GenesisHead, enactmentTxId: null, Now);

        outcome.State.Should().Be(GovernanceProposalState.Expired);
        outcome.Reason.Should().Be(GovernanceProposalStateReason.Expired);
    }

    /// <summary>
    /// <b>Enacted outranks everything.</b> Once an enactment is sealed the change has happened, and no
    /// later expiry or roster movement can un-happen it.
    /// </summary>
    /// <remarks>
    /// Checking expiry first would make every enacted proposal silently re-read as Expired once its
    /// window passed — a surface that contradicts the ledger it is reporting, and one that would look
    /// correct for as long as anybody tested it inside the window.
    /// </remarks>
    [Fact]
    public void AnEnactedProposal_StaysEnactedAfterItsWindowPasses()
    {
        var outcome = GovernanceProposalStatus.Derive(
            Pending(expiresAt: Now.AddDays(-30)), ProposalTx, GenesisHead, EnactmentTx, Now);

        outcome.State.Should().Be(GovernanceProposalState.Enacted);
    }

    [Fact]
    public void AnEnactedProposal_StaysEnactedAfterTheRosterMovesOn()
    {
        // The enactment IS a roster change, so the head has ALWAYS moved past an enacted proposal's
        // snapshot. Ordering invalidation first would report every enacted proposal as Invalidated.
        var outcome = GovernanceProposalStatus.Derive(
            Pending(), ProposalTx, "the-enactment-itself", EnactmentTx, Now);

        outcome.State.Should().Be(GovernanceProposalState.Enacted);
    }

    /// <summary>
    /// Invalidation is reported ahead of expiry because it is the condition that refuses an approval
    /// even inside the window — it is the binding constraint, and the one FR-011b enforces.
    /// </summary>
    [Fact]
    public void AProposalBothExpiredAndInvalidated_ReportsTheRosterChange()
    {
        var outcome = GovernanceProposalStatus.Derive(
            Pending(expiresAt: Now.AddSeconds(-1)), ProposalTx, "moved-on", enactmentTxId: null, Now);

        outcome.State.Should().Be(GovernanceProposalState.Invalidated);
        outcome.Reason.Should().Be(GovernanceProposalStateReason.RosterChanged);
    }

    /// <summary>
    /// An unset window must not read as "expired at the epoch" — that would report EVERY proposal as
    /// expired, which is the failure mode of treating <c>default(DateTimeOffset)</c> as a date.
    /// </summary>
    [Fact]
    public void AProposalWithNoWindow_IsOpen()
    {
        var operation = Pending();
        operation.ExpiresAt = default;

        var outcome = GovernanceProposalStatus.Derive(
            operation, ProposalTx, GenesisHead, enactmentTxId: null, Now);

        outcome.State.Should().Be(GovernanceProposalState.Open);
    }

    /// <summary>
    /// A proposal that captured no snapshot cannot be judged against the current head, so it is not
    /// invalidated by one. Same carve-out the enactment path applies.
    /// </summary>
    [Fact]
    public void AProposalWithNoRosterSnapshot_IsNotInvalidated()
    {
        var operation = Pending();
        operation.RosterSnapshotId = null;

        var outcome = GovernanceProposalStatus.Derive(
            operation, ProposalTx, "any-head-at-all", enactmentTxId: null, Now);

        outcome.State.Should().Be(GovernanceProposalState.Open);
    }

    /// <summary>
    /// Every terminal state carries a reason — nothing reaches an operator as a bare status with no
    /// explanation of how it got there (FR-011c).
    /// </summary>
    [Theory]
    [InlineData(GovernanceProposalState.Enacted)]
    [InlineData(GovernanceProposalState.Invalidated)]
    [InlineData(GovernanceProposalState.Expired)]
    public void EveryTerminalState_CarriesAReason(GovernanceProposalState terminal)
    {
        var outcome = terminal switch
        {
            GovernanceProposalState.Enacted =>
                GovernanceProposalStatus.Derive(Pending(), ProposalTx, GenesisHead, EnactmentTx, Now),
            GovernanceProposalState.Invalidated =>
                GovernanceProposalStatus.Derive(Pending(), ProposalTx, "moved", null, Now),
            _ => GovernanceProposalStatus.Derive(
                Pending(expiresAt: Now.AddSeconds(-1)), ProposalTx, GenesisHead, null, Now),
        };

        outcome.State.Should().Be(terminal);
        outcome.Reason.Should().NotBe(GovernanceProposalStateReason.None);
    }
}
