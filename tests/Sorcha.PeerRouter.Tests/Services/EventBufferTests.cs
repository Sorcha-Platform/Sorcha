// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;

using Sorcha.PeerRouter.Models;
using Sorcha.PeerRouter.Services;

namespace Sorcha.PeerRouter.Tests.Services;

public class EventBufferTests
{
    private static RouterEvent CreateEvent(string peerId = "peer-1") =>
        RouterEvent.Create(RouterEventType.PeerConnected, peerId, "192.168.1.10", 5000);

    [Fact]
    public void Add_SingleEvent_IncrementsCount()
    {
        var sut = new EventBuffer(new RouterConfiguration());
        sut.Add(CreateEvent());

        sut.Count.Should().Be(1);
    }

    [Fact]
    public void GetSnapshot_ReturnsAllEvents()
    {
        var sut = new EventBuffer(new RouterConfiguration());
        sut.Add(CreateEvent("peer-1"));
        sut.Add(CreateEvent("peer-2"));

        var snapshot = sut.GetSnapshot();
        snapshot.Should().HaveCount(2);
        snapshot[0].PeerId.Should().Be("peer-1");
        snapshot[1].PeerId.Should().Be("peer-2");
    }

    [Fact]
    public void Add_ExceedsMaxSize_TrimsOldest()
    {
        var config = new RouterConfiguration { EventBufferSize = 3 };
        var sut = new EventBuffer(config);

        sut.Add(CreateEvent("peer-1"));
        sut.Add(CreateEvent("peer-2"));
        sut.Add(CreateEvent("peer-3"));
        sut.Add(CreateEvent("peer-4"));

        sut.Count.Should().Be(3);
        var snapshot = sut.GetSnapshot();
        snapshot.Should().NotContain(e => e.PeerId == "peer-1");
        snapshot.Should().Contain(e => e.PeerId == "peer-4");
    }

    [Fact]
    public async Task FollowAsync_YieldsBufferedEventsThenNewEvents()
    {
        var sut = new EventBuffer(new RouterConfiguration());
        sut.Add(CreateEvent("buffered-1"));
        sut.Add(CreateEvent("buffered-2"));

        using var cts = new CancellationTokenSource();
        var received = new List<RouterEvent>();

        var followTask = Task.Run(async () =>
        {
            await foreach (var e in sut.FollowAsync(cts.Token))
            {
                received.Add(e);
                if (received.Count >= 3) break;
            }
        });

        // Give follow time to consume buffered events
        await Task.Delay(50);

        // Add a new event while following
        sut.Add(CreateEvent("live-1"));

        await followTask.WaitAsync(TimeSpan.FromSeconds(5));

        received.Should().HaveCount(3);
        received[0].PeerId.Should().Be("buffered-1");
        received[1].PeerId.Should().Be("buffered-2");
        received[2].PeerId.Should().Be("live-1");
    }

    [Fact]
    public async Task FollowAsync_MultipleSubscribers_EachGetsAllEvents()
    {
        var sut = new EventBuffer(new RouterConfiguration());
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var received1 = new List<RouterEvent>();
        var received2 = new List<RouterEvent>();

        var task1 = Task.Run(async () =>
        {
            await foreach (var e in sut.FollowAsync(cts.Token))
            {
                received1.Add(e);
                if (received1.Count >= 2) break;
            }
        });

        var task2 = Task.Run(async () =>
        {
            await foreach (var e in sut.FollowAsync(cts.Token))
            {
                received2.Add(e);
                if (received2.Count >= 2) break;
            }
        });

        await Task.Delay(50);

        sut.Add(CreateEvent("peer-1"));
        sut.Add(CreateEvent("peer-2"));

        await Task.WhenAll(task1, task2).WaitAsync(TimeSpan.FromSeconds(5));

        received1.Should().HaveCount(2);
        received2.Should().HaveCount(2);
    }

    [Fact]
    public async Task FollowAsync_CancellationStopsStream()
    {
        var sut = new EventBuffer(new RouterConfiguration());
        using var cts = new CancellationTokenSource();
        var started = new TaskCompletionSource();

        var followTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var _ in sut.FollowAsync(cts.Token))
                {
                    started.TrySetResult();
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when token is cancelled
            }
        });

        // Ensure the follow loop has started consuming
        sut.Add(CreateEvent());
        await Task.Delay(100);
        await cts.CancelAsync();

        // Should complete without hanging
        var act = () => followTask.WaitAsync(TimeSpan.FromSeconds(2));
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void GetSnapshot_EmptyBuffer_ReturnsEmptyList()
    {
        var sut = new EventBuffer(new RouterConfiguration());
        sut.GetSnapshot().Should().BeEmpty();
    }
}
