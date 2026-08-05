// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Enums;
using Sorcha.Register.Storage.MongoDB;
using Testcontainers.MongoDb;
using Xunit;
using RegisterEntity = Sorcha.Register.Models.Register;

namespace Sorcha.Register.Storage.MongoDB.Tests;

/// <summary>
/// Integration tests for MongoRegisterRepository using Testcontainers.
/// </summary>
public class MongoRegisterRepositoryIntegrationTests : IAsyncLifetime
{
    private readonly MongoDbContainer _container;
    private MongoRegisterRepository _sut = null!;
    private IMongoDatabase _database = null!;
    private readonly ILogger<MongoRegisterRepository> _logger = NullLogger<MongoRegisterRepository>.Instance;

    public MongoRegisterRepositoryIntegrationTests()
    {
        _container = new MongoDbBuilder()
            .WithImage("mongo:7.0")
            .Build();
    }

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        var client = new MongoClient(_container.GetConnectionString());
        _database = client.GetDatabase($"test_{Guid.NewGuid():N}");

        _sut = new MongoRegisterRepository(
            _database,
            "registers",
            "transactions",
            "dockets",
            _logger);
    }

    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    // ===========================
    // Register Tests
    // ===========================

    [Fact]
    public async Task InsertRegisterAsync_StoresRegister()
    {
        // Arrange
        var register = CreateTestRegister("test-register-1");

        // Act
        var result = await _sut.InsertRegisterAsync(register);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be("test-register-1");
    }

    [Fact]
    public async Task GetRegisterAsync_ReturnsStoredRegister()
    {
        // Arrange
        var register = CreateTestRegister("test-register-2");
        await _sut.InsertRegisterAsync(register);

        // Act
        var result = await _sut.GetRegisterAsync("test-register-2");

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be("test-register-2");
        result.Name.Should().Be("Test Register test-register-2");
    }

    [Fact]
    public async Task GetRegistersAsync_ReturnsAllRegisters()
    {
        // Arrange
        await _sut.InsertRegisterAsync(CreateTestRegister("reg-1"));
        await _sut.InsertRegisterAsync(CreateTestRegister("reg-2"));
        await _sut.InsertRegisterAsync(CreateTestRegister("reg-3"));

        // Act
        var result = await _sut.GetRegistersAsync();

        // Assert
        result.Should().HaveCount(3);
    }

    [Fact]
    public async Task QueryRegistersAsync_FiltersByStatus()
    {
        // Arrange
        var reg1 = CreateTestRegister("reg-t1-1");
        var reg2 = CreateTestRegister("reg-t1-2");
        var reg3 = CreateTestRegister("reg-t2-1");
        reg3.Status = RegisterStatus.Online;

        await _sut.InsertRegisterAsync(reg1);
        await _sut.InsertRegisterAsync(reg2);
        await _sut.InsertRegisterAsync(reg3);

        // Act
        var result = await _sut.QueryRegistersAsync(r => r.Status == RegisterStatus.Offline);

        // Assert
        result.Should().HaveCount(2);
        result.Should().OnlyContain(r => r.Status == RegisterStatus.Offline);
    }

    [Fact]
    public async Task UpdateRegisterAsync_UpdatesFields()
    {
        // Arrange
        var register = CreateTestRegister("update-test");
        await _sut.InsertRegisterAsync(register);

        register.Name = "Updated Name";
        register.Status = RegisterStatus.Online;

        // Act
        var result = await _sut.UpdateRegisterAsync(register);

        // Assert
        result.Name.Should().Be("Updated Name");
        result.Status.Should().Be(RegisterStatus.Online);

        // Verify persisted
        var fetched = await _sut.GetRegisterAsync("update-test");
        fetched!.Name.Should().Be("Updated Name");
    }

    [Fact]
    public async Task DeleteRegisterAsync_RemovesRegisterAndAssociatedData()
    {
        // Arrange
        var register = CreateTestRegister("delete-test");
        await _sut.InsertRegisterAsync(register);
        await _sut.InsertTransactionAsync(CreateTestTransaction("tx-1", "delete-test"));

        // Act
        await _sut.DeleteRegisterAsync("delete-test");

        // Assert
        var fetchedRegister = await _sut.GetRegisterAsync("delete-test");
        fetchedRegister.Should().BeNull();

        var fetchedTx = await _sut.GetTransactionAsync("delete-test", "tx-1".PadRight(64, '0'));
        fetchedTx.Should().BeNull();
    }

    [Fact]
    public async Task IsLocalRegisterAsync_ReturnsCorrectValue()
    {
        // Arrange
        await _sut.InsertRegisterAsync(CreateTestRegister("local-test"));

        // Act & Assert
        (await _sut.IsLocalRegisterAsync("local-test")).Should().BeTrue();
        (await _sut.IsLocalRegisterAsync("non-existent")).Should().BeFalse();
    }

    [Fact]
    public async Task CountRegistersAsync_ReturnsCorrectCount()
    {
        // Arrange
        await _sut.InsertRegisterAsync(CreateTestRegister("count-1"));
        await _sut.InsertRegisterAsync(CreateTestRegister("count-2"));

        // Act
        var count = await _sut.CountRegistersAsync();

        // Assert
        count.Should().Be(2);
    }

    // ===========================
    // Transaction Tests
    // ===========================

    [Fact]
    public async Task InsertTransactionAsync_StoresTransaction()
    {
        // Arrange
        await _sut.InsertRegisterAsync(CreateTestRegister("tx-reg"));
        var tx = CreateTestTransaction("tx-insert-1", "tx-reg");

        // Act
        var result = await _sut.InsertTransactionAsync(tx);

        // Assert
        result.Should().NotBeNull();
        result.TxId.Should().Be("tx-insert-1".PadRight(64, '0'));
        result.RegisterId.Should().Be("tx-reg");
    }

    [Fact]
    public async Task GetTransactionAsync_ReturnsStoredTransaction()
    {
        // Arrange
        await _sut.InsertRegisterAsync(CreateTestRegister("get-tx-reg"));
        var tx = CreateTestTransaction("get-tx-1", "get-tx-reg");
        await _sut.InsertTransactionAsync(tx);

        // Act
        var result = await _sut.GetTransactionAsync("get-tx-reg", "get-tx-1".PadRight(64, '0'));

        // Assert
        result.Should().NotBeNull();
        result!.TxId.Should().Be("get-tx-1".PadRight(64, '0'));
    }

    [Fact]
    public async Task GetTransactionsAsync_ReturnsQueryable()
    {
        // Arrange
        await _sut.InsertRegisterAsync(CreateTestRegister("query-tx-reg"));
        for (int i = 1; i <= 10; i++)
        {
            await _sut.InsertTransactionAsync(CreateTestTransaction($"q-tx-{i:D5}", "query-tx-reg"));
        }

        // Act
        var result = await _sut.GetTransactionsAsync("query-tx-reg");
        var list = result.ToList();

        // Assert
        list.Should().HaveCount(10);
    }

    [Fact]
    public async Task QueryTransactionsAsync_FiltersByPredicate()
    {
        // Arrange
        await _sut.InsertRegisterAsync(CreateTestRegister("pred-tx-reg"));
        await _sut.InsertTransactionAsync(CreateTestTransaction("pred-1", "pred-tx-reg", "sender-a"));
        await _sut.InsertTransactionAsync(CreateTestTransaction("pred-2", "pred-tx-reg", "sender-b"));
        await _sut.InsertTransactionAsync(CreateTestTransaction("pred-3", "pred-tx-reg", "sender-a"));

        // Act
        var result = await _sut.QueryTransactionsAsync(
            "pred-tx-reg",
            t => t.SenderWallet == "sender-a");

        // Assert
        result.Should().HaveCount(2);
        result.Should().OnlyContain(t => t.SenderWallet == "sender-a");
    }

    [Fact]
    public async Task GetAllTransactionsBySenderAddressAsync_ReturnsSenderTransactions()
    {
        // Arrange
        await _sut.InsertRegisterAsync(CreateTestRegister("sender-test-reg"));
        await _sut.InsertTransactionAsync(CreateTestTransaction("s-1", "sender-test-reg", "wallet-x"));
        await _sut.InsertTransactionAsync(CreateTestTransaction("s-2", "sender-test-reg", "wallet-y"));
        await _sut.InsertTransactionAsync(CreateTestTransaction("s-3", "sender-test-reg", "wallet-x"));

        // Act
        var result = await _sut.GetAllTransactionsBySenderAddressAsync("sender-test-reg", "wallet-x");

        // Assert
        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetAllTransactionsByRecipientAddressAsync_ReturnsRecipientTransactions()
    {
        // Arrange
        await _sut.InsertRegisterAsync(CreateTestRegister("recip-test-reg"));

        var tx1 = CreateTestTransaction("r-1", "recip-test-reg");
        tx1.RecipientsWallets = new[] { "recip-a", "recip-b" };

        var tx2 = CreateTestTransaction("r-2", "recip-test-reg");
        tx2.RecipientsWallets = new[] { "recip-c" };

        var tx3 = CreateTestTransaction("r-3", "recip-test-reg");
        tx3.RecipientsWallets = new[] { "recip-a" };

        await _sut.InsertTransactionAsync(tx1);
        await _sut.InsertTransactionAsync(tx2);
        await _sut.InsertTransactionAsync(tx3);

        // Act
        var result = await _sut.GetAllTransactionsByRecipientAddressAsync("recip-test-reg", "recip-a");

        // Assert
        result.Should().HaveCount(2);
    }

    // ===========================
    // DocketHeader Tests
    // ===========================

    [Fact]
    public async Task InsertDocketAsync_StoresDocket()
    {
        // Arrange
        await _sut.InsertRegisterAsync(CreateTestRegister("docket-reg"));
        var docket = CreateTestDocket(1, "docket-reg");

        // Act
        var result = await _sut.InsertDocketAsync(docket);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be(1);
        result.RegisterId.Should().Be("docket-reg");
    }

    [Fact]
    public async Task GetDocketAsync_ReturnsStoredDocket()
    {
        // Arrange
        await _sut.InsertRegisterAsync(CreateTestRegister("get-docket-reg"));
        await _sut.InsertDocketAsync(CreateTestDocket(1, "get-docket-reg"));

        // Act
        var result = await _sut.GetDocketAsync("get-docket-reg", 1);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(1);
        result.Hash.Should().Be("hash-1");
    }

    [Fact]
    public async Task GetDocketsAsync_ReturnsAllDocketsInOrder()
    {
        // Arrange
        await _sut.InsertRegisterAsync(CreateTestRegister("multi-docket-reg"));
        await _sut.InsertDocketAsync(CreateTestDocket(3, "multi-docket-reg"));
        await _sut.InsertDocketAsync(CreateTestDocket(1, "multi-docket-reg"));
        await _sut.InsertDocketAsync(CreateTestDocket(2, "multi-docket-reg"));

        // Act
        var result = (await _sut.GetDocketsAsync("multi-docket-reg")).ToList();

        // Assert
        result.Should().HaveCount(3);
        result[0].Id.Should().Be(1);
        result[1].Id.Should().Be(2);
        result[2].Id.Should().Be(3);
    }

    [Fact]
    public async Task GetTransactionsByDocketAsync_ReturnsDocketTransactions()
    {
        // Arrange
        await _sut.InsertRegisterAsync(CreateTestRegister("docket-tx-reg"));

        var tx1 = CreateTestTransaction("dtx-1", "docket-tx-reg");
        tx1.DocketNumber = 1;
        var tx2 = CreateTestTransaction("dtx-2", "docket-tx-reg");
        tx2.DocketNumber = 1;
        var tx3 = CreateTestTransaction("dtx-3", "docket-tx-reg");
        tx3.DocketNumber = 2;

        await _sut.InsertTransactionAsync(tx1);
        await _sut.InsertTransactionAsync(tx2);
        await _sut.InsertTransactionAsync(tx3);

        // Act
        var result = await _sut.GetTransactionsByDocketAsync("docket-tx-reg", 1);

        // Assert
        result.Should().HaveCount(2);
        result.Should().OnlyContain(t => t.DocketNumber == 1);
    }

    [Fact]
    public async Task UpdateRegisterHeightAsync_UpdatesHeight()
    {
        // Arrange
        await _sut.InsertRegisterAsync(CreateTestRegister("height-test"));

        // Act
        await _sut.UpdateRegisterHeightAsync("height-test", 42);

        // Assert
        var register = await _sut.GetRegisterAsync("height-test");
        register!.Height.Should().Be(42);
    }

    // ===========================
    // PrevTxId Query Tests
    // ===========================

    [Fact]
    public async Task GetTransactionsByPrevTxIdAsync_ReturnsMatchingTransactions()
    {
        // Arrange
        var registerId = "prevtx-test-reg";
        await _sut.InsertRegisterAsync(CreateTestRegister(registerId));

        var prevTxId = "abcdef1234567890".PadRight(64, '0');
        var tx1 = CreateTestTransaction("ptx-1", registerId);
        tx1.PrevTxId = prevTxId;
        var tx2 = CreateTestTransaction("ptx-2", registerId);
        tx2.PrevTxId = prevTxId;
        var tx3 = CreateTestTransaction("ptx-3", registerId);
        tx3.PrevTxId = "different".PadRight(64, '0');

        await _sut.InsertTransactionAsync(tx1);
        await _sut.InsertTransactionAsync(tx2);
        await _sut.InsertTransactionAsync(tx3);

        // Act
        var result = (await _sut.GetTransactionsByPrevTxIdAsync(registerId, prevTxId)).ToList();

        // Assert
        result.Should().HaveCount(2);
        result.Should().OnlyContain(t => t.PrevTxId == prevTxId);
    }

    [Fact]
    public async Task GetTransactionsByPrevTxIdAsync_ReturnsEmptyForNullPrevTxId()
    {
        // Arrange
        await _sut.InsertRegisterAsync(CreateTestRegister("prevtx-null-reg"));

        // Act
        var result = await _sut.GetTransactionsByPrevTxIdAsync("prevtx-null-reg", null!);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTransactionsByPrevTxIdAsync_ReturnsEmptyForEmptyPrevTxId()
    {
        // Arrange
        await _sut.InsertRegisterAsync(CreateTestRegister("prevtx-empty-reg"));

        // Act
        var result = await _sut.GetTransactionsByPrevTxIdAsync("prevtx-empty-reg", "");

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTransactionsByPrevTxIdAsync_ResultsSortedByTimestampDescending()
    {
        // Arrange
        var registerId = "prevtx-sort-reg";
        await _sut.InsertRegisterAsync(CreateTestRegister(registerId));

        var prevTxId = "sorttest".PadRight(64, '0');
        var older = CreateTestTransaction("sort-1", registerId);
        older.PrevTxId = prevTxId;
        older.TimeStamp = DateTime.UtcNow.AddMinutes(-10);

        var newer = CreateTestTransaction("sort-2", registerId);
        newer.PrevTxId = prevTxId;
        newer.TimeStamp = DateTime.UtcNow;

        await _sut.InsertTransactionAsync(older);
        await _sut.InsertTransactionAsync(newer);

        // Act
        var result = (await _sut.GetTransactionsByPrevTxIdAsync(registerId, prevTxId)).ToList();

        // Assert
        result.Should().HaveCount(2);
        result[0].TimeStamp.Should().BeAfter(result[1].TimeStamp);
    }

    [Fact]
    public async Task PrevTxIdIndex_ExistsOnTransactionCollection()
    {
        // Arrange — insert a register to ensure the collection exists
        var registerId = "idx-verify-reg";
        await _sut.InsertRegisterAsync(CreateTestRegister(registerId));
        await _sut.InsertTransactionAsync(CreateTestTransaction("idx-tx-1", registerId));

        // Manually create indexes (test constructor doesn't auto-create)
        var collection = _database.GetCollection<TransactionModel>("transactions");
        var indexes = new List<CreateIndexModel<TransactionModel>>
        {
            new(Builders<TransactionModel>.IndexKeys.Ascending(t => t.PrevTxId))
        };
        await collection.Indexes.CreateManyAsync(indexes);

        // Act — list all indexes on the collection
        var cursor = await collection.Indexes.ListAsync();
        var indexList = await cursor.ToListAsync();
        var indexNames = indexList.Select(i => i["name"].AsString).ToList();

        // Assert — verify PrevTxId index exists
        indexNames.Should().Contain(name => name.Contains("PrevTxId"));
    }

    // ===========================
    // Helper Methods
    // ===========================

    private static RegisterEntity CreateTestRegister(string id)
    {
        return new RegisterEntity
        {
            Id = id,
            Name = $"Test Register {id}",
            Height = 0,
            Status = RegisterStatus.Offline,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    private static TransactionModel CreateTestTransaction(
        string txId,
        string registerId,
        string? senderWallet = null)
    {
        return new TransactionModel
        {
            TxId = txId.PadRight(64, '0'),
            RegisterId = registerId,
            SenderWallet = senderWallet ?? "default-sender",
            RecipientsWallets = new[] { "recipient-1" },
            TimeStamp = DateTime.UtcNow,
            Signature = "test-signature"
        };
    }

    private static DocketHeader CreateTestDocket(ulong id, string registerId)
    {
        return new DocketHeader
        {
            Id = id,
            RegisterId = registerId,
            Hash = $"hash-{id}",
            PreviousHash = id > 1 ? $"hash-{id - 1}" : "",
            TransactionIds = new List<string>(),
            TimeStamp = DateTime.UtcNow,
            State = DocketState.Sealed
        };
    }
}
