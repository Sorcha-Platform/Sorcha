// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using FluentAssertions;
using Sorcha.Wallet.Core.Data;
using Sorcha.Wallet.Core.Domain;
using Sorcha.Wallet.Core.Domain.Entities;
using Sorcha.Wallet.Service.Services.Implementation;
using Sorcha.Wallet.Service.Tests.Services;

namespace Sorcha.Wallet.Service.Tests.Services;

/// <summary>
/// Unit tests for TransactionLifecycleService (T051).
/// Uses EF Core InMemory provider via TestRecoveryDbContext.
/// </summary>
public class TransactionLifecycleServiceTests : IDisposable
{
    private readonly TestRecoveryDbContext _db;
    private readonly TransactionLifecycleService _service;

    public TransactionLifecycleServiceTests()
    {
        var options = new DbContextOptionsBuilder<TestRecoveryDbContext>()
            .UseInMemoryDatabase(databaseName: $"WalletTest_{Guid.NewGuid():N}")
            .Options;

        _db = new TestRecoveryDbContext(options);
        var logger = new Mock<ILogger<TransactionLifecycleService>>();
        _service = new TransactionLifecycleService(_db, logger.Object);
    }

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }

    #region RecordSubmissionAsync

    [Fact]
    public async Task RecordSubmissionAsync_NewTransaction_CreatesRecordWithSubmittedState()
    {
        // Arrange
        var walletAddress = "ws-test-wallet-001";
        var txId = "tx-submit-001";
        var registerId = "reg-001";

        // Act
        await _service.RecordSubmissionAsync(walletAddress, txId, registerId);

        // Assert
        var record = await _db.WalletTransactions
            .FirstOrDefaultAsync(t => t.TransactionId == txId);

        record.Should().NotBeNull();
        record!.State.Should().Be(TransactionState.Submitted);
        record.ParentWalletAddress.Should().Be(walletAddress);
        record.RegisterId.Should().Be(registerId);
        record.TransactionType.Should().Be("sent");
        record.Direction.Should().Be(TransactionDirection.Outbound);
    }

    [Fact]
    public async Task RecordSubmissionAsync_DuplicateTransactionId_IsIdempotent()
    {
        // Arrange
        var walletAddress = "ws-test-wallet-002";
        var txId = "tx-dup-001";
        var registerId = "reg-001";

        // Act: submit twice
        await _service.RecordSubmissionAsync(walletAddress, txId, registerId);
        await _service.RecordSubmissionAsync(walletAddress, txId, registerId);

        // Assert: only one record
        var count = await _db.WalletTransactions
            .IgnoreQueryFilters()
            .CountAsync(t => t.TransactionId == txId);

        count.Should().Be(1);
    }

    [Fact]
    public async Task RecordSubmissionAsync_WithCounterparty_StoresCounterpartyAddress()
    {
        // Arrange
        var walletAddress = "ws-test-wallet-003";
        var txId = "tx-counter-001";
        var registerId = "reg-001";
        var counterparty = "ws-counterparty-001";

        // Act
        await _service.RecordSubmissionAsync(walletAddress, txId, registerId, counterparty);

        // Assert
        var record = await _db.WalletTransactions
            .FirstOrDefaultAsync(t => t.TransactionId == txId);

        record.Should().NotBeNull();
        record!.CounterpartyAddress.Should().Be(counterparty);
    }

    #endregion

    #region MarkSealedAsync

    [Fact]
    public async Task MarkSealedAsync_SubmittedTransaction_TransitionsToConfirmed()
    {
        // Arrange
        var txId = "tx-seal-001";
        await _service.RecordSubmissionAsync("ws-wallet-001", txId, "reg-001");

        // Act
        await _service.MarkSealedAsync(txId, docketNumber: 42);

        // Assert
        var record = await _db.WalletTransactions
            .IgnoreQueryFilters()
            .FirstAsync(t => t.TransactionId == txId);

        record.State.Should().Be(TransactionState.Confirmed);
        record.BlockHeight.Should().Be(42);
        record.ConfirmedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task MarkSealedAsync_UnknownTransactionId_DoesNotThrow()
    {
        // Act
        var act = async () => await _service.MarkSealedAsync("tx-nonexistent", docketNumber: 1);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task MarkSealedAsync_AlreadyConfirmed_DoesNotOverwrite()
    {
        // Arrange
        var txId = "tx-seal-002";
        await _service.RecordSubmissionAsync("ws-wallet-001", txId, "reg-001");
        await _service.MarkSealedAsync(txId, docketNumber: 10);

        var firstRecord = await _db.WalletTransactions
            .IgnoreQueryFilters()
            .FirstAsync(t => t.TransactionId == txId);
        var firstConfirmedAt = firstRecord.ConfirmedAt;

        // Act: seal again with different docket number
        await _service.MarkSealedAsync(txId, docketNumber: 20);

        // Assert: state and confirmedAt should not change
        await _db.Entry(firstRecord).ReloadAsync();
        firstRecord.State.Should().Be(TransactionState.Confirmed);
        firstRecord.ConfirmedAt.Should().Be(firstConfirmedAt);
    }

    #endregion

    #region MarkReceiptedAsync

    [Fact]
    public async Task MarkReceiptedAsync_ConfirmedTransaction_TransitionsToReceipted()
    {
        // Arrange
        var txId = "tx-receipt-001";
        await _service.RecordSubmissionAsync("ws-wallet-001", txId, "reg-001");
        await _service.MarkSealedAsync(txId, docketNumber: 5);

        // Act
        await _service.MarkReceiptedAsync(txId, "receipt-abc-123");

        // Assert
        var record = await _db.WalletTransactions
            .IgnoreQueryFilters()
            .FirstAsync(t => t.TransactionId == txId);

        record.State.Should().Be(TransactionState.Receipted);
        record.ReceiptId.Should().Be("receipt-abc-123");
        record.ReceiptedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task MarkReceiptedAsync_SkippedConfirmed_BackfillsConfirmedAt()
    {
        // Arrange: submitted but never sealed
        var txId = "tx-receipt-002";
        await _service.RecordSubmissionAsync("ws-wallet-001", txId, "reg-001");

        // Act: jump straight to receipted
        await _service.MarkReceiptedAsync(txId, "receipt-def-456");

        // Assert
        var record = await _db.WalletTransactions
            .IgnoreQueryFilters()
            .FirstAsync(t => t.TransactionId == txId);

        record.State.Should().Be(TransactionState.Receipted);
        record.ConfirmedAt.Should().NotBeNull("ConfirmedAt should be backfilled");
        record.ReceiptedAt.Should().NotBeNull();
        record.ReceiptId.Should().Be("receipt-def-456");
    }

    [Fact]
    public async Task MarkReceiptedAsync_UnknownTransactionId_DoesNotThrow()
    {
        // Act
        var act = async () => await _service.MarkReceiptedAsync("tx-unknown", "receipt-xxx");

        // Assert
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region GetStatusAsync

    [Fact]
    public async Task GetStatusAsync_ExistingTransaction_ReturnsCorrectRecord()
    {
        // Arrange
        var walletAddress = "ws-wallet-status";
        var txId = "tx-status-001";
        await _service.RecordSubmissionAsync(walletAddress, txId, "reg-001");
        await _service.MarkSealedAsync(txId, docketNumber: 7);

        // Act
        var result = await _service.GetStatusAsync(walletAddress, txId);

        // Assert
        result.Should().NotBeNull();
        result!.TransactionId.Should().Be(txId);
        result.State.Should().Be(TransactionState.Confirmed);
        result.BlockHeight.Should().Be(7);
    }

    [Fact]
    public async Task GetStatusAsync_NonexistentTransaction_ReturnsNull()
    {
        // Act
        var result = await _service.GetStatusAsync("ws-wallet-none", "tx-nope");

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region GetRecentTransactionsAsync

    [Fact]
    public async Task GetRecentTransactionsAsync_MultipleTransactions_ReturnsOrderedByCreatedAtDescending()
    {
        // Arrange
        var walletAddress = "ws-wallet-recent";

        // Create transactions with known ordering
        for (int i = 1; i <= 5; i++)
        {
            var record = new WalletTransaction
            {
                TransactionId = $"tx-recent-{i:D3}",
                ParentWalletAddress = walletAddress,
                TransactionType = "sent",
                RegisterId = "reg-001",
                Direction = TransactionDirection.Outbound,
                State = TransactionState.Submitted,
                CreatedAt = DateTime.UtcNow.AddMinutes(-10 + i) // increasing times
            };
            _db.WalletTransactions.Add(record);
        }
        await _db.SaveChangesAsync();

        // Act
        var results = await _service.GetRecentTransactionsAsync(walletAddress, limit: 3);

        // Assert
        results.Should().HaveCount(3);
        results[0].TransactionId.Should().Be("tx-recent-005"); // most recent first
        results[1].TransactionId.Should().Be("tx-recent-004");
        results[2].TransactionId.Should().Be("tx-recent-003");
    }

    [Fact]
    public async Task GetRecentTransactionsAsync_NoTransactions_ReturnsEmptyList()
    {
        // Act
        var results = await _service.GetRecentTransactionsAsync("ws-wallet-empty");

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRecentTransactionsAsync_DifferentWallets_ReturnsOnlyMatchingWallet()
    {
        // Arrange
        await _service.RecordSubmissionAsync("ws-wallet-A", "tx-a-001", "reg-001");
        await _service.RecordSubmissionAsync("ws-wallet-B", "tx-b-001", "reg-001");

        // Act
        var results = await _service.GetRecentTransactionsAsync("ws-wallet-A");

        // Assert
        results.Should().HaveCount(1);
        results[0].TransactionId.Should().Be("tx-a-001");
    }

    #endregion
}
