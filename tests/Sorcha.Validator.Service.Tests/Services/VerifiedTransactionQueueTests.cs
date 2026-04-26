// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sorcha.Validator.Service.Configuration;
using Sorcha.Validator.Service.Models;
using Sorcha.Validator.Service.Services;
using Sorcha.Validator.Service.Services.Interfaces;

namespace Sorcha.Validator.Service.Tests.Services;

/// <summary>
/// Tests for <see cref="InMemoryVerifiedTransactionQueue"/> — the in-process
/// fallback implementation of <see cref="IVerifiedTransactionQueue"/> with
/// lease-tracked claims.
/// </summary>
public class VerifiedTransactionQueueTests
{
    private static readonly TimeSpan DefaultLease = TimeSpan.FromMinutes(1);

    private readonly Mock<ILogger<InMemoryVerifiedTransactionQueue>> _loggerMock;
    private readonly VerifiedQueueConfiguration _config;
    private readonly InMemoryVerifiedTransactionQueue _queue;

    public VerifiedTransactionQueueTests()
    {
        _loggerMock = new Mock<ILogger<InMemoryVerifiedTransactionQueue>>();

        _config = new VerifiedQueueConfiguration
        {
            MaxTransactionsPerRegister = 100,
            MaxTotalTransactions = 500,
            TransactionTtl = TimeSpan.FromMinutes(30),
            MaxRegisters = 10
        };

        _queue = new InMemoryVerifiedTransactionQueue(
            Options.Create(_config),
            _loggerMock.Object);
    }

    private async Task<IReadOnlyList<VerifiedTransaction>> ClaimAndReturnAsync(string registerId, int maxCount)
    {
        var leases = await _queue.ClaimAsync(registerId, maxCount, DefaultLease, CancellationToken.None);
        return leases.Select(l => l.Transaction).ToList();
    }

    [Fact]
    public void Constructor_WithNullConfig_ThrowsArgumentNullException()
    {
        var act = () => new InMemoryVerifiedTransactionQueue(null!, _loggerMock.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("config");
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        var act = () => new InMemoryVerifiedTransactionQueue(Options.Create(_config), null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public void Enqueue_WithValidTransaction_ReturnsTrue()
    {
        var result = _queue.Enqueue("test-register", CreateTestTransaction("tx-1"));

        result.Should().BeTrue();
        _queue.GetCount("test-register").Should().Be(1);
    }

    [Fact]
    public void Enqueue_WithEmptyRegisterId_ThrowsArgumentException()
    {
        var act = () => _queue.Enqueue("", CreateTestTransaction("tx-1"));
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Enqueue_WithNullTransaction_ThrowsArgumentNullException()
    {
        var act = () => _queue.Enqueue("test-register", null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Enqueue_WhenAtRegisterLimit_ReturnsFalse()
    {
        for (var i = 0; i < _config.MaxTransactionsPerRegister; i++)
        {
            _queue.Enqueue("test-register", CreateTestTransaction($"tx-{i}"));
        }

        var result = _queue.Enqueue("test-register", CreateTestTransaction("tx-overflow"));

        result.Should().BeFalse();
        _queue.GetCount("test-register").Should().Be(_config.MaxTransactionsPerRegister);
    }

    [Fact]
    public void Enqueue_DuplicateTransactionId_ReturnsFalse()
    {
        _queue.Enqueue("test-register", CreateTestTransaction("tx-1"));

        var result = _queue.Enqueue("test-register", CreateTestTransaction("tx-1"));

        result.Should().BeFalse();
        _queue.GetCount("test-register").Should().Be(1);
    }

    [Fact]
    public async Task ClaimAsync_ReturnsTransactionsInPriorityOrder()
    {
        _queue.Enqueue("test-register", CreateTestTransaction("tx-low"), priority: 1);
        _queue.Enqueue("test-register", CreateTestTransaction("tx-high"), priority: 10);
        _queue.Enqueue("test-register", CreateTestTransaction("tx-medium"), priority: 5);

        var leases = await _queue.ClaimAsync("test-register", 3, DefaultLease, CancellationToken.None);

        leases.Should().HaveCount(3);
        leases[0].TransactionId.Should().Be("tx-high");
        leases[1].TransactionId.Should().Be("tx-medium");
        leases[2].TransactionId.Should().Be("tx-low");
    }

    [Fact]
    public async Task ClaimAsync_HoldsTransactionsUnderLease()
    {
        // Claimed transactions should not be visible to subsequent claims until the lease
        // expires or is released. They remain in GetCount (claimed counts as in-queue).
        _queue.Enqueue("test-register", CreateTestTransaction("tx-1"));
        _queue.Enqueue("test-register", CreateTestTransaction("tx-2"));

        var first = await _queue.ClaimAsync("test-register", 1, DefaultLease, CancellationToken.None);
        var second = await _queue.ClaimAsync("test-register", 5, DefaultLease, CancellationToken.None);

        first.Should().HaveCount(1);
        second.Should().HaveCount(1, "the other transaction is still under lease");
        first[0].TransactionId.Should().NotBe(second[0].TransactionId);
        _queue.GetCount("test-register").Should().Be(2, "both are still in the queue (one available released by claim, one claimed)");
    }

    [Fact]
    public async Task ClaimAsync_FromEmptyQueue_ReturnsEmptyList()
    {
        var leases = await _queue.ClaimAsync("test-register", 10, DefaultLease, CancellationToken.None);
        leases.Should().BeEmpty();
    }

    [Fact]
    public async Task ClaimAsync_WithNonExistentRegister_ReturnsEmptyList()
    {
        var leases = await _queue.ClaimAsync("nonexistent", 10, DefaultLease, CancellationToken.None);
        leases.Should().BeEmpty();
    }

    [Fact]
    public async Task ConfirmAsync_RemovesTransactionsPermanently()
    {
        _queue.Enqueue("test-register", CreateTestTransaction("tx-1"));

        var leases = await _queue.ClaimAsync("test-register", 1, DefaultLease, CancellationToken.None);
        await _queue.ConfirmAsync("test-register", leases.Select(l => l.TransactionId), CancellationToken.None);

        _queue.GetCount("test-register").Should().Be(0);
        _queue.Contains("test-register", "tx-1").Should().BeFalse();
    }

    [Fact]
    public async Task ConfirmAsync_OnNeverClaimedTransaction_IsNoOp()
    {
        _queue.Enqueue("test-register", CreateTestTransaction("tx-1"));

        await _queue.ConfirmAsync("test-register", new[] { "tx-1" }, CancellationToken.None);

        // tx-1 was never claimed; confirm is a no-op.
        _queue.GetCount("test-register").Should().Be(1);
    }

    [Fact]
    public async Task ReleaseAsync_ReturnsTransactionsToAvailablePool()
    {
        _queue.Enqueue("test-register", CreateTestTransaction("tx-1"));

        var leases = await _queue.ClaimAsync("test-register", 1, DefaultLease, CancellationToken.None);
        await _queue.ReleaseAsync("test-register", leases.Select(l => l.TransactionId), CancellationToken.None);

        // Transaction should be claimable again immediately.
        var reclaimed = await _queue.ClaimAsync("test-register", 1, DefaultLease, CancellationToken.None);
        reclaimed.Should().HaveCount(1);
        reclaimed[0].TransactionId.Should().Be("tx-1");
    }

    [Fact]
    public async Task ExpiredLease_AutoReleasesOnNextClaim()
    {
        // Use a lease shorter than the test sleep so we can observe auto-release.
        _queue.Enqueue("test-register", CreateTestTransaction("tx-1"));

        var firstClaim = await _queue.ClaimAsync("test-register", 1, TimeSpan.FromMilliseconds(50), CancellationToken.None);
        firstClaim.Should().HaveCount(1);

        // A subsequent claim before lease expiry sees nothing.
        var midClaim = await _queue.ClaimAsync("test-register", 1, DefaultLease, CancellationToken.None);
        midClaim.Should().BeEmpty();

        await Task.Delay(80);

        // After expiry, the next claim auto-releases and re-claims the transaction.
        var afterClaim = await _queue.ClaimAsync("test-register", 1, DefaultLease, CancellationToken.None);
        afterClaim.Should().HaveCount(1);
        afterClaim[0].TransactionId.Should().Be("tx-1");
    }

    [Fact]
    public void Peek_ReturnsTransactionsWithoutClaiming()
    {
        _queue.Enqueue("test-register", CreateTestTransaction("tx-1"));
        _queue.Enqueue("test-register", CreateTestTransaction("tx-2"));

        var peeked = _queue.Peek("test-register", 2);

        peeked.Should().HaveCount(2);
        _queue.GetCount("test-register").Should().Be(2);
    }

    [Fact]
    public async Task Peek_AfterClaim_DoesNotShowClaimedTransactions()
    {
        _queue.Enqueue("test-register", CreateTestTransaction("tx-1"));
        _queue.Enqueue("test-register", CreateTestTransaction("tx-2"));

        await _queue.ClaimAsync("test-register", 1, DefaultLease, CancellationToken.None);
        var peeked = _queue.Peek("test-register", 5);

        peeked.Should().HaveCount(1, "one transaction is held under lease");
    }

    [Fact]
    public void Remove_ExistingTransaction_ReturnsTrue()
    {
        _queue.Enqueue("test-register", CreateTestTransaction("tx-1"));

        var result = _queue.Remove("test-register", "tx-1");

        result.Should().BeTrue();
        _queue.Contains("test-register", "tx-1").Should().BeFalse();
    }

    [Fact]
    public void Remove_NonExistingTransaction_ReturnsFalse()
    {
        var result = _queue.Remove("test-register", "nonexistent");
        result.Should().BeFalse();
    }

    [Fact]
    public void Contains_ExistingTransaction_ReturnsTrue()
    {
        _queue.Enqueue("test-register", CreateTestTransaction("tx-1"));
        _queue.Contains("test-register", "tx-1").Should().BeTrue();
    }

    [Fact]
    public void GetCount_ReturnsCountForRegister()
    {
        _queue.Enqueue("test-register", CreateTestTransaction("tx-1"));
        _queue.Enqueue("test-register", CreateTestTransaction("tx-2"));
        _queue.Enqueue("test-register", CreateTestTransaction("tx-3"));

        _queue.GetCount("test-register").Should().Be(3);
    }

    [Fact]
    public void GetTotalCount_ReturnsCountAcrossAllRegisters()
    {
        _queue.Enqueue("register-1", CreateTestTransaction("tx-1"));
        _queue.Enqueue("register-1", CreateTestTransaction("tx-2"));
        _queue.Enqueue("register-2", CreateTestTransaction("tx-3"));

        _queue.GetTotalCount().Should().Be(3);
    }

    [Fact]
    public async Task GetStats_ReturnsCorrectStatistics()
    {
        _queue.Enqueue("register-1", CreateTestTransaction("tx-1"));
        _queue.Enqueue("register-2", CreateTestTransaction("tx-2"));
        var leases = await _queue.ClaimAsync("register-1", 1, DefaultLease, CancellationToken.None);
        await _queue.ConfirmAsync("register-1", leases.Select(l => l.TransactionId), CancellationToken.None);

        var stats = _queue.GetStats();

        stats.TotalEnqueued.Should().Be(2);
        stats.TotalConfirmed.Should().Be(1);
        stats.TotalTransactions.Should().Be(1);
        stats.ActiveRegisters.Should().Be(1);
    }

    [Fact]
    public void GetRegisterStats_ReturnsCorrectStatistics()
    {
        _queue.Enqueue("test-register", CreateTestTransaction("tx-1"), priority: 5);
        _queue.Enqueue("test-register", CreateTestTransaction("tx-2"), priority: 10);

        var stats = _queue.GetRegisterStats("test-register");

        stats.RegisterId.Should().Be("test-register");
        stats.TransactionCount.Should().Be(2);
        stats.AveragePriority.Should().Be(7.5);
    }

    [Fact]
    public void Clear_RemovesAllTransactionsForRegister()
    {
        _queue.Enqueue("test-register", CreateTestTransaction("tx-1"));
        _queue.Enqueue("test-register", CreateTestTransaction("tx-2"));
        _queue.Enqueue("other-register", CreateTestTransaction("tx-3"));

        var cleared = _queue.Clear("test-register");

        cleared.Should().Be(2);
        _queue.GetCount("test-register").Should().Be(0);
        _queue.GetCount("other-register").Should().Be(1);
    }

    [Fact]
    public void ClearAll_RemovesAllTransactions()
    {
        _queue.Enqueue("register-1", CreateTestTransaction("tx-1"));
        _queue.Enqueue("register-2", CreateTestTransaction("tx-2"));

        var cleared = _queue.ClearAll();

        cleared.Should().Be(2);
        _queue.GetTotalCount().Should().Be(0);
    }

    [Fact]
    public void Enqueue_WhenMaxRegistersReached_ReturnsFalse()
    {
        for (var i = 0; i < _config.MaxRegisters; i++)
        {
            _queue.Enqueue($"register-{i}", CreateTestTransaction($"tx-{i}"));
        }

        var result = _queue.Enqueue("new-register", CreateTestTransaction("tx-new"));

        result.Should().BeFalse();
    }

    [Fact]
    public void Enqueue_ToExistingRegisterWhenMaxRegistersReached_ReturnsTrue()
    {
        for (var i = 0; i < _config.MaxRegisters; i++)
        {
            _queue.Enqueue($"register-{i}", CreateTestTransaction($"tx-{i}"));
        }

        var result = _queue.Enqueue("register-0", CreateTestTransaction("tx-new"));

        result.Should().BeTrue();
    }

    [Fact]
    public async Task ClaimAsync_WithPriority_OrdersCorrectly()
    {
        _queue.Enqueue("test-register", CreateTestTransaction("tx-normal"), priority: 0);
        _queue.Enqueue("test-register", CreateTestTransaction("tx-urgent"), priority: 100);
        _queue.Enqueue("test-register", CreateTestTransaction("tx-high"), priority: 50);
        _queue.Enqueue("test-register", CreateTestTransaction("tx-low"), priority: -10);

        var leases = await _queue.ClaimAsync("test-register", 4, DefaultLease, CancellationToken.None);

        leases[0].TransactionId.Should().Be("tx-urgent");
        leases[1].TransactionId.Should().Be("tx-high");
        leases[2].TransactionId.Should().Be("tx-normal");
        leases[3].TransactionId.Should().Be("tx-low");
    }

    [Fact]
    public async Task ClaimAsync_SamePriority_ReturnsFifoOrder()
    {
        _queue.Enqueue("test-register", CreateTestTransaction("tx-first"), priority: 5);
        Thread.Sleep(10);
        _queue.Enqueue("test-register", CreateTestTransaction("tx-second"), priority: 5);
        Thread.Sleep(10);
        _queue.Enqueue("test-register", CreateTestTransaction("tx-third"), priority: 5);

        var leases = await _queue.ClaimAsync("test-register", 3, DefaultLease, CancellationToken.None);

        leases[0].TransactionId.Should().Be("tx-first");
        leases[1].TransactionId.Should().Be("tx-second");
        leases[2].TransactionId.Should().Be("tx-third");
    }

    private static Transaction CreateTestTransaction(string id) => new()
    {
        TransactionId = id,
        RegisterId = "test-register",
        BlueprintId = "bp-1",
        ActionId = "action-1",
        Payload = JsonSerializer.Deserialize<JsonElement>("{}"),
        CreatedAt = DateTimeOffset.UtcNow,
        Signatures = [],
        PayloadHash = "hash-" + id
    };
}
