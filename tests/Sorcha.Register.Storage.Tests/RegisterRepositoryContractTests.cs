// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Register.Core.Storage;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Enums;
using Xunit;

namespace Sorcha.Register.Storage.Tests;

/// <summary>
/// Contract tests for IRegisterRepository. All implementations must satisfy these tests
/// to ensure interface compliance. Derive from this class and implement CreateRepository()
/// to test a specific implementation.
/// </summary>
public abstract class RegisterRepositoryContractTests
{
    protected abstract IRegisterRepository CreateRepository();

    private IRegisterRepository Sut => CreateRepository();

    // ===========================
    // Register Operations
    // ===========================

    [Fact]
    public async Task IsLocalRegisterAsync_ExistingRegister_ReturnsTrue()
    {
        var sut = Sut;
        await sut.InsertRegisterAsync(CreateRegister("contract-local-1"));

        var result = await sut.IsLocalRegisterAsync("contract-local-1");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsLocalRegisterAsync_NonexistentRegister_ReturnsFalse()
    {
        var result = await Sut.IsLocalRegisterAsync("nonexistent-register");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task GetRegistersAsync_ReturnsInsertedRegisters()
    {
        var sut = Sut;
        await sut.InsertRegisterAsync(CreateRegister("contract-list-1"));
        await sut.InsertRegisterAsync(CreateRegister("contract-list-2"));

        var result = (await sut.GetRegistersAsync()).ToList();

        result.Should().Contain(r => r.Id == "contract-list-1");
        result.Should().Contain(r => r.Id == "contract-list-2");
    }

    [Fact]
    public async Task QueryRegistersAsync_FiltersByPredicate()
    {
        var sut = Sut;
        await sut.InsertRegisterAsync(CreateRegister("contract-query-match"));
        await sut.InsertRegisterAsync(CreateRegister("contract-query-other"));

        var result = (await sut.QueryRegistersAsync(r => r.Id == "contract-query-match")).ToList();

        result.Should().ContainSingle(r => r.Id == "contract-query-match");
    }

    [Fact]
    public async Task GetRegisterAsync_ExistingRegister_ReturnsRegister()
    {
        var sut = Sut;
        await sut.InsertRegisterAsync(CreateRegister("contract-get-1"));

        var result = await sut.GetRegisterAsync("contract-get-1");

        result.Should().NotBeNull();
        result!.Id.Should().Be("contract-get-1");
    }

    [Fact]
    public async Task GetRegisterAsync_NonexistentRegister_ReturnsNull()
    {
        var result = await Sut.GetRegisterAsync("nonexistent");

        result.Should().BeNull();
    }

    [Fact]
    public async Task InsertRegisterAsync_ReturnsInsertedRegister()
    {
        var register = CreateRegister("contract-insert-1");

        var result = await Sut.InsertRegisterAsync(register);

        result.Should().NotBeNull();
        result.Id.Should().Be("contract-insert-1");
    }

    [Fact]
    public async Task UpdateRegisterAsync_ModifiesRegister()
    {
        var sut = Sut;
        var register = CreateRegister("contract-update-1");
        await sut.InsertRegisterAsync(register);

        register.Height = 42;
        var result = await sut.UpdateRegisterAsync(register);

        result.Height.Should().Be(42);
    }

    [Fact]
    public async Task DeleteRegisterAsync_RemovesRegister()
    {
        var sut = Sut;
        await sut.InsertRegisterAsync(CreateRegister("contract-delete-1"));

        await sut.DeleteRegisterAsync("contract-delete-1");

        var result = await sut.GetRegisterAsync("contract-delete-1");
        result.Should().BeNull();
    }

    [Fact]
    public async Task CountRegistersAsync_ReturnsCorrectCount()
    {
        var sut = Sut;
        var initialCount = await sut.CountRegistersAsync();
        await sut.InsertRegisterAsync(CreateRegister("contract-count-1"));
        await sut.InsertRegisterAsync(CreateRegister("contract-count-2"));

        var result = await sut.CountRegistersAsync();

        result.Should().Be(initialCount + 2);
    }

    // ===========================
    // DocketHeader Operations
    // ===========================

    [Fact]
    public virtual async Task GetDocketsAsync_ReturnsInsertedDockets()
    {
        var sut = Sut;
        await sut.InsertRegisterAsync(CreateRegister("contract-docket-list"));
        await sut.InsertDocketAsync(CreateDocket(1, "contract-docket-list"));
        await sut.InsertDocketAsync(CreateDocket(2, "contract-docket-list"));

        var result = (await sut.GetDocketsAsync("contract-docket-list")).ToList();

        result.Should().HaveCountGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task GetDocketAsync_ExistingDocket_ReturnsDocket()
    {
        var sut = Sut;
        await sut.InsertRegisterAsync(CreateRegister("contract-docket-get"));
        await sut.InsertDocketAsync(CreateDocket(10, "contract-docket-get"));

        var result = await sut.GetDocketAsync("contract-docket-get", 10);

        result.Should().NotBeNull();
        result!.Id.Should().Be(10);
    }

    [Fact]
    public async Task GetDocketAsync_NonexistentDocket_ReturnsNull()
    {
        var sut = Sut;
        await sut.InsertRegisterAsync(CreateRegister("contract-docket-miss"));

        var result = await sut.GetDocketAsync("contract-docket-miss", 999);

        result.Should().BeNull();
    }

    [Fact]
    public async Task InsertDocketAsync_ReturnsDocket()
    {
        var sut = Sut;
        await sut.InsertRegisterAsync(CreateRegister("contract-docket-insert"));

        var result = await sut.InsertDocketAsync(CreateDocket(5, "contract-docket-insert"));

        result.Should().NotBeNull();
        result.Id.Should().Be(5);
    }

    [Fact]
    public async Task UpdateRegisterHeightAsync_UpdatesHeight()
    {
        var sut = Sut;
        await sut.InsertRegisterAsync(CreateRegister("contract-height"));

        await sut.UpdateRegisterHeightAsync("contract-height", 99);

        var register = await sut.GetRegisterAsync("contract-height");
        register.Should().NotBeNull();
        register!.Height.Should().Be(99);
    }

    // ===========================
    // Transaction Operations
    // ===========================

    [Fact]
    public async Task GetTransactionsAsync_ReturnsQueryable()
    {
        var sut = Sut;
        await sut.InsertRegisterAsync(CreateRegister("contract-tx-list"));
        await sut.InsertTransactionAsync(CreateTransaction("tx-list-1", "contract-tx-list"));

        var result = await sut.GetTransactionsAsync("contract-tx-list");

        result.Should().NotBeNull();
        result.Count().Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task GetTransactionAsync_ExistingTransaction_ReturnsTransaction()
    {
        var sut = Sut;
        await sut.InsertRegisterAsync(CreateRegister("contract-tx-get"));
        var txId = "tx-get-1".PadRight(64, '0');
        await sut.InsertTransactionAsync(CreateTransaction("tx-get-1", "contract-tx-get"));

        var result = await sut.GetTransactionAsync("contract-tx-get", txId);

        result.Should().NotBeNull();
        result!.TxId.Should().Be(txId);
    }

    [Fact]
    public async Task GetTransactionAsync_NonexistentTransaction_ReturnsNull()
    {
        var sut = Sut;
        await sut.InsertRegisterAsync(CreateRegister("contract-tx-miss"));

        var result = await sut.GetTransactionAsync("contract-tx-miss", "nonexistent".PadRight(64, '0'));

        result.Should().BeNull();
    }

    [Fact]
    public async Task InsertTransactionAsync_ReturnsTransaction()
    {
        var sut = Sut;
        await sut.InsertRegisterAsync(CreateRegister("contract-tx-insert"));
        var txId = "tx-ins-1".PadRight(64, '0');

        var result = await sut.InsertTransactionAsync(CreateTransaction("tx-ins-1", "contract-tx-insert"));

        result.Should().NotBeNull();
        result.TxId.Should().Be(txId);
    }

    [Fact]
    public async Task QueryTransactionsAsync_FiltersByPredicate()
    {
        var sut = Sut;
        await sut.InsertRegisterAsync(CreateRegister("contract-tx-query"));
        await sut.InsertTransactionAsync(CreateTransaction("tx-query-match", "contract-tx-query", sender: "alice"));
        await sut.InsertTransactionAsync(CreateTransaction("tx-query-other", "contract-tx-query", sender: "bob"));

        var result = (await sut.QueryTransactionsAsync("contract-tx-query", t => t.SenderWallet == "alice")).ToList();

        result.Should().ContainSingle();
        result[0].SenderWallet.Should().Be("alice");
    }

    [Fact]
    public async Task GetTransactionsByDocketAsync_ReturnsMatchingTransactions()
    {
        var sut = Sut;
        await sut.InsertRegisterAsync(CreateRegister("contract-tx-docket"));
        await sut.InsertDocketAsync(CreateDocket(1, "contract-tx-docket"));

        var tx = CreateTransaction("tx-in-docket", "contract-tx-docket");
        tx.DocketNumber = 1;
        await sut.InsertTransactionAsync(tx);

        var txOther = CreateTransaction("tx-no-docket", "contract-tx-docket");
        txOther.DocketNumber = 2;
        await sut.InsertTransactionAsync(txOther);

        var result = (await sut.GetTransactionsByDocketAsync("contract-tx-docket", 1)).ToList();

        result.Should().ContainSingle();
        result[0].DocketNumber.Should().Be(1);
    }

    // ===========================
    // Advanced Queries
    // ===========================

    [Fact]
    public async Task GetAllTransactionsByRecipientAddressAsync_ReturnsMatches()
    {
        var sut = Sut;
        await sut.InsertRegisterAsync(CreateRegister("contract-tx-recipient"));
        var tx = CreateTransaction("tx-to-alice", "contract-tx-recipient");
        tx.RecipientsWallets = new List<string> { "alice-address" };
        await sut.InsertTransactionAsync(tx);

        var result = (await sut.GetAllTransactionsByRecipientAddressAsync("contract-tx-recipient", "alice-address")).ToList();

        result.Should().ContainSingle();
    }

    [Fact]
    public async Task GetAllTransactionsBySenderAddressAsync_ReturnsMatches()
    {
        var sut = Sut;
        await sut.InsertRegisterAsync(CreateRegister("contract-tx-sender"));
        await sut.InsertTransactionAsync(CreateTransaction("tx-from-bob", "contract-tx-sender", sender: "bob-address"));

        var result = (await sut.GetAllTransactionsBySenderAddressAsync("contract-tx-sender", "bob-address")).ToList();

        result.Should().ContainSingle();
        result[0].SenderWallet.Should().Be("bob-address");
    }

    [Fact]
    public async Task GetTransactionsByPrevTxIdAsync_ReturnsMatches()
    {
        var sut = Sut;
        await sut.InsertRegisterAsync(CreateRegister("contract-tx-prev"));
        var prevTxId = "prev-parent".PadRight(64, '0');

        var tx1 = CreateTransaction("tx-child-a", "contract-tx-prev");
        tx1.PrevTxId = prevTxId;
        await sut.InsertTransactionAsync(tx1);

        var tx2 = CreateTransaction("tx-child-b", "contract-tx-prev");
        tx2.PrevTxId = prevTxId;
        await sut.InsertTransactionAsync(tx2);

        await sut.InsertTransactionAsync(CreateTransaction("tx-unrelated", "contract-tx-prev"));

        var result = (await sut.GetTransactionsByPrevTxIdAsync("contract-tx-prev", prevTxId)).ToList();

        result.Should().HaveCount(2);
        result.Should().AllSatisfy(t => t.PrevTxId.Should().Be(prevTxId));
    }

    [Fact]
    public async Task GetTransactionsByPrevTxIdAsync_EmptyPrevTxId_ReturnsEmpty()
    {
        var sut = Sut;
        await sut.InsertRegisterAsync(CreateRegister("contract-tx-prev-empty"));
        await sut.InsertTransactionAsync(CreateTransaction("tx-some", "contract-tx-prev-empty"));

        var result = await sut.GetTransactionsByPrevTxIdAsync("contract-tx-prev-empty", "");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTransactionsByPrevTxIdAsync_NoMatches_ReturnsEmpty()
    {
        var sut = Sut;
        await sut.InsertRegisterAsync(CreateRegister("contract-tx-prev-none"));

        var result = await sut.GetTransactionsByPrevTxIdAsync("contract-tx-prev-none", "no-such-prev".PadRight(64, '0'));

        result.Should().BeEmpty();
    }

    // ===========================
    // Test Data Helpers
    // ===========================

    protected static Models.Register CreateRegister(string id) => new()
    {
        Id = id,
        Name = $"Test Register {id}",
        Height = 0,
        Status = RegisterStatus.Online,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    protected static DocketHeader CreateDocket(ulong id, string registerId) => new()
    {
        Id = id,
        RegisterId = registerId,
        Hash = $"hash-{id}",
        PreviousHash = id > 1 ? $"hash-{id - 1}" : "",
        TransactionIds = new List<string>(),
        TimeStamp = DateTime.UtcNow,
        State = DocketState.Sealed
    };

    protected static TransactionModel CreateTransaction(
        string txId,
        string registerId,
        string sender = "sender-wallet") => new()
    {
        TxId = txId.PadRight(64, '0'),
        RegisterId = registerId,
        SenderWallet = sender,
        RecipientsWallets = new List<string> { "recipient-wallet" },
        TimeStamp = DateTime.UtcNow,
        Signature = "test-signature",
        PrevTxId = string.Empty
    };

    // ===========================
    // Pushed-down transaction reads (perf T1)
    // ===========================

    private static TransactionModel Tx(
        string txId, string registerId, DateTime ts,
        TransactionType type = TransactionType.Action, ulong? docket = null) => new()
    {
        TxId = txId.PadRight(64, '0'),
        RegisterId = registerId,
        SenderWallet = "sender-wallet",
        RecipientsWallets = new List<string> { "recipient-wallet" },
        TimeStamp = ts,
        DocketNumber = docket,
        Signature = "sig",
        PrevTxId = string.Empty,
        MetaData = new TransactionMetaData { RegisterId = registerId, TransactionType = type },
    };

    [Fact]
    public async Task GetLatestTransactionsAsync_ReturnsNewestFirstPage()
    {
        var sut = Sut;
        const string reg = "contract-latest-page";
        await sut.InsertRegisterAsync(CreateRegister(reg));
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await sut.InsertTransactionAsync(Tx("lp-1", reg, t0));
        await sut.InsertTransactionAsync(Tx("lp-2", reg, t0.AddMinutes(1)));
        await sut.InsertTransactionAsync(Tx("lp-3", reg, t0.AddMinutes(2)));

        var page = await sut.GetLatestTransactionsAsync(reg, skip: 0, take: 2);

        page.Should().HaveCount(2);
        page[0].TxId.Should().StartWith("lp-3"); // newest first
        page[1].TxId.Should().StartWith("lp-2");
    }

    [Fact]
    public async Task GetLatestTransactionAsync_ReturnsSingleNewest()
    {
        var sut = Sut;
        const string reg = "contract-latest-one";
        await sut.InsertRegisterAsync(CreateRegister(reg));
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await sut.InsertTransactionAsync(Tx("lo-1", reg, t0));
        await sut.InsertTransactionAsync(Tx("lo-2", reg, t0.AddMinutes(5)));

        var latest = await sut.GetLatestTransactionAsync(reg);

        latest.Should().NotBeNull();
        latest!.TxId.Should().StartWith("lo-2");
    }

    [Fact]
    public async Task CountTransactionsAsync_ReturnsCount()
    {
        var sut = Sut;
        const string reg = "contract-count-tx";
        await sut.InsertRegisterAsync(CreateRegister(reg));
        await sut.InsertTransactionAsync(Tx("ct-1", reg, DateTime.UtcNow));
        await sut.InsertTransactionAsync(Tx("ct-2", reg, DateTime.UtcNow.AddSeconds(1)));

        (await sut.CountTransactionsAsync(reg)).Should().Be(2);
    }

    [Fact]
    public async Task GetTransactionsByTypeAsync_FiltersToTypeAndSortsByDocketAscending()
    {
        var sut = Sut;
        const string reg = "contract-by-type";
        await sut.InsertRegisterAsync(CreateRegister(reg));
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await sut.InsertTransactionAsync(Tx("bt-ctrl-0", reg, t0, TransactionType.Control, docket: 0));
        await sut.InsertTransactionAsync(Tx("bt-action", reg, t0.AddMinutes(1), TransactionType.Action, docket: 1));
        await sut.InsertTransactionAsync(Tx("bt-ctrl-2", reg, t0.AddMinutes(2), TransactionType.Control, docket: 2));

        var ctrl = await sut.GetTransactionsByTypeAsync(reg, TransactionType.Control, TransactionSort.DocketNumberAscending);

        ctrl.Should().HaveCount(2, "only Control transactions match");
        ctrl[0].TxId.Should().StartWith("bt-ctrl-0"); // docket ascending
        ctrl[1].TxId.Should().StartWith("bt-ctrl-2");
    }

    [Fact]
    public async Task GetTransactionsBeforeAsync_ReturnsStrictlyOlderNewestFirst()
    {
        var sut = Sut;
        const string reg = "contract-before";
        await sut.InsertRegisterAsync(CreateRegister(reg));
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await sut.InsertTransactionAsync(Tx("bf-1", reg, t0));
        await sut.InsertTransactionAsync(Tx("bf-2", reg, t0.AddMinutes(1)));
        await sut.InsertTransactionAsync(Tx("bf-3", reg, t0.AddMinutes(2)));

        var before = await sut.GetTransactionsBeforeAsync(reg, t0.AddMinutes(2), take: 10);

        before.Should().HaveCount(2, "bf-3 at the cursor is excluded (strictly before)");
        before[0].TxId.Should().StartWith("bf-2"); // newest-first
        before[1].TxId.Should().StartWith("bf-1");
    }

    [Fact]
    public async Task CountTransactionsBeforeAsync_CountsStrictlyOlder()
    {
        var sut = Sut;
        const string reg = "contract-count-before";
        await sut.InsertRegisterAsync(CreateRegister(reg));
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await sut.InsertTransactionAsync(Tx("cb-1", reg, t0));
        await sut.InsertTransactionAsync(Tx("cb-2", reg, t0.AddMinutes(1)));
        await sut.InsertTransactionAsync(Tx("cb-3", reg, t0.AddMinutes(2)));

        (await sut.CountTransactionsBeforeAsync(reg, t0.AddMinutes(2))).Should().Be(2); // cb-1, cb-2
        (await sut.CountTransactionsBeforeAsync(reg, t0)).Should().Be(0); // strictly before, none
    }
}
