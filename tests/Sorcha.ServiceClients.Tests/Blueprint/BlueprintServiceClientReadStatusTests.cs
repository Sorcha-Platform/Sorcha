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

using Xunit;

namespace Sorcha.ServiceClients.Tests.Blueprint;

/// <summary>
/// Cold-start run #5 — a Blueprint read must report WHICH failure occurred.
/// </summary>
/// <remarks>
/// <para>
/// Every non-success used to collapse to null, so the MCP tools could only say "not found". The
/// counterparty organisation was refused an instance it is a participant on (403) and told
/// "Workflow not found", which sent it hypothesising about a missing instance for the rest of the
/// run.
/// </para>
/// <para>
/// The tool-level tests mock this client, so they cannot see whether the real status survives the
/// round trip. That is exactly the seam these tests cover — and a mutation that hard-coded the
/// status survived until they existed.
/// </para>
/// </remarks>
public class BlueprintServiceClientReadStatusTests
{
    private const string InstanceId = "8808a450-5b4d-4e79-b51e-6e0a3b753984";

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task GetWorkflowStatusAsync_CarriesTheRealStatus(HttpStatusCode status)
    {
        var client = BuildClient(status, "");

        var read = await client.GetWorkflowStatusAsync(InstanceId);

        read.Status.Should().Be(status);
        read.IsSuccess.Should().BeFalse();
        read.Body.Should().BeNull();
    }

    [Fact]
    public async Task GetWorkflowStatusAsync_DistinguishesForbiddenFromNotFound()
    {
        // The distinction the whole fix rests on: these demand opposite responses.
        var forbidden = await BuildClient(HttpStatusCode.Forbidden, "").GetWorkflowStatusAsync(InstanceId);
        var missing = await BuildClient(HttpStatusCode.NotFound, "").GetWorkflowStatusAsync(InstanceId);

        forbidden.IsForbidden.Should().BeTrue();
        forbidden.IsNotFound.Should().BeFalse();
        missing.IsNotFound.Should().BeTrue();
        missing.IsForbidden.Should().BeFalse();
    }

    [Fact]
    public async Task GetWorkflowStatusAsync_OnSuccess_ReturnsTheBody()
    {
        var client = BuildClient(HttpStatusCode.OK, """{"id":"8808a450"}""");

        var read = await client.GetWorkflowStatusAsync(InstanceId);

        read.IsSuccess.Should().BeTrue();
        read.Body.Should().Contain("8808a450");
    }

    [Fact]
    public async Task GetActionDetailsAsync_CarriesTheRealStatus()
    {
        var read = await BuildClient(HttpStatusCode.Forbidden, "").GetActionDetailsAsync(InstanceId, "2");

        read.IsForbidden.Should().BeTrue();
    }

    [Fact]
    public async Task GetDisclosedDataAsync_CarriesTheRealStatus()
    {
        var read = await BuildClient(HttpStatusCode.Forbidden, "").GetDisclosedDataAsync(InstanceId);

        read.IsForbidden.Should().BeTrue();
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
