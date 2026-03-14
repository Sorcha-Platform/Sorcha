// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Register.Core.Managers;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Enums;
using Sorcha.Register.Storage.InMemory;
using Xunit;

namespace Sorcha.Register.Core.Tests.Managers;

public class QueryManagerTests
{
    private readonly InMemoryRegisterRepository _repository;
    private readonly QueryManager _manager;
    private readonly string _testRegisterId;

    public QueryManagerTests()
    {
        _repository = new InMemoryRegisterRepository();
        _manager = new QueryManager(_repository);

        // Create a test register
        var register = new Sorcha.Register.Models.Register
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Test Register",
            TenantId = "tenant123",
            Height = 0,
            Status = RegisterStatus.Offline
        };
        _testRegisterId = register.Id;
        _repository.InsertRegisterAsync(register).Wait();
    }

    [Fact]
    public async Task GetTransactionsByWalletAsync_ShouldReturnTransactionsForSenderOrRecipient()
    {
        // Arrange
        var walletAddress = "wallet123";
        await SeedTransactionsAsync(walletAddress);

        // Act
        var result = await _manager.GetTransactionsByWalletPaginatedAsync(_testRegisterId, walletAddress, 1, 20);

        // Assert
        result.Should().NotBeNull();
        result.Items.Should().HaveCountGreaterThan(0);
        result.Items.Should().OnlyContain(t =>
            t.SenderWallet == walletAddress ||
            t.RecipientsWallets.Contains(walletAddress));
    }

    [Fact]
    public async Task GetTransactionsByWalletAsync_ShouldPaginateResults()
    {
        // Arrange
        var walletAddress = "wallet123";
        await SeedTransactionsAsync(walletAddress);

        // Act
        var page1 = await _manager.GetTransactionsByWalletPaginatedAsync(_testRegisterId, walletAddress, 1, 2);
        var page2 = await _manager.GetTransactionsByWalletPaginatedAsync(_testRegisterId, walletAddress, 2, 2);

        // Assert
        page1.Items.Should().HaveCount(2);
        page2.Items.Should().HaveCountGreaterThan(0);
        page1.Page.Should().Be(1);
        page2.Page.Should().Be(2);
        page1.Items.Should().NotIntersectWith(page2.Items);
    }

    [Fact]
    public async Task GetTransactionsByWalletAsync_ShouldCalculateTotalPages()
    {
        // Arrange
        var walletAddress = "wallet123";
        await SeedTransactionsAsync(walletAddress, count: 5);

        // Act
        var result = await _manager.GetTransactionsByWalletPaginatedAsync(_testRegisterId, walletAddress, 1, 2);

        // Assert
        result.TotalPages.Should().Be(3); // 5 transactions / 2 per page = 3 pages
        result.TotalCount.Should().Be(5);
    }

    [Fact]
    public async Task GetTransactionsByWalletAsync_ShouldSetHasNextAndPreviousPage()
    {
        // Arrange
        var walletAddress = "wallet123";
        await SeedTransactionsAsync(walletAddress, count: 5);

        // Act
        var page1 = await _manager.GetTransactionsByWalletPaginatedAsync(_testRegisterId, walletAddress, 1, 2);
        var page2 = await _manager.GetTransactionsByWalletPaginatedAsync(_testRegisterId, walletAddress, 2, 2);
        var page3 = await _manager.GetTransactionsByWalletPaginatedAsync(_testRegisterId, walletAddress, 3, 2);

        // Assert
        page1.HasPreviousPage.Should().BeFalse();
        page1.HasNextPage.Should().BeTrue();

        page2.HasPreviousPage.Should().BeTrue();
        page2.HasNextPage.Should().BeTrue();

        page3.HasPreviousPage.Should().BeTrue();
        page3.HasNextPage.Should().BeFalse();
    }

    [Fact]
    public async Task GetTransactionsBySenderAsync_ShouldReturnOnlySenderTransactions()
    {
        // Arrange
        var senderAddress = "sender123";
        await _repository.InsertTransactionAsync(CreateTransaction(senderAddress, "recipient1"));
        await _repository.InsertTransactionAsync(CreateTransaction(senderAddress, "recipient2"));
        await _repository.InsertTransactionAsync(CreateTransaction("otherSender", "recipient3"));

        // Act
        var result = await _manager.GetTransactionsByWalletPaginatedAsync(_testRegisterId, senderAddress, 1, 20, asSender: true, asRecipient: false);

        // Assert
        result.Items.Should().HaveCount(2);
        result.Items.Should().OnlyContain(t => t.SenderWallet == senderAddress);
    }

    [Fact]
    public async Task GetTransactionsBySenderAsync_ShouldOrderByTimestampDescending()
    {
        // Arrange
        var senderAddress = "sender123";
        var tx1 = CreateTransaction(senderAddress, "recipient1");
        tx1.TimeStamp = DateTime.UtcNow.AddMinutes(-10);
        await _repository.InsertTransactionAsync(tx1);

        var tx2 = CreateTransaction(senderAddress, "recipient2");
        tx2.TimeStamp = DateTime.UtcNow.AddMinutes(-5);
        await _repository.InsertTransactionAsync(tx2);

        var tx3 = CreateTransaction(senderAddress, "recipient3");
        tx3.TimeStamp = DateTime.UtcNow;
        await _repository.InsertTransactionAsync(tx3);

        // Act
        var result = await _manager.GetTransactionsByWalletPaginatedAsync(_testRegisterId, senderAddress, 1, 20, asSender: true, asRecipient: false);

        // Assert
        result.Items.Should().HaveCount(3);
        result.Items[0].TimeStamp.Should().BeAfter(result.Items[1].TimeStamp);
        result.Items[1].TimeStamp.Should().BeAfter(result.Items[2].TimeStamp);
    }

    [Fact]
    public async Task GetTransactionsByBlueprintAsync_ShouldReturnBlueprintTransactions()
    {
        // Arrange
        var blueprintId = "blueprint123";
        await SeedBlueprintTransactionsAsync(blueprintId);

        // Act
        var result = await _manager.GetTransactionsByBlueprintAsync(_testRegisterId, blueprintId);

        // Assert
        result.Should().HaveCountGreaterThan(0);
        result.Should().OnlyContain(t => t.MetaData != null && t.MetaData.BlueprintId == blueprintId);
    }

    [Fact]
    public async Task GetTransactionsByBlueprintAsync_WithInstanceId_ShouldFilterByInstance()
    {
        // Arrange
        var blueprintId = "blueprint123";
        var instanceId = "instance456";
        await SeedBlueprintTransactionsAsync(blueprintId, instanceId);

        // Act
        var result = await _manager.GetTransactionsByBlueprintAsync(_testRegisterId, blueprintId, instanceId);

        // Assert
        result.Should().HaveCountGreaterThan(0);
        result.Should().OnlyContain(t =>
            t.MetaData != null &&
            t.MetaData.BlueprintId == blueprintId &&
            t.MetaData.InstanceId == instanceId);
    }

    [Fact]
    public async Task GetTransactionStatisticsAsync_ShouldReturnCorrectStatistics()
    {
        // Arrange
        await SeedTransactionsForStatistics();

        // Act
        var result = await _manager.GetTransactionStatisticsAsync(_testRegisterId);

        // Assert
        result.Should().NotBeNull();
        result.TotalTransactions.Should().BeGreaterThan(0);
        result.UniqueWallets.Should().BeGreaterThan(0);
        result.UniqueSenders.Should().BeGreaterThan(0);
        result.UniqueRecipients.Should().BeGreaterThan(0);
        result.TotalPayloads.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetTransactionStatisticsAsync_ShouldCalculateUniqueWallets()
    {
        // Arrange
        await _repository.InsertTransactionAsync(CreateTransaction("sender1", "recipient1"));
        await _repository.InsertTransactionAsync(CreateTransaction("sender1", "recipient2"));
        await _repository.InsertTransactionAsync(CreateTransaction("sender2", "recipient1"));

        // Act
        var result = await _manager.GetTransactionStatisticsAsync(_testRegisterId);

        // Assert
        result.UniqueSenders.Should().Be(2); // sender1, sender2
        result.UniqueRecipients.Should().Be(2); // recipient1, recipient2
        result.UniqueWallets.Should().Be(4); // sender1, sender2, recipient1, recipient2 (deduplicated)
    }

    [Fact]
    public async Task GetTransactionStatisticsAsync_ShouldSetEarliestAndLatestTransactions()
    {
        // Arrange
        var earliest = CreateTransaction("sender1", "recipient1");
        earliest.TimeStamp = DateTime.UtcNow.AddDays(-10);
        await _repository.InsertTransactionAsync(earliest);

        var latest = CreateTransaction("sender2", "recipient2");
        latest.TimeStamp = DateTime.UtcNow;
        await _repository.InsertTransactionAsync(latest);

        // Act
        var result = await _manager.GetTransactionStatisticsAsync(_testRegisterId);

        // Assert
        result.EarliestTransaction.Should().BeCloseTo(earliest.TimeStamp, TimeSpan.FromSeconds(1));
        result.LatestTransaction.Should().BeCloseTo(latest.TimeStamp, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task GetTransactionStatisticsAsync_WithNoTransactions_ShouldReturnZeroStatistics()
    {
        // Act
        var result = await _manager.GetTransactionStatisticsAsync(_testRegisterId);

        // Assert
        result.TotalTransactions.Should().Be(0);
        result.UniqueWallets.Should().Be(0);
        result.UniqueSenders.Should().Be(0);
        result.UniqueRecipients.Should().Be(0);
        result.TotalPayloads.Should().Be(0);
        result.EarliestTransaction.Should().BeNull();
        result.LatestTransaction.Should().BeNull();
    }

    [Fact]
    public async Task GetTransactionsByWalletAsync_ShouldDeduplicateResults()
    {
        // Arrange
        var walletAddress = "wallet123";

        // Transaction where wallet123 is both sender and recipient
        var tx = CreateTransaction(walletAddress, walletAddress);
        await _repository.InsertTransactionAsync(tx);

        // Act
        var result = await _manager.GetTransactionsByWalletPaginatedAsync(_testRegisterId, walletAddress, 1, 20);

        // Assert
        result.Items.Should().HaveCount(1); // Should only appear once, not twice
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task GetTransactionsByWalletAsync_WithInvalidPage_ShouldClampToPage1(int invalidPage)
    {
        // Act — implementation clamps invalid page values to 1 instead of throwing
        var result = await _manager.GetTransactionsByWalletPaginatedAsync(_testRegisterId, "wallet123", invalidPage, 20);

        // Assert
        result.Should().NotBeNull();
        result.Page.Should().Be(1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)] // Max is 100
    public async Task GetTransactionsByWalletAsync_WithInvalidPageSize_ShouldClampToDefault(int invalidPageSize)
    {
        // Act — implementation clamps invalid pageSize values to 20 instead of throwing
        var result = await _manager.GetTransactionsByWalletPaginatedAsync(_testRegisterId, "wallet123", 1, invalidPageSize);

        // Assert
        result.Should().NotBeNull();
        result.PageSize.Should().Be(20);
    }

    [Fact]
    public async Task GetTransactionStatisticsAsync_ShouldCountTotalPayloads()
    {
        // Arrange
        var tx1 = CreateTransaction("sender1", "recipient1");
        tx1.PayloadCount = 3;
        tx1.Payloads = new[]
        {
            new PayloadModel { Hash = "hash1", Data = "data1", WalletAccess = Array.Empty<string>(), PayloadSize = 100 },
            new PayloadModel { Hash = "hash2", Data = "data2", WalletAccess = Array.Empty<string>(), PayloadSize = 100 },
            new PayloadModel { Hash = "hash3", Data = "data3", WalletAccess = Array.Empty<string>(), PayloadSize = 100 }
        };
        await _repository.InsertTransactionAsync(tx1);

        var tx2 = CreateTransaction("sender2", "recipient2");
        tx2.PayloadCount = 2;
        tx2.Payloads = new[]
        {
            new PayloadModel { Hash = "hash4", Data = "data4", WalletAccess = Array.Empty<string>(), PayloadSize = 100 },
            new PayloadModel { Hash = "hash5", Data = "data5", WalletAccess = Array.Empty<string>(), PayloadSize = 100 }
        };
        await _repository.InsertTransactionAsync(tx2);

        // Act
        var result = await _manager.GetTransactionStatisticsAsync(_testRegisterId);

        // Assert
        result.TotalPayloads.Should().Be(5);
    }

    private async Task SeedTransactionsAsync(string walletAddress, int count = 3)
    {
        for (int i = 0; i < count; i++)
        {
            var tx = CreateTransaction(
                i % 2 == 0 ? walletAddress : $"sender{i}",
                i % 2 == 0 ? $"recipient{i}" : walletAddress);
            await _repository.InsertTransactionAsync(tx);
        }
    }

    private async Task SeedBlueprintTransactionsAsync(string blueprintId, string? instanceId = null)
    {
        for (int i = 0; i < 3; i++)
        {
            var tx = CreateTransaction($"sender{i}", $"recipient{i}");
            tx.MetaData = new TransactionMetaData
            {
                RegisterId = _testRegisterId,
                TransactionType = TransactionType.Action,
                BlueprintId = blueprintId,
                InstanceId = instanceId ?? $"instance{i}",
                ActionId = (uint)i
            };
            await _repository.InsertTransactionAsync(tx);
        }
    }

    private async Task SeedTransactionsForStatistics()
    {
        await _repository.InsertTransactionAsync(CreateTransaction("sender1", "recipient1"));
        await _repository.InsertTransactionAsync(CreateTransaction("sender1", "recipient2"));
        await _repository.InsertTransactionAsync(CreateTransaction("sender2", "recipient1"));
    }

    private TransactionModel CreateTransaction(string senderWallet, string recipientWallet)
    {
        var txId = Guid.NewGuid().ToString("N") + new string('0', 64);
        txId = txId.Substring(0, 64);

        return new TransactionModel
        {
            RegisterId = _testRegisterId,
            TxId = txId,
            PrevTxId = string.Empty,
            Version = 1,
            SenderWallet = senderWallet,
            RecipientsWallets = new[] { recipientWallet },
            TimeStamp = DateTime.UtcNow,
            PayloadCount = 1,
            Payloads = new[]
            {
                new PayloadModel
                {
                    WalletAccess = new[] { senderWallet },
                    PayloadSize = 1024,
                    Hash = "payload_hash",
                    Data = "encrypted_data"
                }
            },
            Signature = "signature"
        };
    }

    private TransactionModel CreateTransactionWithPrevTxId(string prevTxId, DateTime? timestamp = null)
    {
        var tx = CreateTransaction("sender1", "recipient1");
        tx.PrevTxId = prevTxId;
        if (timestamp.HasValue) tx.TimeStamp = timestamp.Value;
        return tx;
    }

    // ===== PrevTxId Query Tests =====

    [Fact]
    public async Task GetTransactionsByPrevTxIdPaginatedAsync_SingleMatch_ReturnsPaginatedResultWith1Item()
    {
        // Arrange
        var prevTxId = new string('a', 64);
        await _repository.InsertTransactionAsync(CreateTransactionWithPrevTxId(prevTxId));

        // Act
        var result = await _manager.GetTransactionsByPrevTxIdPaginatedAsync(_testRegisterId, prevTxId);

        // Assert
        result.Should().NotBeNull();
        result.Items.Should().HaveCount(1);
        result.TotalCount.Should().Be(1);
        result.Items[0].PrevTxId.Should().Be(prevTxId);
    }

    [Fact]
    public async Task GetTransactionsByPrevTxIdPaginatedAsync_MultipleMatches_ReturnsAll()
    {
        // Arrange - fork scenario: two transactions claim same predecessor
        var prevTxId = new string('b', 64);
        await _repository.InsertTransactionAsync(CreateTransactionWithPrevTxId(prevTxId));
        await _repository.InsertTransactionAsync(CreateTransactionWithPrevTxId(prevTxId));

        // Act
        var result = await _manager.GetTransactionsByPrevTxIdPaginatedAsync(_testRegisterId, prevTxId);

        // Assert
        result.Items.Should().HaveCount(2);
        result.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task GetTransactionsByPrevTxIdPaginatedAsync_NoMatches_ReturnsEmpty()
    {
        // Arrange
        var prevTxId = new string('c', 64);

        // Act
        var result = await _manager.GetTransactionsByPrevTxIdPaginatedAsync(_testRegisterId, prevTxId);

        // Assert
        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task GetTransactionsByPrevTxIdPaginatedAsync_NullOrEmptyPrevTxId_ReturnsEmpty(string? prevTxId)
    {
        // Act
        var result = await _manager.GetTransactionsByPrevTxIdPaginatedAsync(_testRegisterId, prevTxId!);

        // Assert
        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task GetTransactionsByPrevTxIdPaginatedAsync_Pagination_RespectsPageAndPageSize()
    {
        // Arrange - 5 transactions with same prevTxId (severe fork)
        var prevTxId = new string('d', 64);
        for (int i = 0; i < 5; i++)
        {
            await _repository.InsertTransactionAsync(CreateTransactionWithPrevTxId(prevTxId));
        }

        // Act
        var page1 = await _manager.GetTransactionsByPrevTxIdPaginatedAsync(_testRegisterId, prevTxId, page: 1, pageSize: 2);
        var page2 = await _manager.GetTransactionsByPrevTxIdPaginatedAsync(_testRegisterId, prevTxId, page: 2, pageSize: 2);

        // Assert
        page1.Items.Should().HaveCount(2);
        page1.Page.Should().Be(1);
        page1.TotalCount.Should().Be(5);
        page1.TotalPages.Should().Be(3);
        page1.HasNextPage.Should().BeTrue();
        page1.HasPreviousPage.Should().BeFalse();

        page2.Items.Should().HaveCount(2);
        page2.Page.Should().Be(2);
        page2.HasPreviousPage.Should().BeTrue();
    }

    [Fact]
    public async Task GetTransactionsByPrevTxIdPaginatedAsync_InvalidPage_ClampsTo1()
    {
        // Arrange
        var prevTxId = new string('e', 64);
        await _repository.InsertTransactionAsync(CreateTransactionWithPrevTxId(prevTxId));

        // Act
        var result = await _manager.GetTransactionsByPrevTxIdPaginatedAsync(_testRegisterId, prevTxId, page: -1);

        // Assert
        result.Page.Should().Be(1);
        result.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetTransactionsByPrevTxIdPaginatedAsync_InvalidPageSize_ClampsToDefault()
    {
        // Arrange
        var prevTxId = new string('f', 64);
        await _repository.InsertTransactionAsync(CreateTransactionWithPrevTxId(prevTxId));

        // Act
        var result = await _manager.GetTransactionsByPrevTxIdPaginatedAsync(_testRegisterId, prevTxId, pageSize: 200);

        // Assert
        result.PageSize.Should().Be(20);
    }

    [Fact]
    public async Task GetTransactionsByPrevTxIdPaginatedAsync_OrdersByTimestampDescending()
    {
        // Arrange
        var prevTxId = new string('1', 64);
        var older = CreateTransactionWithPrevTxId(prevTxId, DateTime.UtcNow.AddMinutes(-10));
        var newer = CreateTransactionWithPrevTxId(prevTxId, DateTime.UtcNow);
        await _repository.InsertTransactionAsync(older);
        await _repository.InsertTransactionAsync(newer);

        // Act
        var result = await _manager.GetTransactionsByPrevTxIdPaginatedAsync(_testRegisterId, prevTxId);

        // Assert
        result.Items.Should().HaveCount(2);
        result.Items[0].TimeStamp.Should().BeAfter(result.Items[1].TimeStamp);
    }

    [Fact]
    public async Task GetTransactionsByPrevTxIdPaginatedAsync_DoesNotReturnOtherTransactions()
    {
        // Arrange - register scoping
        var targetPrevTxId = new string('a', 64);
        var otherPrevTxId = new string('b', 64);
        await _repository.InsertTransactionAsync(CreateTransactionWithPrevTxId(targetPrevTxId));
        await _repository.InsertTransactionAsync(CreateTransactionWithPrevTxId(otherPrevTxId));

        // Act
        var result = await _manager.GetTransactionsByPrevTxIdPaginatedAsync(_testRegisterId, targetPrevTxId);

        // Assert
        result.Items.Should().HaveCount(1);
        result.Items[0].PrevTxId.Should().Be(targetPrevTxId);
    }

    // ===== Cross-Register Wallet Query Tests =====

    [Fact]
    public async Task GetTransactionsByWalletAcrossRegistersAsync_ReturnsTransactionsFromMultipleRegisters()
    {
        // Arrange — create a second register and seed transactions in both
        var register2 = new Sorcha.Register.Models.Register
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Second Register",
            TenantId = "tenant123",
            Height = 0,
            Status = RegisterStatus.Offline
        };
        await _repository.InsertRegisterAsync(register2);

        var walletAddress = "crossreg-wallet";
        await _repository.InsertTransactionAsync(CreateTransactionForRegister(_testRegisterId, walletAddress, "recipient1"));
        await _repository.InsertTransactionAsync(CreateTransactionForRegister(register2.Id, walletAddress, "recipient2"));

        // Act
        var result = await _manager.GetTransactionsByWalletAcrossRegistersAsync(walletAddress, 1, 20);

        // Assert
        result.TotalCount.Should().Be(2);
        result.Items.Should().Contain(i => i.RegisterId == _testRegisterId);
        result.Items.Should().Contain(i => i.RegisterId == register2.Id);
        result.Items.Should().Contain(i => i.RegisterName == "Second Register");
    }

    [Fact]
    public async Task GetTransactionsByWalletAcrossRegistersAsync_NoTransactions_ReturnsEmpty()
    {
        // Act
        var result = await _manager.GetTransactionsByWalletAcrossRegistersAsync("nonexistent-wallet", 1, 20);

        // Assert
        result.TotalCount.Should().Be(0);
        result.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTransactionsByWalletAcrossRegistersAsync_PaginatesCorrectly()
    {
        // Arrange — seed 5 transactions across 2 registers
        var register2 = new Sorcha.Register.Models.Register
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Second Register",
            TenantId = "tenant123",
            Height = 0,
            Status = RegisterStatus.Offline
        };
        await _repository.InsertRegisterAsync(register2);

        var walletAddress = "paged-wallet";
        for (int i = 0; i < 3; i++)
            await _repository.InsertTransactionAsync(CreateTransactionForRegister(_testRegisterId, walletAddress, $"r{i}"));
        for (int i = 0; i < 2; i++)
            await _repository.InsertTransactionAsync(CreateTransactionForRegister(register2.Id, walletAddress, $"r{i}"));

        // Act
        var page1 = await _manager.GetTransactionsByWalletAcrossRegistersAsync(walletAddress, 1, 2);
        var page2 = await _manager.GetTransactionsByWalletAcrossRegistersAsync(walletAddress, 2, 2);

        // Assert
        page1.Items.Should().HaveCount(2);
        page1.TotalCount.Should().Be(5);
        page1.TotalPages.Should().Be(3);
        page2.Items.Should().HaveCount(2);
    }

    private TransactionModel CreateTransactionForRegister(string registerId, string senderWallet, string recipientWallet)
    {
        var txId = Guid.NewGuid().ToString("N") + new string('0', 64);
        txId = txId.Substring(0, 64);

        return new TransactionModel
        {
            RegisterId = registerId,
            TxId = txId,
            PrevTxId = string.Empty,
            Version = 1,
            SenderWallet = senderWallet,
            RecipientsWallets = new[] { recipientWallet },
            TimeStamp = DateTime.UtcNow,
            PayloadCount = 1,
            Payloads = new[]
            {
                new PayloadModel
                {
                    WalletAccess = new[] { senderWallet },
                    PayloadSize = 1024,
                    Hash = "payload_hash",
                    Data = "encrypted_data"
                }
            },
            Signature = "signature"
        };
    }

    // ===== Chain Traversal Tests (US2) =====

    [Fact]
    public async Task ChainTraversal_WalkForwardFromGenesis_ReachesAllTransactions()
    {
        // Arrange — build a 10-transaction chain
        var chainTxIds = new List<string>();
        var prevTxId = string.Empty; // genesis has no predecessor

        for (int i = 0; i < 10; i++)
        {
            var tx = CreateTransaction("sender1", "recipient1");
            tx.PrevTxId = prevTxId;
            await _repository.InsertTransactionAsync(tx);
            chainTxIds.Add(tx.TxId);
            prevTxId = tx.TxId; // next transaction's predecessor
        }

        // Act — walk forward from genesis (TxId of first transaction)
        var visited = new List<string>();
        var currentPrevTxId = chainTxIds[0]; // start from the first transaction's TxId

        for (int step = 0; step < 10; step++)
        {
            var result = await _manager.GetTransactionsByPrevTxIdPaginatedAsync(_testRegisterId, currentPrevTxId);
            if (result.TotalCount == 0)
                break;

            var successor = result.Items[0];
            visited.Add(successor.TxId);
            currentPrevTxId = successor.TxId;
        }

        // Assert — should have walked 9 successors (tx[1] through tx[9])
        visited.Should().HaveCount(9);
        for (int i = 0; i < visited.Count; i++)
        {
            visited[i].Should().Be(chainTxIds[i + 1]);
        }
    }

    [Fact]
    public async Task ChainTraversal_DetectGap_MissingTransactionBreaksChain()
    {
        // Arrange — create tx1 -> tx2 -> tx3, but tx2 references a non-existent predecessor
        var tx1 = CreateTransaction("sender1", "recipient1");
        tx1.PrevTxId = string.Empty;
        await _repository.InsertTransactionAsync(tx1);

        // tx2 references a TxId that doesn't exist in the chain (gap)
        var missingTxId = new string('x', 64);
        var tx2 = CreateTransaction("sender1", "recipient1");
        tx2.PrevTxId = missingTxId;
        await _repository.InsertTransactionAsync(tx2);

        // Act — try to walk forward from tx1
        var result = await _manager.GetTransactionsByPrevTxIdPaginatedAsync(_testRegisterId, tx1.TxId);

        // Assert — no successor found (gap detected: nothing references tx1 as predecessor)
        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task ChainTraversal_DetectFork_TwoTransactionsSharePredecessor()
    {
        // Arrange — tx1 is predecessor, both tx2a and tx2b reference tx1
        var tx1 = CreateTransaction("sender1", "recipient1");
        tx1.PrevTxId = string.Empty;
        await _repository.InsertTransactionAsync(tx1);

        var tx2a = CreateTransaction("sender1", "recipient1");
        tx2a.PrevTxId = tx1.TxId;
        await _repository.InsertTransactionAsync(tx2a);

        var tx2b = CreateTransaction("sender2", "recipient2");
        tx2b.PrevTxId = tx1.TxId;
        await _repository.InsertTransactionAsync(tx2b);

        // Act — query successors of tx1
        var result = await _manager.GetTransactionsByPrevTxIdPaginatedAsync(_testRegisterId, tx1.TxId);

        // Assert — fork: two successors
        result.TotalCount.Should().Be(2);
        result.Items.Select(t => t.TxId).Should().Contain(tx2a.TxId);
        result.Items.Select(t => t.TxId).Should().Contain(tx2b.TxId);
    }

    [Fact]
    public async Task ChainTraversal_ChainTip_QueryReturnsEmpty()
    {
        // Arrange — single transaction at end of chain (no successors)
        var tx = CreateTransaction("sender1", "recipient1");
        tx.PrevTxId = string.Empty;
        await _repository.InsertTransactionAsync(tx);

        // Act — query for successors of the chain tip
        var result = await _manager.GetTransactionsByPrevTxIdPaginatedAsync(_testRegisterId, tx.TxId);

        // Assert — empty = end of chain
        result.TotalCount.Should().Be(0);
        result.Items.Should().BeEmpty();
    }
}
