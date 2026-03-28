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

namespace Sorcha.Peer.Service.Tests.Communication;

public class ReverseStreamLifecycleTests : IAsyncDisposable
{
    private readonly RelayCommunicationService _service;
    private readonly PeerConnectionPool _connectionPool;
    private readonly PeerListManager _peerListManager;

    public ReverseStreamLifecycleTests()
    {
        var config = new PeerServiceConfiguration
        {
            NodeId = "test-node-reverse",
            PeerDiscovery = new PeerDiscoveryConfiguration
            {
                MaxPeersInList = 100,
                MinHealthyPeers = 5,
                RefreshIntervalMinutes = 15
            },
            SeedNodes = new SeedNodeConfiguration()
        };

        _peerListManager = new PeerListManager(
            new Mock<ILogger<PeerListManager>>().Object,
            Options.Create(config));

        var loggerFactoryMock = new Mock<ILoggerFactory>();
        loggerFactoryMock
            .Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns(new Mock<ILogger>().Object);

        _connectionPool = new PeerConnectionPool(
            new Mock<ILogger<PeerConnectionPool>>().Object,
            loggerFactoryMock.Object,
            _peerListManager,
            Options.Create(config),
            new PeerServiceMetrics(),
            new PeerServiceActivitySource());

        _service = new RelayCommunicationService(
            new Mock<ILogger<RelayCommunicationService>>().Object,
            _connectionPool,
            _peerListManager,
            Options.Create(config));
    }

    [Fact]
    public void IsReverseStreamActive_InitiallyFalse()
    {
        _service.IsReverseStreamActive.Should().BeFalse();
    }

    [Fact]
    public async Task EstablishReverseStreamAsync_NoSeedChannel_RetriesUntilCancelled()
    {
        // No seed nodes available, so it should keep retrying until cancelled
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        // Should not throw — just exits when cancelled
        await _service.Invoking(s => s.EstablishReverseStreamAsync(cts.Token))
            .Should().NotThrowAsync();

        _service.IsReverseStreamActive.Should().BeFalse();
    }

    [Fact]
    public void SetRelayMessageHandler_NullThrows()
    {
        _service.Invoking(s => s.SetRelayMessageHandler(null!))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task EstablishReverseStreamAsync_NoHandler_ReturnsImmediately()
    {
        // Don't set handler — should return without establishing stream
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await _service.EstablishReverseStreamAsync(cts.Token);

        _service.IsReverseStreamActive.Should().BeFalse();
    }

    public ValueTask DisposeAsync()
    {
        // PeerConnectionPool doesn't implement IDisposable — no cleanup needed
        return ValueTask.CompletedTask;
    }
}
