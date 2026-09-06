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

public class ValidatorStatusToolTests
{
    private readonly Mock<IMcpAuthorizationService> _authServiceMock;
    private readonly Mock<IMcpErrorHandler> _errorHandlerMock;
    private readonly Mock<IServiceAvailabilityTracker> _availabilityTrackerMock;
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;
    private readonly Mock<IConfiguration> _configurationMock;
    private readonly Mock<ILogger<ValidatorStatusTool>> _loggerMock;

    public ValidatorStatusToolTests()
    {
        _authServiceMock = new Mock<IMcpAuthorizationService>();
        _errorHandlerMock = new Mock<IMcpErrorHandler>();
        _availabilityTrackerMock = new Mock<IServiceAvailabilityTracker>();
        _httpClientFactoryMock = new Mock<IHttpClientFactory>();
        _configurationMock = new Mock<IConfiguration>();
        _loggerMock = new Mock<ILogger<ValidatorStatusTool>>();

        _configurationMock.Setup(c => c["ServiceClients:ValidatorService:Address"])
            .Returns("http://localhost:5004");
    }

    private ValidatorStatusTool CreateTool()
    {
        return new ValidatorStatusTool(
            _authServiceMock.Object,
            _errorHandlerMock.Object,
            _availabilityTrackerMock.Object,
            _httpClientFactoryMock.Object,
            _configurationMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task GetValidatorStatusAsync_Unauthorized_ReturnsUnauthorizedResult()
    {
        // Arrange
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_validator_status")).Returns(false);
        var tool = CreateTool();

        // Act
        var result = await tool.GetValidatorStatusAsync();

        // Assert
        result.Status.Should().Be("Unauthorized");
        result.Message.Should().Contain("Access denied");
        result.ServiceHealth.Should().BeNull();
    }

    [Fact]
    public async Task GetValidatorStatusAsync_ServiceUnavailable_ReturnsUnavailableResult()
    {
        // Arrange
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_validator_status")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Validator")).Returns(false);
        var tool = CreateTool();

        // Act
        var result = await tool.GetValidatorStatusAsync();

        // Assert
        result.Status.Should().Be("Unavailable");
        result.Message.Should().Contain("unavailable");
    }

    [Fact]
    public async Task GetValidatorStatusAsync_HealthyService_ReturnsHealthyResult()
    {
        // Arrange
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_validator_status")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Validator")).Returns(true);

        var httpClient = CreateMockHttpClient(
            ("http://localhost:5004/health", "Healthy"));

        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);
        var tool = CreateTool();

        // Act
        var result = await tool.GetValidatorStatusAsync();

        // Assert
        result.Status.Should().Be("Healthy");
        result.Message.Should().Contain("operational");
        result.ServiceHealth.Should().NotBeNull();
        result.ServiceHealth!.IsHealthy.Should().BeTrue();
        result.RegisterInfo.Should().BeNull(); // No registerId provided

        _availabilityTrackerMock.Verify(a => a.RecordSuccess("Validator"), Times.Once);
    }

    [Fact]
    public async Task GetValidatorStatusAsync_WithRegisterId_ReturnsRegisterInfo()
    {
        // Arrange
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_validator_status")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Validator")).Returns(true);

        var pipelineStatus = new
        {
            RegisterId = "reg-123",
            IsActive = true,
            TransactionsInMemPool = 15,
            DocketsProposed = 100,
            DocketsConfirmed = 95,
            DocketsRejected = 5,
            StartedAt = DateTimeOffset.UtcNow.AddHours(-2),
            LastDocketBuildAt = DateTimeOffset.UtcNow.AddMinutes(-5)
        };

        var countInfo = new
        {
            RegisterId = "reg-123",
            ActiveCount = 5,
            MinValidators = 3,
            MaxValidators = 10,
            HasQuorum = true
        };

        var handler = new Mock<HttpMessageHandler>();

        // Health endpoint
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.RequestUri!.ToString().EndsWith("/health")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("Healthy")
            });

        // Pipeline status endpoint
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.RequestUri!.ToString().Contains("/api/admin/validators/reg-123/status")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(pipelineStatus))
            });

        // Count endpoint
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.RequestUri!.ToString().Contains("/api/validators/reg-123/count")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(countInfo))
            });

        var httpClient = new HttpClient(handler.Object);
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);
        var tool = CreateTool();

        // Act
        var result = await tool.GetValidatorStatusAsync("reg-123");

        // Assert
        result.Status.Should().Be("Healthy");
        result.RegisterInfo.Should().NotBeNull();
        result.RegisterInfo!.RegisterId.Should().Be("reg-123");
        result.RegisterInfo.IsActive.Should().BeTrue();
        result.RegisterInfo.TransactionsInMemPool.Should().Be(15);
        result.RegisterInfo.DocketsProposed.Should().Be(100);
        result.RegisterInfo.DocketsConfirmed.Should().Be(95);
        result.RegisterInfo.DocketsRejected.Should().Be(5);
        result.RegisterInfo.ActiveValidators.Should().Be(5);
        result.RegisterInfo.MinValidators.Should().Be(3);
        result.RegisterInfo.HasQuorum.Should().BeTrue();
    }

    [Fact]
    public async Task GetValidatorStatusAsync_NoQuorum_ReturnsDegradedResult()
    {
        // Arrange
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_validator_status")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Validator")).Returns(true);

        var pipelineStatus = new { RegisterId = "reg-123", IsActive = true };
        var countInfo = new
        {
            RegisterId = "reg-123",
            ActiveCount = 1,
            MinValidators = 3,
            MaxValidators = 10,
            HasQuorum = false
        };

        var handler = new Mock<HttpMessageHandler>();

        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.RequestUri!.ToString().EndsWith("/health")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Healthy") });

        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.RequestUri!.ToString().Contains("/api/admin/validators/")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(pipelineStatus))
            });

        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.RequestUri!.ToString().Contains("/api/validators/") && r.RequestUri!.ToString().Contains("/count")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(countInfo))
            });

        var httpClient = new HttpClient(handler.Object);
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);
        var tool = CreateTool();

        // Act
        var result = await tool.GetValidatorStatusAsync("reg-123");

        // Assert
        result.Status.Should().Be("Degraded");
        result.Message.Should().Contain("quorum");
        result.Message.Should().Contain("1/3");
        result.RegisterInfo!.HasQuorum.Should().BeFalse();
    }

    [Fact]
    public async Task GetValidatorStatusAsync_UnhealthyService_ReturnsUnhealthyResult()
    {
        // Arrange
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_validator_status")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Validator")).Returns(true);

        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("Unhealthy")
            });

        var httpClient = new HttpClient(handler.Object);
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);
        var tool = CreateTool();

        // Act
        var result = await tool.GetValidatorStatusAsync();

        // Assert
        result.Status.Should().Be("Unhealthy");
        result.ServiceHealth!.IsHealthy.Should().BeFalse();
    }

    [Fact]
    public async Task GetValidatorStatusAsync_RegisterNotFound_ReturnsHealthyWithNullRegisterInfo()
    {
        // Arrange
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_validator_status")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Validator")).Returns(true);

        var handler = new Mock<HttpMessageHandler>();

        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.RequestUri!.ToString().EndsWith("/health")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Healthy") });

        // Status and count endpoints return 404
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => !r.RequestUri!.ToString().EndsWith("/health")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.NotFound));

        var httpClient = new HttpClient(handler.Object);
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);
        var tool = CreateTool();

        // Act
        var result = await tool.GetValidatorStatusAsync("nonexistent-register");

        // Assert
        result.Status.Should().Be("Healthy"); // Service is healthy, just no register info
        result.ServiceHealth!.IsHealthy.Should().BeTrue();
        result.RegisterInfo.Should().BeNull();
    }

    [Fact]
    public async Task GetValidatorStatusAsync_ResponseTimeIsRecorded()
    {
        // Arrange
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_validator_status")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Validator")).Returns(true);

        var httpClient = CreateMockHttpClient(
            ("http://localhost:5004/health", "Healthy"));

        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);
        var tool = CreateTool();

        // Act
        var result = await tool.GetValidatorStatusAsync();

        // Assert
        result.ResponseTimeMs.Should().BeGreaterThanOrEqualTo(0);
        result.CheckedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task GetValidatorStatusAsync_BothEndpointsFail_ReturnsUnhealthyWithPartialInfo()
    {
        // Arrange
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_validator_status")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Validator")).Returns(true);

        var handler = new Mock<HttpMessageHandler>();

        // Health check fails
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
        var result = await tool.GetValidatorStatusAsync("reg-123");

        // Assert
        result.Status.Should().Be("Unhealthy");
        result.ServiceHealth!.IsHealthy.Should().BeFalse();
        // RegisterInfo is not fetched when service is unhealthy
        result.RegisterInfo.Should().BeNull();
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
