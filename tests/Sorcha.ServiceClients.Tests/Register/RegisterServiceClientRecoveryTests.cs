// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Sorcha.ServiceClients.Auth;
using Sorcha.ServiceClients.Register;

namespace Sorcha.ServiceClients.Tests.Register;

/// <summary>
/// Tests for RegisterServiceClient recovery operations:
/// GetInternalRegistersAsync and GetPublishedBlueprintsAsync.
/// </summary>
public class RegisterServiceClientRecoveryTests
{
    private readonly Mock<IServiceAuthClient> _serviceAuthMock;
    private readonly Mock<ILogger<RegisterServiceClient>> _loggerMock;
    private readonly IConfiguration _configuration;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public RegisterServiceClientRecoveryTests()
    {
        _serviceAuthMock = new Mock<IServiceAuthClient>();
        _serviceAuthMock.Setup(a => a.GetTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-token");

        _loggerMock = new Mock<ILogger<RegisterServiceClient>>();

        var configData = new Dictionary<string, string?>
        {
            ["ServiceClients:RegisterService:Address"] = "http://localhost:5290"
        };
        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();
    }

    private RegisterServiceClient CreateClient(Mock<HttpMessageHandler> handlerMock)
    {
        var httpClient = new HttpClient(handlerMock.Object);
        return new RegisterServiceClient(httpClient, _serviceAuthMock.Object, _configuration, _loggerMock.Object);
    }

    private static Mock<HttpMessageHandler> CreateMockHandler(HttpStatusCode statusCode, object? responseBody = null)
    {
        var handlerMock = new Mock<HttpMessageHandler>();

        var response = new HttpResponseMessage(statusCode);
        if (responseBody != null)
        {
            response.Content = new StringContent(
                JsonSerializer.Serialize(responseBody, JsonOptions),
                System.Text.Encoding.UTF8,
                "application/json");
        }

        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        return handlerMock;
    }

    private static Mock<HttpMessageHandler> CreateMockHandlerWithCallback(
        Func<HttpRequestMessage, HttpResponseMessage> callback)
    {
        var handlerMock = new Mock<HttpMessageHandler>();

        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage req, CancellationToken _) => callback(req));

        return handlerMock;
    }

    #region GetInternalRegistersAsync

    [Fact]
    public async Task GetInternalRegistersAsync_Success_ReturnsRegisterList()
    {
        var registers = new[]
        {
            new { id = "reg-1", name = "Register 1", height = 5, status = "Active" },
            new { id = "reg-2", name = "Register 2", height = 12, status = "Active" }
        };
        var handler = CreateMockHandler(HttpStatusCode.OK, registers);
        var client = CreateClient(handler);

        var result = await client.GetInternalRegistersAsync();

        result.Should().HaveCount(2);
        result[0].Id.Should().Be("reg-1");
        result[0].Name.Should().Be("Register 1");
        result[1].Id.Should().Be("reg-2");
    }

    [Fact]
    public async Task GetInternalRegistersAsync_ServerError_ReturnsEmptyList()
    {
        var handler = CreateMockHandler(HttpStatusCode.InternalServerError);
        var client = CreateClient(handler);

        var result = await client.GetInternalRegistersAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetInternalRegistersAsync_HttpException_ReturnsEmptyList()
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var client = CreateClient(handlerMock);

        var result = await client.GetInternalRegistersAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetInternalRegistersAsync_NoRegisters_ReturnsEmptyList()
    {
        var handler = CreateMockHandler(HttpStatusCode.OK, Array.Empty<object>());
        var client = CreateClient(handler);

        var result = await client.GetInternalRegistersAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetInternalRegistersAsync_CallsCorrectEndpoint()
    {
        var handler = CreateMockHandlerWithCallback(req =>
        {
            req.RequestUri!.PathAndQuery.Should().Be("/api/internal/registers");
            req.Headers.Authorization.Should().BeNull("internal endpoint is AllowAnonymous");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", System.Text.Encoding.UTF8, "application/json")
            };
        });

        var client = CreateClient(handler);
        await client.GetInternalRegistersAsync();

        handler.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    #endregion

    #region GetPublishedBlueprintsAsync

    [Fact]
    public async Task GetPublishedBlueprintsAsync_Success_ReturnsResponse()
    {
        var response = new
        {
            registerId = "reg-1",
            blueprints = new[]
            {
                new
                {
                    blueprintId = "bp-1",
                    transactionId = "tx-1",
                    publishedBy = "user-1",
                    publishedAt = DateTimeOffset.UtcNow,
                    blueprintJson = "{\"title\":\"Test\"}"
                }
            },
            registerHeight = 10,
            queriedAt = DateTimeOffset.UtcNow
        };
        var handler = CreateMockHandler(HttpStatusCode.OK, response);
        var client = CreateClient(handler);

        var result = await client.GetPublishedBlueprintsAsync("reg-1");

        result.Should().NotBeNull();
        result!.RegisterId.Should().Be("reg-1");
        result.Blueprints.Should().HaveCount(1);
        result.Blueprints[0].BlueprintId.Should().Be("bp-1");
        result.RegisterHeight.Should().Be(10);
    }

    [Fact]
    public async Task GetPublishedBlueprintsAsync_NotFound_ReturnsNull()
    {
        var handler = CreateMockHandler(HttpStatusCode.NotFound);
        var client = CreateClient(handler);

        var result = await client.GetPublishedBlueprintsAsync("missing-reg");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetPublishedBlueprintsAsync_ServerError_ReturnsNull()
    {
        var handler = CreateMockHandler(HttpStatusCode.InternalServerError);
        var client = CreateClient(handler);

        var result = await client.GetPublishedBlueprintsAsync("reg-1");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetPublishedBlueprintsAsync_HttpException_ReturnsNull()
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Timeout"));

        var client = CreateClient(handlerMock);

        var result = await client.GetPublishedBlueprintsAsync("reg-1");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetPublishedBlueprintsAsync_EscapesRegisterId()
    {
        var handler = CreateMockHandlerWithCallback(req =>
        {
            req.RequestUri!.PathAndQuery.Should().Contain("reg%20with%20spaces");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new { registerId = "reg with spaces", blueprints = Array.Empty<object>(), registerHeight = 0, queriedAt = DateTimeOffset.UtcNow }, JsonOptions),
                    System.Text.Encoding.UTF8,
                    "application/json")
            };
        });

        var client = CreateClient(handler);
        var result = await client.GetPublishedBlueprintsAsync("reg with spaces");

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task GetPublishedBlueprintsAsync_SetsAuthHeader()
    {
        var handler = CreateMockHandlerWithCallback(req =>
        {
            req.Headers.Authorization.Should().NotBeNull("published blueprints requires auth");
            req.Headers.Authorization!.Scheme.Should().Be("Bearer");
            req.Headers.Authorization.Parameter.Should().Be("test-token");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new { registerId = "reg-1", blueprints = Array.Empty<object>(), registerHeight = 0, queriedAt = DateTimeOffset.UtcNow }, JsonOptions),
                    System.Text.Encoding.UTF8,
                    "application/json")
            };
        });

        var client = CreateClient(handler);
        await client.GetPublishedBlueprintsAsync("reg-1");

        handler.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    #endregion
}
