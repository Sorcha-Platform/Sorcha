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

public class DisclosureAnalysisToolTests
{
    private readonly Mock<IMcpAuthorizationService> _authServiceMock;
    private readonly Mock<IMcpErrorHandler> _errorHandlerMock;
    private readonly Mock<IServiceAvailabilityTracker> _availabilityTrackerMock;
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;
    private readonly Mock<IConfiguration> _configurationMock;
    private readonly Mock<ILogger<DisclosureAnalysisTool>> _loggerMock;

    public DisclosureAnalysisToolTests()
    {
        _authServiceMock = new Mock<IMcpAuthorizationService>();
        _errorHandlerMock = new Mock<IMcpErrorHandler>();
        _availabilityTrackerMock = new Mock<IServiceAvailabilityTracker>();
        _httpClientFactoryMock = new Mock<IHttpClientFactory>();
        _configurationMock = new Mock<IConfiguration>();
        _loggerMock = new Mock<ILogger<DisclosureAnalysisTool>>();

        _configurationMock.Setup(c => c["ServiceClients:BlueprintService:Address"])
            .Returns("http://localhost:5000");
    }

    private DisclosureAnalysisTool CreateTool()
    {
        return new DisclosureAnalysisTool(
            _authServiceMock.Object,
            _errorHandlerMock.Object,
            _availabilityTrackerMock.Object,
            _httpClientFactoryMock.Object,
            _configurationMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task AnalyzeDisclosuresAsync_Unauthorized_ReturnsUnauthorizedResult()
    {
        // Arrange
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_disclosure_analysis")).Returns(false);
        var tool = CreateTool();

        // Act
        var result = await tool.AnalyzeDisclosuresAsync("bp-123", "0", "{}");

        // Assert
        result.Status.Should().Be("Unauthorized");
        result.Message.Should().Contain("Access denied");
    }

    [Fact]
    public async Task AnalyzeDisclosuresAsync_ServiceUnavailable_ReturnsUnavailableResult()
    {
        // Arrange
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_disclosure_analysis")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Blueprint")).Returns(false);
        var tool = CreateTool();

        // Act
        var result = await tool.AnalyzeDisclosuresAsync("bp-123", "0", "{}");

        // Assert
        result.Status.Should().Be("Unavailable");
        result.Message.Should().Contain("unavailable");
    }

    [Fact]
    public async Task AnalyzeDisclosuresAsync_EmptyBlueprintId_ReturnsErrorResult()
    {
        // Arrange
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_disclosure_analysis")).Returns(true);
        var tool = CreateTool();

        // Act
        var result = await tool.AnalyzeDisclosuresAsync("", "0", "{}");

        // Assert
        result.Status.Should().Be("Error");
        result.Message.Should().Contain("Blueprint ID");
    }

    [Fact]
    public async Task AnalyzeDisclosuresAsync_EmptyActionId_ReturnsErrorResult()
    {
        // Arrange
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_disclosure_analysis")).Returns(true);
        var tool = CreateTool();

        // Act
        var result = await tool.AnalyzeDisclosuresAsync("bp-123", "", "{}");

        // Assert
        result.Status.Should().Be("Error");
        result.Message.Should().Contain("Action ID");
    }

    [Fact]
    public async Task AnalyzeDisclosuresAsync_InvalidJson_ReturnsErrorResult()
    {
        // Arrange
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_disclosure_analysis")).Returns(true);
        var tool = CreateTool();

        // Act
        var result = await tool.AnalyzeDisclosuresAsync("bp-123", "0", "{ invalid }");

        // Assert
        result.Status.Should().Be("Error");
        result.Message.Should().Contain("Invalid data JSON");
    }

    [Fact]
    public async Task AnalyzeDisclosuresAsync_WithDisclosures_ReturnsSuccessResult()
    {
        // Arrange
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_disclosure_analysis")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Blueprint")).Returns(true);

        var disclosureResponse = new
        {
            disclosures = new[]
            {
                new
                {
                    participantId = "applicant",
                    disclosureId = "disclosure-1",
                    disclosedData = new Dictionary<string, object>
                    {
                        ["name"] = "John Doe",
                        ["email"] = "john@example.com"
                    },
                    fieldCount = 2
                },
                new
                {
                    participantId = "reviewer",
                    disclosureId = "disclosure-2",
                    disclosedData = new Dictionary<string, object>
                    {
                        ["name"] = "John Doe",
                        ["ssn"] = "***-**-1234"
                    },
                    fieldCount = 2
                }
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
                Content = new StringContent(JsonSerializer.Serialize(disclosureResponse))
            });

        var httpClient = new HttpClient(handler.Object);
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);
        var tool = CreateTool();

        // Act
        var result = await tool.AnalyzeDisclosuresAsync("bp-123", "0", "{\"name\": \"John Doe\", \"email\": \"john@example.com\", \"ssn\": \"123-45-6789\"}");

        // Assert
        result.Status.Should().Be("Success");
        result.Message.Should().Contain("2 participant");
        result.Message.Should().Contain("4 field");
        result.TotalParticipants.Should().Be(2);
        result.TotalDisclosedFields.Should().Be(4);
        result.Disclosures.Should().HaveCount(2);
        result.Disclosures[0].ParticipantId.Should().Be("applicant");
        result.Disclosures[0].DisclosedFields.Should().Contain("name");
        result.Disclosures[0].DisclosedFields.Should().Contain("email");
        result.Disclosures[0].FieldCount.Should().Be(2);
        result.Disclosures[1].ParticipantId.Should().Be("reviewer");

        _availabilityTrackerMock.Verify(a => a.RecordSuccess("Blueprint"), Times.Once);
    }

    [Fact]
    public async Task AnalyzeDisclosuresAsync_NoDisclosures_ReturnsSuccessWithMessage()
    {
        // Arrange
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_disclosure_analysis")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Blueprint")).Returns(true);

        var disclosureResponse = new
        {
            disclosures = Array.Empty<object>()
        };

        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(disclosureResponse))
            });

        var httpClient = new HttpClient(handler.Object);
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);
        var tool = CreateTool();

        // Act
        var result = await tool.AnalyzeDisclosuresAsync("bp-123", "0", "{}");

        // Assert
        result.Status.Should().Be("Success");
        result.Message.Should().Contain("No disclosure rules");
        result.Disclosures.Should().BeEmpty();
        result.TotalParticipants.Should().Be(0);
    }

    [Fact]
    public async Task AnalyzeDisclosuresAsync_BlueprintNotFound_ReturnsErrorFromService()
    {
        // Arrange
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_disclosure_analysis")).Returns(true);
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
        var result = await tool.AnalyzeDisclosuresAsync("nonexistent", "0", "{}");

        // Assert
        result.Status.Should().Be("Error");
        result.Message.Should().Contain("Blueprint not found");
    }

    [Fact]
    public async Task AnalyzeDisclosuresAsync_CorrectEndpointCalled()
    {
        // Arrange
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_disclosure_analysis")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Blueprint")).Returns(true);

        var disclosureResponse = new { disclosures = Array.Empty<object>() };

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
                Content = new StringContent(JsonSerializer.Serialize(disclosureResponse))
            });

        var httpClient = new HttpClient(handler.Object);
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);
        var tool = CreateTool();

        // Act
        await tool.AnalyzeDisclosuresAsync("bp-123", "0", "{}");

        // Assert
        capturedUrl.Should().Be("http://localhost:5000/api/execution/disclose");
    }

    [Fact]
    public async Task AnalyzeDisclosuresAsync_ResponseTimeIsRecorded()
    {
        // Arrange
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_disclosure_analysis")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Blueprint")).Returns(true);

        var disclosureResponse = new { disclosures = Array.Empty<object>() };

        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(disclosureResponse))
            });

        var httpClient = new HttpClient(handler.Object);
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);
        var tool = CreateTool();

        // Act
        var result = await tool.AnalyzeDisclosuresAsync("bp-123", "0", "{}");

        // Assert
        result.ResponseTimeMs.Should().BeGreaterThanOrEqualTo(0);
        result.CheckedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }
}
