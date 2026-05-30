// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Grpc.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Peer.Service.Communication;
using Sorcha.Peer.Service.Protos;

namespace Sorcha.Peer.Service.Tests.Communication;

/// <summary>
/// Unit tests for <see cref="ReverseStreamManager"/> — the registry of reverse streams held by
/// NAT'd peers (Feature 143, peer NAT traversal).
/// </summary>
public class ReverseStreamManagerTests
{
    private static ReverseStreamManager NewManager() => new(NullLogger<ReverseStreamManager>.Instance);

    private static Mock<IServerStreamWriter<PeerMessage>> NewStream()
    {
        var mock = new Mock<IServerStreamWriter<PeerMessage>>();
        mock.Setup(s => s.WriteAsync(It.IsAny<PeerMessage>())).Returns(Task.CompletedTask);
        return mock;
    }

    private static PeerMessage Msg(string recipient = "natd-1") => new()
    {
        SenderPeerId = "pub-1",
        RecipientPeerId = recipient,
        MessageType = MessageType.TransactionNotification,
        Payload = Google.Protobuf.ByteString.Empty,
        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
    };

    [Fact]
    public void RegisterStream_ThenTryGetStream_ReturnsActiveEntry()
    {
        var mgr = NewManager();
        var stream = NewStream();

        mgr.RegisterStream("natd-1", stream.Object);

        mgr.TryGetStream("natd-1", out var entry).Should().BeTrue();
        entry.Should().NotBeNull();
        entry!.PeerId.Should().Be("natd-1");
        entry.IsActive.Should().BeTrue();
        mgr.ActiveCount.Should().Be(1);
    }

    [Fact]
    public void TryGetStream_UnknownPeer_ReturnsFalse()
    {
        var mgr = NewManager();

        mgr.TryGetStream("missing", out var entry).Should().BeFalse();
        entry.Should().BeNull();
    }

    [Fact]
    public void RegisterStream_SamePeerTwice_SupersedesAndCancelsOld()
    {
        var mgr = NewManager();
        mgr.RegisterStream("natd-1", NewStream().Object);
        mgr.TryGetStream("natd-1", out var first).Should().BeTrue();

        mgr.RegisterStream("natd-1", NewStream().Object);

        first!.IsActive.Should().BeFalse("the prior stream is superseded by the reconnect");
        first.StreamCts.IsCancellationRequested.Should().BeTrue("the old read loop must be signalled to stop");
        mgr.ActiveCount.Should().Be(1, "supersede replaces rather than accumulates");
        mgr.TryGetStream("natd-1", out var second).Should().BeTrue();
        second.Should().NotBeSameAs(first);
    }

    [Fact]
    public void RemoveStream_RemovesAndDeactivates()
    {
        var mgr = NewManager();
        mgr.RegisterStream("natd-1", NewStream().Object);
        mgr.TryGetStream("natd-1", out var entry).Should().BeTrue();

        mgr.RemoveStream("natd-1");

        mgr.TryGetStream("natd-1", out _).Should().BeFalse();
        entry!.IsActive.Should().BeFalse();
        entry.StreamCts.IsCancellationRequested.Should().BeTrue();
        mgr.ActiveCount.Should().Be(0);
    }

    [Fact]
    public async Task DispatchAsync_WithActiveStream_WritesAndUpdatesActivity()
    {
        var mgr = NewManager();
        var stream = NewStream();
        mgr.RegisterStream("natd-1", stream.Object);
        mgr.TryGetStream("natd-1", out var entry).Should().BeTrue();
        var before = entry!.LastActivityAt;
        var message = Msg();

        await Task.Delay(2);
        await mgr.DispatchAsync("natd-1", message);

        stream.Verify(s => s.WriteAsync(message), Times.Once);
        entry.LastActivityAt.Should().BeOnOrAfter(before);
    }

    [Fact]
    public async Task DispatchAsync_NoActiveStream_ThrowsUnavailable()
    {
        var mgr = NewManager();

        var act = async () => await mgr.DispatchAsync("missing", Msg());

        var ex = (await act.Should().ThrowAsync<RpcException>()).Which;
        ex.StatusCode.Should().Be(StatusCode.Unavailable);
    }

    [Fact]
    public async Task DispatchAsync_StreamWriteThrows_ThrowsUnavailable()
    {
        var mgr = NewManager();
        var stream = new Mock<IServerStreamWriter<PeerMessage>>();
        stream.Setup(s => s.WriteAsync(It.IsAny<PeerMessage>()))
            .ThrowsAsync(new InvalidOperationException("stream closed"));
        mgr.RegisterStream("natd-1", stream.Object);

        var act = async () => await mgr.DispatchAsync("natd-1", Msg());

        var ex = (await act.Should().ThrowAsync<RpcException>()).Which;
        ex.StatusCode.Should().Be(StatusCode.Unavailable);
    }

    [Fact]
    public void ActiveCount_ReflectsMultipleStreams()
    {
        var mgr = NewManager();
        mgr.RegisterStream("natd-1", NewStream().Object);
        mgr.RegisterStream("natd-2", NewStream().Object);
        mgr.RegisterStream("natd-3", NewStream().Object);

        mgr.ActiveCount.Should().Be(3);

        mgr.RemoveStream("natd-2");
        mgr.ActiveCount.Should().Be(2);
        mgr.GetActiveStreams().Select(e => e.PeerId).Should().BeEquivalentTo("natd-1", "natd-3");
    }
}
