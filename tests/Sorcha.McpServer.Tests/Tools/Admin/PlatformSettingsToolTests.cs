// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.McpServer.Tools.Admin;
using Sorcha.ServiceClients.Tenant;

namespace Sorcha.McpServer.Tests.Tools.Admin;

/// <summary>
/// Feature 140 Wave 4: PlatformSettingsTool reads settings (no body) or toggles the public org
/// (with body) via the typed <see cref="ITenantServiceClient"/> (platform tier + admin role).
/// </summary>
public class PlatformSettingsToolTests
{
    private readonly Mock<IMcpAuthorizationService> _authServiceMock = new();
    private readonly Mock<IServiceAvailabilityTracker> _availabilityTrackerMock = new();
    private readonly Mock<ITenantServiceClient> _tenantClientMock = new();

    private PlatformSettingsTool CreateTool() => new(
        _authServiceMock.Object,
        _availabilityTrackerMock.Object,
        _tenantClientMock.Object,
        Mock.Of<ILogger<PlatformSettingsTool>>());

    private void Allow()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_platform_settings")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Tenant")).Returns(true);
    }

    [Fact]
    public async Task InvokeAsync_Unauthorized_ReturnsUnauthorized()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_platform_settings")).Returns(false);

        var result = await CreateTool().InvokeAsync();

        result.Status.Should().Be("Unauthorized");
        _tenantClientMock.Verify(c => c.GetPlatformSettingsAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task InvokeAsync_Unavailable_ReturnsUnavailable()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_platform_settings")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Tenant")).Returns(false);

        var result = await CreateTool().InvokeAsync();

        result.Status.Should().Be("Unavailable");
    }

    [Fact]
    public async Task InvokeAsync_NoArgument_ReadsSettings()
    {
        Allow();
        _tenantClientMock.Setup(c => c.GetPlatformSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"publicOrgEnabled\":true}");

        var result = await CreateTool().InvokeAsync();

        result.Status.Should().Be("Success");
        result.Updated.Should().BeFalse();
        result.Settings.Should().Contain("publicOrgEnabled");
        _tenantClientMock.Verify(c => c.GetPlatformSettingsAsync(It.IsAny<CancellationToken>()), Times.Once);
        _tenantClientMock.Verify(c => c.UpdatePublicOrgAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task InvokeAsync_WithFlag_TogglesPublicOrg()
    {
        Allow();
        _tenantClientMock.Setup(c => c.UpdatePublicOrgAsync(
                It.Is<string>(b => b.Contains("true")), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"publicOrgEnabled\":true}");

        var result = await CreateTool().InvokeAsync(publicOrgEnabled: true);

        result.Status.Should().Be("Success");
        result.Updated.Should().BeTrue();
        _tenantClientMock.Verify(c => c.UpdatePublicOrgAsync(
            It.Is<string>(b => b.Contains("true")), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_NullBody_ReturnsError()
    {
        Allow();
        _tenantClientMock.Setup(c => c.GetPlatformSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var result = await CreateTool().InvokeAsync();

        result.Status.Should().Be("Error");
        _availabilityTrackerMock.Verify(a => a.RecordFailure("Tenant", It.IsAny<Exception?>()), Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_Timeout_ReturnsTimeout()
    {
        Allow();
        _tenantClientMock.Setup(c => c.GetPlatformSettingsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException());

        var result = await CreateTool().InvokeAsync();

        result.Status.Should().Be("Timeout");
    }
}
