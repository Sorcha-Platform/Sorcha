// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.McpServer.Tools.Admin;
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
            .ReturnsAsync("{\"items\":[]}");

        var result = await CreateTool().InvokeAsync("org-1", page: 2, pageSize: 10);

        result.Status.Should().Be("Success");
        result.OrganizationId.Should().Be("org-1");
        result.Users.Should().Contain("items");
        _tenantClientMock.Verify(c => c.GetOrganizationUsersAsync(
            "org-1", "page=2&pageSize=10", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_NullBody_ReturnsNotFound()
    {
        Allow();
        _tenantClientMock.Setup(c => c.GetOrganizationUsersAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

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
