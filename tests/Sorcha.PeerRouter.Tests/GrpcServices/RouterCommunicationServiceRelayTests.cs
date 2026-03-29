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

public sealed class RouterCommunicationServiceRelayTests
{
    private readonly RoutingTable _routingTable;
    private readonly ReverseStreamManager _reverseStreamManager;
    private readonly EventBuffer _eventBuffer;
    private readonly RouterConfiguration _relayEnabledConfig;

    public RouterCommunicationServiceRelayTests()
    {
        _relayEnabledConfig = new RouterConfiguration { EnableRelay = true };
        _eventBuffer = new EventBuffer(_relayEnabledConfig);
        _routingTable = new RoutingTable(_eventBuffer, _relayEnabledConfig);
        _reverseStreamManager = new ReverseStreamManager(
            NullLogger<ReverseStreamManager>.Instance);
    }

    private RouterCommunicationService CreateService() =>
        new(_routingTable, _reverseStreamManager, _eventBuffer, _relayEnabledConfig,
            NullLogger<RouterCommunicationService>.Instance, new RouterChannelPool());

    private static PeerMessage CreateMessage(
        string senderId = "sender-1",
        string recipientId = "recipient-1",
        MessageType type = MessageType.TransactionNotification) => new()
    {
        SenderPeerId = senderId,
        RecipientPeerId = recipientId,
        MessageType = type,
        Payload = ByteString.CopyFromUtf8("test-payload"),
        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
    };

    [Fact]
    public async Task SendMessage_RecipientHasReverseStream_RelaysViaStream()
    {
        var service = CreateService();
        var targetStream = new TestServerStreamWriter<PeerMessage>();

        // Register recipient with empty address (NAT'd) in routing table
        _routingTable.RegisterPeer(new PeerInfo
        {
            PeerId = "nat-recipient",
            Address = "",
            Port = 0
        });

        // Register reverse stream for the recipient
        _reverseStreamManager.RegisterStream("nat-recipient", targetStream);

        var message = CreateMessage(recipientId: "nat-recipient");
        var ack = await service.SendMessage(message, TestServerCallContext.Create());

        ack.Received.Should().BeTrue();
        targetStream.Messages.Should().HaveCount(1);
        targetStream.Messages[0].SenderPeerId.Should().Be("sender-1");
    }

    [Fact]
    public async Task SendMessage_RecipientNotInRoutingTable_ButHasReverseStream_RelaysViaStream()
    {
        var service = CreateService();
        var targetStream = new TestServerStreamWriter<PeerMessage>();

        // Do NOT register in routing table, only register reverse stream
        _reverseStreamManager.RegisterStream("stream-only-peer", targetStream);

        var message = CreateMessage(recipientId: "stream-only-peer");
        var ack = await service.SendMessage(message, TestServerCallContext.Create());

        ack.Received.Should().BeTrue();
        targetStream.Messages.Should().HaveCount(1);
    }

    [Fact]
    public async Task SendMessage_RecipientHasAddressAndNoReverseStream_UsesDirectChannel()
    {
        var service = CreateService();

        // Register peer with actual address (will fail to connect in test)
        _routingTable.RegisterPeer(new PeerInfo
        {
            PeerId = "direct-peer",
            Address = "10.0.0.99",
            Port = 5000
        });

        var message = CreateMessage(recipientId: "direct-peer");

        // Should throw Unavailable because the peer isn't actually running
        var act = () => service.SendMessage(message, TestServerCallContext.Create());
        var ex = await act.Should().ThrowAsync<RpcException>();
        ex.Which.StatusCode.Should().Be(StatusCode.Unavailable);
    }

    [Fact]
    public async Task SendMessage_ReverseStreamRelay_EmitsRelayForwardedEvent()
    {
        var service = CreateService();
        var targetStream = new TestServerStreamWriter<PeerMessage>();

        _reverseStreamManager.RegisterStream("evented-peer", targetStream);

        var message = CreateMessage(recipientId: "evented-peer");
        await service.SendMessage(message, TestServerCallContext.Create());

        var events = _eventBuffer.GetSnapshot();
        events.Should().Contain(e =>
            e.Type == RouterEventType.RelayForwarded &&
            e.PeerId == "sender-1");
    }

    [Fact]
    public async Task SendMessage_NoStreamNoAddress_ThrowsNotFound()
    {
        var service = CreateService();

        // Peer not registered anywhere
        var message = CreateMessage(recipientId: "ghost-peer");
        var act = () => service.SendMessage(message, TestServerCallContext.Create());

        var ex = await act.Should().ThrowAsync<RpcException>();
        ex.Which.StatusCode.Should().Be(StatusCode.NotFound);
    }
}
