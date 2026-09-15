// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.McpServer.Tools.Admin;

namespace Sorcha.McpServer.Tests.Tools.Admin;

/// <summary>
/// <c>sorcha_health_check</c>. #1635: on a node the MCP server's service-client addresses all point
/// at the API gateway, so probing each of them reported the gateway's own <c>/health</c> five times
/// as five healthy backends. The load-bearing guard is that no backend may be reported Healthy
/// unless the gateway's aggregate actually vouched for it.
/// </summary>
public class HealthCheckToolTests
{
    private const string Gateway = "http://api-gateway:8080";

    private static readonly string[] Backends = ["blueprint", "register", "wallet", "tenant", "validator", "peer"];

    [Fact]
    public async Task CheckHealthAsync_AggregateReportsAllHealthy_ReportsEachBackendAtItsOwnEndpoint()
    {
        var h = new Harness(request => request.RequestUri!.AbsolutePath switch
        {
            "/health" => Text(HttpStatusCode.OK, "Healthy"),
            "/api/health" => Aggregate(HttpStatusCode.OK, Backends.ToDictionary(b => b, _ => "healthy")),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });

        var result = await h.Sut().CheckHealthAsync();

        result.OverallStatus.Should().Be("Healthy");
        result.Services.Should().HaveCount(7);
        var backendEndpoints = result.Services.Where(s => s.Name != "ApiGateway").Select(s => s.Endpoint).ToList();
        backendEndpoints.Should().OnlyHaveUniqueItems(
            "each backend must be reported at the address the gateway probed, not all at one shared address (#1635)");
        backendEndpoints.Should().NotContain(Gateway);
        result.Services.Single(s => s.Name == "Peer").Endpoint.Should().Be("http://peer-service:8080");
    }

    /// <summary>
    /// The regression guard for #1635. When the aggregate cannot be read, the gateway's own
    /// <c>/health</c> still answers "Healthy". That is exactly the signal the old tool wrongly relayed
    /// once per backend.
    /// </summary>
    [Fact]
    public async Task CheckHealthAsync_AggregateUnreadable_NoBackendIsReportedHealthy()
    {
        var h = new Harness(request => request.RequestUri!.AbsolutePath switch
        {
            "/health" => Text(HttpStatusCode.OK, "Healthy"),
            "/api/health" => Text(HttpStatusCode.InternalServerError, "<html>oops</html>"),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });

        var result = await h.Sut().CheckHealthAsync();

        var backends = result.Services.Where(s => s.Name != "ApiGateway").ToList();
        backends.Should().HaveCount(6);
        backends.Should().OnlyContain(s => s.Status == "Unknown",
            "a backend the aggregate did not vouch for is unconfirmed, never healthy");
        result.OverallStatus.Should().NotBe("Healthy");
        result.Message.Should().Contain("no backend service is confirmed healthy");
    }

    /// <summary>The gateway answers 503, still with per-service detail, when any backend is unhealthy.</summary>
    [Fact]
    public async Task CheckHealthAsync_AggregateReportsPeerUnhealthyWith503_ReportsPeerUnhealthyWithTheGatewaysError()
    {
        var statuses = Backends.ToDictionary(b => b, _ => "healthy");
        statuses["peer"] = "unhealthy";
        var h = new Harness(request => request.RequestUri!.AbsolutePath switch
        {
            "/health" => Text(HttpStatusCode.OK, "Healthy"),
            "/api/health" => Aggregate(HttpStatusCode.ServiceUnavailable, statuses, errorFor: "peer"),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });

        var result = await h.Sut().CheckHealthAsync();

        result.OverallStatus.Should().Be("Degraded");
        var peer = result.Services.Single(s => s.Name == "Peer");
        peer.Status.Should().Be("Unhealthy");
        peer.ErrorMessage.Should().Be("Connection refused (peer-service:8080)");
        result.Message.Should().Contain("Unhealthy services: Peer");
        h.Availability.Verify(a => a.RecordFailure("Peer", It.IsAny<Exception?>()), Times.Once);
    }

    [Fact]
    public async Task CheckHealthAsync_ServiceMissingFromTheAggregate_IsUnknownNotHealthy()
    {
        var statuses = Backends.Where(b => b != "validator").ToDictionary(b => b, _ => "healthy");
        var h = new Harness(request => request.RequestUri!.AbsolutePath switch
        {
            "/health" => Text(HttpStatusCode.OK, "Healthy"),
            "/api/health" => Aggregate(HttpStatusCode.OK, statuses),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });

        var result = await h.Sut().CheckHealthAsync();

        result.Services.Single(s => s.Name == "Validator").Status.Should().Be("Unknown");
        result.OverallStatus.Should().Be("Degraded");
        result.Message.Should().Contain("Not confirmed (status unknown): Validator");
    }

    /// <summary>
    /// The old tool probed localhost defaults for any service without an address key. The only
    /// requests now go to the configured gateway.
    /// </summary>
    [Fact]
    public async Task CheckHealthAsync_ProbesOnlyTheConfiguredGateway()
    {
        var h = new Harness(request => request.RequestUri!.AbsolutePath switch
        {
            "/health" => Text(HttpStatusCode.OK, "Healthy"),
            "/api/health" => Aggregate(HttpStatusCode.OK, Backends.ToDictionary(b => b, _ => "healthy")),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });

        await h.Sut().CheckHealthAsync();

        h.Handler.RequestedUris.Select(u => u.ToString()).Should().BeEquivalentTo(
            [$"{Gateway}/health", $"{Gateway}/api/health"]);
    }

    [Fact]
    public async Task CheckHealthAsync_GatewayUnreachable_GatewayUnhealthyAndNoBackendConfirmed()
    {
        var h = new Harness(_ => throw new HttpRequestException("Connection refused (api-gateway:8080)"));

        var result = await h.Sut().CheckHealthAsync();

        result.Services.Single(s => s.Name == "ApiGateway").Status.Should().Be("Unhealthy");
        result.Services.Where(s => s.Name != "ApiGateway").Should().OnlyContain(s => s.Status == "Unknown");
        result.OverallStatus.Should().Be("Unhealthy");
    }

    [Fact]
    public async Task CheckHealthAsync_NotEntitled_ReturnsUnauthorizedWithoutProbing()
    {
        var h = new Harness(_ => Text(HttpStatusCode.OK, "Healthy"));
        h.Auth.Setup(a => a.CanInvokeTool("sorcha_health_check")).Returns(false);

        var result = await h.Sut().CheckHealthAsync();

        result.OverallStatus.Should().Be("Unauthorized");
        h.Handler.RequestedUris.Should().BeEmpty();
    }

    private static HttpResponseMessage Text(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "text/plain") };

    /// <summary>The exact shape the gateway's <c>AggregatedHealthResponse</c> serialises to.</summary>
    private static HttpResponseMessage Aggregate(
        HttpStatusCode status, IReadOnlyDictionary<string, string> statuses, string? errorFor = null)
    {
        var services = statuses.ToDictionary(
            kvp => kvp.Key,
            kvp => new Dictionary<string, string?>
            {
                ["status"] = kvp.Value,
                ["endpoint"] = $"http://{kvp.Key}-service:8080",
                ["error"] = kvp.Key == errorFor ? $"Connection refused ({kvp.Key}-service:8080)" : null
            });

        var body = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["status"] = statuses.Values.All(s => s == "healthy") ? "healthy" : "unhealthy",
            ["timestamp"] = DateTimeOffset.UtcNow,
            ["services"] = services
        });

        return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private sealed class Harness
    {
        public Mock<IMcpAuthorizationService> Auth { get; } = new();
        public Mock<IServiceAvailabilityTracker> Availability { get; } = new();
        public RecordingHandler Handler { get; }

        private readonly Mock<IHttpClientFactory> _factory = new();

        public Harness(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            Handler = new RecordingHandler(respond);
            Auth.Setup(a => a.CanInvokeTool("sorcha_health_check")).Returns(true);
            // A fresh client per call, as the real factory hands out.
            _factory.Setup(f => f.CreateClient(It.IsAny<string>()))
                .Returns(() => new HttpClient(Handler, disposeHandler: false));
        }

        public HealthCheckTool Sut()
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    // What the mcp-server-http compose block sets on a node.
                    ["ServiceClients:ApiGateway:Address"] = Gateway,
                    ["ServiceClients:BlueprintService:Address"] = Gateway,
                    ["ServiceClients:RegisterService:Address"] = Gateway
                })
                .Build();

            return new HealthCheckTool(
                Auth.Object,
                new Mock<IMcpErrorHandler>().Object,
                Availability.Object,
                _factory.Object,
                configuration,
                NullLogger<HealthCheckTool>.Instance);
        }
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        private readonly List<Uri> _requested = [];

        public IReadOnlyList<Uri> RequestedUris
        {
            get
            {
                lock (_requested)
                {
                    return _requested.ToList();
                }
            }
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (_requested)
            {
                _requested.Add(request.RequestUri!);
            }

            return Task.FromResult(respond(request));
        }
    }
}
