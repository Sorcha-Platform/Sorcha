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

public sealed class WalletSignToolTests
{
    private readonly Mock<IMcpAuthorizationService> _authServiceMock;
    private readonly Mock<IMcpErrorHandler> _errorHandlerMock;
    private readonly Mock<IServiceAvailabilityTracker> _availabilityTrackerMock;
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;
    private readonly Mock<ILogger<WalletSignTool>> _loggerMock;
    private readonly IConfiguration _configuration;
    private readonly WalletSignTool _tool;

    public WalletSignToolTests()
    {
        _authServiceMock = new Mock<IMcpAuthorizationService>();
        _errorHandlerMock = new Mock<IMcpErrorHandler>();
        _availabilityTrackerMock = new Mock<IServiceAvailabilityTracker>();
        _httpClientFactoryMock = new Mock<IHttpClientFactory>();
        _loggerMock = new Mock<ILogger<WalletSignTool>>();

        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ServiceClients:WalletService:Address"] = "http://localhost:5001"
            })
            .Build();

        _tool = new WalletSignTool(
            _authServiceMock.Object,
            _errorHandlerMock.Object,
            _availabilityTrackerMock.Object,
            _httpClientFactoryMock.Object,
            _configuration,
            _loggerMock.Object);
    }

    [Fact]
    public async Task SignDataAsync_WhenUnauthorized_ReturnsUnauthorizedStatus()
    {
        // Arrange
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_wallet_sign")).Returns(false);

        // Act
        var result = await _tool.SignDataAsync("test data");

        // Assert
        result.Status.Should().Be("Unauthorized");
        result.Message.Should().Contain("sorcha:participant");
    }

    [Fact]
    public async Task SignDataAsync_WithEmptyData_ReturnsError()
    {
        // Arrange
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_wallet_sign")).Returns(true);

        // Act
        var result = await _tool.SignDataAsync("");

        // Assert
        result.Status.Should().Be("Error");
        result.Message.Should().Contain("Data to sign is required");
    }

    [Fact]
    public async Task SignDataAsync_WithNegativeAddressIndex_ReturnsError()
    {
        // Arrange
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_wallet_sign")).Returns(true);

        // Act
        var result = await _tool.SignDataAsync("test data", -1);

        // Assert
        result.Status.Should().Be("Error");
        result.Message.Should().Contain("Address index must be 0 or greater");
    }

    [Fact]
    public async Task SignDataAsync_WhenServiceUnavailable_ReturnsUnavailableStatus()
    {
        // Arrange
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_wallet_sign")).Returns(true);
        _availabilityTrackerMock.Setup(x => x.IsServiceAvailable("Wallet")).Returns(false);

        // Act
        var result = await _tool.SignDataAsync("test data");

        // Assert
        result.Status.Should().Be("Unavailable");
        result.Message.Should().Contain("Wallet service");
    }

    [Fact]
    public async Task SignDataAsync_WithSuccessfulResponse_ReturnsSignature()
    {
        // Arrange
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_wallet_sign")).Returns(true);
        _availabilityTrackerMock.Setup(x => x.IsServiceAvailable("Wallet")).Returns(true);

        var response = new
        {
            signature = "SGVsbG8gV29ybGQhDQo=",
            signatureFormat = "Base64",
            algorithm = "ED25519",
            signerAddress = "addr1qx4j7..."
        };

        SetupHttpClient(HttpStatusCode.OK, JsonSerializer.Serialize(response));

        // Act
        var result = await _tool.SignDataAsync("Hello World!");

        // Assert
        result.Status.Should().Be("Success");
        result.Signature.Should().Be("SGVsbG8gV29ybGQhDQo=");
        result.SignatureFormat.Should().Be("Base64");
        result.Algorithm.Should().Be("ED25519");
        result.SignerAddress.Should().Be("addr1qx4j7...");
    }

    [Fact]
    public async Task SignDataAsync_WithJsonData_SignsSuccessfully()
    {
        // Arrange
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_wallet_sign")).Returns(true);
        _availabilityTrackerMock.Setup(x => x.IsServiceAvailable("Wallet")).Returns(true);

        var response = new
        {
            signature = "JsonSignature==",
            signatureFormat = "Base64",
            algorithm = "P256",
            signerAddress = "addr2..."
        };

        SetupHttpClient(HttpStatusCode.OK, JsonSerializer.Serialize(response));

        // Act
        var result = await _tool.SignDataAsync("{\"action\":\"approve\",\"id\":123}");

        // Assert
        result.Status.Should().Be("Success");
        result.Signature.Should().NotBeEmpty();
    }

    [Fact]
    public async Task SignDataAsync_WithCustomAddressIndex_PassesIndexToService()
    {
        // Arrange
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_wallet_sign")).Returns(true);
        _availabilityTrackerMock.Setup(x => x.IsServiceAvailable("Wallet")).Returns(true);

        var response = new
        {
            signature = "IndexedSignature==",
            signatureFormat = "Base64",
            algorithm = "ED25519",
            signerAddress = "addr_derived_2"
        };

        var handlerMock = SetupHttpClientWithCapture(HttpStatusCode.OK, JsonSerializer.Serialize(response));

        // Act
        await _tool.SignDataAsync("test data", 2);

        // Assert
        handlerMock.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req =>
                req.Method == HttpMethod.Post),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task SignDataAsync_WithHttpError_ReturnsErrorStatus()
    {
        // Arrange
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_wallet_sign")).Returns(true);
        _availabilityTrackerMock.Setup(x => x.IsServiceAvailable("Wallet")).Returns(true);

        SetupHttpClient(HttpStatusCode.BadRequest, "{\"error\":\"Invalid signing request\"}");

        // Act
        var result = await _tool.SignDataAsync("test data");

        // Assert
        result.Status.Should().Be("Error");
        result.Message.Should().Contain("Invalid signing request");
    }

    [Fact]
    public async Task SignDataAsync_WithTimeout_ReturnsTimeoutStatus()
    {
        // Arrange
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_wallet_sign")).Returns(true);
        _availabilityTrackerMock.Setup(x => x.IsServiceAvailable("Wallet")).Returns(true);

        SetupHttpClientWithException(new TaskCanceledException());

        // Act
        var result = await _tool.SignDataAsync("test data");

        // Assert
        result.Status.Should().Be("Timeout");
        _availabilityTrackerMock.Verify(x => x.RecordFailure("Wallet"), Times.Once);
    }

    [Fact]
    public async Task SignDataAsync_WithHttpException_ReturnsErrorStatus()
    {
        // Arrange
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_wallet_sign")).Returns(true);
        _availabilityTrackerMock.Setup(x => x.IsServiceAvailable("Wallet")).Returns(true);

        SetupHttpClientWithException(new HttpRequestException("Connection refused"));

        // Act
        var result = await _tool.SignDataAsync("test data");

        // Assert
        result.Status.Should().Be("Error");
        result.Message.Should().Contain("Connection refused");
    }

    [Fact]
    public async Task SignDataAsync_RecordsSuccessOnSuccessfulResponse()
    {
        // Arrange
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_wallet_sign")).Returns(true);
        _availabilityTrackerMock.Setup(x => x.IsServiceAvailable("Wallet")).Returns(true);

        var response = new
        {
            signature = "test==",
            signatureFormat = "Base64",
            algorithm = "ED25519",
            signerAddress = "addr1..."
        };

        SetupHttpClient(HttpStatusCode.OK, JsonSerializer.Serialize(response));

        // Act
        await _tool.SignDataAsync("test data");

        // Assert
        _availabilityTrackerMock.Verify(x => x.RecordSuccess("Wallet"), Times.Once);
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
