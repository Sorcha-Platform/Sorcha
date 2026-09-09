// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.McpServer.Tools.Admin;
using Sorcha.ServiceClients.Tenant;

namespace Sorcha.McpServer.Tests.Tools.Admin;

/// <summary>
/// Regression cover for #1617. There was no test file for this tool at all, which is exactly why it
/// shipped reading an <c>items</c> envelope from an endpoint that sends <c>organizations</c>.
/// </summary>
/// <remarks>
/// The body below is the real <c>OrganizationListResponse</c> shape. Do not "simplify" it to an
/// <c>items</c> array or a bare array — the envelope IS the contract under test, and a fixture that
/// sends the shape the code happens to read can only ever pass. That is the trap this file exists
/// to close.
/// </remarks>
public class OrgWalletStatusToolTests
{
    private readonly Mock<IMcpAuthorizationService> _authServiceMock = new();
    private readonly Mock<IServiceAvailabilityTracker> _availabilityTrackerMock = new();
    private readonly Mock<ITenantServiceClient> _tenantClientMock = new();

    private OrgWalletStatusTool CreateTool() => new(
        _authServiceMock.Object,
        _availabilityTrackerMock.Object,
        _tenantClientMock.Object,
        Mock.Of<ILogger<OrgWalletStatusTool>>());

    private void Allow()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_org_wallet_status")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Tenant")).Returns(true);
    }

    private void Returns(string body) =>
        _tenantClientMock
            .Setup(c => c.ListOrganizationsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(body);

    private const string TwoOrgsOneWithout = """
        {
          "organizations": [
            { "id": "00000000-0000-0000-0000-000000000001", "name": "Sorcha Local",
              "subdomain": "sorcha-local", "status": "active",
              "walletAddress": "ws11qzang4fwalvtdgemv6lfvdpd4244yqczf09qr85y0kzrr56udcr4gt54m33" },
            { "id": "00000000-0000-0000-0000-000000000002", "name": "Sorcha Public",
              "subdomain": "public", "status": "active", "walletAddress": null }
          ],
          "totalCount": 2
        }
        """;

    [Fact]
    public async Task InvokeAsync_OrganizationsEnvelope_IsRead()
    {
        Allow();
        Returns(TwoOrgsOneWithout);

        var result = await CreateTool().InvokeAsync();

        result.Status.Should().Be("Success");
        result.Organizations.Should().HaveCount(2,
            "the endpoint sends 'organizations'; reading 'items' found nothing and reported it as an all-clear (#1617)");
        result.Organizations.Single(o => o.Name == "Sorcha Local").HasWallet.Should().BeTrue();
        result.Organizations.Single(o => o.Name == "Sorcha Public").HasWallet.Should().BeFalse();
        result.Message.Should().Contain("Sorcha Public");
    }

    [Fact]
    public async Task InvokeAsync_KnownOrgId_IsFound()
    {
        Allow();
        Returns(TwoOrgsOneWithout);

        var result = await CreateTool().InvokeAsync("00000000-0000-0000-0000-000000000001");

        result.Status.Should().Be("Success",
            "this org is in the list — reporting 'not found' for an org that exists is #1617");
        result.Organizations.Should().ContainSingle().Which.HasWallet.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_EmptyList_IsNotReportedAsAnAllClear()
    {
        Allow();
        Returns("""{ "organizations": [], "totalCount": 0 }""");

        var result = await CreateTool().InvokeAsync();

        // "All 0 organisation(s) have a signing wallet." was true, useless, and read as reassurance.
        result.Message.Should().NotContain("All 0",
            "a zero-count result must never be phrased as an all-clear");
        result.Message.Should().Contain("nothing to report");
    }

    [Fact]
    public async Task InvokeAsync_UnrecognisedEnvelope_IsAnErrorNotAnEmptyAllClear()
    {
        Allow();
        Returns("""{ "items": [ { "id": "x", "name": "y" } ] }""");

        var result = await CreateTool().InvokeAsync();

        // The old code read exactly this shape and reported success over an empty set. A body we
        // cannot interpret must say so — "the list did not parse" is not "the list was empty".
        result.Status.Should().Be("Error");
        result.Message.Should().Contain("unrecognised shape");
    }

    [Fact]
    public async Task InvokeAsync_UnknownOrgId_SaysHowManyItLookedAt()
    {
        Allow();
        Returns(TwoOrgsOneWithout);

        var result = await CreateTool().InvokeAsync("99999999-9999-9999-9999-999999999999");

        result.Status.Should().Be("Error");
        result.Message.Should().Contain("2 listed",
            "naming the size of the list it searched distinguishes a genuine miss from a body that never parsed");
    }

    [Fact]
    public async Task InvokeAsync_QueriesPageNumber_NotPage()
    {
        Allow();
        Returns(TwoOrgsOneWithout);

        await CreateTool().InvokeAsync();

        _tenantClientMock.Verify(
            c => c.ListOrganizationsAsync(It.Is<string>(q => q.Contains("pageNumber=")), It.IsAny<CancellationToken>()),
            Times.Once,
            "OrganizationEndpoints.ListOrganizations binds pageNumber; 'page=' bound nothing and silently took the default");
    }

    [Fact]
    public async Task InvokeAsync_Unauthorized_ReturnsUnauthorized()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_org_wallet_status")).Returns(false);

        var result = await CreateTool().InvokeAsync();

        result.Status.Should().Be("Unauthorized");
    }
}
