// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Text.Json;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

using Moq;
using Moq.Protected;

using Sorcha.ServiceClients.Auth;
using Sorcha.ServiceClients.Blueprint;
using Sorcha.ServiceClients.Blueprint.Models;

namespace Sorcha.ServiceClients.Tests.Blueprint;

/// <summary>
/// Feature 142 (T030) — coverage for the rehearsal service-client method implementations on
/// <see cref="BlueprintServiceClient"/>: success deserialization and the documented status-code
/// mappings (409/403/422 → null, 404 → null on get, 204/404 → true on reset).
/// </summary>
public class BlueprintServiceClientRehearsalTests
{
    private readonly Mock<IServiceAuthClient> _serviceAuthMock;
    private readonly Mock<ILogger<BlueprintServiceClient>> _loggerMock;
    private readonly IConfiguration _configuration;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public BlueprintServiceClientRehearsalTests()
    {
        _serviceAuthMock = new Mock<IServiceAuthClient>();
        _serviceAuthMock.Setup(a => a.GetTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-token");

        _loggerMock = new Mock<ILogger<BlueprintServiceClient>>();

        var configData = new Dictionary<string, string?>
        {
            ["ServiceClients:BlueprintService:Address"] = "http://localhost:5000"
        };
        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();
    }

    private BlueprintServiceClient CreateClient(Mock<HttpMessageHandler> handlerMock)
    {
        var httpClient = new HttpClient(handlerMock.Object);
        return new BlueprintServiceClient(httpClient, _serviceAuthMock.Object, _configuration, _loggerMock.Object);
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

    private static Rehearsal SampleRehearsal(Guid id) => new()
    {
        RehearsalId = id,
        BlueprintId = "bp-1",
        ExecDefHash = "hash-1",
        Mode = RehearsalMode.Full,
        SandboxRegisterId = "sandbox-1",
        CurrentActingRole = "applicant",
        Outcome = RehearsalOutcome.InProgress,
        Steps = [new RehearsalStep { ActionId = 1, ActingRole = "applicant", Status = RehearsalStepStatus.Current }],
        Log = []
    };

    [Fact]
    public async Task StartRehearsalAsync_Created_ReturnsRehearsal()
    {
        var id = Guid.NewGuid();
        var handler = CreateMockHandler(HttpStatusCode.Created, SampleRehearsal(id));
        var client = CreateClient(handler);

        var result = await client.StartRehearsalAsync("bp-1", new StartRehearsalRequest { Mode = RehearsalMode.Full });

        Assert.NotNull(result);
        Assert.Equal(id, result.RehearsalId);
        Assert.Equal(RehearsalMode.Full, result.Mode);
    }

    [Fact]
    public async Task StartRehearsalAsync_Conflict_ReturnsNull()
    {
        // 409 blocking validation — surfaces as null per the documented contract.
        var handler = CreateMockHandler(HttpStatusCode.Conflict, new { error = "blocking" });
        var client = CreateClient(handler);

        var result = await client.StartRehearsalAsync("bp-1", new StartRehearsalRequest());

        Assert.Null(result);
    }

    [Fact]
    public async Task StartRehearsalAsync_Forbidden_ReturnsNull()
    {
        var handler = CreateMockHandler(HttpStatusCode.Forbidden);
        var client = CreateClient(handler);

        var result = await client.StartRehearsalAsync("bp-1", new StartRehearsalRequest());

        Assert.Null(result);
    }

    [Fact]
    public async Task GetRehearsalAsync_Ok_ReturnsRehearsal()
    {
        var id = Guid.NewGuid();
        var handler = CreateMockHandler(HttpStatusCode.OK, SampleRehearsal(id));
        var client = CreateClient(handler);

        var result = await client.GetRehearsalAsync("bp-1", id);

        Assert.NotNull(result);
        Assert.Equal(id, result.RehearsalId);
    }

    [Fact]
    public async Task GetRehearsalAsync_NotFound_ReturnsNull()
    {
        var handler = CreateMockHandler(HttpStatusCode.NotFound);
        var client = CreateClient(handler);

        var result = await client.GetRehearsalAsync("bp-1", Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task ResetRehearsalAsync_NoContent_ReturnsTrue()
    {
        var handler = CreateMockHandler(HttpStatusCode.NoContent);
        var client = CreateClient(handler);

        var result = await client.ResetRehearsalAsync("bp-1", Guid.NewGuid());

        Assert.True(result);
    }

    [Fact]
    public async Task ResetRehearsalAsync_NotFound_ReturnsTrue()
    {
        // Idempotent: a 404 is tolerated as "already gone".
        var handler = CreateMockHandler(HttpStatusCode.NotFound);
        var client = CreateClient(handler);

        var result = await client.ResetRehearsalAsync("bp-1", Guid.NewGuid());

        Assert.True(result);
    }

    [Fact]
    public async Task ResetRehearsalAsync_ServerError_ReturnsFalse()
    {
        var handler = CreateMockHandler(HttpStatusCode.InternalServerError);
        var client = CreateClient(handler);

        var result = await client.ResetRehearsalAsync("bp-1", Guid.NewGuid());

        Assert.False(result);
    }

    [Fact]
    public async Task SwitchRehearsalRoleAsync_Ok_ReturnsRehearsal()
    {
        var id = Guid.NewGuid();
        var handler = CreateMockHandler(HttpStatusCode.OK, SampleRehearsal(id));
        var client = CreateClient(handler);

        var result = await client.SwitchRehearsalRoleAsync("bp-1", id, new SwitchRehearsalRoleRequest { Role = "approver" });

        Assert.NotNull(result);
        Assert.Equal(id, result.RehearsalId);
    }

    [Fact]
    public async Task SwitchRehearsalRoleAsync_UnprocessableEntity_ReturnsNull()
    {
        var handler = CreateMockHandler(HttpStatusCode.UnprocessableEntity, new { error = "bad role" });
        var client = CreateClient(handler);

        var result = await client.SwitchRehearsalRoleAsync("bp-1", Guid.NewGuid(), new SwitchRehearsalRoleRequest { Role = "ghost" });

        Assert.Null(result);
    }

    [Fact]
    public async Task SubmitRehearsalStepAsync_Ok_ReturnsRehearsal()
    {
        var id = Guid.NewGuid();
        var handler = CreateMockHandler(HttpStatusCode.OK, SampleRehearsal(id));
        var client = CreateClient(handler);

        var result = await client.SubmitRehearsalStepAsync(
            "bp-1", id, new SubmitRehearsalStepRequest { ActionId = 1, PayloadJson = """{"field":"value"}""" });

        Assert.NotNull(result);
        Assert.Equal(id, result.RehearsalId);
    }

    [Fact]
    public async Task SubmitRehearsalStepAsync_UnprocessableEntity_ReturnsNull()
    {
        // 422 payload/step error — surfaces as null per the documented contract.
        var handler = CreateMockHandler(HttpStatusCode.UnprocessableEntity, new { error = "not current step" });
        var client = CreateClient(handler);

        var result = await client.SubmitRehearsalStepAsync(
            "bp-1", Guid.NewGuid(), new SubmitRehearsalStepRequest { ActionId = 9, PayloadJson = "{}" });

        Assert.Null(result);
    }

    [Fact]
    public async Task SubmitRehearsalStepAsync_InvalidPayloadJson_ReturnsNull()
    {
        // The DTO carries payload as a raw JSON string; malformed JSON fails locally without a call.
        var handler = CreateMockHandler(HttpStatusCode.OK, SampleRehearsal(Guid.NewGuid()));
        var client = CreateClient(handler);

        var result = await client.SubmitRehearsalStepAsync(
            "bp-1", Guid.NewGuid(), new SubmitRehearsalStepRequest { ActionId = 1, PayloadJson = "{not-json" });

        Assert.Null(result);
    }
}
