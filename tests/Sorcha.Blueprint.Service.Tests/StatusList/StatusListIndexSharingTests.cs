// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using Sorcha.Blueprint.Models.Credentials;
using Sorcha.Blueprint.Service.Services;
using Sorcha.Blueprint.Service.Storage;
using Sorcha.ServiceClients.Register;

using Xunit;

namespace Sorcha.Blueprint.Service.Tests.StatusList;

/// <summary>
/// Issue #1502 — one credential must get ONE entry number in BOTH purpose lists.
/// </summary>
/// <remarks>
/// <para>
/// The two lists are a single numbering space that happens to be stored twice. When they were
/// allocated independently they drifted the moment one was created later than the other: a
/// suspension list added to an issuer who already had a revocation list starts at 0 while
/// revocation is at N, and the offset is then permanent.
/// </para>
/// <para>
/// What the drift did NOT do is worth stating, because it is easy to assume: credentials did not
/// collide with each other through the issuance path, because the entry number handed to the
/// credential came from the revocation list for BOTH purposes. Tracing it is what settled that —
/// the reasoning said otherwise.
/// </para>
/// <para>
/// What it DID do is leave the lagging list's <c>NextAvailableIndex</c> permanently below the
/// highest number credentials point at. That list then believes indexes are free that are already
/// spoken for, so its capacity guard fires late, and anything that ever allocates from it — a
/// rebuild, a restore from an older snapshot, a second allocation path — hands out a number another
/// credential already holds. It is a live inconsistency between two artefacts that are supposed to
/// be one numbering space, which the old code detected and then ignored.
/// </para>
/// <para>
/// This was live on n1 (2026-08-18). The old code logged a warning and carried on, which is why
/// these tests assert the INDEXES rather than the absence of a log line — a warning nothing acts on
/// is indistinguishable from silence.
/// </para>
/// </remarks>
public class StatusListIndexSharingTests
{
    private const string Issuer = "ws11qtestissuer";
    private const string Register = "reg-1502";

    [Fact]
    public async Task BothPurposeListsBurnTheSameIndexForOneCredential()
    {
        var manager = BuildManager(out var store);

        var allocation = await manager.AllocateIndexAsync(Issuer, Register, "cred-1");

        var revocation = await store.GetAsync(RevocationId);
        var suspension = await store.GetAsync(SuspensionId);

        revocation!.NextAvailableIndex.Should().Be(allocation.Index + 1);
        suspension!.NextAvailableIndex.Should().Be(allocation.Index + 1,
            "the credential carries one entry number, so both lists must have consumed it");
    }

    [Fact]
    public async Task ASuspensionListCreatedLateIsBroughtUpToItsSibling()
    {
        // The exact n1 condition: the issuer has been issuing credentials since before the purposes
        // were split, so the revocation list is already at index 3 when the suspension list is
        // created empty. Allocating independently hands out revocation 3 / suspension 0.
        var manager = BuildManager(out var store);

        var revocation = BitstringStatusList.Create(Issuer, Register, "revocation");
        revocation.AllocateIndex();
        revocation.AllocateIndex();
        revocation.AllocateIndex();
        await store.SaveAsync(revocation);

        var allocation = await manager.AllocateIndexAsync(Issuer, Register, "cred-late");

        allocation.Index.Should().Be(3, "the shared index continues from the further-advanced list");

        var suspension = await store.GetAsync(SuspensionId);
        suspension!.NextAvailableIndex.Should().Be(4,
            "the late suspension list must jump to its sibling's numbering rather than start at 0");
    }

    [Fact]
    public async Task OnceHealedTheListsNeverDriftAgain()
    {
        // Healing once is not enough — the next allocation must keep them together, which is what
        // makes the invariant hold by construction rather than by a one-off repair.
        var manager = BuildManager(out var store);

        var revocation = BitstringStatusList.Create(Issuer, Register, "revocation");
        revocation.AllocateIndex();
        revocation.AllocateIndex();
        await store.SaveAsync(revocation);

        var first  = await manager.AllocateIndexAsync(Issuer, Register, "cred-a");
        var second = await manager.AllocateIndexAsync(Issuer, Register, "cred-b");
        var third  = await manager.AllocateIndexAsync(Issuer, Register, "cred-c");

        first.Index.Should().Be(2);
        second.Index.Should().Be(3);
        third.Index.Should().Be(4);

        (await store.GetAsync(RevocationId))!.NextAvailableIndex.Should().Be(5);
        (await store.GetAsync(SuspensionId))!.NextAvailableIndex.Should().Be(5);
    }

    [Fact]
    public async Task SuspendingOneCredentialDoesNotTouchAnothersBit()
    {
        // The property that must hold regardless of how the numbering got there. This one passed
        // under the drifted code too — kept deliberately, because it is the invariant a future
        // change to allocation would break first, and its passing is what corrected an overstated
        // reading of #1502 rather than confirming it.
        var manager = BuildManager(out var store);

        var revocation = BitstringStatusList.Create(Issuer, Register, "revocation");
        revocation.AllocateIndex();   // pre-existing credential from before the split
        await store.SaveAsync(revocation);

        var a = await manager.AllocateIndexAsync(Issuer, Register, "cred-a");
        var b = await manager.AllocateIndexAsync(Issuer, Register, "cred-b");

        a.Index.Should().NotBe(b.Index, "two credentials never share an entry number");

        await manager.SetBitAsync(a.SuspensionListId, a.Index, value: true, reason: "suspended A");

        var suspension = await store.GetAsync(SuspensionId);
        suspension!.GetBit(a.Index).Should().BeTrue("A was suspended");
        suspension.GetBit(b.Index).Should().BeFalse("B was not suspended by anyone");
    }

    [Fact]
    public async Task TheSharedIndexClearsWHICHEVERListIsFurtherAhead()
    {
        // The symmetric case, and the one that makes Math.Max load-bearing rather than decorative:
        // if the SUSPENSION list is ahead — a revocation list rebuilt or restored from an older
        // snapshot while its sibling persisted — taking the revocation list's number alone would
        // hand out an index the suspension list has already committed to another credential.
        //
        // Without this test the whole file passes with the sibling ignored entirely (verified by
        // mutation), because every other case here has revocation in front.
        var manager = BuildManager(out var store);

        var suspension = BitstringStatusList.Create(Issuer, Register, "suspension");
        suspension.AllocateIndex();
        suspension.AllocateIndex();
        suspension.AllocateIndex();
        suspension.AllocateIndex();   // suspension is at 4
        await store.SaveAsync(suspension);

        var revocation = BitstringStatusList.Create(Issuer, Register, "revocation");
        revocation.AllocateIndex();   // revocation only at 1
        await store.SaveAsync(revocation);

        var allocation = await manager.AllocateIndexAsync(Issuer, Register, "cred-x");

        allocation.Index.Should().Be(4,
            "the shared index must clear the furthest-advanced list, whichever purpose that is");

        (await store.GetAsync(RevocationId))!.NextAvailableIndex.Should().Be(5);
        (await store.GetAsync(SuspensionId))!.NextAvailableIndex.Should().Be(5);
    }

    [Fact]
    public async Task AnIndexIsNeverHandedOutTwiceWhicheverListLeads()
    {
        // Property form of the above: across a mixed history, no two credentials share a number.
        var manager = BuildManager(out var store);

        var suspension = BitstringStatusList.Create(Issuer, Register, "suspension");
        suspension.AllocateIndex();
        suspension.AllocateIndex();
        await store.SaveAsync(suspension);

        var issued = new List<int>();
        for (var i = 0; i < 5; i++)
        {
            issued.Add((await manager.AllocateIndexAsync(Issuer, Register, $"cred-{i}")).Index);
        }

        issued.Should().OnlyHaveUniqueItems("an entry number identifies exactly one credential");
        issued.Should().BeInAscendingOrder();
    }

    // ── harness ──────────────────────────────────────────────────────────────

    private static string RevocationId => $"{Issuer}-{Register}-revocation-1";
    private static string SuspensionId => $"{Issuer}-{Register}-suspension-1";

    private static StatusListManager BuildManager(out InMemoryStatusListStore store)
    {
        var urls = new Sorcha.Blueprint.Service.Configuration.StatusListUrls.Resolved(
            "https://test.example/api/v1/credentials/status-lists",
            "https://test.example/api/v1/credentials/ietf-status-lists");

        var register = new Mock<IRegisterServiceClient>();
        register.Setup(r => r.GetTransactionsAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionPage { Page = 1, PageSize = 100, Total = 0, Transactions = [] });

        var services = new ServiceCollection();
        services.AddScoped(_ => register.Object);
        var provider = services.BuildServiceProvider();

        var reconciler = new StatusListLedgerReconciler(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<StatusListLedgerReconciler>.Instance);

        store = new InMemoryStatusListStore();
        return new StatusListManager(
            NullLogger<StatusListManager>.Instance, urls, store, reconciler);
    }
}
