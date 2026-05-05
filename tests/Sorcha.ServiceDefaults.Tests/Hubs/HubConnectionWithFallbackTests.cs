// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.AspNetCore.SignalR.Client;
using Sorcha.ServiceDefaults.Hubs;
using Xunit;

namespace Sorcha.ServiceDefaults.Tests.Hubs;

public class HubConnectionWithFallbackTests
{
    private static HubConnection BuildIdleConnection() =>
        new HubConnectionBuilder().WithUrl("https://localhost/hubs/test").Build();

    [Fact]
    public async Task PollFallback_FiresAfterEngageWindow()
    {
        var pollCount = 0;
        var connection = BuildIdleConnection();

        await using var wrapper = new HubConnectionWithFallback(
            connection,
            pollFallback: ct =>
            {
                Interlocked.Increment(ref pollCount);
                return Task.CompletedTask;
            },
            pollInterval: TimeSpan.FromMilliseconds(100),
            engageAfter: TimeSpan.FromMilliseconds(50));

        wrapper.NotifyDisconnected();

        // Wait long enough for engage + a few polls.
        await Task.Delay(500);

        pollCount.Should().BeGreaterThanOrEqualTo(2,
            "polling should engage after the engage-after window and fire repeatedly");
        wrapper.IsPolling.Should().BeTrue();
    }

    [Fact]
    public async Task PollFallback_StopsWhenConnectionRecovers()
    {
        var pollCount = 0;
        var connection = BuildIdleConnection();

        await using var wrapper = new HubConnectionWithFallback(
            connection,
            pollFallback: ct =>
            {
                Interlocked.Increment(ref pollCount);
                return Task.CompletedTask;
            },
            pollInterval: TimeSpan.FromMilliseconds(50),
            engageAfter: TimeSpan.FromMilliseconds(20));

        wrapper.NotifyDisconnected();
        await Task.Delay(200);
        var pollsBeforeRecovery = pollCount;
        pollsBeforeRecovery.Should().BeGreaterThanOrEqualTo(1);

        wrapper.NotifyConnected();
        await Task.Delay(200);
        var pollsAfterRecovery = pollCount;

        // Allow for one or two polls in flight when NotifyConnected races the loop.
        (pollsAfterRecovery - pollsBeforeRecovery).Should().BeLessThanOrEqualTo(2,
            "polling should stop within a cycle of the connection recovering");
        wrapper.IsPolling.Should().BeFalse();
    }

    [Fact]
    public async Task ConnectionStateChanged_FiresOnNotify()
    {
        var states = new List<HubConnectionState>();
        var connection = BuildIdleConnection();

        await using var wrapper = new HubConnectionWithFallback(connection);
        wrapper.ConnectionStateChanged += s => states.Add(s);

        wrapper.NotifyDisconnected();
        wrapper.NotifyConnected();

        states.Should().ContainInOrder(HubConnectionState.Disconnected, HubConnectionState.Connected);
    }

    [Fact]
    public async Task NoFallback_StillSurfacesConnectionStateEvents()
    {
        var changed = false;
        var connection = BuildIdleConnection();

        await using var wrapper = new HubConnectionWithFallback(connection, pollFallback: null);
        wrapper.ConnectionStateChanged += _ => changed = true;

        wrapper.NotifyDisconnected();
        changed.Should().BeTrue();
        wrapper.IsPolling.Should().BeFalse(
            "with no fallback delegate the wrapper still tracks state but never polls");
    }

    [Fact]
    public async Task PollFallback_SwallowsExceptionsAndKeepsPolling()
    {
        var attempts = 0;
        var connection = BuildIdleConnection();

        await using var wrapper = new HubConnectionWithFallback(
            connection,
            pollFallback: ct =>
            {
                Interlocked.Increment(ref attempts);
                throw new InvalidOperationException("simulated transient failure");
            },
            pollInterval: TimeSpan.FromMilliseconds(50),
            engageAfter: TimeSpan.FromMilliseconds(20));

        wrapper.NotifyDisconnected();
        await Task.Delay(300);

        attempts.Should().BeGreaterThanOrEqualTo(2,
            "polling continues across transient failures — the delegate is responsible for its own logging");
    }
}
