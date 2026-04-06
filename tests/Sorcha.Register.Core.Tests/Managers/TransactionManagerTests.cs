// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Register.Core.Events;
using Sorcha.Register.Core.Managers;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Enums;
using Sorcha.Register.Storage.InMemory;
using Xunit;

namespace Sorcha.Register.Core.Tests.Managers;

public class TransactionManagerTests
{
    private readonly InMemoryRegisterRepository _repository;
    private readonly InMemoryEventPublisher _eventPublisher;
    private readonly TransactionManager _manager;
    private readonly string _testRegisterId;

    public TransactionManagerTests()
    {
        _repository = new InMemoryRegisterRepository();
        _eventPublisher = new InMemoryEventPublisher();
        _manager = new TransactionManager(_repository, _eventPublisher);

        // Create a test register
        var register = new Sorcha.Register.Models.Register
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Test Register",
            Height = 0,
            Status = RegisterStatus.Offline
        };
        _testRegisterId = register.Id;
        _repository.InsertRegisterAsync(register).Wait();
    }

    [Fact]
    public async Task StoreTransactionAsync_WithValidTransaction_ShouldStoreTransaction()
    {
        // Arrange
        var transaction = CreateValidTransaction();

        // Act
        var result = await _manager.StoreTransactionAsync(transaction);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().NotBeNullOrWhiteSpace();
        result.TxId.Should().Be(transaction.TxId);
        result.RegisterId.Should().Be(_testRegisterId);
    }

    [Fact]
    public async Task StoreTransactionAsync_ShouldGenerateDIDUri()
    {
        // Arrange
        var transaction = CreateValidTransaction();

        // Act
        var result = await _manager.StoreTransactionAsync(transaction);

        // Assert
        result.Id.Should().NotBeNullOrWhiteSpace();
        result.Id.Should().StartWith("did:sorcha:register:");
        result.Id.Should().Contain($":{_testRegisterId}/tx/");
        result.Id.Should().EndWith(transaction.TxId);
    }

    [Fact]
    public async Task StoreTransactionAsync_ShouldSetTimestampIfNotProvided()
    {
        // Arrange
        var transaction = CreateValidTransaction();
        transaction.TimeStamp = default;

        // Act
        var result = await _manager.StoreTransactionAsync(transaction);

        // Assert
        result.TimeStamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task StoreTransactionAsync_ShouldPublishTransactionConfirmedEvent()
    {
        // Arrange
        var transaction = CreateValidTransaction();

        // Act
        var result = await _manager.StoreTransactionAsync(transaction);

        // Assert
        var events = _eventPublisher.GetPublishedEvents<TransactionConfirmedEvent>();
        events.Should().HaveCount(1);

        var evt = events.First();
        evt.TransactionId.Should().Be(result.TxId);
        evt.RegisterId.Should().Be(_testRegisterId);
        evt.SenderWallet.Should().Be(transaction.SenderWallet);
    }

    [Fact]
    public async Task StoreTransactionAsync_WithNullTransaction_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await _manager.StoreTransactionAsync(null!));
    }

    [Fact]
    public async Task StoreTransactionAsync_WithMissingTxId_ShouldThrowArgumentException()
    {
        // Arrange
        var transaction = CreateValidTransaction();
        transaction.TxId = string.Empty;

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(
            async () => await _manager.StoreTransactionAsync(transaction));

        exception.Message.Should().Contain("TxId");
    }

    [Fact]
    public async Task StoreTransactionAsync_WithInvalidTxIdLength_ShouldThrowArgumentException()
    {
        // Arrange
        var transaction = CreateValidTransaction();
        transaction.TxId = "tooshort";

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(
            async () => await _manager.StoreTransactionAsync(transaction));

        exception.Message.Should().Contain("64 characters");
    }

    [Fact]
    public async Task StoreTransactionAsync_WithMissingRegisterId_ShouldThrowArgumentException()
    {
        // Arrange
        var transaction = CreateValidTransaction();
        transaction.RegisterId = string.Empty;

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(
            async () => await _manager.StoreTransactionAsync(transaction));

        exception.Message.Should().Contain("RegisterId");
    }

    [Fact]
    public async Task StoreTransactionAsync_WithMissingSenderWallet_ShouldThrowArgumentException()
    {
        // Arrange
        var transaction = CreateValidTransaction();
        transaction.SenderWallet = string.Empty;

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(
            async () => await _manager.StoreTransactionAsync(transaction));

        exception.Message.Should().Contain("SenderWallet");
    }

    [Fact]
    public async Task StoreTransactionAsync_WithMissingSignature_ShouldThrowArgumentException()
    {
        // Arrange
        var transaction = CreateValidTransaction();
        transaction.Signature = string.Empty;

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(
            async () => await _manager.StoreTransactionAsync(transaction));

        exception.Message.Should().Contain("Signature");
    }

    [Fact]
    public async Task StoreTransactionAsync_WithMismatchedPayloadCount_ShouldThrowArgumentException()
    {
        // Arrange
        var transaction = CreateValidTransaction();
        transaction.PayloadCount = 5; // But only has 1 payload

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(
            async () => await _manager.StoreTransactionAsync(transaction));

        exception.Message.Should().Contain("PayloadCount");
    }

    [Fact]
    public async Task GetTransactionAsync_WithExistingTransaction_ShouldReturnTransaction()
    {
        // Arrange
        var transaction = CreateValidTransaction();
        var stored = await _manager.StoreTransactionAsync(transaction);

        // Act
        var result = await _manager.GetTransactionAsync(_testRegisterId, stored.TxId);

        // Assert
        result.Should().NotBeNull();
        result!.TxId.Should().Be(stored.TxId);
        result.RegisterId.Should().Be(_testRegisterId);
    }

    [Fact]
    public async Task GetTransactionAsync_WithNonExistentTransaction_ShouldReturnNull()
    {
        // Act
        var result = await _manager.GetTransactionAsync(_testRegisterId, "nonexistent" + new string('0', 53));

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetTransactionsAsync_ShouldReturnAllTransactionsForRegister()
    {
        // Arrange
        await _manager.StoreTransactionAsync(CreateValidTransaction("tx1"));
        await _manager.StoreTransactionAsync(CreateValidTransaction("tx2"));
        await _manager.StoreTransactionAsync(CreateValidTransaction("tx3"));

        // Act
        var result = await _manager.GetTransactionsAsync(_testRegisterId);

        // Assert
        result.Should().HaveCount(3);
        result.Should().OnlyContain(t => t.RegisterId == _testRegisterId);
    }

    [Fact]
    public async Task GetTransactionsAsync_ShouldReturnQueryable()
    {
        // Arrange
        await _manager.StoreTransactionAsync(CreateValidTransaction("tx1"));
        await _manager.StoreTransactionAsync(CreateValidTransaction("tx2"));

        // Act
        var queryable = await _manager.GetTransactionsAsync(_testRegisterId);
        var filtered = queryable.Where(t => t.TxId.Contains("tx1")).ToList();

        // Assert
        filtered.Should().HaveCount(1);
        filtered.First().TxId.Should().Contain("tx1");
    }

    [Fact]
    public async Task StoreTransactionAsync_WithMetaData_ShouldStoreMetaData()
    {
        // Arrange
        var transaction = CreateValidTransaction();
        transaction.MetaData = new TransactionMetaData
        {
            RegisterId = _testRegisterId,
            TransactionType = TransactionType.Action,
            BlueprintId = "blueprint123",
            InstanceId = "instance456",
            ActionId = 1,
            NextActionId = 2
        };

        // Act
        var result = await _manager.StoreTransactionAsync(transaction);

        // Assert
        result.MetaData.Should().NotBeNull();
        result.MetaData!.BlueprintId.Should().Be("blueprint123");
        result.MetaData.InstanceId.Should().Be("instance456");
        result.MetaData.ActionId.Should().Be(1);
    }

    [Fact]
    public async Task StoreTransactionAsync_WithPayloads_ShouldStorePayloads()
    {
        // Arrange
        var transaction = CreateValidTransaction();
        transaction.PayloadCount = 2;
        transaction.Payloads = new[]
        {
            new PayloadModel
            {
                WalletAccess = new[] { "wallet1" },
                PayloadSize = 1024,
                Hash = "hash1",
                Data = "data1"
            },
            new PayloadModel
            {
                WalletAccess = new[] { "wallet2" },
                PayloadSize = 2048,
                Hash = "hash2",
                Data = "data2"
            }
        };

        // Act
        var result = await _manager.StoreTransactionAsync(transaction);

        // Assert
        result.Payloads.Should().HaveCount(2);
        result.Payloads[0].Hash.Should().Be("hash1");
        result.Payloads[1].Hash.Should().Be("hash2");
    }

    [Fact]
    public async Task StoreTransactionAsync_WithRecipientsWallets_ShouldStoreRecipients()
    {
        // Arrange
        var transaction = CreateValidTransaction();
        transaction.RecipientsWallets = new[] { "wallet1", "wallet2", "wallet3" };

        // Act
        var result = await _manager.StoreTransactionAsync(transaction);

        // Assert
        result.RecipientsWallets.Should().HaveCount(3);
        result.RecipientsWallets.Should().Contain("wallet1");
        result.RecipientsWallets.Should().Contain("wallet2");
        result.RecipientsWallets.Should().Contain("wallet3");
    }

    [Fact]
    public async Task StoreTransactionAsync_WithPrevTxId_ShouldLinkToPreviousTransaction()
    {
        // Arrange
        var tx1 = CreateValidTransaction("tx1");
        var stored1 = await _manager.StoreTransactionAsync(tx1);

        var tx2 = CreateValidTransaction("tx2");
        tx2.PrevTxId = stored1.TxId;

        // Act
        var stored2 = await _manager.StoreTransactionAsync(tx2);

        // Assert
        stored2.PrevTxId.Should().Be(stored1.TxId);
    }

    [Fact]
    public async Task StoreTransactionAsync_ShouldPreserveVersion()
    {
        // Arrange
        var transaction = CreateValidTransaction();
        transaction.Version = 2;

        // Act
        var result = await _manager.StoreTransactionAsync(transaction);

        // Assert
        result.Version.Should().Be(2);
    }

    [Fact]
    public async Task StoreTransactionAsync_WithContext_ShouldPreserveContext()
    {
        // Arrange
        var transaction = CreateValidTransaction();
        transaction.Context = "https://custom.context.example.com/v1.jsonld";

        // Act
        var result = await _manager.StoreTransactionAsync(transaction);

        // Assert
        result.Context.Should().Be("https://custom.context.example.com/v1.jsonld");
    }

    [Fact]
    public async Task StoreTransactionAsync_WithType_ShouldPreserveType()
    {
        // Arrange
        var transaction = CreateValidTransaction();
        transaction.Type = "CustomTransaction";

        // Act
        var result = await _manager.StoreTransactionAsync(transaction);

        // Assert
        result.Type.Should().Be("CustomTransaction");
    }

    private TransactionModel CreateValidTransaction(string? suffix = null)
    {
        var txId = (suffix ?? Guid.NewGuid().ToString("N")) + new string('0', 64);
        txId = txId.Substring(0, 64);

        return new TransactionModel
        {
            RegisterId = _testRegisterId,
            TxId = txId,
            PrevTxId = string.Empty,
            Version = 1,
            SenderWallet = "sender_wallet_address",
            RecipientsWallets = new[] { "recipient_wallet_address" },
            TimeStamp = DateTime.UtcNow,
            PayloadCount = 1,
            Payloads = new[]
            {
                new PayloadModel
                {
                    WalletAccess = new[] { "sender_wallet_address" },
                    PayloadSize = 1024,
                    Hash = "payload_hash",
                    Data = "encrypted_payload_data"
                }
            },
            Signature = "transaction_signature"
        };
    }
}
