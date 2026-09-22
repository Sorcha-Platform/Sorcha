// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.McpServer.Tools.Admin;
using Sorcha.ServiceClients.Shared;
using Sorcha.ServiceClients.Tenant;

namespace Sorcha.McpServer.Tests.Tools.Admin;

/// <summary>
/// Spec 139 US4: TenantCreateTool writes via the typed <see cref="ITenantServiceClient"/>
/// onto POST /api/platform/organizations (the correct provisioning route), so these tests mock the client.
/// </summary>
public class TenantCreateToolTests
{
    private readonly Mock<IMcpAuthorizationService> _authServiceMock = new();
    private readonly Mock<IServiceAvailabilityTracker> _availabilityTrackerMock = new();
    private readonly Mock<ITenantServiceClient> _tenantClientMock = new();

    private TenantCreateTool CreateTool() => new(
        _authServiceMock.Object,
        _availabilityTrackerMock.Object,
        _tenantClientMock.Object,
        Mock.Of<ILogger<TenantCreateTool>>());

    private void Allow()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_tenant_create")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Tenant")).Returns(true);
    }

    [Fact]
    public async Task CreateTenantAsync_Unauthorized_ReturnsUnauthorizedResult()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_tenant_create")).Returns(false);

        var result = await CreateTool().CreateTenantAsync("Test", "admin@test.com");

        result.Status.Should().Be("Unauthorized");
    }

    [Fact]
    public async Task CreateTenantAsync_ServiceUnavailable_ReturnsUnavailableResult()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_tenant_create")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Tenant")).Returns(false);

        var result = await CreateTool().CreateTenantAsync("Test", "admin@test.com");

        result.Status.Should().Be("Unavailable");
    }

    [Fact]
    public async Task CreateTenantAsync_MissingName_ReturnsError()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_tenant_create")).Returns(true);

        var result = await CreateTool().CreateTenantAsync("", "admin@test.com");

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task CreateTenantAsync_MissingAdminEmail_ReturnsError()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_tenant_create")).Returns(true);

        var result = await CreateTool().CreateTenantAsync("Test", "");

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task CreateTenantAsync_InvalidEmail_ReturnsError()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_tenant_create")).Returns(true);

        var result = await CreateTool().CreateTenantAsync("Test", "not-an-email");

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task CreateTenantAsync_Success_ReturnsTenantInfo()
    {
        Allow();
        _tenantClientMock
            .Setup(c => c.CreateOrganizationAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceReadResult(
                HttpStatusCode.Created,
                JsonSerializer.Serialize(new { OrganizationId = "tenant-123", OrganizationName = "Test Tenant", InvitationId = "inv-456" })));

        var result = await CreateTool().CreateTenantAsync("Test Tenant", "admin@test.com");

        result.Status.Should().Be("Success");
        result.TenantId.Should().Be("tenant-123");
        result.TenantName.Should().Be("Test Tenant");
        result.AdminUserId.Should().Be("inv-456");
        result.AdminEmail.Should().Be("admin@test.com");
    }

    [Fact]
    public async Task CreateTenantAsync_CallsPlatformProvisioningRoute()
    {
        Allow();
        _tenantClientMock
            .Setup(c => c.CreateOrganizationAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceReadResult(
                HttpStatusCode.Created,
                JsonSerializer.Serialize(new { OrganizationId = "tenant-123", OrganizationName = "Test Tenant" })));

        await CreateTool().CreateTenantAsync("Test Tenant", "admin@test.com");

        _tenantClientMock.Verify(
            c => c.CreateOrganizationAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateTenantAsync_EmptyBody_ReturnsErrorResult()
    {
        Allow();
        _tenantClientMock
            .Setup(c => c.CreateOrganizationAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceReadResult(HttpStatusCode.OK, null));

        var result = await CreateTool().CreateTenantAsync("Test", "admin@test.com");

        result.Status.Should().Be("Error");
    }

    /// <summary>
    /// #1685(a). The real defect: an org Administrator calling this SystemAdmin-only route gets a
    /// 403, and the old message ("Tenant creation failed.") gave no hint that this was an
    /// authority problem rather than a bad input — so the agent varied its inputs three times.
    /// </summary>
    [Fact]
    public async Task CreateTenantAsync_Forbidden_ReturnsRefusedNamingRequiredAuthority()
    {
        Allow();
        _tenantClientMock
            .Setup(c => c.CreateOrganizationAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceReadResult(HttpStatusCode.Forbidden, null));

        var result = await CreateTool().CreateTenantAsync("Test", "admin@test.com");

        result.Status.Should().Be("Refused");
        result.Message.Should().Contain("403");
        result.Message.Should().Contain("platform-system-admin");
        result.Message.Should().NotBe("Tenant creation failed.");
    }

    [Fact]
    public async Task CreateTenantAsync_Timeout_ReturnsTimeoutResult()
    {
        Allow();
        _tenantClientMock
            .Setup(c => c.CreateOrganizationAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException());

        var result = await CreateTool().CreateTenantAsync("Test", "admin@test.com");

        result.Status.Should().Be("Timeout");
    }

    [Fact]
    public async Task CreateTenantAsync_ResponseTimeIsRecorded()
    {
        Allow();
        _tenantClientMock
            .Setup(c => c.CreateOrganizationAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceReadResult(
                HttpStatusCode.Created,
                JsonSerializer.Serialize(new { OrganizationId = "t-1", OrganizationName = "T" })));

        var result = await CreateTool().CreateTenantAsync("Test", "admin@test.com");

        result.ResponseTimeMs.Should().BeGreaterThanOrEqualTo(0);
        result.CheckedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }
}
