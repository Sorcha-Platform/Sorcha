// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Services.Implementation;

namespace Sorcha.Blueprint.Service.Tests.Projection;

/// <summary>
/// Feature 145 US1 — determinism, order-independence, and idempotency of the pure instance
/// projection fold (the deterministic core; SC-001). The same sealed transaction set must
/// project to identical control state regardless of arrival order or restart.
/// </summary>
public class InstanceProjectionTests
{
    private static readonly IReadOnlyDictionary<string, string> NoBindings =
        new Dictionary<string, string>();

    private static ProjectedTransaction Tx(
        string id, string? prev, int completed, int[] next, bool rejection = false,
        IReadOnlyDictionary<string, string>? bindings = null)
        => new(id, prev, completed, next, bindings ?? NoBindings, rejection);

    /// <summary>A linear 3-action chain: start(1)→2→3→complete.</summary>
    private static List<ProjectedTransaction> LinearChain() =>
    [
        Tx("tx1", null, 1, [2], bindings: new Dictionary<string, string> { ["citizen"] = "ws-citizen" }),
        Tx("tx2", "tx1", 2, [3], bindings: new Dictionary<string, string> { ["analyst"] = "ws-analyst" }),
        Tx("tx3", "tx2", 3, []),
    ];

    [Fact]
    public void Project_LinearChain_ReachesCompletedTerminalState()
    {
        var instance = InstanceProjection.Project("inst-1", "reg", "bp", 1, "tenant", LinearChain());

        instance.Should().NotBeNull();
        instance!.State.Should().Be(InstanceState.Completed);
        instance.CurrentActionIds.Should().BeEmpty();
        instance.CompletedActionCount.Should().Be(3);
        instance.FirstTransactionId.Should().Be("tx1");
        instance.LastTransactionId.Should().Be("tx3");
        instance.LastAppliedTxId.Should().Be("tx3");
        instance.ParticipantWallets.Should().Contain("citizen", "ws-citizen");
        instance.ParticipantWallets.Should().Contain("analyst", "ws-analyst");
    }

    [Fact]
    public void Project_IsOrderIndependent_AcrossAllPermutations()
    {
        var canonical = InstanceProjection.Project("inst-1", "reg", "bp", 1, "tenant", LinearChain())!;

        var permutations = new[]
        {
            new[] { 0, 1, 2 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 },
            new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 },
        };

        foreach (var perm in permutations)
        {
            var src = LinearChain();
            var shuffled = perm.Select(i => src[i]).ToList();
            var result = InstanceProjection.Project("inst-1", "reg", "bp", 1, "tenant", shuffled)!;

            result.State.Should().Be(canonical.State);
            result.CurrentActionIds.Should().Equal(canonical.CurrentActionIds);
            result.CompletedActionCount.Should().Be(canonical.CompletedActionCount);
            result.LastTransactionId.Should().Be(canonical.LastTransactionId);
            result.FirstTransactionId.Should().Be(canonical.FirstTransactionId);
            result.ParticipantWallets.Should().BeEquivalentTo(canonical.ParticipantWallets);
        }
    }

    [Fact]
    public void Project_DuplicateTransactions_FoldOnce()
    {
        var withDupes = LinearChain();
        withDupes.Add(LinearChain()[0]); // duplicate tx1
        withDupes.Add(LinearChain()[1]); // duplicate tx2

        var instance = InstanceProjection.Project("inst-1", "reg", "bp", 1, "tenant", withDupes)!;

        instance.CompletedActionCount.Should().Be(3, "duplicate sealed transactions must fold once");
        instance.State.Should().Be(InstanceState.Completed);
    }

    [Fact]
    public void Project_ParallelBranch_KeepsAllCurrentActions()
    {
        // start(1) fans out to 2 AND 3; only 2 completes → 3 still current.
        var txs = new List<ProjectedTransaction>
        {
            Tx("tx1", null, 1, [2, 3]),
            Tx("tx2", "tx1", 2, []),
        };

        var instance = InstanceProjection.Project("inst-1", "reg", "bp", 1, "tenant", txs)!;

        instance.CurrentActionIds.Should().Equal(3);
        instance.State.Should().Be(InstanceState.Active);
        instance.CompletedActionCount.Should().Be(2);
    }

    [Fact]
    public void Apply_ReApplyingSameTransaction_IsNoOp()
    {
        var instance = InstanceProjection.Project("inst-1", "reg", "bp", 1, "tenant",
        [
            Tx("tx1", null, 1, [2]),
        ])!;
        var countBefore = instance.CompletedActionCount;

        var advanced = InstanceProjection.Apply(instance, Tx("tx1", null, 1, [2]));

        advanced.Should().BeFalse();
        instance.CompletedActionCount.Should().Be(countBefore);
    }

    [Fact]
    public void Apply_NewTransaction_AdvancesAndReportsTrue()
    {
        var instance = InstanceProjection.Project("inst-1", "reg", "bp", 1, "tenant",
        [
            Tx("tx1", null, 1, [2]),
        ])!;

        var advanced = InstanceProjection.Apply(instance, Tx("tx2", "tx1", 2, [3]));

        advanced.Should().BeTrue();
        instance.CurrentActionIds.Should().Equal(3);
        instance.LastAppliedTxId.Should().Be("tx2");
        instance.CompletedActionCount.Should().Be(2);
    }

    [Fact]
    public void Apply_IncrementalEqualsBatchProject_SameFinalState()
    {
        // Incremental online fold must equal an offline batch rebuild (the FR-003 parity invariant).
        var batch = InstanceProjection.Project("inst-1", "reg", "bp", 1, "tenant", LinearChain())!;

        var incremental = InstanceProjection.Project("inst-1", "reg", "bp", 1, "tenant",
        [
            LinearChain()[0],
        ])!;
        InstanceProjection.Apply(incremental, LinearChain()[1]);
        InstanceProjection.Apply(incremental, LinearChain()[2]);

        incremental.State.Should().Be(batch.State);
        incremental.CurrentActionIds.Should().Equal(batch.CurrentActionIds);
        incremental.CompletedActionCount.Should().Be(batch.CompletedActionCount);
        incremental.LastAppliedTxId.Should().Be(batch.LastAppliedTxId);
        incremental.ParticipantWallets.Should().BeEquivalentTo(batch.ParticipantWallets);
    }

    [Fact]
    public void Project_Rejection_ReachesRejectedTerminalState()
    {
        var txs = new List<ProjectedTransaction>
        {
            Tx("tx1", null, 1, [2]),
            Tx("tx2", "tx1", 2, [], rejection: true),
        };

        var instance = InstanceProjection.Project("inst-1", "reg", "bp", 1, "tenant", txs)!;

        instance.State.Should().Be(InstanceState.Rejected);
    }

    [Fact]
    public void Project_EmptyInput_ReturnsNull()
    {
        var instance = InstanceProjection.Project("inst-1", "reg", "bp", 1, "tenant", []);
        instance.Should().BeNull();
    }
}
