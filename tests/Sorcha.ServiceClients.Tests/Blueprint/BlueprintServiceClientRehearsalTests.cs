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
/// mappings (409/403/422/404 → a refusal carrying the server's reason, #1691; 204/404 → true on reset).
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

        Assert.Null(result.Refusal);
        Assert.NotNull(result.Rehearsal);
        Assert.Equal(id, result.Rehearsal.RehearsalId);
        Assert.Equal(RehearsalMode.Full, result.Rehearsal.Mode);
    }

    [Fact]
    public async Task StartRehearsalAsync_Conflict_CarriesTheBlockingValidationErrors()
    {
        // #1691. Was StartRehearsalAsync_Conflict_ReturnsNull: the 409 names exactly which
        // validation errors block the rehearsal, and a bare null threw that away.
        var handler = CreateMockHandler(
            HttpStatusCode.Conflict,
            new { error = "Blueprint has blocking validation errors.", errors = new[] { "Action 2 has no sender", "No starting action" } });
        var client = CreateClient(handler);

        var result = await client.StartRehearsalAsync("bp-1", new StartRehearsalRequest());

        Assert.Null(result.Rehearsal);
        Assert.Equal(409, result.Refusal!.StatusCode);
        Assert.Equal("Blueprint has blocking validation errors.", result.Refusal.Reason);
        Assert.Equal(["Action 2 has no sender", "No starting action"], result.Refusal.Errors);
    }

    [Fact]
    public async Task StartRehearsalAsync_Forbidden_ReportsTheStatus()
    {
        var handler = CreateMockHandler(HttpStatusCode.Forbidden);
        var client = CreateClient(handler);

        var result = await client.StartRehearsalAsync("bp-1", new StartRehearsalRequest());

        Assert.Null(result.Rehearsal);
        Assert.Equal(403, result.Refusal!.StatusCode);
        Assert.Null(result.Refusal.Reason);
        Assert.Empty(result.Refusal.Errors);
    }

    [Fact]
    public async Task GetRehearsalAsync_Ok_ReturnsRehearsal()
    {
        var id = Guid.NewGuid();
        var handler = CreateMockHandler(HttpStatusCode.OK, SampleRehearsal(id));
        var client = CreateClient(handler);

        var result = await client.GetRehearsalAsync("bp-1", id);

        Assert.NotNull(result.Rehearsal);
        Assert.Equal(id, result.Rehearsal.RehearsalId);
    }

    [Fact]
    public async Task GetRehearsalAsync_NotFound_ReportsTheStatus()
    {
        var handler = CreateMockHandler(HttpStatusCode.NotFound);
        var client = CreateClient(handler);

        var result = await client.GetRehearsalAsync("bp-1", Guid.NewGuid());

        Assert.Null(result.Rehearsal);
        Assert.Equal(404, result.Refusal!.StatusCode);
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

        Assert.NotNull(result.Rehearsal);
        Assert.Equal(id, result.Rehearsal.RehearsalId);
    }

    [Fact]
    public async Task SwitchRehearsalRoleAsync_UnprocessableEntity_CarriesTheReason()
    {
        var handler = CreateMockHandler(HttpStatusCode.UnprocessableEntity, new { error = "Role 'ghost' is not a participant in this rehearsal." });
        var client = CreateClient(handler);

        var result = await client.SwitchRehearsalRoleAsync("bp-1", Guid.NewGuid(), new SwitchRehearsalRoleRequest { Role = "ghost" });

        Assert.Null(result.Rehearsal);
        Assert.Equal(422, result.Refusal!.StatusCode);
        Assert.Equal("Role 'ghost' is not a participant in this rehearsal.", result.Refusal.Reason);
    }

    [Fact]
    public async Task SubmitRehearsalStepAsync_Ok_ReturnsRehearsal()
    {
        var id = Guid.NewGuid();
        var handler = CreateMockHandler(HttpStatusCode.OK, SampleRehearsal(id));
        var client = CreateClient(handler);

        var result = await client.SubmitRehearsalStepAsync(
            "bp-1", id, new SubmitRehearsalStepRequest { ActionId = 1, PayloadJson = """{"field":"value"}""" });

        Assert.NotNull(result.Rehearsal);
        Assert.Equal(id, result.Rehearsal.RehearsalId);
    }

    [Fact]
    public async Task SubmitRehearsalStepAsync_SendsThePayloadAsAJsonObject_NotAString()
    {
        // The server binds `payload` as a JsonElement and keeps only an OBJECT's properties — a JSON
        // string root silently becomes an empty payload. The DTO holds the payload as a raw string
        // under the same wire name, so posting the DTO directly would send exactly that string.
        // Pin the bytes that actually leave the client.
        string? sentBody = null;
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>(async (request, ct) =>
            {
                sentBody = await request.Content!.ReadAsStringAsync(ct);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(SampleRehearsal(Guid.NewGuid()), JsonOptions),
                        System.Text.Encoding.UTF8,
                        "application/json")
                };
            });
        var client = CreateClient(handler);

        await client.SubmitRehearsalStepAsync(
            "bp-1", Guid.NewGuid(), new SubmitRehearsalStepRequest { ActionId = 2, PayloadJson = """{"decision":"approved"}""" });

        Assert.NotNull(sentBody);
        using var document = JsonDocument.Parse(sentBody!);
        Assert.Equal(2, document.RootElement.GetProperty("actionId").GetInt32());
        var payload = document.RootElement.GetProperty("payload");
        Assert.Equal(JsonValueKind.Object, payload.ValueKind);
        Assert.Equal("approved", payload.GetProperty("decision").GetString());
    }

    [Fact]
    public async Task SubmitRehearsalStepAsync_UnprocessableEntity_CarriesTheReason()
    {
        // Was SubmitRehearsalStepAsync_UnprocessableEntity_ReturnsNull (#1691).
        var handler = CreateMockHandler(HttpStatusCode.UnprocessableEntity, new { error = "Action 9 is not the current rehearsal step." });
        var client = CreateClient(handler);

        var result = await client.SubmitRehearsalStepAsync(
            "bp-1", Guid.NewGuid(), new SubmitRehearsalStepRequest { ActionId = 9, PayloadJson = "{}" });

        Assert.Null(result.Rehearsal);
        Assert.Equal(422, result.Refusal!.StatusCode);
        Assert.Equal("Action 9 is not the current rehearsal step.", result.Refusal.Reason);
    }

    [Fact]
    public async Task SubmitRehearsalStepAsync_InvalidPayloadJson_IsRefusedWithoutACall()
    {
        // The DTO carries payload as a raw JSON string; malformed JSON fails locally without a call.
        var handler = CreateMockHandler(HttpStatusCode.OK, SampleRehearsal(Guid.NewGuid()));
        var client = CreateClient(handler);

        var result = await client.SubmitRehearsalStepAsync(
            "bp-1", Guid.NewGuid(), new SubmitRehearsalStepRequest { ActionId = 1, PayloadJson = "{not-json" });

        Assert.Null(result.Rehearsal);
        Assert.Equal(400, result.Refusal!.StatusCode);
        Assert.Contains("not valid JSON", result.Refusal.Reason);
        handler.Protected().Verify("SendAsync", Times.Never(), ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
    }

    [Theory]
    [InlineData("[1,2]", "array")]
    [InlineData("\"{}\"", "string")]
    [InlineData("42", "number")]
    public async Task SubmitRehearsalStepAsync_NonObjectPayload_IsRefusedWithoutACall(string payloadJson, string kind)
    {
        // A non-object would reach the server as a payload it silently empties — refuse it here,
        // where the reason can still be said.
        var handler = CreateMockHandler(HttpStatusCode.OK, SampleRehearsal(Guid.NewGuid()));
        var client = CreateClient(handler);

        var result = await client.SubmitRehearsalStepAsync(
            "bp-1", Guid.NewGuid(), new SubmitRehearsalStepRequest { ActionId = 1, PayloadJson = payloadJson });

        Assert.Null(result.Rehearsal);
        Assert.Equal(400, result.Refusal!.StatusCode);
        Assert.Contains($"not a JSON {kind}", result.Refusal.Reason);
        handler.Protected().Verify("SendAsync", Times.Never(), ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
    }

    // Bucket B: PublishBlueprintAsync was a NotImplementedException stub; now it calls the endpoint.

    [Fact]
    public async Task PublishBlueprintAsync_Ok_ReturnsResult()
    {
        var body = new
        {
            blueprintId = "bp-1",
            version = 3,
            registerId = "reg-1",
            publishedAt = DateTimeOffset.UtcNow,
            overridden = true
        };
        var handler = CreateMockHandler(HttpStatusCode.OK, body);
        var client = CreateClient(handler);

        var result = await client.PublishBlueprintAsync("bp-1", new PublishBlueprintRequest { RegisterId = "reg-1" });

        Assert.NotNull(result);
        Assert.NotNull(result.Result);
        Assert.False(result.IsRehearsalRequired);
        Assert.Equal(3, result.Result!.Version);
        Assert.True(result.Result.Overridden);
    }

    [Fact]
    public async Task PublishBlueprintAsync_Conflict_ReturnsRehearsalRequired()
    {
        var body = new { code = "REHEARSAL_REQUIRED", execDefHash = "abc123", message = "Rehearse first." };
        var handler = CreateMockHandler(HttpStatusCode.Conflict, body);
        var client = CreateClient(handler);

        var result = await client.PublishBlueprintAsync("bp-1", new PublishBlueprintRequest { RegisterId = "reg-1" });

        Assert.NotNull(result);
        Assert.True(result.IsRehearsalRequired);
        Assert.Equal("abc123", result.RehearsalRequired!.ExecDefHash);
    }

    [Fact]
    public async Task PublishBlueprintAsync_Forbidden_ReportsTheRefusalRatherThanNull()
    {
        // Was PublishBlueprintAsync_Forbidden_ReturnsNull, asserting the behaviour #1641 fixed:
        // collapsing the 403 governance-hard-gate refusal into a bare null threw away the server's
        // explanation, leaving the caller to list what the failure might have been. The outcome type
        // now carries a refusal arm, and a body-less 403 still reports its status.
        var handler = CreateMockHandler(HttpStatusCode.Forbidden);
        var client = CreateClient(handler);

        var result = await client.PublishBlueprintAsync("bp-1", new PublishBlueprintRequest { RegisterId = "reg-1" });

        Assert.NotNull(result);
        Assert.Equal(403, result.Refusal!.StatusCode);
        Assert.False(result.IsRehearsalRequired);
        Assert.Null(result.Result);
    }
}
