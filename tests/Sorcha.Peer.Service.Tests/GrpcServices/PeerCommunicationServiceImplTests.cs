// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sorcha.Peer.Service.Communication;
using Sorcha.Peer.Service.Connection;
using Sorcha.Peer.Service.Core;
using Sorcha.Peer.Service.Discovery;
using Sorcha.Peer.Service.GrpcServices;
using Sorcha.Peer.Service.Observability;
using Sorcha.Peer.Service.Protos;
using Sorcha.Peer.Service.Replication;

namespace Sorcha.Peer.Service.Tests.GrpcServices;

public class PeerCommunicationServiceImplTests : IAsyncDisposable
{
    private readonly PeerCommunicationServiceImpl _service;
    private readonly PeerConnectionPool _connectionPool;

    public PeerCommunicationServiceImplTests()
    {
        var config = new PeerServiceConfiguration
        {
            NodeId = "test-node",
            PeerDiscovery = new PeerDiscoveryConfiguration { MaxPeersInList = 100, MinHealthyPeers = 5 },
            SeedNodes = new SeedNodeConfiguration(),
            RegisterSync = new RegisterSyncConfiguration()
        };

        var peerListManager = new PeerListManager(
            new Mock<ILogger<PeerListManager>>().Object, Options.Create(config));

        var loggerFactoryMock = new Mock<ILoggerFactory>();
        loggerFactoryMock.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);

        _connectionPool = new PeerConnectionPool(
            new Mock<ILogger<PeerConnectionPool>>().Object, loggerFactoryMock.Object,
            peerListManager, Options.Create(config), new PeerServiceMetrics(), new PeerServiceActivitySource());

        var relayCommunication = new RelayCommunicationService(
            new Mock<ILogger<RelayCommunicationService>>().Object,
            _connectionPool, peerListManager, Options.Create(config),
            new Lazy<RelayMessageHandler>(() => null!));

        var registerCache = new RegisterCache(new Mock<ILogger<RegisterCache>>().Object);

        var advertisementService = new RegisterAdvertisementService(
            new Mock<ILogger<RegisterAdvertisementService>>().Object,
            peerListManager);

        var replicationService = new RegisterReplicationService(
            new Mock<ILogger<RegisterReplicationService>>().Object,
            _connectionPool, peerListManager, advertisementService, registerCache);

        var syncBackgroundService = new RegisterSyncBackgroundService(
            new Mock<ILogger<RegisterSyncBackgroundService>>().Object,
            replicationService, Options.Create(config), Mock.Of<IServiceScopeFactory>());

        var relayMessageHandler = new RelayMessageHandler(
            new Mock<ILogger<RelayMessageHandler>>().Object,
            relayCommunication, registerCache, syncBackgroundService);

        _service = new PeerCommunicationServiceImpl(
            new Mock<ILogger<PeerCommunicationServiceImpl>>().Object,
            relayMessageHandler);
    }

    [Fact]
    public async Task SendMessage_RegisterSyncRequest_ReturnsReceivedTrue()
    {
        var message = new PeerMessage
        {
            SenderPeerId = "peer-a",
            RecipientPeerId = "test-node",
            MessageType = MessageType.RegisterSyncRequest,
            Payload = ByteString.CopyFromUtf8("{\"CorrelationId\":\"abc\",\"RegisterId\":\"reg1\",\"FromDocketVersion\":0,\"MaxDockets\":50}"),
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        var ack = await _service.SendMessage(message, CreateTestContext());
        ack.Received.Should().BeTrue();
    }

    [Fact]
    public async Task SendMessage_TransactionNotification_ReturnsReceivedTrue()
    {
        var message = new PeerMessage
        {
            SenderPeerId = "peer-b",
            RecipientPeerId = "test-node",
            MessageType = MessageType.TransactionNotification,
            Payload = ByteString.CopyFromUtf8("{\"RegisterId\":\"reg1\"}"),
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        var ack = await _service.SendMessage(message, CreateTestContext());
        ack.Received.Should().BeTrue();
    }

    [Fact]
    public async Task SendMessage_UnknownType_ReturnsReceivedTrue()
    {
        var message = new PeerMessage
        {
            SenderPeerId = "peer-c",
            RecipientPeerId = "test-node",
            MessageType = MessageType.Unknown,
            Payload = ByteString.CopyFromUtf8("{}"),
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        var ack = await _service.SendMessage(message, CreateTestContext());
        ack.Received.Should().BeTrue();
    }

    [Fact]
    public void Constructor_NullRelayMessageHandler_ThrowsArgumentNullException()
    {
        Action act = () => new PeerCommunicationServiceImpl(
            new Mock<ILogger<PeerCommunicationServiceImpl>>().Object, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    private static ServerCallContext CreateTestContext(CancellationToken ct = default)
        => new TestServerCallContext(ct);

    private sealed class TestServerCallContext(CancellationToken cancellationToken = default) : ServerCallContext
    {
        protected override string MethodCore => "TestMethod";
        protected override string HostCore => "localhost";
        protected override string PeerCore => "test-peer";
        protected override DateTime DeadlineCore => DateTime.MaxValue;
        protected override Metadata RequestHeadersCore => new();
        protected override CancellationToken CancellationTokenCore => cancellationToken;
        protected override Metadata ResponseTrailersCore => new();
        protected override Status StatusCore { get; set; }
        protected override WriteOptions? WriteOptionsCore { get; set; }
        protected override AuthContext AuthContextCore =>
            new(string.Empty, new Dictionary<string, List<AuthProperty>>());
        protected override ContextPropagationToken CreatePropagationTokenCore(
            ContextPropagationOptions? options) => throw new NotImplementedException();
        protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) =>
            Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _connectionPool.DisposeAsync();
    }
}
