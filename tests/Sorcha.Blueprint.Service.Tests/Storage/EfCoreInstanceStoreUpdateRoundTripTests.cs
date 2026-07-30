// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Reflection;
using System.Text.Json.Nodes;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Microsoft.Extensions.DependencyInjection;

using Sorcha.Blueprint.Service.Data;
using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Services.Implementation;
using Sorcha.Blueprint.Service.Storage;

using Moq;

namespace Sorcha.Blueprint.Service.Tests.Storage;

/// <summary>
/// <see cref="EfCoreInstanceStore.UpdateAsync"/> copies the model onto the tracked entity
/// field-by-field, by hand. A field missing from that list is written in memory, reported as saved,
/// and silently lost — no exception, no warning, and every other field on the same call persists
/// normally, so the instance looks healthy.
/// </summary>
/// <remarks>
/// That is exactly what happened to <c>LastAppliedTxId</c>. <c>InstanceProjection.ApplyInPlace</c>
/// sets <c>LastTransactionId</c> and <c>LastAppliedTxId</c> on adjacent lines; only the first was in
/// the copy list. Live on n1 every instance — rehearsal and AIAS alike — had
/// <c>LastTransactionId</c> populated and <c>LastAppliedTxId</c> NULL.
/// <para>
/// Two things broke, both silently:
/// </para>
/// <list type="number">
/// <item><description>
/// <b>The projector's replay guard is dead.</b> <c>InstanceProjector</c> re-reads the instance from
/// the store on every fold and <c>InstanceProjection.Apply</c> skips a transaction only when
/// <c>LastAppliedTxId == tx.TxId</c>. Read back as NULL, that comparison can never match, so a
/// redelivered <c>docket:confirmed</c> re-applies a transaction that was already folded — inflating
/// <c>CompletedActionCount</c> and, on out-of-order redelivery, rewinding <c>CurrentActionIds</c> to
/// an earlier action's next-set.
/// </description></item>
/// <item><description>
/// <b>Feature 142's go-live gate stays unearnable.</b> The rehearsal waits for the projector to fold
/// <i>its own</i> transaction, matching on this watermark, so it always times out and reports a
/// sealing delay — even though the docket sealed and the projection advanced correctly.
/// </description></item>
/// </list>
/// <para>
/// The round-trip test below is written against the whole model rather than the one field, because
/// the defect is structural: the next field added to <c>Instance</c> can be forgotten the same way.
/// </para>
/// </remarks>
public class EfCoreInstanceStoreUpdateRoundTripTests
{
    /// <summary>
    /// Properties legitimately not expected to survive an update unchanged, each with a reason.
    /// Keep this list short and justified — every entry is a hole in the guard.
    /// </summary>
    private static readonly Dictionary<string, string> NotRoundTripped = new()
    {
        ["Version"] = "UpdateAsync increments it — that is the optimistic-concurrency contract",
        ["UpdatedAt"] = "UpdateAsync stamps it to UtcNow by design",
        ["TenantId"] = "not a column; persisted inside the Metadata dictionary",
    };

    private static EfCoreInstanceStore CreateStore(string dbName)
    {
        var options = new DbContextOptionsBuilder<BlueprintDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics
                .InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        var scope = new Mock<IServiceScope>();
        scope.SetupGet(s => s.ServiceProvider).Returns(new Mock<IServiceProvider>().Object);
        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);

        return new EfCoreInstanceStore(
            new StubContextFactory(options),
            NullLogger<EfCoreInstanceStore>.Instance,
            scopeFactory.Object);
    }

    private sealed class StubContextFactory(DbContextOptions<BlueprintDbContext> options)
        : IDbContextFactory<BlueprintDbContext>
    {
        public BlueprintDbContext CreateDbContext() => new(options);

        public Task<BlueprintDbContext> CreateDbContextAsync(CancellationToken ct = default) =>
            Task.FromResult(CreateDbContext());
    }

    private static Instance SeedInstance(string id) => new()
    {
        Id = id,
        BlueprintId = "bp-original",
        BlueprintVersion = 1,
        RegisterId = "reg-original",
        TenantId = "tenant-1",
        State = InstanceState.Active,
        CurrentActionIds = [0],
        ParticipantWallets = new Dictionary<string, string> { ["applicant"] = "ws-a" },
        FirstTransactionId = "tx-first",
        LastTransactionId = "tx-first",
        CompletedActionCount = 0,
        AccumulatedData = new Dictionary<string, object>(),
        PendingActionPayloads = new Dictionary<int, JsonObject>(),
        ActiveBranches = [],
        Metadata = new Dictionary<string, string> { ["k"] = "v0" },
        Version = 0,
        CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
        UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
        LastAppliedTxId = null,
    };

    /// <summary>
    /// Mutates every settable field to a value distinct from the seed, so that a field dropped by
    /// <c>UpdateAsync</c> reads back as the seed value and is detected. Init-only properties
    /// (<c>Id</c>, <c>BlueprintId</c>, <c>BlueprintVersion</c>, <c>RegisterId</c>, <c>TenantId</c>,
    /// <c>CreatedAt</c>) cannot change after creation by design, so for those the assertion only
    /// confirms the create-path value survived.
    /// </summary>
    private static void MutateEverything(Instance i)
    {
        i.State = InstanceState.Completed;
        i.CurrentActionIds = [3, 5];
        i.ParticipantWallets["reviewer"] = "ws-r";
        i.FirstTransactionId = "tx-first";      // immutable by design once set
        i.LastTransactionId = "tx-second";
        i.CompletedActionCount = 2;
        i.AccumulatedData["field"] = "value";
        i.PendingActionPayloads[4] = new JsonObject { ["p"] = "q" };
        i.ActiveBranches = [new Branch { Id = "b1", CurrentActionId = 3 }];
        i.Metadata["k"] = "v1";
        i.CompletedAt = DateTimeOffset.UtcNow;
        i.LastAppliedTxId = "tx-second";
    }

    [Fact]
    public async Task UpdateAsync_PersistsLastAppliedTxId()
    {
        // The projection watermark. Feature 142's rehearsal wait and the projector's replay guard
        // both key on it, and both fail open — silently — when it comes back NULL.
        var store = CreateStore(nameof(UpdateAsync_PersistsLastAppliedTxId) + Guid.NewGuid());
        var created = await store.CreateAsync(SeedInstance("inst-watermark"));

        created.LastAppliedTxId = "tx-folded";
        created.LastTransactionId = "tx-folded";
        await store.UpdateAsync(created);

        var reread = await store.GetAsync("inst-watermark");
        reread.Should().NotBeNull();
        reread!.LastTransactionId.Should().Be("tx-folded",
            "this field is in UpdateAsync's copy list, so it proves the update itself ran");
        reread.LastAppliedTxId.Should().Be("tx-folded",
            "InstanceProjection sets LastAppliedTxId on the line after LastTransactionId; if only " +
            "one survives, UpdateAsync's hand-written copy list dropped the other");
    }

    [Fact]
    public async Task UpdateAsync_PersistsEveryMutableField()
    {
        // Structural guard: the defect is a hand-maintained copy list, so pin the whole model rather
        // than the single field that happened to be missed. A newly-added Instance property that
        // UpdateAsync forgets fails here without anyone having to remember to write a test for it.
        var store = CreateStore(nameof(UpdateAsync_PersistsEveryMutableField) + Guid.NewGuid());
        var created = await store.CreateAsync(SeedInstance("inst-all-fields"));

        MutateEverything(created);
        await store.UpdateAsync(created);

        var reread = await store.GetAsync("inst-all-fields");
        reread.Should().NotBeNull();

        var dropped = new List<string>();
        foreach (var prop in typeof(Instance).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.GetSetMethod() is null || NotRoundTripped.ContainsKey(prop.Name))
                continue;

            var expected = prop.GetValue(created);
            var actual = prop.GetValue(reread);

            // Compare by serialised shape so dictionaries/lists compare by value, not reference.
            var expectedJson = System.Text.Json.JsonSerializer.Serialize(expected);
            var actualJson = System.Text.Json.JsonSerializer.Serialize(actual);
            if (!string.Equals(expectedJson, actualJson, StringComparison.Ordinal))
                dropped.Add($"{prop.Name}: expected {expectedJson}, got {actualJson}");
        }

        dropped.Should().BeEmpty(
            "every settable Instance property must survive UpdateAsync — a field missing from its " +
            "hand-written copy list is lost silently, with no exception and no warning");
    }

    [Fact]
    public async Task UpdateAsync_PersistedWatermark_RestoresTheProjectorReplayGuard()
    {
        // The consequence that matters beyond the rehearsal: InstanceProjector re-reads the instance
        // from the store before every fold, so the idempotency check runs against the PERSISTED
        // watermark. If it does not survive the round trip, an already-folded transaction is applied
        // a second time.
        var store = CreateStore(nameof(UpdateAsync_PersistedWatermark_RestoresTheProjectorReplayGuard)
                                + Guid.NewGuid());
        await store.CreateAsync(SeedInstance("inst-replay"));

        var tx = new ProjectedTransaction(
            TxId: "tx-fold-1",
            PreviousTransactionId: null,
            CompletedActionId: 0,
            NextActionIds: [1],
            ParticipantBindings: new Dictionary<string, string>());

        var first = await store.GetAsync("inst-replay");
        InstanceProjection.Apply(first!, tx).Should().BeTrue("the first fold must advance");
        await store.UpdateAsync(first!);

        // Second delivery of the SAME transaction, read fresh from the store as the projector does.
        var second = await store.GetAsync("inst-replay");
        InstanceProjection.Apply(second!, tx).Should().BeFalse(
            "a redelivered docket:confirmed must be recognised as already folded");

        second!.CompletedActionCount.Should().Be(1,
            "re-applying an already-folded transaction inflates the completed-action count");
    }
}
