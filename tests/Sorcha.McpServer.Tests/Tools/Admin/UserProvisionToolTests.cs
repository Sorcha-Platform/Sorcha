// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.McpServer.Tools.Admin;
using Sorcha.ServiceClients.Tenant;

namespace Sorcha.McpServer.Tests.Tools.Admin;

/// <summary>
/// Feature 140 Wave 4: UserProvisionTool provisions a platform user into an organisation via the
/// typed <see cref="ITenantServiceClient"/> (platform tier + admin role).
/// </summary>
public class UserProvisionToolTests
{
    private readonly Mock<IMcpAuthorizationService> _authServiceMock = new();
    private readonly Mock<IServiceAvailabilityTracker> _availabilityTrackerMock = new();
    private readonly Mock<ITenantServiceClient> _tenantClientMock = new();

    private UserProvisionTool CreateTool() => new(
        _authServiceMock.Object,
        _availabilityTrackerMock.Object,
        _tenantClientMock.Object,
        Mock.Of<ILogger<UserProvisionTool>>());

    private void Allow()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_user_provision")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Tenant")).Returns(true);
    }

    [Fact]
    public async Task InvokeAsync_Unauthorized_ReturnsUnauthorized()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_user_provision")).Returns(false);

        var result = await CreateTool().InvokeAsync("a@b.com", "Ada", "org-1", "Member");

        result.Status.Should().Be("Unauthorized");
        _tenantClientMock.Verify(c => c.ProvisionPlatformUserAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task InvokeAsync_MissingRequiredFields_ReturnsError()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_user_provision")).Returns(true);

        var result = await CreateTool().InvokeAsync("", "Ada", "org-1", "Member");

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task InvokeAsync_Unavailable_ReturnsUnavailable()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_user_provision")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Tenant")).Returns(false);

        var result = await CreateTool().InvokeAsync("a@b.com", "Ada", "org-1", "Member");

        result.Status.Should().Be("Unavailable");
    }

    [Fact]
    public async Task InvokeAsync_Success_SerialisesRequestAndReturnsBody()
    {
        Allow();
        _tenantClientMock.Setup(c => c.ProvisionPlatformUserAsync(
                It.Is<string>(b => b.Contains("a@b.com") && b.Contains("org-1") && b.Contains("Member")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"userId\":\"u1\"}");

        var result = await CreateTool().InvokeAsync("a@b.com", "Ada", "org-1", "Member", skipEmailVerification: true);

        result.Status.Should().Be("Success");
        result.User.Should().Contain("userId");
        _availabilityTrackerMock.Verify(a => a.RecordSuccess("Tenant"), Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_WithPassword_IncludesPasswordInBody()
    {
        Allow();
        _tenantClientMock.Setup(c => c.ProvisionPlatformUserAsync(
                It.Is<string>(b => b.Contains("password")), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{}");

        var result = await CreateTool().InvokeAsync("a@b.com", "Ada", "org-1", "Member", password: "Sup3r-Secret-Pwd!");

        result.Status.Should().Be("Success");
        _tenantClientMock.Verify(c => c.ProvisionPlatformUserAsync(
            It.Is<string>(b => b.Contains("password")), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_NullBody_ReturnsError()
    {
        Allow();
        _tenantClientMock.Setup(c => c.ProvisionPlatformUserAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var result = await CreateTool().InvokeAsync("a@b.com", "Ada", "org-1", "Member");

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task InvokeAsync_Timeout_ReturnsTimeout()
    {
        Allow();
        _tenantClientMock.Setup(c => c.ProvisionPlatformUserAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException());

        var result = await CreateTool().InvokeAsync("a@b.com", "Ada", "org-1", "Member");

        result.Status.Should().Be("Timeout");
    }
}
