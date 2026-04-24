// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sorcha.Blueprint.Service.Configuration;
using Sorcha.Blueprint.Service.Services.Implementation;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.Blueprint.Service.Storage.Presentations;
using StackExchange.Redis;
using Xunit;

namespace Sorcha.Blueprint.Service.Tests.Services;

/// <summary>
/// Feature 111 US4 — T055 / T056 unit tests for <see cref="AbandonmentSweeper"/>.
/// Exercises the leader-lock SET NX path and the candidate-dispatch loop via
/// the internal TickAsync (InternalsVisibleTo makes this visible to the test
/// assembly).
/// </summary>
public class AbandonmentSweeperTests
{
    private readonly Mock<IConnectionMultiplexer> _redisMock = new();
    private readonly Mock<IDatabase> _dbMock = new();
    private readonly Mock<IPendingPresentationStore> _storeMock = new();
    private readonly Mock<IPresentationLifecycleService> _lifecycleMock = new();

    public AbandonmentSweeperTests()
    {
        _redisMock.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(_dbMock.Object);
    }

    private AbandonmentSweeper Make(int tickSec = 30, int lockTtlSec = 60)
    {
        var services = new ServiceCollection();
        services.AddSingleton(_storeMock.Object);
        services.AddSingleton(_lifecycleMock.Object);
        var sp = services.BuildServiceProvider();

        var options = Options.Create(new PresentationLifecycleOptions
        {
            SweeperIntervalSeconds = tickSec,
            SweeperLeaderLockTtlSeconds = lockTtlSec
        });
        return new AbandonmentSweeper(
            sp, _redisMock.Object, options,
            new Mock<ILogger<AbandonmentSweeper>>().Object);
    }

    [Fact]
    public async Task TickAsync_AcquiresLeaderLock_ScansAndDispatches_T055()
    {
        // Leader-lock SET NX returns true — we are the leader this tick.
        _dbMock.Setup(d => d.StringSetAsync(
                It.Is<RedisKey>(k => k.ToString() == "sorcha:presentation:sweeper-lock"),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(),
                When.NotExists))
            .ReturnsAsync(true);
        _dbMock.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        // Store returns three near-expiry candidates.
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        _storeMock.Setup(s => s.ListPendingNearExpiryAsync(
                It.IsAny<TimeSpan>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ids);

        // Act
        await Make().TickAsync(TimeSpan.FromSeconds(60), CancellationToken.None);

        // Assert — each candidate was dispatched to HandleAbandonmentAsync.
        foreach (var id in ids)
        {
            _lifecycleMock.Verify(
                l => l.HandleAbandonmentAsync(id, It.IsAny<CancellationToken>()),
                Times.Once);
        }

        // Near-expiry window = 2x tick interval (60s).
        _storeMock.Verify(s => s.ListPendingNearExpiryAsync(
            TimeSpan.FromSeconds(60), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Once);

        // Lock released at end of tick.
        _dbMock.Verify(d => d.KeyDeleteAsync(
            It.Is<RedisKey>(k => k.ToString() == "sorcha:presentation:sweeper-lock"),
            It.IsAny<CommandFlags>()),
            Times.Once);
    }

    [Fact]
    public async Task TickAsync_LostLeaderLock_DoesNotScan_T056()
    {
        // Leader-lock SET NX returns false — another replica is the leader.
        _dbMock.Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(), When.NotExists))
            .ReturnsAsync(false);

        await Make().TickAsync(TimeSpan.FromSeconds(60), CancellationToken.None);

        // Critical guarantee: no scan, no dispatch, no lock release when
        // we're not the leader — otherwise two replicas would both sweep
        // and write duplicate PresentationAbandoned transactions.
        _storeMock.Verify(s => s.ListPendingNearExpiryAsync(
            It.IsAny<TimeSpan>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _lifecycleMock.Verify(l => l.HandleAbandonmentAsync(
            It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _dbMock.Verify(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()),
            Times.Never);
    }

    [Fact]
    public async Task TickAsync_NoCandidates_NoOp()
    {
        _dbMock.Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(), When.NotExists))
            .ReturnsAsync(true);
        _dbMock.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        _storeMock.Setup(s => s.ListPendingNearExpiryAsync(
                It.IsAny<TimeSpan>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Guid>());

        await Make().TickAsync(TimeSpan.FromSeconds(60), CancellationToken.None);

        _lifecycleMock.Verify(l => l.HandleAbandonmentAsync(
            It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        // Lock still released.
        _dbMock.Verify(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()),
            Times.Once);
    }

    [Fact]
    public async Task TickAsync_DispatchThrows_ContinuesWithRemainingCandidates()
    {
        _dbMock.Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(), When.NotExists))
            .ReturnsAsync(true);
        _dbMock.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        var bad = Guid.NewGuid();
        var good = Guid.NewGuid();
        _storeMock.Setup(s => s.ListPendingNearExpiryAsync(
                It.IsAny<TimeSpan>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { bad, good });

        _lifecycleMock.Setup(l => l.HandleAbandonmentAsync(bad, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated"));
        _lifecycleMock.Setup(l => l.HandleAbandonmentAsync(good, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await Make().TickAsync(TimeSpan.FromSeconds(60), CancellationToken.None);

        // Bad dispatch was attempted once (and swallowed); good dispatch still ran.
        _lifecycleMock.Verify(l => l.HandleAbandonmentAsync(bad, It.IsAny<CancellationToken>()), Times.Once);
        _lifecycleMock.Verify(l => l.HandleAbandonmentAsync(good, It.IsAny<CancellationToken>()), Times.Once);
        _dbMock.Verify(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task TickAsync_CancellationRequested_StopsDispatchEarly()
    {
        _dbMock.Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(), When.NotExists))
            .ReturnsAsync(true);
        _dbMock.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        var ids = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();
        _storeMock.Setup(s => s.ListPendingNearExpiryAsync(
                It.IsAny<TimeSpan>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ids);

        using var cts = new CancellationTokenSource();
        int dispatched = 0;
        _lifecycleMock.Setup(l => l.HandleAbandonmentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                if (++dispatched == 1) cts.Cancel();
            })
            .Returns(Task.CompletedTask);

        await Make().TickAsync(TimeSpan.FromSeconds(60), cts.Token);

        // After the first dispatch triggers cancellation, the loop must stop —
        // no further HandleAbandonmentAsync invocations.
        _lifecycleMock.Verify(l => l.HandleAbandonmentAsync(
            It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
