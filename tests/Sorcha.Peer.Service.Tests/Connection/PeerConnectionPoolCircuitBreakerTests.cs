// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sorcha.Peer.Service.Communication;
using Sorcha.Peer.Service.Connection;
using Sorcha.Peer.Service.Core;
using Sorcha.Peer.Service.Discovery;
using Sorcha.Peer.Service.Observability;

namespace Sorcha.Peer.Service.Tests.Connection;

public class PeerConnectionPoolCircuitBreakerTests : IAsyncDisposable
{
    private readonly PeerConnectionPool _pool;
    private readonly PeerListManager _peerListManager;
    private readonly PeerServiceMetrics _metrics;
    private readonly PeerServiceActivitySource _activitySource;
    private readonly Mock<ILoggerFactory> _loggerFactoryMock;

    /// <summary>
    /// Circuit breaker threshold used in tests — low value for fast tripping.
    /// </summary>
    private const int CircuitBreakerThreshold = 3;

    /// <summary>
    /// Circuit breaker reset timeout — very short for testing half-open transitions.
    /// </summary>
    private const int CircuitBreakerResetMinutes = 1;

    public PeerConnectionPoolCircuitBreakerTests()
    {
        var peerLoggerMock = new Mock<ILogger<PeerListManager>>();
        var config = Options.Create(new PeerServiceConfiguration
        {
            PeerDiscovery = new PeerDiscoveryConfiguration
            {
                MaxPeersInList = 50,
                MinHealthyPeers = 5,
                RefreshIntervalMinutes = 15
            },
            Communication = new CommunicationConfiguration
            {
                CircuitBreakerThreshold = CircuitBreakerThreshold,
                CircuitBreakerResetMinutes = CircuitBreakerResetMinutes
            },
            SeedNodes = new SeedNodeConfiguration()
        });

        _peerListManager = new PeerListManager(peerLoggerMock.Object, config);

        _loggerFactoryMock = new Mock<ILoggerFactory>();
        _loggerFactoryMock
            .Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns(new Mock<ILogger>().Object);

        _metrics = new PeerServiceMetrics();
        _activitySource = new PeerServiceActivitySource();

        _pool = new PeerConnectionPool(
            new Mock<ILogger<PeerConnectionPool>>().Object,
            _loggerFactoryMock.Object,
            _peerListManager,
            config,
            _metrics,
            _activitySource);
    }

    [Fact]
    public async Task ConnectToPeerAsync_CircuitClosed_AllowsConnection()
    {
        // Act
        var result = await _pool.ConnectToPeerAsync("peer-1", "http://localhost:5001");

        // Assert
        result.Should().BeTrue();
        _pool.ActiveConnectionCount.Should().Be(1);
    }

    [Fact]
    public async Task ConnectToPeerAsync_CircuitOpen_ThrowsCircuitBreakerOpenException()
    {
        // Arrange — trip the circuit breaker by recording failures
        var breaker = _pool.GetOrCreateCircuitBreaker("peer-open");
        for (int i = 0; i < CircuitBreakerThreshold; i++)
        {
            breaker.OnFailure();
        }

        breaker.State.Should().Be(CircuitState.Open);

        // Act & Assert
        var act = () => _pool.ConnectToPeerAsync("peer-open", "http://localhost:5001");
        await act.Should().ThrowAsync<CircuitBreakerOpenException>();
    }

    [Fact]
    public async Task ConnectToPeerAsync_CircuitHalfOpen_AllowsProbeConnection()
    {
        // Arrange — create a breaker with zero reset timeout so it immediately
        // transitions from Open to HalfOpen when State is checked
        var loggerMock = new Mock<ILogger<CircuitBreaker>>();
        var breaker = new CircuitBreaker(
            loggerMock.Object,
            "test-halfopen",
            CircuitBreakerThreshold,
            TimeSpan.Zero);

        // Trip the circuit
        for (int i = 0; i < CircuitBreakerThreshold; i++)
        {
            breaker.OnFailure();
        }

        // With zero timeout, the State getter will auto-transition to HalfOpen
        await Task.Delay(10);
        breaker.State.Should().Be(CircuitState.HalfOpen);

        // Act — HalfOpen should allow a probe execution
        var result = await breaker.ExecuteAsync(async () =>
        {
            await Task.CompletedTask;
            return true;
        });

        // Assert — successful probe should close the circuit
        result.Should().BeTrue();
        breaker.State.Should().Be(CircuitState.Closed);
    }

    [Fact]
    public void RecordFailure_OpensCircuitAfterThreshold()
    {
        // Arrange
        var breaker = _pool.GetOrCreateCircuitBreaker("peer-fail");
        breaker.State.Should().Be(CircuitState.Closed);

        // Act — record failures up to threshold
        for (int i = 0; i < CircuitBreakerThreshold - 1; i++)
        {
            breaker.OnFailure();
        }

        // Still closed (one below threshold)
        breaker.State.Should().Be(CircuitState.Closed);

        // One more failure trips the circuit
        breaker.OnFailure();

        // Assert
        breaker.State.Should().Be(CircuitState.Open);
    }

    [Fact]
    public async Task RecordSuccess_AfterHalfOpen_ClosesCircuit()
    {
        // Arrange — create a breaker with zero reset timeout so it
        // auto-transitions to HalfOpen immediately
        var loggerMock = new Mock<ILogger<CircuitBreaker>>();
        var breaker = new CircuitBreaker(
            loggerMock.Object,
            "test-success-close",
            CircuitBreakerThreshold,
            TimeSpan.Zero);

        // Trip the circuit
        for (int i = 0; i < CircuitBreakerThreshold; i++)
        {
            breaker.OnFailure();
        }

        // With zero timeout, State getter transitions to HalfOpen
        await Task.Delay(10);
        breaker.State.Should().Be(CircuitState.HalfOpen);

        // Act — record success while half-open
        breaker.OnSuccess();

        // Assert — circuit should close
        breaker.State.Should().Be(CircuitState.Closed);
    }

    [Fact]
    public async Task ConnectToPeerAsync_SuccessfulConnection_RecordsSuccessOnBreaker()
    {
        // Arrange — connect successfully
        await _pool.ConnectToPeerAsync("peer-success", "http://localhost:5001");

        // Act — get the breaker stats
        var stats = _pool.GetCircuitBreakerStats();

        // Assert — breaker exists and is closed with zero failures
        stats.Should().ContainKey("peer-success");
        stats["peer-success"].State.Should().Be(CircuitState.Closed);
        stats["peer-success"].FailureCount.Should().Be(0);
    }

    [Fact]
    public void GetCircuitBreakerStats_ReturnsAllBreakerStats()
    {
        // Arrange — create breakers for multiple peers
        _pool.GetOrCreateCircuitBreaker("peer-a");
        _pool.GetOrCreateCircuitBreaker("peer-b");
        _pool.GetOrCreateCircuitBreaker("peer-c");

        // Act
        var stats = _pool.GetCircuitBreakerStats();

        // Assert
        stats.Should().HaveCount(3);
        stats.Should().ContainKey("peer-a");
        stats.Should().ContainKey("peer-b");
        stats.Should().ContainKey("peer-c");
    }

    [Fact]
    public void GetOrCreateCircuitBreaker_ReturnsSameInstanceForSamePeer()
    {
        // Act
        var breaker1 = _pool.GetOrCreateCircuitBreaker("peer-same");
        var breaker2 = _pool.GetOrCreateCircuitBreaker("peer-same");

        // Assert
        breaker1.Should().BeSameAs(breaker2);
    }

    [Fact]
    public void GetOrCreateCircuitBreaker_ReturnsDifferentInstancesForDifferentPeers()
    {
        // Act
        var breaker1 = _pool.GetOrCreateCircuitBreaker("peer-x");
        var breaker2 = _pool.GetOrCreateCircuitBreaker("peer-y");

        // Assert
        breaker1.Should().NotBeSameAs(breaker2);
    }

    [Fact]
    public void CircuitBreaker_FailureInHalfOpen_ReOpensCircuit()
    {
        // Arrange — use a non-zero but short timeout so we can control transitions
        var loggerMock = new Mock<ILogger<CircuitBreaker>>();
        var breaker = new CircuitBreaker(
            loggerMock.Object,
            "test-halfopen-fail",
            CircuitBreakerThreshold,
            TimeSpan.Zero);

        // Trip the circuit
        for (int i = 0; i < CircuitBreakerThreshold; i++)
        {
            breaker.OnFailure();
        }

        // With zero timeout, accessing State transitions to HalfOpen
        Thread.Sleep(10);
        breaker.State.Should().Be(CircuitState.HalfOpen);

        // Act — failure in HalfOpen should re-open the circuit
        breaker.OnFailure();

        // Assert — circuit is open again; but with zero timeout the State getter
        // will auto-transition back to HalfOpen. Verify via GetStats() which
        // reads the raw state inside the lock before the transition check.
        var stats = breaker.GetStats();
        stats.State.Should().Be(CircuitState.Open);
    }

    [Fact]
    public async Task ConnectToPeerAsync_CircuitBreakerConfiguredFromCommunicationSettings()
    {
        // Arrange — connect to create the breaker
        await _pool.ConnectToPeerAsync("peer-config", "http://localhost:5001");

        // Act
        var stats = _pool.GetCircuitBreakerStats();

        // Assert — threshold should match configuration
        stats["peer-config"].FailureThreshold.Should().Be(CircuitBreakerThreshold);
        stats["peer-config"].ResetTimeout.Should().Be(TimeSpan.FromMinutes(CircuitBreakerResetMinutes));
    }

    public async ValueTask DisposeAsync()
    {
        await _pool.DisposeAsync();
        _peerListManager.Dispose();
        _metrics.Dispose();
        _activitySource.Dispose();
    }
}
