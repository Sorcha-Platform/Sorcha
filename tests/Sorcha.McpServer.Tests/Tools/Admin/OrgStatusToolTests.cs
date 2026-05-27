// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.McpServer.Tools.Admin;
using Sorcha.ServiceClients.Tenant;

namespace Sorcha.McpServer.Tests.Tools.Admin;

/// <summary>
/// Feature 140 Wave 4: OrgStatusTool suspends/reactivates an organisation via the typed
/// <see cref="ITenantServiceClient"/> (platform tier + admin role).
/// </summary>
public class OrgStatusToolTests
{
    private readonly Mock<IMcpAuthorizationService> _authServiceMock = new();
    private readonly Mock<IServiceAvailabilityTracker> _availabilityTrackerMock = new();
    private readonly Mock<ITenantServiceClient> _tenantClientMock = new();

    private OrgStatusTool CreateTool() => new(
        _authServiceMock.Object,
        _availabilityTrackerMock.Object,
        _tenantClientMock.Object,
        Mock.Of<ILogger<OrgStatusTool>>());

    private void Allow()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_org_status")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Tenant")).Returns(true);
    }

    [Fact]
    public async Task InvokeAsync_Unauthorized_ReturnsUnauthorized()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_org_status")).Returns(false);

        var result = await CreateTool().InvokeAsync("org-1", "Suspended");

        result.Status.Should().Be("Unauthorized");
        _tenantClientMock.Verify(c => c.SetOrganizationStatusAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task InvokeAsync_MissingOrgId_ReturnsError()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_org_status")).Returns(true);

        var result = await CreateTool().InvokeAsync("", "Active");

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task InvokeAsync_InvalidStatus_ReturnsError()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_org_status")).Returns(true);

        var result = await CreateTool().InvokeAsync("org-1", "Bogus");

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task InvokeAsync_Unavailable_ReturnsUnavailable()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_org_status")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Tenant")).Returns(false);

        var result = await CreateTool().InvokeAsync("org-1", "Active");

        result.Status.Should().Be("Unavailable");
    }

    [Fact]
    public async Task InvokeAsync_Success_CanonicalisesStatusAndReturnsBody()
    {
        Allow();
        _tenantClientMock.Setup(c => c.SetOrganizationStatusAsync(
                "org-1", It.Is<string>(b => b.Contains("Suspended")), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"status\":\"Suspended\"}");

        var result = await CreateTool().InvokeAsync("org-1", "suspended");

        result.Status.Should().Be("Success");
        result.NewStatus.Should().Be("Suspended");
        result.OrganizationId.Should().Be("org-1");
        result.Organization.Should().Contain("Suspended");
        _availabilityTrackerMock.Verify(a => a.RecordSuccess("Tenant"), Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_NullBody_ReturnsError()
    {
        Allow();
        _tenantClientMock.Setup(c => c.SetOrganizationStatusAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var result = await CreateTool().InvokeAsync("org-1", "Active");

        result.Status.Should().Be("Error");
        _availabilityTrackerMock.Verify(a => a.RecordFailure("Tenant", It.IsAny<Exception?>()), Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_Timeout_ReturnsTimeout()
    {
        Allow();
        _tenantClientMock.Setup(c => c.SetOrganizationStatusAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException());

        var result = await CreateTool().InvokeAsync("org-1", "Active");

        result.Status.Should().Be("Timeout");
    }
}
