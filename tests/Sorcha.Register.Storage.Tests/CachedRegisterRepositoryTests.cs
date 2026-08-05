// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sorcha.Register.Core.Storage;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Enums;
// TransactionType enum is not used on TransactionModel
using Sorcha.Register.Storage;
using Sorcha.Register.Storage.InMemory;
using Sorcha.Storage.Abstractions;
using Sorcha.Storage.Abstractions.Caching;
using Sorcha.Storage.InMemory;
using Xunit;

namespace Sorcha.Register.Storage.Tests;

/// <summary>
/// Tests for CachedRegisterRepository with verified cache integration.
/// </summary>
public class CachedRegisterRepositoryTests
{
    private readonly InMemoryRegisterRepository _innerRepository;
    private readonly InMemoryCacheStore _cacheStore;
    private readonly InMemoryWormStore<DocketHeader, ulong> _wormStore;
    private readonly IVerifiedCache<DocketHeader, ulong> _docketCache;
    private readonly CachedRegisterRepository _sut;

    public CachedRegisterRepositoryTests()
    {
        _innerRepository = new InMemoryRegisterRepository();
        _cacheStore = new InMemoryCacheStore();
        _wormStore = new InMemoryWormStore<DocketHeader, ulong>(d => d.Id);

        var cacheConfig = Options.Create(new VerifiedCacheConfiguration
        {
            KeyPrefix = "register:docket:",
            CacheTtlSeconds = 3600,
            EnableHashVerification = true
        });

        _docketCache = new VerifiedCache<DocketHeader, ulong>(
            _cacheStore,
            _wormStore,
            d => d.Id,
            cacheConfig,
            d => d.Hash);

        var storageConfig = Options.Create(new RegisterStorageConfiguration());

        _sut = new CachedRegisterRepository(
            _innerRepository,
            _docketCache,
            _cacheStore,
            storageConfig);
    }

    // ===========================
    // Register Tests
    // ===========================

    [Fact]
    public async Task InsertRegisterAsync_CachesRegister()
    {
        // Arrange
        var register = CreateTestRegister("test-register-1");

        // Act
        var result = await _sut.InsertRegisterAsync(register);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be("test-register-1");

        // Verify cached
        var cached = await _cacheStore.GetAsync<Models.Register>("register:reg:test-register-1");
        cached.Should().NotBeNull();
    }

    [Fact]
    public async Task GetRegisterAsync_ReturnsCachedValue()
    {
        // Arrange
        var register = CreateTestRegister("test-register-2");
        await _sut.InsertRegisterAsync(register);

        // Act - First call populates cache
        var result1 = await _sut.GetRegisterAsync("test-register-2");
        // Second call should hit cache
        var result2 = await _sut.GetRegisterAsync("test-register-2");

        // Assert
        result1.Should().NotBeNull();
        result2.Should().NotBeNull();
        result1!.Id.Should().Be(result2!.Id);
    }

    [Fact]
    public async Task UpdateRegisterAsync_UpdatesCache()
    {
        // Arrange
        var register = CreateTestRegister("test-register-3");
        await _sut.InsertRegisterAsync(register);

        // Act
        register.Height = 100;
        var result = await _sut.UpdateRegisterAsync(register);

        // Assert
        result.Height.Should().Be(100);

        // Verify cache updated
        var cached = await _cacheStore.GetAsync<Models.Register>("register:reg:test-register-3");
        cached!.Height.Should().Be(100);
    }

    [Fact]
    public async Task DeleteRegisterAsync_InvalidatesCache()
    {
        // Arrange
        var register = CreateTestRegister("test-register-4");
        await _sut.InsertRegisterAsync(register);

        // Act
        await _sut.DeleteRegisterAsync("test-register-4");

        // Assert
        var cached = await _cacheStore.GetAsync<Models.Register>("register:reg:test-register-4");
        cached.Should().BeNull();
    }

    // ===========================
    // DocketHeader Tests (Verified Cache)
    // ===========================

    [Fact]
    public async Task InsertDocketAsync_UsesVerifiedCache()
    {
        // Arrange
        var register = CreateTestRegister("reg-with-docket-1");
        await _sut.InsertRegisterAsync(register);

        var docket = CreateTestDocket(1, "reg-with-docket-1");

        // Act
        var result = await _sut.InsertDocketAsync(docket);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be(1);

        // Verify in WORM store
        var inWorm = await _wormStore.GetAsync(1UL);
        inWorm.Should().NotBeNull();
    }

    [Fact]
    public async Task GetDocketAsync_UsesCachedValue()
    {
        // Arrange
        var register = CreateTestRegister("reg-with-docket-2");
        await _sut.InsertRegisterAsync(register);

        var docket = CreateTestDocket(2, "reg-with-docket-2");
        await _sut.InsertDocketAsync(docket);

        // Act
        var result = await _sut.GetDocketAsync("reg-with-docket-2", 2);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(2);
        result.Hash.Should().Be("hash-2");
    }

    [Fact]
    public async Task MultipleDockets_AllCachedCorrectly()
    {
        // Arrange
        var register = CreateTestRegister("reg-multi-docket");
        await _sut.InsertRegisterAsync(register);

        var dockets = Enumerable.Range(1, 10)
            .Select(i => CreateTestDocket((ulong)i, "reg-multi-docket"))
            .ToList();

        // Act
        foreach (var docket in dockets)
        {
            await _sut.InsertDocketAsync(docket);
        }

        // Assert - All should be retrievable from cache
        for (ulong i = 1; i <= 10; i++)
        {
            var result = await _sut.GetDocketAsync("reg-multi-docket", i);
            result.Should().NotBeNull();
            result!.Id.Should().Be(i);
        }

        // Verify cache stats
        var stats = await _sut.GetDocketCacheStatisticsAsync();
        stats.Should().NotBeNull();
        stats!.CacheHits.Should().BeGreaterThan(0);
    }

    // ===========================
    // Transaction Tests
    // ===========================

    [Fact]
    public async Task InsertTransactionAsync_CachesTransaction()
    {
        // Arrange
        var register = CreateTestRegister("reg-with-tx");
        await _sut.InsertRegisterAsync(register);

        var txId = "tx-1".PadRight(64, '0');
        var transaction = CreateTestTransaction("tx-1", "reg-with-tx");

        // Act
        var result = await _sut.InsertTransactionAsync(transaction);

        // Assert
        result.Should().NotBeNull();
        result.TxId.Should().Be(txId);

        // Verify cached
        var cached = await _cacheStore.GetAsync<TransactionModel>($"register:tx:reg-with-tx:{txId}");
        cached.Should().NotBeNull();
    }

    [Fact]
    public async Task GetTransactionAsync_UsesCachedValue()
    {
        // Arrange
        var register = CreateTestRegister("reg-with-tx-2");
        await _sut.InsertRegisterAsync(register);

        var txId = "tx-2".PadRight(64, '0');
        var transaction = CreateTestTransaction("tx-2", "reg-with-tx-2");
        await _sut.InsertTransactionAsync(transaction);

        // Act
        var result = await _sut.GetTransactionAsync("reg-with-tx-2", txId);

        // Assert
        result.Should().NotBeNull();
        result!.TxId.Should().Be(txId);
    }

    // ===========================
    // GetTransactionsByPrevTxIdAsync Tests
    // ===========================

    [Fact]
    public async Task GetTransactionsByPrevTxIdAsync_DelegatesToInnerRepository()
    {
        // Arrange
        var register = CreateTestRegister("reg-prevtx-1");
        await _sut.InsertRegisterAsync(register);

        var prevTxId = "prev-tx-1".PadRight(64, '0');
        var tx1 = CreateTestTransaction("tx-child-1", "reg-prevtx-1", prevTxId);
        var tx2 = CreateTestTransaction("tx-child-2", "reg-prevtx-1", prevTxId);
        var txUnrelated = CreateTestTransaction("tx-other", "reg-prevtx-1");
        await _sut.InsertTransactionAsync(tx1);
        await _sut.InsertTransactionAsync(tx2);
        await _sut.InsertTransactionAsync(txUnrelated);

        // Act
        var result = (await _sut.GetTransactionsByPrevTxIdAsync("reg-prevtx-1", prevTxId)).ToList();

        // Assert
        result.Should().HaveCount(2);
        result.Should().AllSatisfy(t => t.PrevTxId.Should().Be(prevTxId));
    }

    [Fact]
    public async Task GetTransactionsByPrevTxIdAsync_EmptyPrevTxId_ReturnsEmpty()
    {
        // Arrange
        var register = CreateTestRegister("reg-prevtx-2");
        await _sut.InsertRegisterAsync(register);

        var tx = CreateTestTransaction("tx-child-3", "reg-prevtx-2", "some-prev".PadRight(64, '0'));
        await _sut.InsertTransactionAsync(tx);

        // Act
        var result = await _sut.GetTransactionsByPrevTxIdAsync("reg-prevtx-2", "");

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTransactionsByPrevTxIdAsync_NoMatches_ReturnsEmpty()
    {
        // Arrange
        var register = CreateTestRegister("reg-prevtx-3");
        await _sut.InsertRegisterAsync(register);

        var tx = CreateTestTransaction("tx-child-4", "reg-prevtx-3", "known-prev".PadRight(64, '0'));
        await _sut.InsertTransactionAsync(tx);

        // Act
        var result = await _sut.GetTransactionsByPrevTxIdAsync("reg-prevtx-3", "nonexistent".PadRight(64, '0'));

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTransactionsByPrevTxIdAsync_NonexistentRegister_ReturnsEmpty()
    {
        // Act
        var result = await _sut.GetTransactionsByPrevTxIdAsync("no-such-register", "some-prev".PadRight(64, '0'));

        // Assert
        result.Should().BeEmpty();
    }

    // ===========================
    // Cache Management Tests
    // ===========================

    [Fact]
    public async Task GetDocketCacheStatisticsAsync_ReturnsStatistics()
    {
        // Arrange
        var register = CreateTestRegister("stats-test");
        await _sut.InsertRegisterAsync(register);

        var docket = CreateTestDocket(100, "stats-test");
        await _sut.InsertDocketAsync(docket);
        await _sut.GetDocketAsync("stats-test", 100);

        // Act
        var stats = await _sut.GetDocketCacheStatisticsAsync();

        // Assert
        stats.Should().NotBeNull();
        stats!.CacheHits.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task VerifyDocketCacheIntegrityAsync_ReturnsValidResult()
    {
        // Arrange
        var register = CreateTestRegister("integrity-test");
        await _sut.InsertRegisterAsync(register);

        for (int i = 1; i <= 5; i++)
        {
            var docket = CreateTestDocket((ulong)i, "integrity-test");
            await _sut.InsertDocketAsync(docket);
        }

        // Act
        var result = await _sut.VerifyDocketCacheIntegrityAsync();

        // Assert
        result.Should().NotBeNull();
        result!.IsValid.Should().BeTrue();
        result.DocumentsVerified.Should().Be(5);
    }

    [Fact]
    public async Task InvalidateRegisterCachesAsync_ClearsAllCaches()
    {
        // Arrange
        var register = CreateTestRegister("invalidate-test");
        await _sut.InsertRegisterAsync(register);

        var docket = CreateTestDocket(1, "invalidate-test");
        await _sut.InsertDocketAsync(docket);

        var txId = "tx-inv".PadRight(64, '0');
        var transaction = CreateTestTransaction("tx-inv", "invalidate-test");
        await _sut.InsertTransactionAsync(transaction);

        // Verify register is cached
        var cachedReg = await _cacheStore.GetAsync<Models.Register>("register:reg:invalidate-test");
        cachedReg.Should().NotBeNull();

        // Act
        await _sut.InvalidateRegisterCachesAsync("invalidate-test");

        // Assert - Register cache should be invalidated
        var cachedRegAfter = await _cacheStore.GetAsync<Models.Register>("register:reg:invalidate-test");
        cachedRegAfter.Should().BeNull();
    }

    // ===========================
    // Helper Methods
    // ===========================

    private static Models.Register CreateTestRegister(string id)
    {
        return new Models.Register
        {
            Id = id,
            Name = $"Test Register {id}",
            Height = 0,
            Status = RegisterStatus.Online,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
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

    private static TransactionModel CreateTestTransaction(string txId, string registerId, string? prevTxId = null)
    {
        return new TransactionModel
        {
            TxId = txId.PadRight(64, '0'), // Must be 64 chars
            RegisterId = registerId,
            SenderWallet = "sender-wallet",
            RecipientsWallets = new List<string> { "recipient-wallet" },
            TimeStamp = DateTime.UtcNow,
            Signature = "test-signature",
            PrevTxId = prevTxId ?? string.Empty
        };
    }
}
