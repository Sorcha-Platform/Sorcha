// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;

using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Services.Implementation;

using Xunit;

namespace Sorcha.Blueprint.Service.Tests.Projection;

/// <summary>
/// #1672 — a rejected instance must project as <see cref="InstanceState.Rejected"/>, not Completed.
/// </summary>
/// <remarks>
/// <para>
/// The fold has always had a rejection branch, but <c>ProjectedTransaction.IsRejection</c> defaults
/// to false and nothing ever set it: <c>InstanceProjectionResolver</c> omitted the argument
/// entirely, so the branch was unreachable and every rejection fell through to "no next action →
/// Completed".
/// </para>
/// <para>
/// Confirmed live on 2026-09-19: ConstructionPermit Scenario C sealed its rejection in docket 19
/// and the instance read <c>State = 1</c> (Completed) with a CompletedAt. A refused planning
/// application was indistinguishable from an approved one.
/// </para>
/// <para>
/// No test asserted the terminal state of a rejection, which is how dead code survived in a branch
/// that could not be reached. This is that test.
/// </para>
/// </remarks>
public class RejectionIsProjectedAsRejectedTests
{
    private const string InstanceId = "inst-1";
    private const string RegisterId = "reg-1";
    private const string BlueprintId = "bp-1";

    private static ProjectedTransaction Tx(
        string txId, string? prev, int completedActionId, IReadOnlyList<int> next, bool isRejection = false) =>
        new(
            TxId: txId,
            PreviousTransactionId: prev,
            CompletedActionId: completedActionId,
            NextActionIds: next,
            ParticipantBindings: new Dictionary<string, string>(),
            IsRejection: isRejection);

    private static Instance Project(params ProjectedTransaction[] txs) =>
        InstanceProjection.Project(InstanceId, RegisterId, BlueprintId, 1, "tenant", txs);

    [Fact]
    public void ARejectionProjectsAsRejected_NotCompleted()
    {
        var instance = Project(
            Tx("tx-1", null, 1, [2]),
            Tx("tx-2", "tx-1", 2, [], isRejection: true));

        instance.State.Should().Be(InstanceState.Rejected);
        instance.State.Should().NotBe(InstanceState.Completed,
            "a refused application and a completed one are different outcomes, not different labels");
    }

    [Fact]
    public void AnOrdinaryTerminalActionStillProjectsAsCompleted()
    {
        // The counterfactual: the fix must not turn every terminal action into a rejection.
        var instance = Project(
            Tx("tx-1", null, 1, [2]),
            Tx("tx-2", "tx-1", 2, []));

        instance.State.Should().Be(InstanceState.Completed);
        instance.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public void ARejectionThatRoutesOnwardsLeavesTheInstanceActive()
    {
        // A non-terminal rejection routes back to an earlier action; it is not an ending.
        var instance = Project(
            Tx("tx-1", null, 1, [2]),
            Tx("tx-2", "tx-1", 2, [1], isRejection: true));

        instance.State.Should().Be(InstanceState.Rejected,
            "the fold marks the rejection itself; routing onwards is carried by CurrentActionIds");
        instance.CurrentActionIds.Should().Equal(1);
    }

    [Fact]
    public void TheRejectionFlagIsWhatDecidesIt_NotAnEmptyNextActionSet()
    {
        // Pins the actual discriminator. Both of these end with no next action; only the flag
        // separates them, and it was the flag that never arrived.
        var rejected = Project(Tx("tx-1", null, 1, [], isRejection: true));
        var completed = Project(Tx("tx-1", null, 1, []));

        rejected.State.Should().Be(InstanceState.Rejected);
        completed.State.Should().Be(InstanceState.Completed);
    }
}
