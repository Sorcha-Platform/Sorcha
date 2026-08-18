// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text;
using System.Text.Json;

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Sorcha.Blueprint.Models.Credentials;
using Sorcha.Blueprint.Service.Services;
using Sorcha.Blueprint.Service.Storage;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Enums;
using Sorcha.ServiceClients.Register;

using Xunit;

namespace Sorcha.Blueprint.Service.Tests.Credentials;

/// <summary>
/// Status lists must survive a process restart and rebuild from the register (#1482).
/// </summary>
/// <remarks>
/// Before this, <c>StatusListManager</c> held every list in a process-memory dictionary. A restart
/// destroyed them, every status-list URL 404ed, and because fail-closed genuinely works, every
/// credential-gated action refused. Two properties are pinned here: revocations survive, and — the
/// subtler one — the ALLOCATION counter survives, because a reset counter hands a new credential an
/// index an older credential already holds, and revoking one then silently revokes the other.
/// </remarks>
public class StatusListDurabilityTests
{
    private const string Issuer = "ws11qissuer";
    private const string Register = "2141b08339d34c27824536ec250b025e";

    private static string ListId => $"{Issuer}-{Register}-revocation-1";

    /// <summary>A fresh manager over the SAME store — i.e. a process restart.</summary>
    private static StatusListManager NewManager(
        IStatusListStore store, IRegisterServiceClient? register = null)
    {
        register ??= EmptyRegister();
        var reconciler = new StatusListLedgerReconciler(
            register, NullLogger<StatusListLedgerReconciler>.Instance);

        return new StatusListManager(
            NullLogger<StatusListManager>.Instance,
            new Sorcha.Blueprint.Service.Configuration.StatusListUrls.Resolved(
                "https://n1.sorcha.dev/api/v1/credentials/status-lists",
                "https://n1.sorcha.dev/api/v1/credentials/status-lists"),
            store,
            reconciler);
    }

    private static IRegisterServiceClient EmptyRegister()
    {
        var m = new Mock<IRegisterServiceClient>();
        m.Setup(r => r.GetTransactionsAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionPage { Page = 1, PageSize = 100, Total = 0, Transactions = [] });
        return m.Object;
    }

    private static IRegisterServiceClient RegisterWith(
        params (string CredentialId, int Index, string NewStatus, ulong Docket)[] events)
    {
        var txs = events.Select(e =>
        {
            var payload = new CredentialStatusChangePayload
            {
                CredentialId = e.CredentialId,
                NewStatus = e.NewStatus,
                IssuerWallet = Issuer,
                SubjectDid = "ws11qsubject",
                ChangedAt = DateTimeOffset.UtcNow,
                StatusListId = ListId,
                StatusListIndex = e.Index
            };

            var json = JsonSerializer.Serialize(payload);

            return new TransactionModel
            {
                TxId = Guid.NewGuid().ToString("N"),
                RegisterId = Register,
                DocketNumber = e.Docket,
                TimeStamp = DateTime.UtcNow.AddSeconds(e.Docket),
                MetaData = new TransactionMetaData
                {
                    RegisterId = Register,
                    TransactionType = TransactionType.CredentialStatusChange
                },
                PayloadCount = 1,
                Payloads = [new PayloadModel { Data = Convert.ToBase64String(Encoding.UTF8.GetBytes(json)) }]
            };
        }).ToList();

        var m = new Mock<IRegisterServiceClient>();
        m.Setup(r => r.GetTransactionsAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionPage
            {
                Page = 1,
                PageSize = 100,
                Total = txs.Count,
                Transactions = txs
            });
        return m.Object;
    }

    [Fact]
    public async Task AllocatedIndicesSurviveARestart_SoTwoCredentialsNeverShareABit()
    {
        // The subtle half of #1482. If the counter resets, credential C is handed index 0 which
        // credential A already holds — and revoking C silently marks A revoked too.
        var store = new InMemoryStatusListStore();

        var first = NewManager(store);
        var a = await first.AllocateIndexAsync(Issuer, Register, "urn:uuid:a");
        var b = await first.AllocateIndexAsync(Issuer, Register, "urn:uuid:b");

        // Process restart: brand-new manager, same durable store.
        var afterRestart = NewManager(store);
        var c = await afterRestart.AllocateIndexAsync(Issuer, Register, "urn:uuid:c");

        a.Index.Should().Be(0);
        b.Index.Should().Be(1);
        c.Index.Should().Be(2, "the allocation counter must survive a restart or indices are reused");
        new[] { a.Index, b.Index, c.Index }.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task ARevokedBitSurvivesARestart()
    {
        var store = new InMemoryStatusListStore();

        var first = NewManager(store);
        var alloc = await first.AllocateIndexAsync(Issuer, Register, "urn:uuid:a");
        await first.SetBitAsync(alloc.ListId, alloc.Index, true, "revoked in a previous life");

        var afterRestart = NewManager(store);
        var list = await afterRestart.GetListAsync(alloc.ListId);

        list.Should().NotBeNull("the status list must not vanish with the process");
        list!.GetBit(alloc.Index).Should().BeTrue("a revocation is permanent, not per-process");
    }

    [Fact]
    public async Task AListIsRebuiltFromTheRegisterWhenThisNodeNeverSawTheRevocation()
    {
        // The cross-node case: another node raised the revocation, so it exists ONLY as a sealed
        // transaction. Folding it is what makes tiny agree with n1.
        var store = new InMemoryStatusListStore();
        var seed = NewManager(store);
        var alloc = await seed.AllocateIndexAsync(Issuer, Register, "urn:uuid:a");

        var withRemoteRevocation = NewManager(
            store, RegisterWith(("urn:uuid:a", alloc.Index, "Revoked", 7)));

        var list = await withRemoteRevocation.GetListAsync(alloc.ListId);

        list!.GetBit(alloc.Index).Should().BeTrue(
            "a revocation sealed on another node must be folded from the register");
    }

    [Fact]
    public async Task EventsAreAppliedInLedgerOrder_NotArrivalOrder()
    {
        // Suspend and reinstate share the revocation bit, so the bit is NOT monotonic:
        // set(d5) then clear(d9) ends CLEAR, and applying them the other way round ends SET.
        // Two nodes folding in different orders would disagree and both believe themselves right.
        var store = new InMemoryStatusListStore();
        var seed = NewManager(store);
        var alloc = await seed.AllocateIndexAsync(Issuer, Register, "urn:uuid:a");

        // Deliberately supplied newest-first.
        var manager = NewManager(store, RegisterWith(
            ("urn:uuid:a", alloc.Index, "Active", 9),
            ("urn:uuid:a", alloc.Index, "Suspended", 5)));

        var list = await manager.GetListAsync(alloc.ListId);

        list!.GetBit(alloc.Index).Should().BeFalse(
            "docket 9 (reinstate) is later than docket 5 (suspend), whatever order they arrived in");
    }

    [Fact]
    public async Task AnUnreadableRegisterIsReportedAsFailed_NotAsNothingRevoked()
    {
        // The answer that must never be given: "I could not read the register, therefore nothing is
        // revoked." Fail-closed callers depend on telling those apart.
        var store = new InMemoryStatusListStore();
        var seed = NewManager(store);
        var alloc = await seed.AllocateIndexAsync(Issuer, Register, "urn:uuid:a");

        var broken = new Mock<IRegisterServiceClient>();
        broken.Setup(r => r.GetTransactionsAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("register unreachable"));

        var manager = NewManager(store, broken.Object);
        await manager.GetListAsync(alloc.ListId);

        manager.GetReadiness(alloc.ListId).Should().Be(
            StatusListReadiness.Failed,
            "an unreadable register means 'I cannot tell', which callers must fail closed on");
    }

    [Fact]
    public async Task AListThisNodeHasNeverSeenIsStillNull()
    {
        // A genuine 404 must stay distinguishable from "known but not folded yet".
        var manager = NewManager(new InMemoryStatusListStore());

        (await manager.GetListAsync("ws11qnope-deadbeef-revocation-1")).Should().BeNull();
    }
}
