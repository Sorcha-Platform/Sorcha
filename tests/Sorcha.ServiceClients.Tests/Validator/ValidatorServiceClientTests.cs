// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sorcha.ServiceClients.Auth;
using Sorcha.ServiceClients.Validator;

namespace Sorcha.ServiceClients.Tests.Validator;

/// <summary>
/// Unit tests for ValidatorServiceClient.SubmitTransactionAsync
/// </summary>
public class ValidatorServiceClientTests
{
    private readonly Mock<ILogger<ValidatorServiceClient>> _mockLogger;
    private readonly Mock<IServiceAuthClient> _mockServiceAuth;
    private readonly IConfiguration _configuration;

    public ValidatorServiceClientTests()
    {
        _mockLogger = new Mock<ILogger<ValidatorServiceClient>>();
        _mockServiceAuth = new Mock<IServiceAuthClient>();
        _mockServiceAuth.Setup(s => s.GetTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-token");
        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ServiceClients:ValidatorService:Address"] = "http://localhost:5004"
            })
            .Build();
    }

    private ValidatorServiceClient CreateClient(HttpClient httpClient)
    {
        return new ValidatorServiceClient(httpClient, _configuration, _mockServiceAuth.Object, _mockLogger.Object);
    }

    private static TransactionSubmission CreateTestRequest()
    {
        return new TransactionSubmission
        {
            TransactionId = "abc123def456abc123def456abc123def456abc123def456abc123def456abc12345",
            RegisterId = "register-1",
            BlueprintId = "blueprint-1",
            ActionId = "1",
            Payload = JsonSerializer.Deserialize<JsonElement>("""{"type":"action","data":"test"}"""),
            PayloadHash = "abc123def456abc123def456abc123def456abc123def456abc123def456abc12345",
            Signatures =
            [
                new SignatureInfo
                {
                    PublicKey = Convert.ToBase64String(new byte[32]),
                    SignatureValue = Convert.ToBase64String(new byte[64]),
                    Algorithm = "ED25519"
                }
            ],
            CreatedAt = DateTimeOffset.UtcNow,
            Metadata = new Dictionary<string, string>
            {
                ["instanceId"] = "instance-1",
                ["Type"] = "Action"
            }
        };
    }

    [Fact]
    public async Task SubmitTransactionAsync_Success200_ReturnsSuccessResult()
    {
        // Arrange
        var responseJson = JsonSerializer.Serialize(new
        {
            isValid = true,
            added = true,
            transactionId = "tx-123",
            registerId = "register-1",
            addedAt = DateTimeOffset.UtcNow
        });

        var handler = new FakeHttpHandler(HttpStatusCode.OK, responseJson);
        var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);
        var request = CreateTestRequest();

        // Act
        var result = await client.SubmitTransactionAsync(request);

        // Assert
        result.Success.Should().BeTrue();
        result.TransactionId.Should().Be("tx-123");
        result.RegisterId.Should().Be("register-1");
        result.AddedAt.Should().NotBeNull();
        result.ErrorMessage.Should().BeNull();
        result.ErrorCode.Should().BeNull();
    }

    [Fact]
    public async Task SubmitTransactionAsync_ValidationFailure400_ReturnsFailureWithValidationError()
    {
        // Arrange
        var responseJson = JsonSerializer.Serialize(new
        {
            isValid = false,
            errors = new[]
            {
                new { code = "INVALID_SIGNATURE", message = "Signature verification failed", field = "Signatures[0]" }
            }
        });

        var handler = new FakeHttpHandler(HttpStatusCode.BadRequest, responseJson);
        var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);
        var request = CreateTestRequest();

        // Act
        var result = await client.SubmitTransactionAsync(request);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        result.ErrorMessage.Should().Contain("INVALID_SIGNATURE");
    }

    [Fact]
    public async Task SubmitTransactionAsync_MempoolFull409_ReturnsFailureWithMempoolError()
    {
        // Arrange
        var responseJson = JsonSerializer.Serialize(new
        {
            isValid = true,
            added = false,
            message = "Failed to add transaction to memory pool (pool full or duplicate)"
        });

        var handler = new FakeHttpHandler(HttpStatusCode.Conflict, responseJson);
        var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);
        var request = CreateTestRequest();

        // Act
        var result = await client.SubmitTransactionAsync(request);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("MEMPOOL_FULL");
        result.ErrorMessage.Should().Contain("pool full or duplicate");
    }

    [Fact]
    public async Task SubmitTransactionAsync_HttpException_ReturnsFailureWithHttpError()
    {
        // Arrange
        var handler = new FakeHttpHandler(new HttpRequestException("Connection refused"));
        var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);
        var request = CreateTestRequest();

        // Act
        var result = await client.SubmitTransactionAsync(request);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("HTTP_ERROR");
        result.ErrorMessage.Should().Contain("Connection refused");
    }

    [Fact]
    public async Task SubmitTransactionAsync_UnexpectedStatusCode_ReturnsFailure()
    {
        // Arrange
        var handler = new FakeHttpHandler(HttpStatusCode.InternalServerError, """{"error":"unexpected"}""");
        var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);
        var request = CreateTestRequest();

        // Act
        var result = await client.SubmitTransactionAsync(request);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("HTTP_ERROR");
        result.ErrorMessage.Should().Contain("InternalServerError");
    }

    /// <summary>
    /// Fake HTTP message handler for testing
    /// </summary>
    private class FakeHttpHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode? _statusCode;
        private readonly string? _responseContent;
        private readonly Exception? _exception;

        public FakeHttpHandler(HttpStatusCode statusCode, string responseContent)
        {
            _statusCode = statusCode;
            _responseContent = responseContent;
        }

        public FakeHttpHandler(Exception exception)
        {
            _exception = exception;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_exception != null)
                throw _exception;

            var response = new HttpResponseMessage(_statusCode!.Value)
            {
                Content = new StringContent(_responseContent!, System.Text.Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }
}
