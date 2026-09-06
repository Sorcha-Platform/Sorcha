// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq.Protected;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.McpServer.Tools.Designer;

namespace Sorcha.McpServer.Tests.Tools.Designer;

public class BlueprintValidateToolTests
{
    private readonly Mock<IMcpAuthorizationService> _authServiceMock;
    private readonly Mock<IMcpErrorHandler> _errorHandlerMock;
    private readonly Mock<IServiceAvailabilityTracker> _availabilityTrackerMock;
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;
    private readonly Mock<IConfiguration> _configurationMock;
    private readonly Mock<ILogger<BlueprintValidateTool>> _loggerMock;

    public BlueprintValidateToolTests()
    {
        _authServiceMock = new Mock<IMcpAuthorizationService>();
        _errorHandlerMock = new Mock<IMcpErrorHandler>();
        _availabilityTrackerMock = new Mock<IServiceAvailabilityTracker>();
        _httpClientFactoryMock = new Mock<IHttpClientFactory>();
        _configurationMock = new Mock<IConfiguration>();
        _loggerMock = new Mock<ILogger<BlueprintValidateTool>>();

        _configurationMock.Setup(c => c["ServiceClients:BlueprintService:Address"])
            .Returns("http://localhost:5000");
    }

    private BlueprintValidateTool CreateTool()
    {
        return new BlueprintValidateTool(
            _authServiceMock.Object,
            _errorHandlerMock.Object,
            _availabilityTrackerMock.Object,
            _httpClientFactoryMock.Object,
            _configurationMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task ValidateActionDataAsync_Unauthorized_ReturnsUnauthorizedResult()
    {
        // Arrange
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_blueprint_validate")).Returns(false);
        var tool = CreateTool();

        // Act
        var result = await tool.ValidateActionDataAsync("bp-123", "0", "{}");

        // Assert
        result.Status.Should().Be("Unauthorized");
        result.Message.Should().Contain("Access denied");
    }

    [Fact]
    public async Task ValidateActionDataAsync_ServiceUnavailable_ReturnsUnavailableResult()
    {
        // Arrange
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_blueprint_validate")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Blueprint")).Returns(false);
        var tool = CreateTool();

        // Act
        var result = await tool.ValidateActionDataAsync("bp-123", "0", "{}");

        // Assert
        result.Status.Should().Be("Unavailable");
        result.Message.Should().Contain("unavailable");
    }

    [Fact]
    public async Task ValidateActionDataAsync_EmptyBlueprintId_ReturnsErrorResult()
    {
        // Arrange
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_blueprint_validate")).Returns(true);
        var tool = CreateTool();

        // Act
        var result = await tool.ValidateActionDataAsync("", "0", "{}");

        // Assert
        result.Status.Should().Be("Error");
        result.Message.Should().Contain("Blueprint ID");
    }

    [Fact]
    public async Task ValidateActionDataAsync_EmptyActionId_ReturnsErrorResult()
    {
        // Arrange
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_blueprint_validate")).Returns(true);
        var tool = CreateTool();

        // Act
        var result = await tool.ValidateActionDataAsync("bp-123", "", "{}");

        // Assert
        result.Status.Should().Be("Error");
        result.Message.Should().Contain("Action ID");
    }

    [Fact]
    public async Task ValidateActionDataAsync_EmptyDataJson_ReturnsErrorResult()
    {
        // Arrange
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_blueprint_validate")).Returns(true);
        var tool = CreateTool();

        // Act
        var result = await tool.ValidateActionDataAsync("bp-123", "0", "");

        // Assert
        result.Status.Should().Be("Error");
        result.Message.Should().Contain("Data JSON");
    }

    [Fact]
    public async Task ValidateActionDataAsync_InvalidJson_ReturnsErrorResult()
    {
        // Arrange
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_blueprint_validate")).Returns(true);
        var tool = CreateTool();

        // Act
        var result = await tool.ValidateActionDataAsync("bp-123", "0", "{ invalid }");

        // Assert
        result.Status.Should().Be("Error");
        result.Message.Should().Contain("Invalid data JSON");
    }

    [Fact]
    public async Task ValidateActionDataAsync_ValidData_ReturnsValidResult()
    {
        // Arrange
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_blueprint_validate")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Blueprint")).Returns(true);

        var validationResponse = new
        {
            isValid = true,
            errors = Array.Empty<object>()
        };

        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(validationResponse))
            });

        var httpClient = new HttpClient(handler.Object);
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);
        var tool = CreateTool();

        // Act
        var result = await tool.ValidateActionDataAsync("bp-123", "0", "{\"name\": \"test\"}");

        // Assert
        result.Status.Should().Be("Valid");
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.Message.Should().Contain("valid");

        _availabilityTrackerMock.Verify(a => a.RecordSuccess("Blueprint"), Times.Once);
    }

    [Fact]
    public async Task ValidateActionDataAsync_InvalidData_ReturnsInvalidResult()
    {
        // Arrange
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_blueprint_validate")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Blueprint")).Returns(true);

        var validationResponse = new
        {
            isValid = false,
            errors = new[]
            {
                new { path = "/name", message = "Required property is missing", schemaLocation = "/properties/name", keyword = "required" },
                new { path = "/age", message = "Value must be at least 18", schemaLocation = "/properties/age", keyword = "minimum" }
            }
        };

        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(validationResponse))
            });

        var httpClient = new HttpClient(handler.Object);
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);
        var tool = CreateTool();

        // Act
        var result = await tool.ValidateActionDataAsync("bp-123", "0", "{\"age\": 15}");

        // Assert
        result.Status.Should().Be("Invalid");
        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(2);
        result.Errors[0].Path.Should().Be("/name");
        result.Errors[0].Message.Should().Contain("missing");
        result.Errors[0].Keyword.Should().Be("required");
        result.Errors[1].Path.Should().Be("/age");
        result.Errors[1].Keyword.Should().Be("minimum");
        result.Message.Should().Contain("2 error");
    }

    [Fact]
    public async Task ValidateActionDataAsync_BlueprintNotFound_ReturnsErrorFromService()
    {
        // Arrange
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_blueprint_validate")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Blueprint")).Returns(true);

        var errorResponse = new { error = "Blueprint not found" };

        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(JsonSerializer.Serialize(errorResponse))
            });

        var httpClient = new HttpClient(handler.Object);
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);
        var tool = CreateTool();

        // Act
        var result = await tool.ValidateActionDataAsync("nonexistent", "0", "{}");

        // Assert
        result.Status.Should().Be("Invalid");
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle();
        result.Errors[0].Message.Should().Contain("Blueprint not found");
    }

    [Fact]
    public async Task ValidateActionDataAsync_CorrectUrlCalled()
    {
        // Arrange
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_blueprint_validate")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Blueprint")).Returns(true);

        var validationResponse = new { isValid = true, errors = Array.Empty<object>() };

        string? capturedUrl = null;
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedUrl = req.RequestUri?.ToString())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(validationResponse))
            });

        var httpClient = new HttpClient(handler.Object);
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);
        var tool = CreateTool();

        // Act
        await tool.ValidateActionDataAsync("bp-123", "0", "{}");

        // Assert
        capturedUrl.Should().Be("http://localhost:5000/api/execution/validate");
    }

    [Fact]
    public async Task ValidateActionDataAsync_ResponseTimeIsRecorded()
    {
        // Arrange
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_blueprint_validate")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Blueprint")).Returns(true);

        var validationResponse = new { isValid = true, errors = Array.Empty<object>() };

        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(validationResponse))
            });

        var httpClient = new HttpClient(handler.Object);
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);
        var tool = CreateTool();

        // Act
        var result = await tool.ValidateActionDataAsync("bp-123", "0", "{}");

        // Assert
        result.ResponseTimeMs.Should().BeGreaterThanOrEqualTo(0);
        result.CheckedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }
}
