// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Sorcha.ServiceClients.Auth;
using Sorcha.ServiceClients.Tenant;

using Xunit;

namespace Sorcha.ServiceClients.Tests.Tenant;

/// <summary>
/// #1673 — the Tenant client must report WHICH failure occurred.
/// </summary>
/// <remarks>
/// <para>
/// Cold-start run #6: the Provider agent read the counterparty's organisation and was correctly
/// refused (403). <c>sorcha_org_user_audit</c> reported "NotFound" — that the organisation did not
/// exist — because the client collapsed every non-success into null.
/// </para>
/// <para>
/// The tool-level tests mock this client, so they cannot see whether the real status survives: a
/// mutation hard-coding it to NotFound passed all four of them. These tests are that seam.
/// </para>
/// </remarks>
public class TenantServiceClientReadStatusTests
{
    private const string OrgId = "072a9b81-8e0e-4300-b7db-c3c8d9567b90";

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task ListUsersAsync_CarriesTheRealStatus(HttpStatusCode status)
    {
        var read = await CreateClient(new StubHandler(status, "")).ListUsersAsync(OrgId);

        read.Status.Should().Be(status);
        read.IsSuccess.Should().BeFalse();
        read.Body.Should().BeNull();
    }

    [Fact]
    public async Task ListUsersAsync_DistinguishesForbiddenFromNotFound()
    {
        // The distinction the whole fix rests on: one means "it exists and you may not", the other
        // "it is not there". They call for opposite responses.
        var forbidden = await CreateClient(new StubHandler(HttpStatusCode.Forbidden, "")).ListUsersAsync(OrgId);
        var missing = await CreateClient(new StubHandler(HttpStatusCode.NotFound, "")).ListUsersAsync(OrgId);

        forbidden.IsForbidden.Should().BeTrue();
        forbidden.IsNotFound.Should().BeFalse();
        missing.IsNotFound.Should().BeTrue();
        missing.IsForbidden.Should().BeFalse();
    }

    [Fact]
    public async Task GetOrganizationUsersAsync_CarriesTheRealStatus()
    {
        var read = await CreateClient(new StubHandler(HttpStatusCode.Forbidden, ""))
            .GetOrganizationUsersAsync(OrgId);

        read.IsForbidden.Should().BeTrue();
    }

    [Fact]
    public async Task ListUsersAsync_OnSuccess_ReturnsTheBody()
    {
        var read = await CreateClient(new StubHandler(HttpStatusCode.OK, """{"items":[]}"""))
            .ListUsersAsync(OrgId);

        read.IsSuccess.Should().BeTrue();
        read.Body.Should().Contain("items");
    }

    [Theory]
    [InlineData("""{"error":"Listing all organisations requires SystemAdmin"}""", "Listing all organisations requires SystemAdmin")]
    [InlineData("""{"title":"Forbidden","detail":"Not a member"}""", "Not a member")]
    [InlineData("", null)]
    public async Task ARefusal_CarriesTheServicesOwnReason(string body, string? expected)
    {
        // #1673 — the status-carrying reads used to drop the body on failure, so even a caller that
        // could tell a 403 from a 404 could not say WHY.
        var read = await CreateClient(new StubHandler(HttpStatusCode.Forbidden, body)).ListOrganizationsAsync();

        read.IsForbidden.Should().BeTrue();
        read.Body.Should().BeNull();
        read.Reason.Should().Be(expected);
    }

    [Fact]
    public async Task EveryReadTheMcpToolsUse_ReportsItsStatus()
    {
        // The four reads behind sorcha_tenant_list, sorcha_org_wallet_status, sorcha_platform_settings
        // and sorcha_my_persona all used to collapse a 403 to null (#1673).
        var client = CreateClient(new StubHandler(HttpStatusCode.Forbidden, ""));

        (await client.ListOrganizationsAsync()).IsForbidden.Should().BeTrue();
        (await client.GetPlatformSettingsAsync()).IsForbidden.Should().BeTrue();
        (await client.GetMyPersonaAsync()).IsForbidden.Should().BeTrue();
        (await client.UpdatePublicOrgAsync("{}")).IsForbidden.Should().BeTrue();
        (await client.ReplaceMyPersonaAsync("{}")).IsForbidden.Should().BeTrue();
    }

    private static TenantServiceClient CreateClient(HttpMessageHandler handler)
    {
        var auth = new Mock<IServiceAuthClient>();
        auth.Setup(a => a.GetTokenAsync(It.IsAny<CancellationToken>())).ReturnsAsync("test-token");
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ServiceClients:TenantService:Address"] = "http://localhost:5450",
            })
            .Build();

        return new TenantServiceClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5450") },
            auth.Object, config, NullLogger<TenantServiceClient>.Instance);
    }

    /// <summary>Answers every request with one status and body.</summary>
    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
            });
    }
}
