// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq.Protected;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.McpServer.Tools.Admin;

namespace Sorcha.McpServer.Tests.Tools.Admin;

public class PeerStatusToolTests
{
    private readonly Mock<IMcpAuthorizationService> _authServiceMock;
    private readonly Mock<IMcpErrorHandler> _errorHandlerMock;
    private readonly Mock<IServiceAvailabilityTracker> _availabilityTrackerMock;
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;
    private readonly Mock<IConfiguration> _configurationMock;
    private readonly Mock<ILogger<PeerStatusTool>> _loggerMock;

    public PeerStatusToolTests()
    {
        _authServiceMock = new Mock<IMcpAuthorizationService>();
        _errorHandlerMock = new Mock<IMcpErrorHandler>();
        _availabilityTrackerMock = new Mock<IServiceAvailabilityTracker>();
        _httpClientFactoryMock = new Mock<IHttpClientFactory>();
        _configurationMock = new Mock<IConfiguration>();
        _loggerMock = new Mock<ILogger<PeerStatusTool>>();

        // Default configuration
        _configurationMock.Setup(c => c["ServiceClients:PeerService:Address"])
            .Returns("http://localhost:5002");
    }

    private PeerStatusTool CreateTool()
    {
        return new PeerStatusTool(
            _authServiceMock.Object,
            _errorHandlerMock.Object,
            _availabilityTrackerMock.Object,
            _httpClientFactoryMock.Object,
            _configurationMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task GetPeerStatusAsync_Unauthorized_ReturnsUnauthorizedResult()
    {
        // Arrange
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_peer_status")).Returns(false);
        var tool = CreateTool();

        // Act
        var result = await tool.GetPeerStatusAsync();

        // Assert
        result.Status.Should().Be("Unauthorized");
        result.Message.Should().Contain("Access denied");
        result.NetworkStatistics.Should().BeNull();
    }

    [Fact]
    public async Task GetPeerStatusAsync_ServiceUnavailable_ReturnsUnavailableResult()
    {
        // Arrange
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_peer_status")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Peer")).Returns(false);
        var tool = CreateTool();

        // Act
        var result = await tool.GetPeerStatusAsync();

        // Assert
        result.Status.Should().Be("Unavailable");
        result.Message.Should().Contain("unavailable");
    }

    [Fact]
    public async Task GetPeerStatusAsync_HealthyNetwork_ReturnsHealthyResult()
    {
        // Arrange
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_peer_status")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Peer")).Returns(true);

        var statsResponse = new
        {
            Timestamp = DateTimeOffset.UtcNow,
            PeerStats = new
            {
                TotalPeers = 10,
                HealthyPeers = 9,
                UnhealthyPeers = 1,
                BootstrapNodes = 3,
                AverageLatencyMs = 45.5,
                TotalFailures = 2
            },
            QualityStats = new
            {
                TotalTrackedPeers = 10,
                ExcellentPeers = 5,
                GoodPeers = 3,
                FairPeers = 1,
                PoorPeers = 1,
                AverageQualityScore = 72.5
            },
            QueueStats = new { QueueSize = 5, IsEmpty = false },
            CircuitBreakerStats = new
            {
                TotalCircuitBreakers = 10,
                OpenCircuits = 0,
                HalfOpenCircuits = 1,
                ClosedCircuits = 9
            }
        };

        var healthResponse = new
        {
            TotalPeers = 10,
            HealthyPeers = 9,
            UnhealthyPeers = 1,
            HealthPercentage = 90.0
        };

        var httpClient = CreateMockHttpClient(
            ("http://localhost:5002/api/peers/stats", JsonSerializer.Serialize(statsResponse)),
            ("http://localhost:5002/api/peers/health", JsonSerializer.Serialize(healthResponse)));

        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);
        var tool = CreateTool();

        // Act
        var result = await tool.GetPeerStatusAsync();

        // Assert
        result.Status.Should().Be("Healthy");
        result.Message.Should().Contain("healthy");
        result.NetworkStatistics.Should().NotBeNull();
        result.NetworkStatistics!.TotalPeers.Should().Be(10);
        result.NetworkStatistics.HealthyPeers.Should().Be(9);
        result.NetworkStatistics.AverageLatencyMs.Should().Be(45.5);
        result.NetworkStatistics.QualityMetrics.Should().NotBeNull();
        result.NetworkStatistics.CircuitBreakers.Should().NotBeNull();
        result.HealthStatus.Should().NotBeNull();
        result.HealthStatus!.HealthPercentage.Should().Be(90.0);

        // Verify success was recorded
        _availabilityTrackerMock.Verify(a => a.RecordSuccess("Peer"), Times.Once);
    }

    [Fact]
    public async Task GetPeerStatusAsync_DegradedNetwork_ReturnsDegradedResult()
    {
        // Arrange
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_peer_status")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Peer")).Returns(true);

        var statsResponse = new
        {
            PeerStats = new { TotalPeers = 10, HealthyPeers = 3, UnhealthyPeers = 7 },
            QualityStats = new { TotalTrackedPeers = 10, AverageQualityScore = 35.0 },
            QueueStats = new { QueueSize = 100 }
        };

        var healthResponse = new
        {
            TotalPeers = 10,
            HealthyPeers = 3,
            UnhealthyPeers = 7,
            HealthPercentage = 30.0
        };

        var httpClient = CreateMockHttpClient(
            ("http://localhost:5002/api/peers/stats", JsonSerializer.Serialize(statsResponse)),
            ("http://localhost:5002/api/peers/health", JsonSerializer.Serialize(healthResponse)));

        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);
        var tool = CreateTool();

        // Act
        var result = await tool.GetPeerStatusAsync();

        // Assert
        result.Status.Should().Be("Degraded");
        result.Message.Should().Contain("30");
        result.HealthStatus!.HealthPercentage.Should().Be(30.0);
    }

    [Fact]
    public async Task GetPeerStatusAsync_NoHealthyPeers_ReturnsDegradedResult()
    {
        // Arrange
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_peer_status")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Peer")).Returns(true);

        var statsResponse = new
        {
            PeerStats = new { TotalPeers = 5, HealthyPeers = 0, UnhealthyPeers = 5 }
        };

        var healthResponse = new
        {
            TotalPeers = 5,
            HealthyPeers = 0,
            UnhealthyPeers = 5,
            HealthPercentage = 0.0
        };

        var httpClient = CreateMockHttpClient(
            ("http://localhost:5002/api/peers/stats", JsonSerializer.Serialize(statsResponse)),
            ("http://localhost:5002/api/peers/health", JsonSerializer.Serialize(healthResponse)));

        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);
        var tool = CreateTool();

        // Act
        var result = await tool.GetPeerStatusAsync();

        // Assert
        result.Status.Should().Be("Degraded");
        result.Message.Should().Contain("No healthy peers");
    }

    [Fact]
    public async Task GetPeerStatusAsync_BothEndpointsFail_ReturnsUnknownResult()
    {
        // Arrange - When both endpoints fail (caught internally), we get null for both
        // stats and health, resulting in "Unknown" status
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_peer_status")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Peer")).Returns(true);

        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var httpClient = new HttpClient(handler.Object);
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);
        var tool = CreateTool();

        // Act
        var result = await tool.GetPeerStatusAsync();

        // Assert
        result.Status.Should().Be("Unknown");
        result.Message.Should().Contain("Unable to retrieve");
        result.NetworkStatistics.Should().BeNull();
        result.HealthStatus.Should().BeNull();
    }

    [Fact]
    public async Task GetPeerStatusAsync_EndpointsReturnInvalidJson_ReturnsUnknownResult()
    {
        // Arrange - When endpoints return invalid JSON, parsing fails and we get null
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_peer_status")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Peer")).Returns(true);

        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("invalid json")
            });

        var httpClient = new HttpClient(handler.Object);
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);
        var tool = CreateTool();

        // Act
        var result = await tool.GetPeerStatusAsync();

        // Assert
        result.Status.Should().Be("Unknown");
        result.NetworkStatistics.Should().BeNull();
        result.HealthStatus.Should().BeNull();
    }

    [Fact]
    public async Task GetPeerStatusAsync_PartialData_ReturnsAvailableData()
    {
        // Arrange - stats fails, health succeeds
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_peer_status")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Peer")).Returns(true);

        var healthResponse = new
        {
            TotalPeers = 5,
            HealthyPeers = 5,
            UnhealthyPeers = 0,
            HealthPercentage = 100.0
        };

        var handler = new Mock<HttpMessageHandler>();

        // Stats endpoint returns 500
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.RequestUri!.ToString().Contains("/api/peers/stats")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.InternalServerError));

        // Health endpoint returns success
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.RequestUri!.ToString().Contains("/api/peers/health")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(healthResponse))
            });

        var httpClient = new HttpClient(handler.Object);
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);
        var tool = CreateTool();

        // Act
        var result = await tool.GetPeerStatusAsync();

        // Assert
        result.Status.Should().Be("Healthy");
        result.NetworkStatistics.Should().BeNull(); // Stats failed
        result.HealthStatus.Should().NotBeNull(); // Health succeeded
        result.HealthStatus!.HealthPercentage.Should().Be(100.0);
    }

    [Fact]
    public async Task GetPeerStatusAsync_ResponseTimeIsRecorded()
    {
        // Arrange
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_peer_status")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Peer")).Returns(true);

        var healthResponse = new { TotalPeers = 1, HealthyPeers = 1, UnhealthyPeers = 0, HealthPercentage = 100.0 };

        var httpClient = CreateMockHttpClient(
            ("http://localhost:5002/api/peers/stats", "{}"),
            ("http://localhost:5002/api/peers/health", JsonSerializer.Serialize(healthResponse)));

        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);
        var tool = CreateTool();

        // Act
        var result = await tool.GetPeerStatusAsync();

        // Assert
        result.ResponseTimeMs.Should().BeGreaterThanOrEqualTo(0);
        result.CheckedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    private static HttpClient CreateMockHttpClient(params (string url, string content)[] responses)
    {
        var handler = new Mock<HttpMessageHandler>();

        foreach (var (url, content) in responses)
        {
            handler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.Is<HttpRequestMessage>(r => r.RequestUri!.ToString() == url),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(content)
                });
        }

        return new HttpClient(handler.Object);
    }
}
