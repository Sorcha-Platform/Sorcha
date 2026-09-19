// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Text;

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

using Moq;
using Moq.Protected;

using Sorcha.ServiceClients.Auth;
using Sorcha.ServiceClients.Blueprint;
using Sorcha.ServiceClients.Blueprint.Models;

using Xunit;

namespace Sorcha.ServiceClients.Tests.Blueprint;

/// <summary>
/// #1641 — a publish refusal must carry the server's own reason to the caller.
/// </summary>
/// <remarks>
/// <para>
/// The client used to log the status and return <c>null</c> for every non-success response, so the
/// only thing a caller could say was that publishing "failed". Cold-start run #5 hit exactly that:
/// #1659 had just taught the Blueprint Service to answer 503 with "register … has no sealed
/// governance roster", and the agent still saw nothing, then spent six tool calls rediscovering it.
/// One fix silently cancelled the other out.
/// </para>
/// <para>
/// A rehearsal-required 409 is NOT a refusal and keeps its own arm — publish clients read any 409 as
/// REHEARSAL_REQUIRED, which is why #1659 answers 503 rather than 409 in the first place.
/// </para>
/// </remarks>
public class BlueprintServiceClientPublishRefusalTests
{
    private const string RosterBody =
        """{"code":"ROSTER_UNAVAILABLE","message":"register 4145b4e7 has no sealed governance roster"}""";

    [Fact]
    public async Task PublishBlueprintAsync_WhenRefused_CarriesTheServersReasonAndStatus()
    {
        var client = BuildClient(HttpStatusCode.ServiceUnavailable, RosterBody);

        var outcome = await client.PublishBlueprintAsync("bp-1", new PublishBlueprintRequest { RegisterId = "reg-1" });

        outcome.Should().NotBeNull("a refusal the server explained is not the same as no answer at all");
        outcome!.Refusal.Should().NotBeNull();
        outcome.Refusal!.StatusCode.Should().Be(503);
        outcome.Refusal.Code.Should().Be("ROSTER_UNAVAILABLE");
        outcome.Refusal.Reason.Should().Be("register 4145b4e7 has no sealed governance roster");
        outcome.Result.Should().BeNull();
        outcome.IsRehearsalRequired.Should().BeFalse();
    }

    [Fact]
    public async Task PublishBlueprintAsync_ReadsProblemJsonDetail()
    {
        // Sorcha's sanitized handler emits RFC 7807, so "detail" is the reason on any unhandled path.
        var client = BuildClient(
            HttpStatusCode.Forbidden,
            """{"type":"about:blank","title":"Forbidden","detail":"you hold no publish-governance role","traceId":"t-1"}""");

        var outcome = await client.PublishBlueprintAsync("bp-1", new PublishBlueprintRequest { RegisterId = "reg-1" });

        outcome!.Refusal!.Reason.Should().Be("you hold no publish-governance role");
    }

    [Fact]
    public async Task PublishBlueprintAsync_WhenTheBodyExplainsNothing_ReportsNoReasonRatherThanRawJson()
    {
        // Pasting undigested JSON into a message looks like an explanation without being one, and
        // makes a "contains the reason" assertion pass for the wrong reason.
        var client = BuildClient(HttpStatusCode.BadRequest, """{"unexpected":{"nested":true}}""");

        var outcome = await client.PublishBlueprintAsync("bp-1", new PublishBlueprintRequest { RegisterId = "reg-1" });

        outcome!.Refusal!.StatusCode.Should().Be(400);
        outcome.Refusal.Reason.Should().BeNull();
    }

    [Fact]
    public async Task PublishBlueprintAsync_RehearsalRequired_StaysItsOwnOutcome_NotARefusal()
    {
        var client = BuildClient(
            HttpStatusCode.Conflict,
            """{"code":"REHEARSAL_REQUIRED","message":"this version has not been rehearsed"}""");

        var outcome = await client.PublishBlueprintAsync("bp-1", new PublishBlueprintRequest { RegisterId = "reg-1" });

        outcome!.IsRehearsalRequired.Should().BeTrue();
        outcome.Refusal.Should().BeNull("a 409 is the soft gate, and the tool must still ask a person");
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData("", null, null)]
    [InlineData("service unavailable", null, "service unavailable")]
    [InlineData("""{"error":"nope"}""", null, "nope")]
    [InlineData("""{"code":"X","title":"T"}""", "X", "T")]
    [InlineData("not json {", null, "not json {")]
    public void ReadRefusal_ReadsTheShapesTheServiceActuallySends(string? body, string? code, string? reason)
    {
        var (readCode, readReason) = BlueprintServiceClient.ReadRefusal(body);

        readCode.Should().Be(code);
        readReason.Should().Be(reason);
    }

    private static BlueprintServiceClient BuildClient(HttpStatusCode status, string body)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });

        var http = new HttpClient(handler.Object) { BaseAddress = new Uri("http://localhost:5000") };
        var auth = new Mock<IServiceAuthClient>();
        auth.Setup(a => a.GetTokenAsync(It.IsAny<CancellationToken>())).ReturnsAsync("test-token");
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ServiceClients:BlueprintService:Address"] = "http://localhost:5000",
            })
            .Build();

        return new BlueprintServiceClient(http, auth.Object, config, Mock.Of<ILogger<BlueprintServiceClient>>());
    }
}
