// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Sorcha.ServiceClients.Auth;
using Sorcha.ServiceClients.Tenant;

namespace Sorcha.ServiceClients.Tests.Tenant;

/// <summary>
/// #1648 — <see cref="TenantServiceClient.GetOrganizationAuditEventsAsync"/> keeps the HTTP status,
/// so the MCP audit tool can tell "you lack the Auditor role" from "the Tenant Service is down".
/// </summary>
public class TenantServiceClientAuditTests
{
    [Fact]
    public async Task Success_ReturnsTheStatusAndBody_FromTheOrganisationAuditRoute()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"events":[],"totalCount":0}""");

        var (status, body) = await CreateClient(handler).GetOrganizationAuditEventsAsync(
            "00000000-0000-0000-0000-000000000001", "eventType=PermissionDenied&page=1&pageSize=50");

        status.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("\"events\"");
        handler.RequestUri!.AbsolutePath.Should().Be("/api/organizations/00000000-0000-0000-0000-000000000001/audit");
        handler.RequestUri.Query.Should().Be("?eventType=PermissionDenied&page=1&pageSize=50");
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task NonSuccess_ReturnsThatStatusWithNoBody(HttpStatusCode refusal)
    {
        var handler = new StubHandler(refusal, """{"error":"denied"}""");

        var (status, body) = await CreateClient(handler).GetOrganizationAuditEventsAsync("org-1");

        status.Should().Be(refusal);
        body.Should().BeNull();
    }

    private static TenantServiceClient CreateClient(HttpMessageHandler handler)
    {
        var auth = new Mock<IServiceAuthClient>();
        auth.Setup(a => a.GetTokenAsync(It.IsAny<CancellationToken>())).ReturnsAsync("token");

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ServiceClients:TenantService:Address"] = "http://tenant-service"
            })
            .Build();

        return new TenantServiceClient(
            new HttpClient(handler), auth.Object, config, NullLogger<TenantServiceClient>.Instance);
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }
}
