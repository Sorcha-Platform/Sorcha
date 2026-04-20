// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;

using Sorcha.Register.Models;
using Sorcha.ServiceClients.Auth;
using Sorcha.ServiceClients.Register;

using Xunit;

namespace Sorcha.ServiceClients.Tests.Register;

/// <summary>
/// Pins the service-auth contract for <see cref="RegisterServiceClient.GetTransactionsByInstanceIdAsync"/>.
/// The Register Service's <c>/api/query</c> group is gated by <c>CanReadTransactions</c>, so the
/// client must attach a service bearer; without it the call hits 401, the client silently returns
/// an empty list, and the validator's Tier 3 chain-binding lookup misfires VAL_BP_002 on every
/// late-bound participant reuse. Regression guard for that incident on n1 (2026-04-20).
/// </summary>
public class RegisterServiceClientInstanceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public async Task GetTransactionsByInstanceIdAsync_AttachesBearerTokenBeforeQuery()
    {
        var serviceAuth = new Mock<IServiceAuthClient>();
        serviceAuth.Setup(a => a.GetTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-service-bearer");

        AuthenticationHeaderValue? capturedAuth = null;
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>((req, _) =>
            {
                capturedAuth = req.Headers.Authorization;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]", System.Text.Encoding.UTF8, "application/json")
                });
            });

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ServiceClients:RegisterService:Address"] = "http://localhost:5290"
            })
            .Build();

        var client = new RegisterServiceClient(
            new HttpClient(handler.Object),
            serviceAuth.Object,
            config,
            Mock.Of<ILogger<RegisterServiceClient>>());

        await client.GetTransactionsByInstanceIdAsync(
            registerId: "reg-abc",
            instanceId: "inst-xyz");

        capturedAuth.Should().NotBeNull(
            "Tier 3 chain-binding lookup goes through /api/query which requires CanReadTransactions auth");
        capturedAuth!.Scheme.Should().Be("Bearer");
        capturedAuth.Parameter.Should().Be("test-service-bearer");
        serviceAuth.Verify(a => a.GetTokenAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
