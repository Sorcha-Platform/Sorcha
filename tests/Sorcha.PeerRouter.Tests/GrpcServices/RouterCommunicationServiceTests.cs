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

public sealed class RouterCommunicationServiceTests
{
    private readonly RoutingTable _routingTable;
    private readonly EventBuffer _eventBuffer;
    private readonly RouterConfiguration _relayEnabledConfig;
    private readonly RouterConfiguration _relayDisabledConfig;

    public RouterCommunicationServiceTests()
    {
        _relayEnabledConfig = new RouterConfiguration { EnableRelay = true };
        _relayDisabledConfig = new RouterConfiguration { EnableRelay = false };
        _eventBuffer = new EventBuffer(_relayEnabledConfig);
        _routingTable = new RoutingTable(_eventBuffer);
    }

    private RouterCommunicationService CreateService(RouterConfiguration config) =>
        new(_routingTable, _eventBuffer, config,
            NullLogger<RouterCommunicationService>.Instance);

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

    #region Relay Disabled

    [Fact]
    public async Task SendMessage_RelayDisabled_ThrowsFailedPrecondition()
    {
        var service = CreateService(_relayDisabledConfig);
        var message = CreateMessage();

        var act = () => service.SendMessage(message, TestServerCallContext.Create());

        var ex = await act.Should().ThrowAsync<RpcException>();
        ex.Which.StatusCode.Should().Be(StatusCode.FailedPrecondition);
        ex.Which.Status.Detail.Should().Contain("not enabled");
    }

    #endregion

    #region Validation

    [Fact]
    public async Task SendMessage_EmptyRecipientPeerId_ThrowsInvalidArgument()
    {
        var service = CreateService(_relayEnabledConfig);
        var message = CreateMessage(recipientId: "");

        var act = () => service.SendMessage(message, TestServerCallContext.Create());

        var ex = await act.Should().ThrowAsync<RpcException>();
        ex.Which.StatusCode.Should().Be(StatusCode.InvalidArgument);
        ex.Which.Status.Detail.Should().Contain("Recipient");
    }

    [Fact]
    public async Task SendMessage_EmptySenderPeerId_ThrowsInvalidArgument()
    {
        var service = CreateService(_relayEnabledConfig);
        var message = CreateMessage(senderId: "");

        var act = () => service.SendMessage(message, TestServerCallContext.Create());

        var ex = await act.Should().ThrowAsync<RpcException>();
        ex.Which.StatusCode.Should().Be(StatusCode.InvalidArgument);
        ex.Which.Status.Detail.Should().Contain("Sender");
    }

    #endregion

    #region Unknown Peer

    [Fact]
    public async Task SendMessage_UnknownRecipient_ThrowsNotFound()
    {
        var service = CreateService(_relayEnabledConfig);
        var message = CreateMessage(recipientId: "nonexistent-peer");

        var act = () => service.SendMessage(message, TestServerCallContext.Create());

        var ex = await act.Should().ThrowAsync<RpcException>();
        ex.Which.StatusCode.Should().Be(StatusCode.NotFound);
        ex.Which.Status.Detail.Should().Contain("nonexistent-peer");
    }

    [Fact]
    public async Task SendMessage_UnhealthyRecipient_ThrowsNotFound()
    {
        var service = CreateService(_relayEnabledConfig);

        // Register peer then mark unhealthy via timeout sweep
        _routingTable.RegisterPeer(new PeerInfo
        {
            PeerId = "unhealthy-peer",
            Address = "10.0.0.1",
            Port = 5000
        });

        // Force peer to be unhealthy by sweeping with zero timeout
        _routingTable.SweepUnhealthyPeers(TimeSpan.Zero);

        var message = CreateMessage(recipientId: "unhealthy-peer");
        var act = () => service.SendMessage(message, TestServerCallContext.Create());

        var ex = await act.Should().ThrowAsync<RpcException>();
        ex.Which.StatusCode.Should().Be(StatusCode.NotFound);
    }

    #endregion

    #region Relay Forwarding

    [Fact]
    public async Task SendMessage_RelayEnabled_ValidRecipient_AttemptsForward()
    {
        var service = CreateService(_relayEnabledConfig);

        // Register the recipient peer (it won't actually be reachable in unit tests)
        _routingTable.RegisterPeer(new PeerInfo
        {
            PeerId = "recipient-1",
            Address = "10.0.0.99",
            Port = 5000
        });

        var message = CreateMessage();

        // The forward will fail because the peer isn't actually running,
        // but we verify it gets past validation and attempts the relay
        var act = () => service.SendMessage(message, TestServerCallContext.Create());

        // Should throw Unavailable (connection failed) not FailedPrecondition/NotFound
        var ex = await act.Should().ThrowAsync<RpcException>();
        ex.Which.StatusCode.Should().Be(StatusCode.Unavailable);
    }

    #endregion

    #region Stream

    [Fact]
    public async Task Stream_ThrowsUnimplemented()
    {
        var service = CreateService(_relayEnabledConfig);

        var act = () => service.Stream(
            null!, null!, TestServerCallContext.Create());

        var ex = await act.Should().ThrowAsync<RpcException>();
        ex.Which.StatusCode.Should().Be(StatusCode.Unimplemented);
    }

    #endregion
}
