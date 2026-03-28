// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;

using Grpc.Core;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using Sorcha.Peer.Service.Protos;
using Sorcha.PeerRouter.Services;

namespace Sorcha.PeerRouter.Tests.Services;

public sealed class ReverseStreamManagerTests
{
    private readonly ReverseStreamManager _manager;

    public ReverseStreamManagerTests()
    {
        _manager = new ReverseStreamManager(
            NullLogger<ReverseStreamManager>.Instance);
    }

    private static IServerStreamWriter<PeerMessage> CreateMockStream() =>
        new Mock<IServerStreamWriter<PeerMessage>>().Object;

    [Fact]
    public void RegisterStream_NewPeer_AddsEntry()
    {
        var stream = CreateMockStream();

        _manager.RegisterStream("peer-1", stream);

        _manager.GetActiveStreamCount().Should().Be(1);
        _manager.TryGetStream("peer-1", out var entry).Should().BeTrue();
        entry!.PeerId.Should().Be("peer-1");
        entry.ResponseStream.Should().BeSameAs(stream);
        entry.IsActive.Should().BeTrue();
    }

    [Fact]
    public void RegisterStream_ExistingPeer_ReplacesEntry()
    {
        var stream1 = CreateMockStream();
        var stream2 = CreateMockStream();

        _manager.RegisterStream("peer-1", stream1);
        _manager.RegisterStream("peer-1", stream2);

        _manager.GetActiveStreamCount().Should().Be(1);
        _manager.TryGetStream("peer-1", out var entry).Should().BeTrue();
        entry!.ResponseStream.Should().BeSameAs(stream2);
    }

    [Fact]
    public void RemoveStream_ExistingPeer_RemovesAndMarksInactive()
    {
        var stream = CreateMockStream();
        _manager.RegisterStream("peer-1", stream);

        _manager.RemoveStream("peer-1");

        _manager.GetActiveStreamCount().Should().Be(0);
        _manager.TryGetStream("peer-1", out _).Should().BeFalse();
    }

    [Fact]
    public void RemoveStream_UnknownPeer_NoError()
    {
        var act = () => _manager.RemoveStream("nonexistent");
        act.Should().NotThrow();
    }

    [Fact]
    public void TryGetStream_ExistingPeer_ReturnsTrue()
    {
        _manager.RegisterStream("peer-1", CreateMockStream());

        _manager.TryGetStream("peer-1", out var entry).Should().BeTrue();
        entry.Should().NotBeNull();
        entry!.PeerId.Should().Be("peer-1");
    }

    [Fact]
    public void TryGetStream_UnknownPeer_ReturnsFalse()
    {
        _manager.TryGetStream("nonexistent", out var entry).Should().BeFalse();
        entry.Should().BeNull();
    }

    [Fact]
    public void GetActiveStreamCount_ReturnsCorrectCount()
    {
        _manager.GetActiveStreamCount().Should().Be(0);

        _manager.RegisterStream("peer-1", CreateMockStream());
        _manager.RegisterStream("peer-2", CreateMockStream());
        _manager.RegisterStream("peer-3", CreateMockStream());

        _manager.GetActiveStreamCount().Should().Be(3);

        _manager.RemoveStream("peer-2");

        _manager.GetActiveStreamCount().Should().Be(2);
    }

    [Fact]
    public void GetActiveStreams_ReturnsAllActiveEntries()
    {
        _manager.RegisterStream("peer-1", CreateMockStream());
        _manager.RegisterStream("peer-2", CreateMockStream());

        var streams = _manager.GetActiveStreams();

        streams.Should().HaveCount(2);
        streams.Select(s => s.PeerId).Should().BeEquivalentTo(["peer-1", "peer-2"]);
    }
}
