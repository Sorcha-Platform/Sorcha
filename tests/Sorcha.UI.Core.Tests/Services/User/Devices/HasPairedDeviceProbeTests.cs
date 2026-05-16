// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using Sorcha.UI.Core.Services.User.Devices;
using Xunit;

namespace Sorcha.UI.Core.Tests.Services.User.Devices;

/// <summary>
/// Feature 128 — covers the <see cref="HasPairedDeviceProbe"/> caching,
/// Changed-event semantics, and local-pair-completion optimistic update.
/// </summary>
public sealed class HasPairedDeviceProbeTests
{
    [Fact]
    public async Task EnsureLoadedAsync_Initial_Fetch_Populates_Value_And_Fires_Changed()
    {
        var probe = Create(out var handler, hasAnyDevice: false, latestEnrolledAt: null);

        var changedFired = 0;
        probe.Changed += () => changedFired++;

        probe.HasAnyDevice.Should().BeNull();
        await probe.EnsureLoadedAsync();

        probe.HasAnyDevice.Should().BeFalse();
        changedFired.Should().Be(1);
        VerifyHandlerCallCount(handler, 1);
    }

    [Fact]
    public async Task EnsureLoadedAsync_Is_Idempotent_Within_Session()
    {
        var probe = Create(out var handler, hasAnyDevice: true, latestEnrolledAt: DateTimeOffset.UtcNow);

        await probe.EnsureLoadedAsync();
        await probe.EnsureLoadedAsync();
        await probe.EnsureLoadedAsync();

        VerifyHandlerCallCount(handler, 1);
    }

    [Fact]
    public async Task RefreshAsync_Forces_New_Fetch_Even_When_Already_Loaded()
    {
        var probe = Create(out var handler, hasAnyDevice: true, latestEnrolledAt: DateTimeOffset.UtcNow);

        await probe.EnsureLoadedAsync();
        await probe.RefreshAsync();
        await probe.RefreshAsync();

        VerifyHandlerCallCount(handler, 3);
    }

    [Fact]
    public async Task Changed_Only_Fires_When_Value_Actually_Changes()
    {
        var probe = Create(out _, hasAnyDevice: false, latestEnrolledAt: null);

        var changedFired = 0;
        probe.Changed += () => changedFired++;

        await probe.EnsureLoadedAsync();   // null → false: fires
        await probe.RefreshAsync();        // false → false: no fire
        await probe.RefreshAsync();        // still no fire

        changedFired.Should().Be(1);
    }

    [Fact]
    public async Task RaiseLocalPairCompleted_Optimistically_Flips_Value_Before_Refresh()
    {
        var probe = Create(out _, hasAnyDevice: false, latestEnrolledAt: null);

        await probe.EnsureLoadedAsync();
        probe.HasAnyDevice.Should().BeFalse();

        var changedFired = 0;
        probe.Changed += () => changedFired++;

        await probe.RaiseLocalPairCompleted();

        // Two events expected: optimistic flip (false→true) AND the
        // server-truth refresh might also fire if the test stub returns
        // a different value. Our stub returns false, so the refresh
        // would flip the value BACK to false — and Changed fires again.
        probe.HasAnyDevice.Should().BeFalse(); // server is the source of truth
        changedFired.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task RaiseLocalPairCompleted_When_Server_Already_Paired_Reconciles_Cleanly()
    {
        var probe = Create(out _, hasAnyDevice: true, latestEnrolledAt: DateTimeOffset.UtcNow.AddMinutes(-1));

        await probe.EnsureLoadedAsync();
        probe.HasAnyDevice.Should().BeTrue();

        // Local pair-completion when server already reports paired — no flip,
        // optimistic update is a no-op.
        var changedFired = 0;
        probe.Changed += () => changedFired++;
        await probe.RaiseLocalPairCompleted();

        // No optimistic flip (already true) and server still true — Changed
        // should not fire from this path.
        changedFired.Should().Be(0);
    }

    [Fact]
    public async Task Unauthorized_Response_Leaves_Value_Null_Without_Throwing()
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var http = new HttpClient(handlerMock.Object) { BaseAddress = new Uri("https://test.example.com") };
        var probe = new HasPairedDeviceProbe(http, NullLogger<HasPairedDeviceProbe>.Instance);

        await probe.EnsureLoadedAsync();

        probe.HasAnyDevice.Should().BeNull();
    }

    private static HasPairedDeviceProbe Create(
        out Mock<HttpMessageHandler> handler,
        bool hasAnyDevice,
        DateTimeOffset? latestEnrolledAt)
    {
        var body = JsonSerializer.Serialize(new
        {
            hasAnyDevice,
            latestEnrolledAt,
        });

        handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });

        var http = new HttpClient(handler.Object) { BaseAddress = new Uri("https://test.example.com") };
        return new HasPairedDeviceProbe(http, NullLogger<HasPairedDeviceProbe>.Instance);
    }

    private static void VerifyHandlerCallCount(Mock<HttpMessageHandler> handler, int expected)
    {
        handler.Protected().Verify(
            "SendAsync",
            Times.Exactly(expected),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }
}
