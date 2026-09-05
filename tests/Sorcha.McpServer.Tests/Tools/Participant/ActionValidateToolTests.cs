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

public sealed class ActionValidateToolTests
{
    private readonly Mock<IMcpAuthorizationService> _authServiceMock;
    private readonly Mock<IMcpErrorHandler> _errorHandlerMock;
    private readonly Mock<IServiceAvailabilityTracker> _availabilityTrackerMock;
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;
    private readonly Mock<ILogger<ActionValidateTool>> _loggerMock;
    private readonly IConfiguration _configuration;
    private readonly ActionValidateTool _tool;

    public ActionValidateToolTests()
    {
        _authServiceMock = new Mock<IMcpAuthorizationService>();
        _errorHandlerMock = new Mock<IMcpErrorHandler>();
        _availabilityTrackerMock = new Mock<IServiceAvailabilityTracker>();
        _httpClientFactoryMock = new Mock<IHttpClientFactory>();
        _loggerMock = new Mock<ILogger<ActionValidateTool>>();

        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ServiceClients:BlueprintService:Address"] = "http://localhost:5000"
            })
            .Build();

        _tool = new ActionValidateTool(
            _authServiceMock.Object,
            _errorHandlerMock.Object,
            _availabilityTrackerMock.Object,
            _httpClientFactoryMock.Object,
            _configuration,
            _loggerMock.Object);
    }

    [Fact]
    public async Task ValidateActionDataAsync_WhenUnauthorized_ReturnsUnauthorizedStatus()
    {
        // Arrange
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_action_validate")).Returns(false);

        // Act
        var result = await _tool.ValidateActionDataAsync("bp-1", "1", "{}");

        // Assert
        result.Status.Should().Be("Unauthorized");
        result.Message.Should().Contain("sorcha:participant");
    }

    [Fact]
    public async Task ValidateActionDataAsync_WithEmptyBlueprintId_ReturnsError()
    {
        // Arrange
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_action_validate")).Returns(true);

        // Act
        var result = await _tool.ValidateActionDataAsync("", "1", "{}");

        // Assert
        result.Status.Should().Be("Error");
        result.Message.Should().Contain("Blueprint ID is required");
    }

    [Fact]
    public async Task ValidateActionDataAsync_WithEmptyActionId_ReturnsError()
    {
        // Arrange
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_action_validate")).Returns(true);

        // Act
        var result = await _tool.ValidateActionDataAsync("bp-1", "", "{}");

        // Assert
        result.Status.Should().Be("Error");
        result.Message.Should().Contain("Action ID is required");
    }

    [Fact]
    public async Task ValidateActionDataAsync_WithEmptyData_ReturnsError()
    {
        // Arrange
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_action_validate")).Returns(true);

        // Act
        var result = await _tool.ValidateActionDataAsync("bp-1", "1", "");

        // Assert
        result.Status.Should().Be("Error");
        result.Message.Should().Contain("Data JSON is required");
    }

    [Fact]
    public async Task ValidateActionDataAsync_WithInvalidJson_ReturnsError()
    {
        // Arrange
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_action_validate")).Returns(true);

        // Act
        var result = await _tool.ValidateActionDataAsync("bp-1", "1", "{invalid json");

        // Assert
        result.Status.Should().Be("Error");
        result.Message.Should().Contain("Invalid data JSON format");
    }

    [Fact]
    public async Task ValidateActionDataAsync_WhenServiceUnavailable_ReturnsUnavailableStatus()
    {
        // Arrange
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_action_validate")).Returns(true);
        _availabilityTrackerMock.Setup(x => x.IsServiceAvailable("Blueprint")).Returns(false);

        // Act
        var result = await _tool.ValidateActionDataAsync("bp-1", "1", "{\"name\":\"test\"}");

        // Assert
        result.Status.Should().Be("Unavailable");
        result.Message.Should().Contain("Blueprint service");
    }

    [Fact]
    public async Task ValidateActionDataAsync_WithValidData_ReturnsSuccess()
    {
        // Arrange
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_action_validate")).Returns(true);
        _availabilityTrackerMock.Setup(x => x.IsServiceAvailable("Blueprint")).Returns(true);

        var response = new
        {
            isValid = true,
            errors = Array.Empty<object>()
        };

        HttpRequestMessage? capturedRequest = null;
        SetupHttpClient(HttpStatusCode.OK, JsonSerializer.Serialize(response), r => capturedRequest = r);

        // Act
        var result = await _tool.ValidateActionDataAsync("bp-1", "1", "{\"name\":\"John\",\"email\":\"john@example.com\"}");

        // Assert
        result.Status.Should().Be("Valid");
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();

        // Posted to /api/execution/validate — GET /api/actions/{id}/validate is not mapped.
        capturedRequest.Should().NotBeNull();
        capturedRequest!.RequestUri!.AbsolutePath.Should().Be("/api/execution/validate");
        var body = await capturedRequest.Content!.ReadAsStringAsync();
        body.Should().Contain("\"blueprintId\":\"bp-1\"");
        body.Should().Contain("\"actionId\":\"1\"");
    }

    [Fact]
    public async Task ValidateActionDataAsync_WithInvalidData_ReturnsValidationErrors()
    {
        // Arrange
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_action_validate")).Returns(true);
        _availabilityTrackerMock.Setup(x => x.IsServiceAvailable("Blueprint")).Returns(true);

        var response = new
        {
            isValid = false,
            errors = new[]
            {
                new { path = "$.email", message = "Invalid email format" },
                new { path = "$.age", message = "Age must be positive" }
            }
        };

        SetupHttpClient(HttpStatusCode.OK, JsonSerializer.Serialize(response));

        // Act
        var result = await _tool.ValidateActionDataAsync("bp-1", "1", "{\"name\":\"John\"}");

        // Assert
        result.Status.Should().Be("Invalid");
        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(2);
        result.Errors.Should().Contain(e => e.Path == "$.email");
    }

    [Fact]
    public async Task ValidateActionDataAsync_WithNotFound_ReturnsErrorStatus()
    {
        // Arrange
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_action_validate")).Returns(true);
        _availabilityTrackerMock.Setup(x => x.IsServiceAvailable("Blueprint")).Returns(true);

        SetupHttpClient(HttpStatusCode.NotFound, "{\"error\":\"Blueprint not found\"}");

        // Act
        var result = await _tool.ValidateActionDataAsync("bp-invalid", "1", "{}");

        // Assert
        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task ValidateActionDataAsync_WithTimeout_ReturnsTimeoutStatus()
    {
        // Arrange
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_action_validate")).Returns(true);
        _availabilityTrackerMock.Setup(x => x.IsServiceAvailable("Blueprint")).Returns(true);

        SetupHttpClientWithException(new TaskCanceledException());

        // Act
        var result = await _tool.ValidateActionDataAsync("bp-1", "1", "{}");

        // Assert
        result.Status.Should().Be("Timeout");
        _availabilityTrackerMock.Verify(x => x.RecordFailure("Blueprint"), Times.Once);
    }

    [Fact]
    public async Task ValidateActionDataAsync_WithHttpException_ReturnsErrorStatus()
    {
        // Arrange
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_action_validate")).Returns(true);
        _availabilityTrackerMock.Setup(x => x.IsServiceAvailable("Blueprint")).Returns(true);

        SetupHttpClientWithException(new HttpRequestException("Connection failed"));

        // Act
        var result = await _tool.ValidateActionDataAsync("bp-1", "1", "{}");

        // Assert
        result.Status.Should().Be("Error");
        result.Message.Should().Contain("Connection failed");
    }

    private void SetupHttpClient(HttpStatusCode statusCode, string content, Action<HttpRequestMessage>? onRequest = null)
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((request, _) => onRequest?.Invoke(request))
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = statusCode,
                Content = new StringContent(content)
            });

        var client = new HttpClient(handlerMock.Object);
        _httpClientFactoryMock.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(client);
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
