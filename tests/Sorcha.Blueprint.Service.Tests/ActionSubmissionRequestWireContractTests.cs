// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Sorcha.Blueprint.Service.Models.Requests;
using Sorcha.ServiceClients.Auth;
using Sorcha.ServiceClients.Blueprint;
using Sorcha.ServiceClients.Blueprint.Models;
using Xunit;

namespace Sorcha.Blueprint.Service.Tests;

/// <summary>
/// #1658 — the join between the shared client and the execute endpoint. The MCP tool's tests mocked the
/// client and so never saw a body; the endpoint's tests built an <see cref="ActionSubmissionRequest"/> in
/// memory and so never saw one either. Both sides were individually fine and every real submission was a
/// 400. This takes the bytes the real client writes and puts them through what the endpoint does: bind
/// under the web defaults, then the request validation filter.
/// </summary>
public sealed class ActionSubmissionRequestWireContractTests
{
    private static async Task<string> BytesTheClientSendsAsync(ExecuteActionRequest request)
    {
        string? captured = null;
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Returns(async (HttpRequestMessage message, CancellationToken ct) =>
            {
                captured = await message.Content!.ReadAsStringAsync(ct);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
            });

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ServiceClients:BlueprintService:Address"] = "http://localhost:5000" })
            .Build();
        var client = new BlueprintServiceClient(
            new HttpClient(handler.Object), Mock.Of<IServiceAuthClient>(), configuration, Mock.Of<ILogger<BlueprintServiceClient>>());

        await client.ExecuteActionAsync(request.InstanceId!, request.ActionId, request);
        return captured!;
    }

    private static async Task<bool> PassesEndpointValidationAsync(ActionSubmissionRequest bound)
    {
        var filter = new RequestValidationFilter();
        var context = EndpointFilterInvocationContext.Create(new DefaultHttpContext(), bound);
        var nextCalled = false;
        await filter.InvokeAsync(context, _ =>
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>(Results.Ok());
        });
        return nextCalled;
    }

    [Fact]
    public async Task ClientBytes_BindIntoTheServerRequest_AndPassItsValidation()
    {
        var sent = new ExecuteActionRequest
        {
            BlueprintId = "bp-1",
            ActionId = "2",
            InstanceId = "inst-1",
            SenderWallet = "ws11qpws9aazexample",
            RegisterAddress = "2737dbe4dbb347819055e83faf81c239",
            PayloadData = JsonDocument.Parse("""{"decision":"approved","amount":1200}""").RootElement,
        };

        var bytes = await BytesTheClientSendsAsync(sent);
        var bound = JsonSerializer.Deserialize<ActionSubmissionRequest>(bytes, JsonSerializerOptions.Web);

        bound.Should().NotBeNull();
        bound!.BlueprintId.Should().Be("bp-1");
        bound.ActionId.Should().Be("2");
        bound.InstanceId.Should().Be("inst-1");
        bound.SenderWallet.Should().Be("ws11qpws9aazexample");
        bound.RegisterAddress.Should().Be("2737dbe4dbb347819055e83faf81c239");
        bound.PayloadData.Should().ContainKeys("decision", "amount");
        (await PassesEndpointValidationAsync(bound)).Should().BeTrue();
    }

    [Fact]
    public void BareActionData_TheShapeTheToolUsedToSend_DoesNotBind()
    {
        // The counterfactual: this is what the tool posted before #1658. It must fail, or the test above
        // proves nothing about the shape.
        var act = () => JsonSerializer.Deserialize<ActionSubmissionRequest>(
            """{"decision":"approved","amount":1200}""", JsonSerializerOptions.Web);

        act.Should().Throw<JsonException>("the request's required members are absent");
    }
}
