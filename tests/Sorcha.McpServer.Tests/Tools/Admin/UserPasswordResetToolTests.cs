// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.McpServer.Tools.Admin;
using Sorcha.ServiceClients.Tenant;

namespace Sorcha.McpServer.Tests.Tools.Admin;

/// <summary>
/// Feature 140 Wave 4: UserPasswordResetTool resets a platform user's password via the typed
/// <see cref="ITenantServiceClient"/> (platform tier + admin role).
/// </summary>
public class UserPasswordResetToolTests
{
    private readonly Mock<IMcpAuthorizationService> _authServiceMock = new();
    private readonly Mock<IServiceAvailabilityTracker> _availabilityTrackerMock = new();
    private readonly Mock<ITenantServiceClient> _tenantClientMock = new();

    private UserPasswordResetTool CreateTool() => new(
        _authServiceMock.Object,
        _availabilityTrackerMock.Object,
        _tenantClientMock.Object,
        Mock.Of<ILogger<UserPasswordResetTool>>());

    private void Allow()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_user_password_reset")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Tenant")).Returns(true);
    }

    [Fact]
    public async Task InvokeAsync_Unauthorized_ReturnsUnauthorized()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_user_password_reset")).Returns(false);

        var result = await CreateTool().InvokeAsync("u1", "New-Pwd-123456!");

        result.Status.Should().Be("Unauthorized");
        _tenantClientMock.Verify(c => c.ResetPlatformUserPasswordAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task InvokeAsync_MissingUserId_ReturnsError()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_user_password_reset")).Returns(true);

        var result = await CreateTool().InvokeAsync("", "New-Pwd-123456!");

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task InvokeAsync_MissingPassword_ReturnsError()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_user_password_reset")).Returns(true);

        var result = await CreateTool().InvokeAsync("u1", "");

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task InvokeAsync_Unavailable_ReturnsUnavailable()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_user_password_reset")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Tenant")).Returns(false);

        var result = await CreateTool().InvokeAsync("u1", "New-Pwd-123456!");

        result.Status.Should().Be("Unavailable");
    }

    [Fact]
    public async Task InvokeAsync_Success_PassesUserIdAndBody()
    {
        Allow();
        _tenantClientMock.Setup(c => c.ResetPlatformUserPasswordAsync(
                "u1", It.Is<string>(b => b.Contains("newPassword")), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"message\":\"ok\"}");

        var result = await CreateTool().InvokeAsync("u1", "New-Pwd-123456!");

        result.Status.Should().Be("Success");
        result.UserId.Should().Be("u1");
        _tenantClientMock.Verify(c => c.ResetPlatformUserPasswordAsync(
            "u1", It.Is<string>(b => b.Contains("newPassword")), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_NullBody_ReturnsError()
    {
        Allow();
        _tenantClientMock.Setup(c => c.ResetPlatformUserPasswordAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var result = await CreateTool().InvokeAsync("u1", "New-Pwd-123456!");

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task InvokeAsync_Timeout_ReturnsTimeout()
    {
        Allow();
        _tenantClientMock.Setup(c => c.ResetPlatformUserPasswordAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException());

        var result = await CreateTool().InvokeAsync("u1", "New-Pwd-123456!");

        result.Status.Should().Be("Timeout");
    }
}
