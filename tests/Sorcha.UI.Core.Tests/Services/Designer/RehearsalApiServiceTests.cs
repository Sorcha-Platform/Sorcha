// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using Sorcha.ServiceClients.Blueprint.Models;
using Sorcha.UI.Core.Services.Designer;
using Xunit;

namespace Sorcha.UI.Core.Tests.Services.Designer;

/// <summary>
/// #1724 — <see cref="RehearsalApiService.SubmitStepAsync"/> must send <c>payload</c> as a JSON
/// OBJECT (mirroring <c>BlueprintServiceClient.SubmitRehearsalStepAsync</c>, #1691/#1723), and must
/// surface a non-success response's <c>{ "error": ... }</c> body as a refusal reason instead of
/// misreading it as a blank <see cref="Rehearsal"/>.
/// </summary>
public sealed class RehearsalApiServiceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private static (RehearsalApiService Service, Mock<HttpMessageHandler> Handler) Build()
    {
        var handler = new Mock<HttpMessageHandler>();
        var httpClient = new HttpClient(handler.Object) { BaseAddress = new Uri("http://localhost") };
        var service = new RehearsalApiService(httpClient, NullLogger<RehearsalApiService>.Instance);
        return (service, handler);
    }

    private static Rehearsal SampleRehearsal(Guid rehearsalId) => new()
    {
        RehearsalId = rehearsalId,
        BlueprintId = "bp-1",
        ExecDefHash = "hash-1",
        Mode = RehearsalMode.Full,
        SandboxRegisterId = "reg-1",
        CurrentActingRole = "applicant",
        Outcome = RehearsalOutcome.InProgress,
        Steps = [],
        Log = []
    };

    [Fact]
    public async Task SubmitStepAsync_SendsThePayloadAsAJsonObject_NotAString()
    {
        // The server binds `payload` as a JsonElement and keeps only an OBJECT's properties — a
        // JSON string root silently becomes an empty payload. SubmitRehearsalStepRequest holds the
        // payload as a raw string under the same wire name, so posting the DTO directly would send
        // exactly that string. Pin the bytes that actually leave the client.
        string? sentBody = null;
        var (service, handler) = Build();
        var rehearsalId = Guid.NewGuid();

        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>(async (request, ct) =>
            {
                sentBody = await request.Content!.ReadAsStringAsync(ct);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(SampleRehearsal(rehearsalId), JsonOptions),
                        System.Text.Encoding.UTF8, "application/json")
                };
            });

        await service.SubmitStepAsync("bp-1", rehearsalId, 2, """{"decision":"approved"}""");

        sentBody.Should().NotBeNull();
        using var document = JsonDocument.Parse(sentBody!);
        document.RootElement.GetProperty("actionId").GetInt32().Should().Be(2);
        var payload = document.RootElement.GetProperty("payload");
        payload.ValueKind.Should().Be(JsonValueKind.Object);
        payload.GetProperty("decision").GetString().Should().Be("approved");
    }

    [Fact]
    public async Task SubmitStepAsync_Success_ReturnsTheAppliedRehearsal()
    {
        var (service, handler) = Build();
        var rehearsalId = Guid.NewGuid();

        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(SampleRehearsal(rehearsalId), JsonOptions),
                    System.Text.Encoding.UTF8, "application/json")
            });

        var outcome = await service.SubmitStepAsync("bp-1", rehearsalId, 1, "{}");

        outcome.Rehearsal.Should().NotBeNull();
        outcome.Rehearsal!.RehearsalId.Should().Be(rehearsalId);
        outcome.RefusalReason.Should().BeNull();
    }

    [Fact]
    public async Task SubmitStepAsync_UnprocessableEntity_ReturnsTheServersErrorAsARefusal_NotABlankRehearsal()
    {
        // The server's 422 body is `{ "error": "..." }` — never a Rehearsal. Reading it as one used
        // to silently succeed (unknown/missing properties don't throw) and hand back a BLANK
        // rehearsal that wiped the caller's walk-through state (#1724).
        var (service, handler) = Build();
        var rehearsalId = Guid.NewGuid();

        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new { error = "Action 9 is not the current rehearsal step." }),
                    System.Text.Encoding.UTF8, "application/json")
            });

        var outcome = await service.SubmitStepAsync("bp-1", rehearsalId, 9, "{}");

        outcome.Rehearsal.Should().BeNull();
        outcome.RefusalReason.Should().Be("Action 9 is not the current rehearsal step.");
    }

    [Fact]
    public async Task SubmitStepAsync_NonObjectPayload_IsRefusedWithoutCallingTheServer()
    {
        var (service, handler) = Build();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Throws(new InvalidOperationException("must not be called"));

        var outcome = await service.SubmitStepAsync("bp-1", Guid.NewGuid(), 1, "\"just a string\"");

        outcome.Rehearsal.Should().BeNull();
        outcome.RefusalReason.Should().Contain("JSON object");
    }

    [Fact]
    public async Task SubmitStepAsync_TransportFailure_ReturnsARefusalRatherThanThrowing()
    {
        var (service, handler) = Build();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("network down"));

        var outcome = await service.SubmitStepAsync("bp-1", Guid.NewGuid(), 1, "{}");

        outcome.Rehearsal.Should().BeNull();
        outcome.RefusalReason.Should().NotBeNullOrEmpty();
    }
}
