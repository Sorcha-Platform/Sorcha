// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Text;
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
/// #1658 — <c>sorcha_action_submit</c> could never submit because this client posted the agent's data as
/// the whole body. The execute endpoint binds <c>ActionSubmissionRequest</c>, so the payload has to ride
/// inside <c>payloadData</c> beside the ids and sender wallet, and the endpoint also refuses a request
/// with no <c>X-Delegation-Token</c>. These tests look at the bytes and headers actually sent, which the
/// tool's own tests (mocking this client) never could.
/// </summary>
public class BlueprintServiceClientExecuteActionTests
{
    private readonly IConfiguration _configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ServiceClients:BlueprintService:Address"] = "http://localhost:5000",
        })
        .Build();

    private BlueprintServiceClient CreateClient(HttpStatusCode status, string body, List<HttpRequestMessage> sent, List<string> sentBodies)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Returns(async (HttpRequestMessage request, CancellationToken ct) =>
            {
                sent.Add(request);
                sentBodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(ct));
                return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            });

        var auth = new Mock<IServiceAuthClient>();
        auth.Setup(a => a.GetTokenAsync(It.IsAny<CancellationToken>())).ReturnsAsync("test-token");

        return new BlueprintServiceClient(
            new HttpClient(handler.Object), auth.Object, _configuration, Mock.Of<ILogger<BlueprintServiceClient>>());
    }

    private static ExecuteActionRequest Request() => new()
    {
        BlueprintId = "bp-1",
        ActionId = "2",
        InstanceId = "inst-1",
        SenderWallet = "ws1qmine",
        RegisterAddress = "reg-1",
        PayloadData = JsonDocument.Parse("""{"amount":42,"notes":"ok"}""").RootElement,
    };

    [Fact]
    public async Task ExecuteActionAsync_PostsTheSubmissionRequestShape_NotTheBarePayload()
    {
        var sent = new List<HttpRequestMessage>();
        var bodies = new List<string>();
        var client = CreateClient(HttpStatusCode.OK, """{"transactionId":"tx-1"}""", sent, bodies);

        await client.ExecuteActionAsync("inst-1", "2", Request());

        sent.Should().ContainSingle();
        sent[0].Method.Should().Be(HttpMethod.Post);
        sent[0].RequestUri!.ToString().Should().EndWith("api/instances/inst-1/actions/2/execute");

        using var doc = JsonDocument.Parse(bodies[0]);
        var root = doc.RootElement;
        root.GetProperty("blueprintId").GetString().Should().Be("bp-1");
        root.GetProperty("actionId").GetString().Should().Be("2");
        root.GetProperty("instanceId").GetString().Should().Be("inst-1");
        root.GetProperty("senderWallet").GetString().Should().Be("ws1qmine");
        root.GetProperty("registerAddress").GetString().Should().Be("reg-1");
        root.GetProperty("payloadData").GetProperty("amount").GetInt32().Should().Be(42);
        root.TryGetProperty("amount", out _).Should().BeFalse("the payload must not be spread over the request body");
    }

    [Fact]
    public async Task ExecuteActionAsync_SendsTheDelegationHeaderTheEndpointRequires()
    {
        var sent = new List<HttpRequestMessage>();
        var client = CreateClient(HttpStatusCode.OK, "{}", sent, []);

        await client.ExecuteActionAsync("inst-1", "2", Request());

        sent[0].Headers.TryGetValues("X-Delegation-Token", out var values).Should().BeTrue(
            "the execute endpoint returns 400 when the header is absent");
        values!.Single().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ExecuteActionAsync_OnRefusal_ReturnsStatusAndTheServersBody()
    {
        const string problem = """{"error":"Action 2 is not a current action for instance inst-1"}""";
        var client = CreateClient(HttpStatusCode.BadRequest, problem, [], []);

        var (status, body) = await client.ExecuteActionAsync("inst-1", "2", Request());

        status.Should().Be(HttpStatusCode.BadRequest);
        body.Should().Be(problem, "collapsing a refusal to null is how the agent was left with 'Action submission failed.'");
    }

    [Fact]
    public async Task GetActionForSubmissionAsync_OnForbidden_ReturnsStatusAndBody()
    {
        const string problem = """{"detail":"You are not a participant on this instance."}""";
        var sent = new List<HttpRequestMessage>();
        var client = CreateClient(HttpStatusCode.Forbidden, problem, sent, []);

        var (status, body) = await client.GetActionForSubmissionAsync("inst-1", "2");

        sent[0].Method.Should().Be(HttpMethod.Get);
        sent[0].RequestUri!.ToString().Should().EndWith("api/instances/inst-1/actions/2");
        status.Should().Be(HttpStatusCode.Forbidden);
        body.Should().Be(problem);
    }
}
