// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Threading.Channels;
using FluentAssertions;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
    private readonly ReverseStreamManager _reverseStreams;

    public PeerCommunicationServiceImplTests()
    {
        // Rendezvous-capable: a configured external address makes this node accept reverse streams.
        var config = new PeerServiceConfiguration
        {
            NodeId = "test-node",
            NetworkAddress = new NetworkAddressConfiguration { ExternalAddress = "test-node:5000" },
            PeerDiscovery = new PeerDiscoveryConfiguration { MaxPeersInList = 100, MinHealthyPeers = 5 },
            SeedNodes = new SeedNodeConfiguration(),
            RegisterSync = new RegisterSyncConfiguration()
        };

        _reverseStreams = new ReverseStreamManager(NullLogger<ReverseStreamManager>.Instance);
        var relayMessageHandler = BuildRelayMessageHandler(config, out _connectionPool);

        _service = new PeerCommunicationServiceImpl(
            new Mock<ILogger<PeerCommunicationServiceImpl>>().Object,
            relayMessageHandler,
            _reverseStreams,
            new PeerServiceMetrics(),
            Options.Create(config));
    }

    private static RelayMessageHandler BuildRelayMessageHandler(
        PeerServiceConfiguration config, out PeerConnectionPool connectionPool)
    {
        var peerListManager = new PeerListManager(
            new Mock<ILogger<PeerListManager>>().Object, Options.Create(config));

        var loggerFactoryMock = new Mock<ILoggerFactory>();
        loggerFactoryMock.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);

        connectionPool = new PeerConnectionPool(
            new Mock<ILogger<PeerConnectionPool>>().Object, loggerFactoryMock.Object,
            peerListManager, Options.Create(config), new PeerServiceMetrics(), new PeerServiceActivitySource());

        var relayCommunication = new RelayCommunicationService(
            new Mock<ILogger<RelayCommunicationService>>().Object,
            connectionPool, peerListManager, Options.Create(config),
            new Lazy<RelayMessageHandler>(() => null!));

        var registerCache = new RegisterCache(new Mock<ILogger<RegisterCache>>().Object);

        var advertisementService = new RegisterAdvertisementService(
            new Mock<ILogger<RegisterAdvertisementService>>().Object, peerListManager);

        var replicationService = new RegisterReplicationService(
            new Mock<ILogger<RegisterReplicationService>>().Object,
            connectionPool, peerListManager, advertisementService, registerCache);

        var syncBackgroundService = new RegisterSyncBackgroundService(
            new Mock<ILogger<RegisterSyncBackgroundService>>().Object,
            replicationService, Options.Create(config), Mock.Of<IServiceScopeFactory>());

        var scopeMock = new Mock<IServiceScope>();
        scopeMock.Setup(s => s.ServiceProvider).Returns(new Mock<IServiceProvider>().Object);
        var scopeFactoryMock = new Mock<IServiceScopeFactory>();
        scopeFactoryMock.Setup(f => f.CreateScope()).Returns(scopeMock.Object);

        return new RelayMessageHandler(
            new Mock<ILogger<RelayMessageHandler>>().Object,
            relayCommunication, registerCache, syncBackgroundService,
            scopeFactoryMock.Object);
    }

    [Fact]
    public async Task SendMessage_RegisterSyncRequest_ReturnsAck()
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
        ack.Should().NotBeNull();
        ack.Timestamp.Should().BeGreaterThan(0);
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
            new Mock<ILogger<PeerCommunicationServiceImpl>>().Object, null!,
            _reverseStreams, new PeerServiceMetrics(),
            Options.Create(new PeerServiceConfiguration()));
        act.Should().Throw<ArgumentNullException>();
    }

    // --- Feature 143: reverse-stream (Stream) server behaviour ---

    [Fact]
    public async Task Stream_FirstMessage_RegistersThenRemovesOnComplete()
    {
        var reader = new FakeAsyncStreamReader();
        var writer = new Mock<IServerStreamWriter<PeerMessage>>();
        writer.Setup(w => w.WriteAsync(It.IsAny<PeerMessage>())).Returns(Task.CompletedTask);

        reader.Enqueue(HelloFrom("natd-1"));
        var streamTask = _service.Stream(reader, writer.Object, CreateTestContext());

        // While the stream is open, the NAT'd peer is registered.
        await WaitForAsync(() => _reverseStreams.ActiveCount == 1);
        _reverseStreams.TryGetStream("natd-1", out _).Should().BeTrue();

        // Completing the request stream tears the registration down.
        reader.Complete();
        await streamTask;
        _reverseStreams.ActiveCount.Should().Be(0);
        _reverseStreams.TryGetStream("natd-1", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Stream_FirstMessageMissingSenderId_ThrowsInvalidArgument()
    {
        var reader = new FakeAsyncStreamReader();
        var writer = new Mock<IServerStreamWriter<PeerMessage>>();
        reader.Enqueue(new PeerMessage
        {
            SenderPeerId = "",
            MessageType = MessageType.Heartbeat,
            Payload = ByteString.Empty,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        });

        var act = async () => await _service.Stream(reader, writer.Object, CreateTestContext());

        var ex = (await act.Should().ThrowAsync<RpcException>()).Which;
        ex.StatusCode.Should().Be(StatusCode.InvalidArgument);
        _reverseStreams.ActiveCount.Should().Be(0);
    }

    [Fact]
    public async Task Stream_NotRendezvousCapable_ThrowsFailedPrecondition()
    {
        // No external address and no explicit override → not rendezvous-capable.
        var config = new PeerServiceConfiguration
        {
            NodeId = "natd-node",
            PeerDiscovery = new PeerDiscoveryConfiguration { MaxPeersInList = 100, MinHealthyPeers = 5 },
            SeedNodes = new SeedNodeConfiguration(),
            RegisterSync = new RegisterSyncConfiguration()
        };
        config.IsRendezvousCapable.Should().BeFalse();

        var natdService = new PeerCommunicationServiceImpl(
            new Mock<ILogger<PeerCommunicationServiceImpl>>().Object,
            BuildRelayMessageHandler(config, out var pool),
            new ReverseStreamManager(NullLogger<ReverseStreamManager>.Instance),
            new PeerServiceMetrics(),
            Options.Create(config));

        try
        {
            var act = async () => await natdService.Stream(
                new FakeAsyncStreamReader(), Mock.Of<IServerStreamWriter<PeerMessage>>(), CreateTestContext());

            var ex = (await act.Should().ThrowAsync<RpcException>()).Which;
            ex.StatusCode.Should().Be(StatusCode.FailedPrecondition);
        }
        finally
        {
            await pool.DisposeAsync();
        }
    }

    private static PeerMessage HelloFrom(string peerId) => new()
    {
        SenderPeerId = peerId,
        MessageType = MessageType.Heartbeat,
        Payload = ByteString.Empty,
        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
    };

    private static async Task WaitForAsync(Func<bool> condition, int timeoutMs = 2000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return;
            await Task.Delay(10);
        }

        condition().Should().BeTrue("the condition should be met within {0}ms", timeoutMs);
    }

    private static ServerCallContext CreateTestContext(CancellationToken ct = default)
        => new TestServerCallContext(ct);

    /// <summary>Channel-backed fake reader for driving the server <c>Stream</c> method in tests.</summary>
    private sealed class FakeAsyncStreamReader : IAsyncStreamReader<PeerMessage>
    {
        private readonly Channel<PeerMessage> _channel = Channel.CreateUnbounded<PeerMessage>();
        public PeerMessage Current { get; private set; } = null!;

        public void Enqueue(PeerMessage message) => _channel.Writer.TryWrite(message);
        public void Complete() => _channel.Writer.TryComplete();

        public async Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            if (await _channel.Reader.WaitToReadAsync(cancellationToken) &&
                _channel.Reader.TryRead(out var message))
            {
                Current = message;
                return true;
            }

            return false;
        }
    }

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
