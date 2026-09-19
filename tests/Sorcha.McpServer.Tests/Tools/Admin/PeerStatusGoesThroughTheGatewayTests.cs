// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.McpServer.Tools.Admin;

using Xunit;

namespace Sorcha.McpServer.Tests.Tools.Admin;

/// <summary>
/// Cold-start run #6 — <c>sorcha_peer_status</c> must call the gateway, not a localhost address
/// that does not exist inside the container.
/// </summary>
/// <remarks>
/// <para>
/// The tool makes HTTP calls to <c>/api/peers/stats</c> and <c>/api/peers/health</c>, but resolved
/// its endpoint from <c>SorchaService.Peer</c> — which is the peer's <b>gRPC</b> address and is
/// deliberately a different key from its HTTP one (CLAUDE.md pattern 17). The MCP server sets no
/// peer address at all, so it fell through to the local-dev default and every call died with
/// <c>Connection refused (localhost:5002)</c>, six stack traces per invocation, reported to the
/// agent as <c>Unknown</c>.
/// </para>
/// <para>
/// The gateway routes <c>/api/peers/**</c> under <c>RequireAuthenticated</c>, so going through it
/// is also what keeps the forwarded caller token authorised by the platform — the same reason
/// every other client in this server points there.
/// </para>
/// <para>
/// Nothing asserted which address the tool called, which is why a default that cannot work in the
/// deployment survived. This test is that assertion.
/// </para>
/// </remarks>
public class PeerStatusGoesThroughTheGatewayTests
{
    [Fact]
    public async Task ItCallsTheGatewayAddress_NotTheLocalDevPeerPort()
    {
        var requested = new List<string>();
        var tool = BuildTool(requested, gateway: "http://api-gateway:8080");

        await tool.GetPeerStatusAsync();

        requested.Should().NotBeEmpty("the tool must actually reach out");
        requested.Should().OnlyContain(u => u.StartsWith("http://api-gateway:8080/api/peers/"));
        requested.Should().NotContain(u => u.Contains("localhost:5002"));
    }

    [Fact]
    public async Task WithNoGatewayConfigured_ItStillUsesTheGatewayDefault_NotThePeerPort()
    {
        // The local-dev fallback must be the gateway's, so a missing key degrades to something
        // that works on a developer machine rather than to a port nothing listens on.
        var requested = new List<string>();
        var tool = BuildTool(requested, gateway: null);

        await tool.GetPeerStatusAsync();

        // Note :80 is the default port, so the Uri normalises it away — the point is that it is
        // the gateway's default and not the peer's 5002.
        requested.Should().OnlyContain(u => u.StartsWith("http://localhost/api/peers/"));
        requested.Should().NotContain(u => u.Contains("5002"));
    }

    private static PeerStatusTool BuildTool(List<string> requested, string? gateway)
    {
        var auth = new Mock<IMcpAuthorizationService>();
        auth.Setup(a => a.CanInvokeTool("sorcha_peer_status")).Returns(true);

        var availability = new Mock<IServiceAvailabilityTracker>();
        availability.Setup(a => a.IsServiceAvailable(It.IsAny<string>())).Returns(true);

        var handler = new RecordingHandler(requested);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(handler));

        var settings = new Dictionary<string, string?>();
        if (gateway is not null)
        {
            settings["ServiceClients:ApiGateway:Address"] = gateway;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        return new PeerStatusTool(
            auth.Object,
            Mock.Of<IMcpErrorHandler>(),
            availability.Object,
            factory.Object,
            configuration,
            Mock.Of<ILogger<PeerStatusTool>>());
    }

    /// <summary>Records every URL asked for and answers with an empty JSON object.</summary>
    private sealed class RecordingHandler(List<string> requested) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            requested.Add(request.RequestUri!.ToString());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
            });
        }
    }
}
