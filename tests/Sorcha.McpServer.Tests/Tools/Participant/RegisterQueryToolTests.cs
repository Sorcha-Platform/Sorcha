// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.McpServer.Tools.Participant;

namespace Sorcha.McpServer.Tests.Tools.Participant;

public sealed class RegisterQueryToolTests
{
    private readonly Mock<IMcpAuthorizationService> _authServiceMock;
    private readonly Mock<IMcpErrorHandler> _errorHandlerMock;
    private readonly Mock<IServiceAvailabilityTracker> _availabilityTrackerMock;
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;
    private readonly Mock<ILogger<RegisterQueryTool>> _loggerMock;
    private readonly IConfiguration _configuration;
    private readonly RegisterQueryTool _tool;

    public RegisterQueryToolTests()
    {
        _authServiceMock = new Mock<IMcpAuthorizationService>();
        _errorHandlerMock = new Mock<IMcpErrorHandler>();
        _availabilityTrackerMock = new Mock<IServiceAvailabilityTracker>();
        _httpClientFactoryMock = new Mock<IHttpClientFactory>();
        _loggerMock = new Mock<ILogger<RegisterQueryTool>>();

        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ServiceClients:RegisterService:Address"] = "http://localhost:5290"
            })
            .Build();

        _tool = new RegisterQueryTool(
            _authServiceMock.Object,
            _errorHandlerMock.Object,
            _availabilityTrackerMock.Object,
            _httpClientFactoryMock.Object,
            _configuration,
            _loggerMock.Object);
    }

    [Fact]
    public async Task QueryRegisterAsync_WhenUnauthorized_ReturnsUnauthorizedStatus()
    {
        // Arrange
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_register_query")).Returns(false);

        // Act
        var result = await _tool.QueryRegisterAsync("register-123");

        // Assert
        result.Status.Should().Be("Unauthorized");
        result.Message.Should().Contain("sorcha:participant");
    }

    [Fact]
    public async Task QueryRegisterAsync_WithEmptyRegisterId_ReturnsError()
    {
        // Arrange
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_register_query")).Returns(true);

        // Act
        var result = await _tool.QueryRegisterAsync("");

        // Assert
        result.Status.Should().Be("Error");
        result.Message.Should().Contain("Register ID is required");
    }

    [Fact]
    public async Task QueryRegisterAsync_WhenServiceUnavailable_ReturnsUnavailableStatus()
    {
        // Arrange
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_register_query")).Returns(true);
        _availabilityTrackerMock.Setup(x => x.IsServiceAvailable("Register")).Returns(false);

        // Act
        var result = await _tool.QueryRegisterAsync("register-123");

        // Assert
        result.Status.Should().Be("Unavailable");
        result.Message.Should().Contain("Register service");
    }

    [Fact]
    public async Task QueryRegisterAsync_WithSuccessfulResponse_ReturnsRecords()
    {
        // Arrange
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_register_query")).Returns(true);
        _availabilityTrackerMock.Setup(x => x.IsServiceAvailable("Register")).Returns(true);

        var response = new
        {
            value = new[]
            {
                new
                {
                    id = "record-1",
                    docketId = "docket-1",
                    transactionId = "tx-1",
                    data = new Dictionary<string, object>
                    {
                        ["name"] = "John Doe",
                        ["status"] = "Active"
                    },
                    createdAt = (DateTimeOffset?)DateTimeOffset.UtcNow.AddDays(-1),
                    updatedAt = (DateTimeOffset?)DateTimeOffset.UtcNow.AddHours(-2)
                },
                new
                {
                    id = "record-2",
                    docketId = "docket-2",
                    transactionId = "tx-2",
                    data = new Dictionary<string, object>
                    {
                        ["name"] = "Jane Smith",
                        ["status"] = "Pending"
                    },
                    createdAt = (DateTimeOffset?)DateTimeOffset.UtcNow.AddDays(-2),
                    updatedAt = (DateTimeOffset?)null
                }
            },
            count = 2
        };

        SetupHttpClient(HttpStatusCode.OK, JsonSerializer.Serialize(response));

        // Act
        var result = await _tool.QueryRegisterAsync("register-123");

        // Assert
        result.Status.Should().Be("Success");
        result.Records.Should().HaveCount(2);
        result.Records[0].RecordId.Should().Be("record-1");
        result.Records[0].DocketId.Should().Be("docket-1");
        result.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task QueryRegisterAsync_WithEmptyResults_ReturnsSuccessWithEmptyList()
    {
        // Arrange
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_register_query")).Returns(true);
        _availabilityTrackerMock.Setup(x => x.IsServiceAvailable("Register")).Returns(true);

        var response = new { value = Array.Empty<object>(), count = 0 };
        SetupHttpClient(HttpStatusCode.OK, JsonSerializer.Serialize(response));

        // Act
        var result = await _tool.QueryRegisterAsync("register-123");

        // Assert
        result.Status.Should().Be("Success");
        result.Records.Should().BeEmpty();
        result.Message.Should().Contain("0 record");
    }

    [Fact]
    public async Task QueryRegisterAsync_WithDocketFilter_PassesCorrectParameter()
    {
        // Arrange
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_register_query")).Returns(true);
        _availabilityTrackerMock.Setup(x => x.IsServiceAvailable("Register")).Returns(true);

        var response = new { value = Array.Empty<object>(), count = 0 };
        var handlerMock = SetupHttpClientWithCapture(HttpStatusCode.OK, JsonSerializer.Serialize(response));

        // Act
        await _tool.QueryRegisterAsync("register-123", "docket-456");

        // Assert
        handlerMock.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req =>
                req.RequestUri!.ToString().Contains("docketId=docket-456")),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task QueryRegisterAsync_WithODataFilter_PassesCorrectParameter()
    {
        // Arrange
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_register_query")).Returns(true);
        _availabilityTrackerMock.Setup(x => x.IsServiceAvailable("Register")).Returns(true);

        var response = new { value = Array.Empty<object>(), count = 0 };
        var handlerMock = SetupHttpClientWithCapture(HttpStatusCode.OK, JsonSerializer.Serialize(response));

        // Act
        await _tool.QueryRegisterAsync("register-123", query: "status eq 'Active'");

        // Assert
        handlerMock.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req =>
                req.RequestUri!.ToString().Contains("$filter=")),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task QueryRegisterAsync_WithPagination_PassesCorrectParameters()
    {
        // Arrange
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_register_query")).Returns(true);
        _availabilityTrackerMock.Setup(x => x.IsServiceAvailable("Register")).Returns(true);

        var response = new { value = Array.Empty<object>(), count = 0 };
        var handlerMock = SetupHttpClientWithCapture(HttpStatusCode.OK, JsonSerializer.Serialize(response));

        // Act
        await _tool.QueryRegisterAsync("register-123", page: 3, pageSize: 25);

        // Assert
        handlerMock.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req =>
                req.RequestUri!.ToString().Contains("$skip=50") &&
                req.RequestUri.ToString().Contains("$top=25")),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task QueryRegisterAsync_WithPageSizeOverMax_CapsAt100()
    {
        // Arrange
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_register_query")).Returns(true);
        _availabilityTrackerMock.Setup(x => x.IsServiceAvailable("Register")).Returns(true);

        var response = new { value = Array.Empty<object>(), count = 0 };
        var handlerMock = SetupHttpClientWithCapture(HttpStatusCode.OK, JsonSerializer.Serialize(response));

        // Act
        await _tool.QueryRegisterAsync("register-123", pageSize: 200);

        // Assert
        handlerMock.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req =>
                req.RequestUri!.ToString().Contains("$top=100")),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task QueryRegisterAsync_WithHttpError_ReturnsErrorStatus()
    {
        // Arrange
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_register_query")).Returns(true);
        _availabilityTrackerMock.Setup(x => x.IsServiceAvailable("Register")).Returns(true);

        SetupHttpClient(HttpStatusCode.NotFound, "{\"error\":\"Register not found\"}");

        // Act
        var result = await _tool.QueryRegisterAsync("register-invalid");

        // Assert
        result.Status.Should().Be("Error");
        result.Message.Should().Contain("not found");
    }

    [Fact]
    public async Task QueryRegisterAsync_WithTimeout_ReturnsTimeoutStatus()
    {
        // Arrange
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_register_query")).Returns(true);
        _availabilityTrackerMock.Setup(x => x.IsServiceAvailable("Register")).Returns(true);

        SetupHttpClientWithException(new TaskCanceledException());

        // Act
        var result = await _tool.QueryRegisterAsync("register-123");

        // Assert
        result.Status.Should().Be("Timeout");
        _availabilityTrackerMock.Verify(x => x.RecordFailure("Register"), Times.Once);
    }

    [Fact]
    public async Task QueryRegisterAsync_WithHttpException_ReturnsErrorStatus()
    {
        // Arrange
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_register_query")).Returns(true);
        _availabilityTrackerMock.Setup(x => x.IsServiceAvailable("Register")).Returns(true);

        SetupHttpClientWithException(new HttpRequestException("Connection refused"));

        // Act
        var result = await _tool.QueryRegisterAsync("register-123");

        // Assert
        result.Status.Should().Be("Error");
        result.Message.Should().Contain("Connection refused");
    }

    [Fact]
    public async Task QueryRegisterAsync_RecordsSuccessOnSuccessfulResponse()
    {
        // Arrange
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_register_query")).Returns(true);
        _availabilityTrackerMock.Setup(x => x.IsServiceAvailable("Register")).Returns(true);

        var response = new { value = Array.Empty<object>(), count = 0 };
        SetupHttpClient(HttpStatusCode.OK, JsonSerializer.Serialize(response));

        // Act
        await _tool.QueryRegisterAsync("register-123");

        // Assert
        _availabilityTrackerMock.Verify(x => x.RecordSuccess("Register"), Times.Once);
    }

    private void SetupHttpClient(HttpStatusCode statusCode, string content)
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = statusCode,
                Content = new StringContent(content)
            });

        var client = new HttpClient(handlerMock.Object);
        _httpClientFactoryMock.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(client);
    }

    private Mock<HttpMessageHandler> SetupHttpClientWithCapture(HttpStatusCode statusCode, string content)
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = statusCode,
                Content = new StringContent(content)
            });

        var client = new HttpClient(handlerMock.Object);
        _httpClientFactoryMock.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(client);
        return handlerMock;
    }

    private void SetupHttpClientWithException(Exception exception)
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(exception);

        var client = new HttpClient(handlerMock.Object);
        _httpClientFactoryMock.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(client);
    }
}
