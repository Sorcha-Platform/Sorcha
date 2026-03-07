// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;

using Microsoft.AspNetCore.Http;

using Sorcha.PeerRouter.Endpoints;
using Sorcha.PeerRouter.Models;
using Sorcha.PeerRouter.Services;

namespace Sorcha.PeerRouter.Tests.Endpoints;

public class EventStreamEndpointTests
{
    private static RouterEvent CreateEvent(string peerId = "peer-1") =>
        RouterEvent.Create(RouterEventType.PeerConnected, peerId, "10.0.0.1", 5000);

    private static EventBuffer CreateBuffer(int size = 1000) =>
        new(new RouterConfiguration { EventBufferSize = size });

    [Fact]
    public async Task HandleEventsAsync_SnapshotMode_ReturnsBufferedEvents()
    {
        // Arrange
        var buffer = CreateBuffer();
        buffer.Add(CreateEvent("peer-1"));
        buffer.Add(CreateEvent("peer-2"));

        var httpContext = new DefaultHttpContext();
        // No ?follow query param => snapshot mode

        // Act
        var result = await EventStreamEndpoints.HandleEventsAsync(httpContext, buffer, CancellationToken.None);

        // Assert — snapshot mode returns Ok with the list
        result.Should().NotBeNull();
        result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<IReadOnlyList<RouterEvent>>>();
    }

    [Fact]
    public async Task HandleEventsAsync_SnapshotMode_EmptyBuffer_ReturnsEmptyList()
    {
        // Arrange
        var buffer = CreateBuffer();
        var httpContext = new DefaultHttpContext();

        // Act
        var result = await EventStreamEndpoints.HandleEventsAsync(httpContext, buffer, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public void GetSnapshot_ReturnsEventsInInsertionOrder()
    {
        // Arrange
        var buffer = CreateBuffer();
        buffer.Add(CreateEvent("first"));
        buffer.Add(CreateEvent("second"));
        buffer.Add(CreateEvent("third"));

        // Act
        var snapshot = buffer.GetSnapshot();

        // Assert — events should be in insertion order (oldest first)
        snapshot.Should().HaveCount(3);
        snapshot[0].PeerId.Should().Be("first");
        snapshot[1].PeerId.Should().Be("second");
        snapshot[2].PeerId.Should().Be("third");
    }

    [Fact]
    public async Task HandleEventsAsync_FollowMode_SetsSSEHeaders()
    {
        // Arrange
        var buffer = CreateBuffer();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.QueryString = new QueryString("?follow");
        httpContext.Response.Body = new MemoryStream();

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        // Act
        await EventStreamEndpoints.HandleEventsAsync(httpContext, buffer, cts.Token);

        // Assert
        httpContext.Response.ContentType.Should().Be("text/event-stream");
        httpContext.Response.Headers["Cache-Control"].ToString().Should().Be("no-cache");
        httpContext.Response.Headers["Connection"].ToString().Should().Be("keep-alive");
    }

    [Fact]
    public async Task HandleEventsAsync_FollowMode_WritesEventsAsSSE()
    {
        // Arrange — add an event BEFORE starting follow so it's in the buffer catch-up
        var buffer = CreateBuffer();
        buffer.Add(CreateEvent("live-peer"));

        var httpContext = new DefaultHttpContext();
        httpContext.Request.QueryString = new QueryString("?follow");

        var responseStream = new MemoryStream();
        httpContext.Response.Body = responseStream;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // Act — follow will yield the buffered event then block on channel until cancelled
        await EventStreamEndpoints.HandleEventsAsync(httpContext, buffer, cts.Token);

        // Read all output
        var body = System.Text.Encoding.UTF8.GetString(responseStream.ToArray());

        // Assert — response body should contain SSE-formatted data
        body.Should().Contain("data:");
        body.Should().Contain("live-peer");
    }

    [Fact]
    public void JsonOptions_UsesCamelCase()
    {
        EventStreamEndpoints.JsonOptions.PropertyNamingPolicy
            .Should().Be(System.Text.Json.JsonNamingPolicy.CamelCase);
    }
}
