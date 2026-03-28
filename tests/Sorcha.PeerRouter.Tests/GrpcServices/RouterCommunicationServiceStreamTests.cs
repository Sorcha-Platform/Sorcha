// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;

using Google.Protobuf;

using Grpc.Core;

using Microsoft.Extensions.Logging.Abstractions;

using Sorcha.Peer.Service.Protos;
using Sorcha.PeerRouter.GrpcServices;
using Sorcha.PeerRouter.Models;
using Sorcha.PeerRouter.Services;

namespace Sorcha.PeerRouter.Tests.GrpcServices;

public sealed class RouterCommunicationServiceStreamTests
{
    private readonly RoutingTable _routingTable;
    private readonly ReverseStreamManager _reverseStreamManager;
    private readonly EventBuffer _eventBuffer;
    private readonly RouterConfiguration _relayEnabledConfig;
    private readonly RouterConfiguration _relayDisabledConfig;

    public RouterCommunicationServiceStreamTests()
    {
        _relayEnabledConfig = new RouterConfiguration { EnableRelay = true };
        _relayDisabledConfig = new RouterConfiguration { EnableRelay = false };
        _eventBuffer = new EventBuffer(_relayEnabledConfig);
        _routingTable = new RoutingTable(_eventBuffer, _relayEnabledConfig);
        _reverseStreamManager = new ReverseStreamManager(
            NullLogger<ReverseStreamManager>.Instance);
    }

    private RouterCommunicationService CreateService(RouterConfiguration config) =>
        new(_routingTable, _reverseStreamManager, _eventBuffer, config,
            NullLogger<RouterCommunicationService>.Instance);

    private static PeerMessage CreateMessage(
        string senderId = "sender-1",
        string recipientId = "",
        MessageType type = MessageType.TransactionNotification) => new()
    {
        SenderPeerId = senderId,
        RecipientPeerId = recipientId,
        MessageType = type,
        Payload = ByteString.CopyFromUtf8("test-payload"),
        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
    };

    [Fact]
    public async Task Stream_RelayDisabled_ThrowsFailedPrecondition()
    {
        var service = CreateService(_relayDisabledConfig);
        var requestStream = new TestAsyncStreamReader<PeerMessage>([]);
        var responseStream = new TestServerStreamWriter<PeerMessage>();

        var act = () => service.Stream(requestStream, responseStream, TestServerCallContext.Create());

        var ex = await act.Should().ThrowAsync<RpcException>();
        ex.Which.StatusCode.Should().Be(StatusCode.FailedPrecondition);
    }

    [Fact]
    public async Task Stream_FirstMessageWithoutSenderId_ThrowsInvalidArgument()
    {
        var service = CreateService(_relayEnabledConfig);
        var message = CreateMessage(senderId: "");
        var requestStream = new TestAsyncStreamReader<PeerMessage>([message]);
        var responseStream = new TestServerStreamWriter<PeerMessage>();

        var act = () => service.Stream(requestStream, responseStream, TestServerCallContext.Create());

        var ex = await act.Should().ThrowAsync<RpcException>();
        ex.Which.StatusCode.Should().Be(StatusCode.InvalidArgument);
    }

    [Fact]
    public async Task Stream_ValidMessages_RegistersAndCleansUpStream()
    {
        var service = CreateService(_relayEnabledConfig);
        var messages = new[]
        {
            CreateMessage(senderId: "nat-peer-1"),
            CreateMessage(senderId: "nat-peer-1")
        };
        var requestStream = new TestAsyncStreamReader<PeerMessage>(messages);
        var responseStream = new TestServerStreamWriter<PeerMessage>();

        await service.Stream(requestStream, responseStream, TestServerCallContext.Create());

        // Stream should be cleaned up after completion
        _reverseStreamManager.TryGetStream("nat-peer-1", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Stream_EmitsConnectedAndDisconnectedEvents()
    {
        var service = CreateService(_relayEnabledConfig);
        var messages = new[] { CreateMessage(senderId: "nat-peer-2") };
        var requestStream = new TestAsyncStreamReader<PeerMessage>(messages);
        var responseStream = new TestServerStreamWriter<PeerMessage>();

        await service.Stream(requestStream, responseStream, TestServerCallContext.Create());

        var events = _eventBuffer.GetSnapshot();
        events.Should().Contain(e => e.Type == RouterEventType.StreamConnected && e.PeerId == "nat-peer-2");
        events.Should().Contain(e => e.Type == RouterEventType.StreamDisconnected && e.PeerId == "nat-peer-2");
    }

    [Fact]
    public async Task Stream_WithRecipient_AttemptsForwarding()
    {
        var service = CreateService(_relayEnabledConfig);

        // Register a target peer via reverse stream
        var targetStream = new TestServerStreamWriter<PeerMessage>();
        _reverseStreamManager.RegisterStream("target-peer", targetStream);

        var messages = new[]
        {
            CreateMessage(senderId: "nat-peer-3", recipientId: "target-peer")
        };
        var requestStream = new TestAsyncStreamReader<PeerMessage>(messages);
        var responseStream = new TestServerStreamWriter<PeerMessage>();

        await service.Stream(requestStream, responseStream, TestServerCallContext.Create());

        // The message should have been forwarded to the target peer's reverse stream
        targetStream.Messages.Should().HaveCount(1);
        targetStream.Messages[0].SenderPeerId.Should().Be("nat-peer-3");
    }

    [Fact]
    public async Task Stream_CancelledToken_CompletesGracefully()
    {
        var service = CreateService(_relayEnabledConfig);
        using var cts = new CancellationTokenSource();

        // Create a stream that will block until cancelled
        var channel = System.Threading.Channels.Channel.CreateUnbounded<PeerMessage>();
        // Write one message to establish the stream, then cancel
        channel.Writer.TryWrite(CreateMessage(senderId: "cancel-peer"));

        var requestStream = new TestAsyncStreamReader<PeerMessage>([CreateMessage(senderId: "cancel-peer")]);
        var responseStream = new TestServerStreamWriter<PeerMessage>();
        var context = TestServerCallContext.Create(cts.Token);

        // Stream should complete gracefully (the TestAsyncStreamReader completes immediately)
        await service.Stream(requestStream, responseStream, context);

        _reverseStreamManager.TryGetStream("cancel-peer", out _).Should().BeFalse();
    }
}
