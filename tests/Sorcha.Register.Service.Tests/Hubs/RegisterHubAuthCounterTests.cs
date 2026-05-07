// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics.Metrics;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Moq;
using Sorcha.Register.Service.Hubs;
using Sorcha.ServiceClients.Subscription;
using Sorcha.ServiceDefaults.Hubs;
using Xunit;

namespace Sorcha.Register.Service.Tests.Hubs;

/// <summary>
/// Verifies T090 — RegisterHub.OnConnectedAsync emits the
/// <c>sorcha_signalr_connections_total</c> counter with the
/// <c>authenticated</c> tag so the cutover gauge can drive the FR-011
/// auth-hardening release.
/// </summary>
public class RegisterHubAuthCounterTests
{
    [Fact]
    public async Task OnConnectedAsync_AnonymousConnection_RecordsAuthenticatedFalse()
    {
        var captured = await CaptureSingleConnectionMeasurementAsync(authenticated: false);

        captured.Tags.Should().Contain(new KeyValuePair<string, object?>("hub", "register"));
        captured.Tags.Should().Contain(new KeyValuePair<string, object?>("state", "connected"));
        captured.Tags.Should().Contain(new KeyValuePair<string, object?>("authenticated", false));
    }

    [Fact]
    public async Task OnConnectedAsync_AuthenticatedConnection_RecordsAuthenticatedTrue()
    {
        var captured = await CaptureSingleConnectionMeasurementAsync(authenticated: true);

        captured.Tags.Should().Contain(new KeyValuePair<string, object?>("authenticated", true));
    }

    private static async Task<(long Value, IReadOnlyList<KeyValuePair<string, object?>> Tags)>
        CaptureSingleConnectionMeasurementAsync(bool authenticated)
    {
        using var metrics = new SignalRMetrics();
        using var listener = new MeterListener();
        long? capturedValue = null;
        IReadOnlyList<KeyValuePair<string, object?>>? capturedTags = null;

        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == SignalRMetrics.MeterName &&
                instrument.Name == "sorcha_signalr_connections_total")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            capturedValue = value;
            capturedTags = tags.ToArray();
        });
        listener.Start();

        var hub = new RegisterHub(Mock.Of<ISubscriptionServiceClient>(), metrics);

        var hubCallerContext = new Mock<HubCallerContext>();
        hubCallerContext.Setup(c => c.ConnectionId).Returns("test-conn");

        var identity = authenticated
            ? new ClaimsIdentity(authenticationType: "Bearer")
            : new ClaimsIdentity();
        hubCallerContext.Setup(c => c.User).Returns(new ClaimsPrincipal(identity));

        var groups = new Mock<IGroupManager>();
        hub.Context = hubCallerContext.Object;
        hub.Groups = groups.Object;

        await hub.OnConnectedAsync();

        capturedValue.Should().Be(1);
        capturedTags.Should().NotBeNull();
        return (capturedValue!.Value, capturedTags!);
    }
}
