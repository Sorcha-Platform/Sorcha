// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.McpServer.Tools.Admin;
using Sorcha.ServiceClients.Tenant;

namespace Sorcha.McpServer.Tests.Tools.Admin;

/// <summary>
/// Spec 139 US4 / MCP P0 Task 5: UserManageTool writes via the typed <see cref="ITenantServiceClient"/>
/// (route pinned, caller token forwarded), so these tests mock the client rather than HTTP.
/// </summary>
public class UserManageToolTests
{
    private const string OrgId = "11111111-1111-1111-1111-111111111111";

    private readonly Mock<IMcpAuthorizationService> _authServiceMock = new();
    private readonly Mock<IServiceAvailabilityTracker> _availabilityTrackerMock = new();
    private readonly Mock<ITenantServiceClient> _tenantClientMock = new();

    private UserManageTool CreateTool() => new(
        _authServiceMock.Object,
        _availabilityTrackerMock.Object,
        _tenantClientMock.Object,
        Mock.Of<ILogger<UserManageTool>>());

    private void Allow()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_user_manage")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Tenant")).Returns(true);
    }

    private void SuccessClient() =>
        _tenantClientMock
            .Setup(c => c.ManageUserAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(string.Empty);

    [Fact]
    public async Task ManageUserAsync_Unauthorized_ReturnsUnauthorizedResult()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_user_manage")).Returns(false);

        var result = await CreateTool().ManageUserAsync(OrgId, "user-123", "Suspend");

        result.Status.Should().Be("Unauthorized");
    }

    [Fact]
    public async Task ManageUserAsync_ServiceUnavailable_ReturnsUnavailableResult()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_user_manage")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Tenant")).Returns(false);

        var result = await CreateTool().ManageUserAsync(OrgId, "user-123", "Suspend");

        result.Status.Should().Be("Unavailable");
    }

    [Fact]
    public async Task ManageUserAsync_MissingOrganizationId_ReturnsError()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_user_manage")).Returns(true);

        var result = await CreateTool().ManageUserAsync("", "user-123", "Suspend");

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task ManageUserAsync_NonGuidOrganizationId_ReturnsError()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_user_manage")).Returns(true);

        var result = await CreateTool().ManageUserAsync("not-a-guid", "user-123", "Suspend");

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task ManageUserAsync_MissingUserId_ReturnsError()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_user_manage")).Returns(true);

        var result = await CreateTool().ManageUserAsync(OrgId, "", "Suspend");

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task ManageUserAsync_InvalidAction_ReturnsError()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_user_manage")).Returns(true);

        var result = await CreateTool().ManageUserAsync(OrgId, "user-123", "Bogus");

        result.Status.Should().Be("Error");
    }

    [Theory]
    [InlineData("Activate")]
    [InlineData("Deactivate")]
    [InlineData("Lock")]
    [InlineData("AddRole")]
    [InlineData("RemoveRole")]
    public async Task ManageUserAsync_RetiredActions_ReturnError(string action)
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_user_manage")).Returns(true);

        var result = await CreateTool().ManageUserAsync(OrgId, "user-123", action);

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task ManageUserAsync_ChangeRoleWithoutRole_ReturnsError()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_user_manage")).Returns(true);

        var result = await CreateTool().ManageUserAsync(OrgId, "user-123", "ChangeRole");

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task ManageUserAsync_ChangeRoleWithInvalidRole_ReturnsError()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_user_manage")).Returns(true);

        var result = await CreateTool().ManageUserAsync(OrgId, "user-123", "ChangeRole", "Bogus");

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task ManageUserAsync_ChangeRoleWithSystemAdmin_ReturnsError()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_user_manage")).Returns(true);

        var result = await CreateTool().ManageUserAsync(OrgId, "user-123", "ChangeRole", "SystemAdmin");

        result.Status.Should().Be("Error");
    }

    [Theory]
    [InlineData("Suspend")]
    [InlineData("Reactivate")]
    [InlineData("Unlock")]
    public async Task ManageUserAsync_LifecycleActions_Success(string action)
    {
        Allow();
        SuccessClient();

        var result = await CreateTool().ManageUserAsync(OrgId, "user-123", action);

        result.Status.Should().Be("Success");
        result.UserId.Should().Be("user-123");
        result.ActionPerformed.Should().Be(action);
    }

    [Fact]
    public async Task ManageUserAsync_ChangeRole_Success()
    {
        Allow();
        SuccessClient();

        var result = await CreateTool().ManageUserAsync(OrgId, "user-123", "ChangeRole", "Designer");

        result.Status.Should().Be("Success");
        result.UserId.Should().Be("user-123");
        result.ActionPerformed.Should().Be("ChangeRole");
        result.RoleAffected.Should().Be("Designer");
    }

    [Fact]
    public async Task ManageUserAsync_PassesOrganizationAndUserIdToClient()
    {
        Allow();
        SuccessClient();

        await CreateTool().ManageUserAsync(OrgId, "user-123", "Suspend");

        _tenantClientMock.Verify(
            c => c.ManageUserAsync(OrgId, "user-123", "Suspend", It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ManageUserAsync_ChangeRole_PassesRoleBodyToClient()
    {
        Allow();
        SuccessClient();

        await CreateTool().ManageUserAsync(OrgId, "user-123", "ChangeRole", "Auditor");

        _tenantClientMock.Verify(
            c => c.ManageUserAsync(OrgId, "user-123", "ChangeRole", It.Is<string>(b => b.Contains("Auditor")), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ManageUserAsync_Null_ReturnsErrorResult()
    {
        Allow();
        _tenantClientMock
            .Setup(c => c.ManageUserAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var result = await CreateTool().ManageUserAsync(OrgId, "user-123", "Suspend");

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task ManageUserAsync_Timeout_ReturnsTimeoutResult()
    {
        Allow();
        _tenantClientMock
            .Setup(c => c.ManageUserAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException());

        var result = await CreateTool().ManageUserAsync(OrgId, "user-123", "Suspend");

        result.Status.Should().Be("Timeout");
    }

    [Fact]
    public async Task ManageUserAsync_ResponseTimeIsRecorded()
    {
        Allow();
        SuccessClient();

        var result = await CreateTool().ManageUserAsync(OrgId, "user-123", "Suspend");

        result.ResponseTimeMs.Should().BeGreaterThanOrEqualTo(0);
        result.CheckedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }
}
