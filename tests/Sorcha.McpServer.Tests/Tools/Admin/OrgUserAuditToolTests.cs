// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using Microsoft.Extensions.Logging;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.McpServer.Tools.Admin;
using Sorcha.ServiceClients.Shared;
using Sorcha.ServiceClients.Tenant;

namespace Sorcha.McpServer.Tests.Tools.Admin;

/// <summary>
/// Feature 140 Wave 4: OrgUserAuditTool returns a read-only paginated org-user list via the
/// typed <see cref="ITenantServiceClient"/> (platform tier + admin role).
/// </summary>
public class OrgUserAuditToolTests
{
    private readonly Mock<IMcpAuthorizationService> _authServiceMock = new();
    private readonly Mock<IServiceAvailabilityTracker> _availabilityTrackerMock = new();
    private readonly Mock<ITenantServiceClient> _tenantClientMock = new();

    private OrgUserAuditTool CreateTool() => new(
        _authServiceMock.Object,
        _availabilityTrackerMock.Object,
        _tenantClientMock.Object,
        Mock.Of<ILogger<OrgUserAuditTool>>());

    private void Allow()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_org_user_audit")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Tenant")).Returns(true);
    }

    [Fact]
    public async Task InvokeAsync_Unauthorized_ReturnsUnauthorized()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_org_user_audit")).Returns(false);

        var result = await CreateTool().InvokeAsync("org-1");

        result.Status.Should().Be("Unauthorized");
        _tenantClientMock.Verify(c => c.GetOrganizationUsersAsync(
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task InvokeAsync_MissingOrgId_ReturnsError()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_org_user_audit")).Returns(true);

        var result = await CreateTool().InvokeAsync("");

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task InvokeAsync_Unavailable_ReturnsUnavailable()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_org_user_audit")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Tenant")).Returns(false);

        var result = await CreateTool().InvokeAsync("org-1");

        result.Status.Should().Be("Unavailable");
    }

    [Fact]
    public async Task InvokeAsync_Success_BuildsPaginationQueryAndReturnsBody()
    {
        Allow();
        _tenantClientMock.Setup(c => c.GetOrganizationUsersAsync(
                "org-1", "page=2&pageSize=10", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceReadResult(HttpStatusCode.OK, "{\"items\":[]}"));

        var result = await CreateTool().InvokeAsync("org-1", page: 2, pageSize: 10);

        result.Status.Should().Be("Success");
        result.OrganizationId.Should().Be("org-1");
        // #1645 — structured, not a JSON document embedded in a string.
        result.Users!.Value.ValueKind.Should().Be(System.Text.Json.JsonValueKind.Object);
        result.Users.Value.GetProperty("items").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Array);
        _tenantClientMock.Verify(c => c.GetOrganizationUsersAsync(
            "org-1", "page=2&pageSize=10", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_OrganizationIdAlias_IsAccepted()
    {
        // #1645 — every sibling tool names this argument organizationId.
        Allow();
        _tenantClientMock.Setup(c => c.GetOrganizationUsersAsync(
                "org-9", It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceReadResult(HttpStatusCode.OK, "{\"items\":[]}"));

        var result = await CreateTool().InvokeAsync(organizationId: "org-9");

        result.Status.Should().Be("Success");
        result.OrganizationId.Should().Be("org-9");
    }

    [Fact]
    public async Task InvokeAsync_Forbidden_StatesThePlatformRequirementAndTheToolToUseInstead()
    {
        // #1706 — an org Administrator auditing their OWN org was told "only an administrator of
        // that organisation can audit its users". The endpoint needs PLATFORM audit authority.
        Allow();
        _tenantClientMock.Setup(c => c.GetOrganizationUsersAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceReadResult(HttpStatusCode.Forbidden, null));

        var result = await CreateTool().InvokeAsync("org-1");

        result.Status.Should().Be("Refused");
        result.Message.Should().Contain("system-admin organisation")
            .And.Contain("not enough")
            .And.Contain("sorcha_user_list");
        result.Message.Should().NotContain("Only an administrator of that organisation");
    }

    [Fact]
    public async Task InvokeAsync_NullBody_ReturnsNotFound()
    {
        Allow();
        _tenantClientMock.Setup(c => c.GetOrganizationUsersAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceReadResult(HttpStatusCode.NotFound, null));

        var result = await CreateTool().InvokeAsync("org-1");

        result.Status.Should().Be("NotFound");
    }

    [Fact]
    public async Task InvokeAsync_Timeout_ReturnsTimeout()
    {
        Allow();
        _tenantClientMock.Setup(c => c.GetOrganizationUsersAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException());

        var result = await CreateTool().InvokeAsync("org-1");

        result.Status.Should().Be("Timeout");
    }
}
