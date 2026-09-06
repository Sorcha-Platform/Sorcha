// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.McpServer.Tools.Admin;
using Sorcha.ServiceClients.Tenant;

namespace Sorcha.McpServer.Tests.Tools.Admin;

/// <summary>
/// Spec 139 US4 / MCP P0 Task 5: TokenRevokeTool writes via the typed <see cref="ITenantServiceClient"/>
/// (route pinned, caller token forwarded), so these tests mock the client rather than HTTP.
/// </summary>
public class TokenRevokeToolTests
{
    private readonly Mock<IMcpAuthorizationService> _authServiceMock = new();
    private readonly Mock<IServiceAvailabilityTracker> _availabilityTrackerMock = new();
    private readonly Mock<ITenantServiceClient> _tenantClientMock = new();

    private TokenRevokeTool CreateTool() => new(
        _authServiceMock.Object,
        _availabilityTrackerMock.Object,
        _tenantClientMock.Object,
        Mock.Of<ILogger<TokenRevokeTool>>());

    private void Allow()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_token_revoke")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Tenant")).Returns(true);
    }

    [Fact]
    public async Task RevokeTokensAsync_Unauthorized_ReturnsUnauthorizedResult()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_token_revoke")).Returns(false);

        var result = await CreateTool().RevokeTokensAsync(userId: "user-123", reason: "x");

        result.Status.Should().Be("Unauthorized");
    }

    [Fact]
    public async Task RevokeTokensAsync_ServiceUnavailable_ReturnsUnavailableResult()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_token_revoke")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Tenant")).Returns(false);

        var result = await CreateTool().RevokeTokensAsync(userId: "user-123", reason: "x");

        result.Status.Should().Be("Unavailable");
    }

    [Fact]
    public async Task RevokeTokensAsync_NoTargetProvided_ReturnsError()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_token_revoke")).Returns(true);

        var result = await CreateTool().RevokeTokensAsync(reason: "x");

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task RevokeTokensAsync_BothTargetsProvided_ReturnsError()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_token_revoke")).Returns(true);

        var result = await CreateTool().RevokeTokensAsync(userId: "user-123", tenantId: "tenant-123", reason: "x");

        result.Status.Should().Be("Error");
        result.Message.Should().Contain("not both");
    }

    [Fact]
    public async Task RevokeTokensAsync_NoReasonProvided_ReturnsError()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_token_revoke")).Returns(true);

        var result = await CreateTool().RevokeTokensAsync(userId: "user-123", reason: "");

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task RevokeTokensAsync_ForUser_Success()
    {
        Allow();
        _tenantClientMock
            .Setup(c => c.RevokeTokenAsync("user-123", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Serialize(new { success = true, message = "All tokens for user user-123 have been revoked" }));

        var result = await CreateTool().RevokeTokensAsync(userId: "user-123", reason: "Security incident");

        result.Status.Should().Be("Success");
        result.UserId.Should().Be("user-123");
        result.Reason.Should().Be("Security incident");
        result.Message.Should().Contain("user-123");
        _availabilityTrackerMock.Verify(a => a.RecordSuccess("Tenant"), Times.Once);
    }

    [Fact]
    public async Task RevokeTokensAsync_ForTenant_Success()
    {
        Allow();
        _tenantClientMock
            .Setup(c => c.RevokeTokenAsync(null, "tenant-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Serialize(new { success = true, message = "All tokens for organization tenant-123 have been revoked" }));

        var result = await CreateTool().RevokeTokensAsync(tenantId: "tenant-123", reason: "Organization security reset");

        result.Status.Should().Be("Success");
        result.TenantId.Should().Be("tenant-123");
        result.Reason.Should().Be("Organization security reset");
    }

    [Fact]
    public async Task RevokeTokensAsync_PassesUserIdToClient()
    {
        Allow();
        _tenantClientMock
            .Setup(c => c.RevokeTokenAsync("user-123", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Serialize(new { success = true, message = "ok" }));

        await CreateTool().RevokeTokensAsync(userId: "user-123", reason: "x");

        _tenantClientMock.Verify(
            c => c.RevokeTokenAsync("user-123", null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RevokeTokensAsync_Null_ReturnsErrorResult()
    {
        Allow();
        _tenantClientMock
            .Setup(c => c.RevokeTokenAsync("user-123", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var result = await CreateTool().RevokeTokensAsync(userId: "user-123", reason: "x");

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task RevokeTokensAsync_Timeout_ReturnsTimeoutResult()
    {
        Allow();
        _tenantClientMock
            .Setup(c => c.RevokeTokenAsync("user-123", null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException());

        var result = await CreateTool().RevokeTokensAsync(userId: "user-123", reason: "x");

        result.Status.Should().Be("Timeout");
    }

    [Fact]
    public async Task RevokeTokensAsync_ResponseTimeIsRecorded()
    {
        Allow();
        _tenantClientMock
            .Setup(c => c.RevokeTokenAsync("user-123", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Serialize(new { success = true, message = "ok" }));

        var result = await CreateTool().RevokeTokensAsync(userId: "user-123", reason: "x");

        result.ResponseTimeMs.Should().BeGreaterThanOrEqualTo(0);
        result.CheckedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }
}
