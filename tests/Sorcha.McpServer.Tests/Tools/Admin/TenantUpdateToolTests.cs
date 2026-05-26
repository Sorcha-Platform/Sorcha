// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.McpServer.Tools.Admin;
using Sorcha.ServiceClients.Tenant;

namespace Sorcha.McpServer.Tests.Tools.Admin;

/// <summary>
/// Spec 139 US4: TenantUpdateTool writes via the typed <see cref="ITenantServiceClient"/>
/// (route pinned, caller token forwarded), so these tests mock the client rather than HTTP.
/// </summary>
public class TenantUpdateToolTests
{
    private readonly Mock<IMcpAuthorizationService> _authServiceMock = new();
    private readonly Mock<IServiceAvailabilityTracker> _availabilityTrackerMock = new();
    private readonly Mock<ITenantServiceClient> _tenantClientMock = new();

    private TenantUpdateTool CreateTool() => new(
        _authServiceMock.Object,
        _availabilityTrackerMock.Object,
        _tenantClientMock.Object,
        Mock.Of<ILogger<TenantUpdateTool>>());

    private void Allow()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_tenant_update")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Tenant")).Returns(true);
    }

    private void SuccessClient() =>
        _tenantClientMock
            .Setup(c => c.UpdateOrganizationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(string.Empty);

    [Fact]
    public async Task UpdateTenantAsync_Unauthorized_ReturnsUnauthorizedResult()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_tenant_update")).Returns(false);

        var result = await CreateTool().UpdateTenantAsync("tenant-123", name: "X");

        result.Status.Should().Be("Unauthorized");
    }

    [Fact]
    public async Task UpdateTenantAsync_ServiceUnavailable_ReturnsUnavailableResult()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_tenant_update")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Tenant")).Returns(false);

        var result = await CreateTool().UpdateTenantAsync("tenant-123", name: "X");

        result.Status.Should().Be("Unavailable");
    }

    [Fact]
    public async Task UpdateTenantAsync_MissingTenantId_ReturnsError()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_tenant_update")).Returns(true);

        var result = await CreateTool().UpdateTenantAsync("", name: "X");

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task UpdateTenantAsync_InvalidStatus_ReturnsError()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_tenant_update")).Returns(true);

        var result = await CreateTool().UpdateTenantAsync("tenant-123", status: "Bogus");

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task UpdateTenantAsync_NoUpdateFields_ReturnsError()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_tenant_update")).Returns(true);

        var result = await CreateTool().UpdateTenantAsync("tenant-123");

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task UpdateTenantAsync_UpdateName_Success()
    {
        Allow();
        SuccessClient();

        var result = await CreateTool().UpdateTenantAsync("tenant-123", name: "New Name");

        result.Status.Should().Be("Success");
        result.TenantId.Should().Be("tenant-123");
        result.UpdatedName.Should().Be("New Name");
        result.UpdatedStatus.Should().BeNull();
        result.Message.Should().Contain("name to 'New Name'");
        _availabilityTrackerMock.Verify(a => a.RecordSuccess("Tenant"), Times.Once);
    }

    [Fact]
    public async Task UpdateTenantAsync_UpdateStatus_Success()
    {
        Allow();
        SuccessClient();

        var result = await CreateTool().UpdateTenantAsync("tenant-123", status: "Suspended");

        result.Status.Should().Be("Success");
        result.UpdatedStatus.Should().Be("Suspended");
        result.UpdatedName.Should().BeNull();
    }

    [Fact]
    public async Task UpdateTenantAsync_UpdateBothNameAndStatus_Success()
    {
        Allow();
        SuccessClient();

        var result = await CreateTool().UpdateTenantAsync("tenant-123", name: "New Name", status: "Active");

        result.Status.Should().Be("Success");
        result.UpdatedName.Should().Be("New Name");
        result.UpdatedStatus.Should().Be("Active");
    }

    [Fact]
    public async Task UpdateTenantAsync_PassesTenantIdToClient()
    {
        Allow();
        SuccessClient();

        await CreateTool().UpdateTenantAsync("tenant-123", name: "X");

        _tenantClientMock.Verify(
            c => c.UpdateOrganizationAsync("tenant-123", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateTenantAsync_Null_ReturnsErrorResult()
    {
        Allow();
        _tenantClientMock
            .Setup(c => c.UpdateOrganizationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var result = await CreateTool().UpdateTenantAsync("tenant-123", name: "X");

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task UpdateTenantAsync_Timeout_ReturnsTimeoutResult()
    {
        Allow();
        _tenantClientMock
            .Setup(c => c.UpdateOrganizationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException());

        var result = await CreateTool().UpdateTenantAsync("tenant-123", name: "X");

        result.Status.Should().Be("Timeout");
    }
}
