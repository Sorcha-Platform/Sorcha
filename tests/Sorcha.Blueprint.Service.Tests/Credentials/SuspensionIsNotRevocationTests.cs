// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text;
using System.Text.Json;

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
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
/// W3C Bitstring Status List treats revocation and suspension as different statuses, and Sorcha
/// must too.
/// </summary>
/// <remarks>
/// <para>
/// The spec is explicit: <c>revocation</c> is "used to cancel the validity of a verifiable
/// credential… <b>This status is not reversible</b>", while <c>suspension</c> is "used to
/// temporarily prevent the acceptance of a verifiable credential… <b>This status is reversible</b>".
/// </para>
/// <para>
/// Sorcha previously hardcoded a single list with purpose <c>revocation</c> and set that one bit for
/// BOTH operations. Two consequences, both wrong and both invisible from inside: a merely SUSPENDED
/// credential was advertised to every verifier as REVOKED, and lifting the suspension cleared a
/// revocation bit that the specification says can never be cleared.
/// </para>
/// </remarks>
public class SuspensionIsNotRevocationTests
{
    private const string Issuer = "ws11qissuer";
    private const string Register = "2141b08339d34c27824536ec250b025e";

    private static string RevocationList => $"{Issuer}-{Register}-revocation-1";
    private static string SuspensionList => $"{Issuer}-{Register}-suspension-1";

    [Fact]
    public async Task AllocationReservesTheSameIndexInBothLists()
    {
        // One credential, one entry number, two purposes — so a suspension and a revocation of the
        // same credential can never address different rows.
        var manager = NewManager(new InMemoryStatusListStore());

        var alloc = await manager.AllocateIndexAsync(Issuer, Register, "urn:uuid:a");

        alloc.ListId.Should().Be(RevocationList);
        alloc.SuspensionListId.Should().Be(SuspensionList);
        alloc.SuspensionListUrl.Should().EndWith(SuspensionList);
    }

    [Fact]
    public async Task SuspendingDoesNotSetTheRevocationBit()
    {
        // The headline defect: a suspended credential must not read as revoked.
        var store = new InMemoryStatusListStore();
        var manager = NewManager(store, RegisterWith(("Suspended", 0, 5)));

        var alloc = await manager.AllocateIndexAsync(Issuer, Register, "urn:uuid:a");

        var revocation = await manager.GetListAsync(alloc.ListId);
        var suspension = await manager.GetListAsync(alloc.SuspensionListId);

        suspension!.GetBit(alloc.Index).Should().BeTrue("the credential is suspended");
        revocation!.GetBit(alloc.Index).Should().BeFalse(
            "suspension is reversible and revocation is not — a suspended credential is not revoked");
    }

    [Fact]
    public async Task ReinstatementNeverClearsARevocation()
    {
        // Revocation is terminal per the spec. A later Active event must not un-revoke.
        var store = new InMemoryStatusListStore();
        var manager = NewManager(store, RegisterWith(
            ("Revoked", 0, 5),
            ("Active", 0, 9)));

        var alloc = await manager.AllocateIndexAsync(Issuer, Register, "urn:uuid:a");
        var revocation = await manager.GetListAsync(alloc.ListId);

        revocation!.GetBit(alloc.Index).Should().BeTrue(
            "revocation is not reversible, so a later Active event must not clear it");
    }

    [Fact]
    public async Task ReinstatementDoesClearASuspension()
    {
        // The mirror of the above — suspension IS reversible, so this must clear.
        var store = new InMemoryStatusListStore();
        var manager = NewManager(store, RegisterWith(
            ("Suspended", 0, 5),
            ("Active", 0, 9)));

        var alloc = await manager.AllocateIndexAsync(Issuer, Register, "urn:uuid:a");
        var suspension = await manager.GetListAsync(alloc.SuspensionListId);

        suspension!.GetBit(alloc.Index).Should().BeFalse("suspension is reversible");
    }

    [Fact]
    public void EachListDeclaresItsOwnPurpose()
    {
        // A verifier MUST raise STATUS_VERIFICATION_ERROR when the purpose it is checking is absent
        // from the list, so each list has to say which purpose it serves.
        BitstringStatusList.Create(Issuer, Register, "revocation").Purpose.Should().Be("revocation");
        BitstringStatusList.Create(Issuer, Register, "suspension").Purpose.Should().Be("suspension");
    }

    [Fact]
    public async Task ALegacyEventNamingTheRevocationListWithASuspendedStatusDoesNotRevoke()
    {
        // Events written before the purposes were split all name the REVOCATION list, including
        // suspensions — the payload's StatusListId cannot be trusted to imply the purpose for them.
        // The status word is what decides, so a Suspended event must leave the revocation bit clear
        // even when the event points at the revocation list.
        var store = new InMemoryStatusListStore();
        var legacy = RegisterWithRawListId("Suspended", RevocationList, index: 0, docket: 5);
        var manager = NewManager(store, legacy);

        var alloc = await manager.AllocateIndexAsync(Issuer, Register, "urn:uuid:a");
        var revocation = await manager.GetListAsync(alloc.ListId);

        revocation!.GetBit(alloc.Index).Should().BeFalse(
            "a suspension must never set a revocation bit, whichever list the legacy event named");
    }

    [Fact]
    public async Task ALegacyActiveEventNamingTheRevocationListDoesNotUnRevoke()
    {
        // The mirror: an Active event pointing at the revocation list must not clear it, because
        // revocation is not reversible.
        var store = new InMemoryStatusListStore();
        var m = new Mock<IRegisterServiceClient>();
        m.Setup(r => r.GetTransactionsAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionPage
            {
                Page = 1,
                PageSize = 100,
                Total = 2,
                Transactions =
                [
                    NewTx(Payload("Revoked", RevocationList, 0), 5),
                    NewTx(Payload("Active", RevocationList, 0), 9)
                ]
            });

        var manager = NewManager(store, m.Object);
        var alloc = await manager.AllocateIndexAsync(Issuer, Register, "urn:uuid:a");
        var revocation = await manager.GetListAsync(alloc.ListId);

        revocation!.GetBit(alloc.Index).Should().BeTrue(
            "revocation is not reversible — an Active event must not clear it");
    }

    private static CredentialStatusChangePayload Payload(string status, string listId, int index) => new()
    {
        CredentialId = "urn:uuid:a",
        NewStatus = status,
        IssuerWallet = Issuer,
        SubjectDid = "ws11qsubject",
        ChangedAt = DateTimeOffset.UtcNow,
        StatusListId = listId,
        StatusListIndex = index
    };

    private static IRegisterServiceClient RegisterWithRawListId(
        string status, string listId, int index, ulong docket)
    {
        var m = new Mock<IRegisterServiceClient>();
        m.Setup(r => r.GetTransactionsAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionPage
            {
                Page = 1,
                PageSize = 100,
                Total = 1,
                Transactions = [NewTx(Payload(status, listId, index), docket)]
            });
        return m.Object;
    }

    private static StatusListManager NewManager(
        IStatusListStore store, IRegisterServiceClient? register = null)
    {
        register ??= EmptyRegister();

        var services = new ServiceCollection();
        services.AddScoped(_ => register);
        var provider = services.BuildServiceProvider();

        var reconciler = new StatusListLedgerReconciler(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<StatusListLedgerReconciler>.Instance);

        return new StatusListManager(
            NullLogger<StatusListManager>.Instance,
            new Sorcha.Blueprint.Service.Configuration.StatusListUrls.Resolved(
                "https://example.test/api/v1/credentials/status-lists",
                "https://example.test/api/v1/credentials/ietf-status-lists"),
            store,
            reconciler);
    }

    private static IRegisterServiceClient EmptyRegister() => RegisterWith();

    /// <summary>
    /// A register carrying status-change events. Each event names BOTH lists in turn, exactly as the
    /// lifecycle endpoints emit them, so the reconciler picks the ones belonging to each list.
    /// </summary>
    private static IRegisterServiceClient RegisterWith(
        params (string NewStatus, int Index, ulong Docket)[] events)
    {
        var txs = new List<TransactionModel>();

        foreach (var e in events)
        {
            var listId = e.NewStatus.Equals("Revoked", StringComparison.OrdinalIgnoreCase)
                ? RevocationList
                : SuspensionList;

            var payload = new CredentialStatusChangePayload
            {
                CredentialId = "urn:uuid:a",
                NewStatus = e.NewStatus,
                IssuerWallet = Issuer,
                SubjectDid = "ws11qsubject",
                ChangedAt = DateTimeOffset.UtcNow,
                StatusListId = listId,
                StatusListIndex = e.Index
            };

            txs.Add(NewTx(payload, e.Docket));

            // An Active (reinstate) event belongs to the suspension list; the lifecycle endpoint
            // writes it there, so mirror that rather than inventing a second event.
        }

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

    private static TransactionModel NewTx(CredentialStatusChangePayload payload, ulong docket) => new()
    {
        TxId = Guid.NewGuid().ToString("N"),
        RegisterId = Register,
        DocketNumber = docket,
        TimeStamp = DateTime.UtcNow.AddSeconds(docket),
        MetaData = new TransactionMetaData
        {
            RegisterId = Register,
            TransactionType = TransactionType.CredentialStatusChange
        },
        PayloadCount = 1,
        Payloads =
        [
            new PayloadModel
            {
                Data = Convert.ToBase64String(
                    Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)))
            }
        ]
    };
}
