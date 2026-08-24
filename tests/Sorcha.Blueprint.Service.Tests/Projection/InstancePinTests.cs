// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Services.Implementation;

namespace Sorcha.Blueprint.Service.Tests.Projection;

/// <summary>
/// Feature 194 — the pin as folded: an instance runs the definition it started on, forever.
/// </summary>
/// <remarks>
/// These exercise the pure fold, which is the only place the rule can be stated once and hold on
/// every node. Both the online projector and the offline rebuild go through it, so a rule proven
/// here cannot be true on one path and false on the other.
/// </remarks>
public class InstancePinTests
{
    private const string DefinitionA = "aaaa000000000000000000000000000000000000000000000000000000000001";
    private const string DefinitionB = "bbbb000000000000000000000000000000000000000000000000000000000002";

    private static readonly IReadOnlyDictionary<string, string> NoBindings =
        new Dictionary<string, string>();

    private static ProjectedTransaction Tx(
        string id, string? prev, int completed, int[] next, string? pin)
        => new(id, prev, completed, next, NoBindings,
               IsRejection: false, RouteId: null, ReasonCode: null, BlueprintDefinitionTxId: pin);

    [Fact]
    public void TheStartingAction_EstablishesThePin()
    {
        var instance = InstanceProjection.Project("inst-1", "reg", "bp", 1, "tenant",
        [
            Tx("tx1", null, 1, [2], DefinitionA),
        ])!;

        instance.BlueprintDefinitionTxId.Should().Be(DefinitionA);
    }

    [Fact]
    public void ThePin_SurvivesEveryLaterActionOnTheSameDefinition()
    {
        var instance = InstanceProjection.Project("inst-1", "reg", "bp", 1, "tenant",
        [
            Tx("tx1", null, 1, [2], DefinitionA),
            Tx("tx2", "tx1", 2, [3], DefinitionA),
            Tx("tx3", "tx2", 3, [], DefinitionA),
        ])!;

        instance.BlueprintDefinitionTxId.Should().Be(DefinitionA);
        instance.State.Should().Be(InstanceState.Completed);
    }

    [Fact]
    public void ATransactionClaimingAnotherDefinition_IsRefused_AndTheInstanceDoesNotAdvance()
    {
        // The core protection. Without it a sender could move a running instance onto a newly
        // published definition simply by asserting one — and two nodes folding the same ledger
        // could reach different answers about which rules the instance runs under.
        var instance = InstanceProjection.Project("inst-1", "reg", "bp", 1, "tenant",
        [
            Tx("tx1", null, 1, [2], DefinitionA),
        ])!;

        var outcome = InstanceProjection.Apply(instance, Tx("tx2", "tx1", 2, [3], DefinitionB));

        outcome.Should().Be(FoldOutcome.RefusedForeignDefinition);
        instance.BlueprintDefinitionTxId.Should().Be(DefinitionA, "the pin is immutable");
        instance.CurrentActionIds.Should().Equal([2],
            "the refused transaction must not advance the instance");
        instance.CompletedActionCount.Should().Be(1);
        instance.LastAppliedTxId.Should().Be("tx1", "the watermark must not move past a refused transaction");
    }

    [Fact]
    public void ABatchRebuild_SkipsAForeignTransaction_JustAsTheOnlineFoldRefusesIt()
    {
        // If the two paths disagreed, a rebuild would "repair" an instance into a state the
        // projector would never produce — breaking the F145 parity guarantee in the direction
        // hardest to notice, because the rebuild is the thing you reach for when in doubt.
        var instance = InstanceProjection.Project("inst-1", "reg", "bp", 1, "tenant",
        [
            Tx("tx1", null, 1, [2], DefinitionA),
            Tx("tx2", "tx1", 2, [3], DefinitionB),
        ])!;

        instance.BlueprintDefinitionTxId.Should().Be(DefinitionA);
        instance.CurrentActionIds.Should().Equal(2);
        instance.CompletedActionCount.Should().Be(1);
    }

    [Fact]
    public void AnUnpinnedTransaction_IsAccepted_AndDoesNotClearAnEstablishedPin()
    {
        // Null means "sealed before Feature 194", not "claims something different". Refusing it
        // would wedge instances whose earlier actions predate the feature — a worse outcome than
        // folding one action through the documented fallback.
        var instance = InstanceProjection.Project("inst-1", "reg", "bp", 1, "tenant",
        [
            Tx("tx1", null, 1, [2], DefinitionA),
        ])!;

        var outcome = InstanceProjection.Apply(instance, Tx("tx2", "tx1", 2, [3], pin: null));

        outcome.Should().Be(FoldOutcome.Advanced);
        instance.BlueprintDefinitionTxId.Should().Be(DefinitionA, "an unpinned transaction must not erase the pin");
        instance.CurrentActionIds.Should().Equal(3);
    }

    [Fact]
    public void AWhollyUnpinnedInstance_FoldsCleanly_AndReportsItselfUnpinned()
    {
        var instance = InstanceProjection.Project("inst-1", "reg", "bp", 1, "tenant",
        [
            Tx("tx1", null, 1, [2], pin: null),
            Tx("tx2", "tx1", 2, [], pin: null),
        ])!;

        instance.BlueprintDefinitionTxId.Should().BeEmpty(
            "an empty pin is how an operator tells a pre-feature instance from a pinned one");
        instance.State.Should().Be(InstanceState.Completed);
    }

    [Fact]
    public void AnUnpinnedInstance_IsPinnedByTheFirstTransactionThatCarriesOne()
    {
        // The migration case: an instance that started before the deploy and continues after it.
        var instance = InstanceProjection.Project("inst-1", "reg", "bp", 1, "tenant",
        [
            Tx("tx1", null, 1, [2], pin: null),
        ])!;
        instance.BlueprintDefinitionTxId.Should().BeEmpty();

        InstanceProjection.Apply(instance, Tx("tx2", "tx1", 2, [3], DefinitionA))
            .Should().Be(FoldOutcome.Advanced);

        instance.BlueprintDefinitionTxId.Should().Be(DefinitionA);
    }

    [Fact]
    public void ThePin_IsOrderIndependent_SoTwoNodesCannotDisagree()
    {
        // Determinism is the whole reason the pin had to become a sealed fact. Folding the same
        // sealed set in any order must reach the same pin.
        var chain = new[]
        {
            Tx("tx1", null, 1, [2], DefinitionA),
            Tx("tx2", "tx1", 2, [3], DefinitionA),
            Tx("tx3", "tx2", 3, [], DefinitionA),
        };

        var forward = InstanceProjection.Project("inst-1", "reg", "bp", 1, "tenant", chain)!;
        var reversed = InstanceProjection.Project("inst-1", "reg", "bp", 1, "tenant", chain.Reverse())!;
        var shuffled = InstanceProjection.Project("inst-1", "reg", "bp", 1, "tenant",
            [chain[1], chain[2], chain[0]])!;

        forward.BlueprintDefinitionTxId.Should().Be(DefinitionA);
        reversed.BlueprintDefinitionTxId.Should().Be(forward.BlueprintDefinitionTxId);
        shuffled.BlueprintDefinitionTxId.Should().Be(forward.BlueprintDefinitionTxId);
    }

    [Fact]
    public void IsDefinitionCompatible_StatesTheRuleDirectly()
    {
        var pinned = InstanceProjection.Project("inst-1", "reg", "bp", 1, "tenant",
        [
            Tx("tx1", null, 1, [2], DefinitionA),
        ])!;

        InstanceProjection.IsDefinitionCompatible(pinned, Tx("t", null, 2, [], DefinitionA)).Should().BeTrue();
        InstanceProjection.IsDefinitionCompatible(pinned, Tx("t", null, 2, [], DefinitionB)).Should().BeFalse();
        InstanceProjection.IsDefinitionCompatible(pinned, Tx("t", null, 2, [], pin: null)).Should().BeTrue();
    }
}
