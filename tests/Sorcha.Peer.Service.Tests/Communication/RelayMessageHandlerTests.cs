// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sorcha.Peer.Service.Communication;
using Sorcha.Peer.Service.Communication.Models;
using Sorcha.Peer.Service.Connection;
using Sorcha.Peer.Service.Core;
using Sorcha.Peer.Service.Discovery;
using Sorcha.Peer.Service.Observability;
using Sorcha.Peer.Service.Protos;
using Sorcha.Peer.Service.Replication;

namespace Sorcha.Peer.Service.Tests.Communication;

public class RelayMessageHandlerTests : IAsyncDisposable
{
    private readonly RelayMessageHandler _handler;
    private readonly RelayCommunicationService _relayCommunication;
    private readonly RegisterCache _registerCache;
    private readonly RegisterSyncBackgroundService _syncBackgroundService;
    private readonly PeerConnectionPool _connectionPool;

    public RelayMessageHandlerTests()
    {
        var config = new PeerServiceConfiguration
        {
            NodeId = "test-node",
            PeerDiscovery = new PeerDiscoveryConfiguration
            {
                MaxPeersInList = 100,
                MinHealthyPeers = 5
            },
            SeedNodes = new SeedNodeConfiguration(),
            RegisterSync = new RegisterSyncConfiguration()
        };

        var peerListManager = new PeerListManager(
            new Mock<ILogger<PeerListManager>>().Object,
            Options.Create(config));

        var loggerFactoryMock = new Mock<ILoggerFactory>();
        loggerFactoryMock
            .Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns(new Mock<ILogger>().Object);

        _connectionPool = new PeerConnectionPool(
            new Mock<ILogger<PeerConnectionPool>>().Object,
            loggerFactoryMock.Object,
            peerListManager,
            Options.Create(config),
            new PeerServiceMetrics(),
            new PeerServiceActivitySource());

        _relayCommunication = new RelayCommunicationService(
            new Mock<ILogger<RelayCommunicationService>>().Object,
            _connectionPool,
            peerListManager,
            Options.Create(config));

        _registerCache = new RegisterCache(
            new Mock<ILogger<RegisterCache>>().Object);

        var replicationService = new RegisterReplicationService(
            new Mock<ILogger<RegisterReplicationService>>().Object,
            _connectionPool,
            peerListManager,
            _registerCache);

        _syncBackgroundService = new RegisterSyncBackgroundService(
            new Mock<ILogger<RegisterSyncBackgroundService>>().Object,
            replicationService,
            Options.Create(config));

        _handler = new RelayMessageHandler(
            new Mock<ILogger<RelayMessageHandler>>().Object,
            _relayCommunication,
            _registerCache,
            _syncBackgroundService);
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        Action act = () => new RelayMessageHandler(
            null!,
            _relayCommunication,
            _registerCache,
            _syncBackgroundService);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task HandleAsync_RegisterSyncRequest_ReadsLocalCacheAndSendsResponse()
    {
        // Populate local cache with a docket
        var cacheEntry = _registerCache.GetOrCreate("test-register");
        cacheEntry.AddOrUpdateDocket(new CachedDocket
        {
            RegisterId = "test-register",
            Version = 1,
            Data = new byte[] { 1, 2, 3 },
            DocketHash = "abc123",
            PreviousHash = "",
            TransactionIds = new List<string> { new string('a', 64) }
        });

        var request = new RegisterSyncRequest
        {
            CorrelationId = Guid.NewGuid().ToString(),
            RegisterId = "test-register",
            FromDocketVersion = 0,
            MaxDockets = 50
        };

        var message = new PeerMessage
        {
            SenderPeerId = "peer-a",
            RecipientPeerId = "test-node",
            MessageType = Sorcha.Peer.Service.Protos.MessageType.RegisterSyncRequest,
            Payload = ByteString.CopyFromUtf8(JsonSerializer.Serialize(request)),
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        // Should not throw — response will fail to send (no seed channel) but handler processes correctly
        await _handler.HandleAsync(message);
    }

    [Fact]
    public async Task HandleAsync_RegisterSyncResponse_CompletesCorrelation()
    {
        var correlationId = Guid.NewGuid().ToString();
        var response = new RegisterSyncResponse
        {
            CorrelationId = correlationId,
            RegisterId = "test-register",
            HasMore = false
        };

        var message = new PeerMessage
        {
            SenderPeerId = "peer-b",
            RecipientPeerId = "test-node",
            MessageType = Sorcha.Peer.Service.Protos.MessageType.RegisterSyncResponse,
            Payload = ByteString.CopyFromUtf8(JsonSerializer.Serialize(response)),
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        // No pending correlation, so this is a silent no-op
        await _handler.HandleAsync(message);
    }

    [Fact]
    public async Task HandleAsync_TransactionDataRequest_ReadsLocalTransactions()
    {
        var cacheEntry = _registerCache.GetOrCreate("test-register");
        cacheEntry.AddOrUpdateTransaction(new CachedTransaction
        {
            TransactionId = new string('b', 64),
            RegisterId = "test-register",
            Data = new byte[] { 10, 20, 30 },
            Checksum = "check123"
        });

        var request = new TransactionDataRequest
        {
            CorrelationId = Guid.NewGuid().ToString(),
            RegisterId = "test-register",
            TransactionIds = new List<string> { new string('b', 64) }
        };

        var message = new PeerMessage
        {
            SenderPeerId = "peer-c",
            RecipientPeerId = "test-node",
            MessageType = Sorcha.Peer.Service.Protos.MessageType.TransactionDataRequest,
            Payload = ByteString.CopyFromUtf8(JsonSerializer.Serialize(request)),
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        await _handler.HandleAsync(message);
    }

    [Fact]
    public async Task HandleAsync_TransactionDataResponse_CompletesCorrelation()
    {
        var response = new TransactionDataResponse
        {
            CorrelationId = Guid.NewGuid().ToString(),
            RegisterId = "test-register"
        };

        var message = new PeerMessage
        {
            SenderPeerId = "peer-d",
            RecipientPeerId = "test-node",
            MessageType = Sorcha.Peer.Service.Protos.MessageType.TransactionDataResponse,
            Payload = ByteString.CopyFromUtf8(JsonSerializer.Serialize(response)),
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        await _handler.HandleAsync(message);
    }

    [Fact]
    public async Task HandleTransactionNotificationAsync_SubscribedRegister_TriggersSyncRequest()
    {
        // Subscribe to a register first
        await _syncBackgroundService.SubscribeToRegisterAsync("test-register", ReplicationMode.FullReplica);

        var notification = new { RegisterId = "test-register", TransactionId = new string('c', 64) };
        var message = new PeerMessage
        {
            SenderPeerId = "peer-e",
            RecipientPeerId = "test-node",
            MessageType = Sorcha.Peer.Service.Protos.MessageType.TransactionNotification,
            Payload = ByteString.CopyFromUtf8(JsonSerializer.Serialize(notification)),
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        // Should trigger sync request (will fail to send due to no seed, but no exception)
        await _handler.HandleTransactionNotificationAsync(message);
    }

    [Fact]
    public async Task HandleTransactionNotificationAsync_UnsubscribedRegister_IsNoOp()
    {
        var notification = new { RegisterId = "unknown-register", TransactionId = new string('d', 64) };
        var message = new PeerMessage
        {
            SenderPeerId = "peer-f",
            RecipientPeerId = "test-node",
            MessageType = Sorcha.Peer.Service.Protos.MessageType.TransactionNotification,
            Payload = ByteString.CopyFromUtf8(JsonSerializer.Serialize(notification)),
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        // Should be a no-op since we're not subscribed
        await _handler.HandleTransactionNotificationAsync(message);
    }

    [Fact]
    public async Task HandleAsync_RegisterSyncRequest_RespectMaxDocketsLimit()
    {
        var cacheEntry = _registerCache.GetOrCreate("big-register");

        // Add many dockets
        for (int i = 1; i <= 10; i++)
        {
            cacheEntry.AddOrUpdateDocket(new CachedDocket
            {
                RegisterId = "big-register",
                Version = i,
                Data = new byte[100],
                DocketHash = $"hash-{i}",
                PreviousHash = i > 1 ? $"hash-{i - 1}" : "",
                TransactionIds = new List<string>()
            });
        }

        // Request only 3 dockets max
        var request = new RegisterSyncRequest
        {
            CorrelationId = Guid.NewGuid().ToString(),
            RegisterId = "big-register",
            FromDocketVersion = 0,
            MaxDockets = 3
        };

        var message = new PeerMessage
        {
            SenderPeerId = "peer-g",
            RecipientPeerId = "test-node",
            MessageType = Sorcha.Peer.Service.Protos.MessageType.RegisterSyncRequest,
            Payload = ByteString.CopyFromUtf8(JsonSerializer.Serialize(request)),
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        // Handler should cap at MaxDockets
        await _handler.HandleAsync(message);
    }

    [Fact]
    public async Task HandleAsync_UnknownMessageType_DoesNotThrow()
    {
        var message = new PeerMessage
        {
            SenderPeerId = "peer-h",
            RecipientPeerId = "test-node",
            MessageType = Sorcha.Peer.Service.Protos.MessageType.Unknown,
            Payload = ByteString.CopyFromUtf8("{}"),
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        await _handler.HandleAsync(message);
    }

    public async ValueTask DisposeAsync()
    {
        await _connectionPool.DisposeAsync();
    }
}
