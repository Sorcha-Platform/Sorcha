// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sorcha.UI.Core.Services.User.Enrolment;
using Xunit;

namespace Sorcha.UI.Core.Tests.Services.User.Enrolment;

/// <summary>
/// Tests for <see cref="HttpTierProbeService"/> — covers the three citizen-tier
/// branches and the failure-tolerant probe semantics from research §R-001.
/// </summary>
public sealed class HttpTierProbeServiceTests
{
    [Fact]
    public async Task ProbeAsync_Whoami401_Returns_ColdStart()
    {
        var handler = new StubHandler((req, _) => req.RequestUri!.AbsolutePath switch
        {
            "/api/auth/whoami" => new HttpResponseMessage(HttpStatusCode.Unauthorized),
            _ => new HttpResponseMessage(HttpStatusCode.OK),
        });
        var probe = CreateProbe(handler);

        var tier = await probe.ProbeAsync(CancellationToken.None);

        tier.Should().Be(CitizenTier.ColdStart);
    }

    [Fact]
    public async Task ProbeAsync_Whoami200_NoDevices_Returns_MiniGate()
    {
        var handler = new StubHandler((req, _) => req.RequestUri!.AbsolutePath switch
        {
            "/api/auth/whoami" => Ok("{\"platformUserId\":\"00000000-0000-0000-0000-000000000001\"}"),
            "/api/v1/me/devices" => Ok("{\"activeCount\":0,\"devices\":[]}"),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        var probe = CreateProbe(handler);

        var tier = await probe.ProbeAsync(CancellationToken.None);

        tier.Should().Be(CitizenTier.MiniGate);
    }

    [Fact]
    public async Task ProbeAsync_Whoami200_WithDevices_Returns_FastPath()
    {
        var handler = new StubHandler((req, _) => req.RequestUri!.AbsolutePath switch
        {
            "/api/auth/whoami" => Ok("{\"platformUserId\":\"00000000-0000-0000-0000-000000000001\"}"),
            "/api/v1/me/devices" => Ok("{\"activeCount\":2,\"devices\":[{\"status\":\"active\"},{\"status\":\"active\"}]}"),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        var probe = CreateProbe(handler);

        var tier = await probe.ProbeAsync(CancellationToken.None);

        tier.Should().Be(CitizenTier.FastPath);
    }

    [Fact]
    public async Task ProbeAsync_DevicesEndpoint_Falls_Back_To_Active_Filter_When_ActiveCount_Missing()
    {
        var handler = new StubHandler((req, _) => req.RequestUri!.AbsolutePath switch
        {
            "/api/auth/whoami" => Ok("{\"platformUserId\":\"00000000-0000-0000-0000-000000000001\"}"),
            // No activeCount field — counted from device list.
            "/api/v1/me/devices" => Ok("{\"devices\":[{\"status\":\"active\"},{\"status\":\"revoked\"}]}"),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        var probe = CreateProbe(handler);

        var tier = await probe.ProbeAsync(CancellationToken.None);

        tier.Should().Be(CitizenTier.FastPath);
    }

    [Fact]
    public async Task ProbeAsync_Whoami_Network_Error_Treated_As_ColdStart()
    {
        var handler = new StubHandler((req, _) => throw new HttpRequestException("network down"));
        var probe = CreateProbe(handler);

        var tier = await probe.ProbeAsync(CancellationToken.None);

        tier.Should().Be(CitizenTier.ColdStart);
    }

    [Fact]
    public async Task ProbeAsync_Devices_Network_Error_Treated_As_MiniGate()
    {
        var handler = new StubHandler((req, _) => req.RequestUri!.AbsolutePath switch
        {
            "/api/auth/whoami" => Ok("{\"platformUserId\":\"00000000-0000-0000-0000-000000000001\"}"),
            _ => throw new HttpRequestException("devices endpoint down"),
        });
        var probe = CreateProbe(handler);

        var tier = await probe.ProbeAsync(CancellationToken.None);

        tier.Should().Be(CitizenTier.MiniGate);
    }

    private static HttpResponseMessage Ok(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    private static HttpTierProbeService CreateProbe(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://test.local") },
            NullLogger<HttpTierProbeService>.Instance);

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _responder;
        public StubHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder) =>
            _responder = responder;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_responder(request, cancellationToken));
    }
}
