// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.McpServer.Tools.Citizen;
using Sorcha.ServiceClients.CitizenWallet;

namespace Sorcha.McpServer.Tests.Tools.Citizen;

/// <summary>
/// Feature 140 Wave 3: MyDeviceRevokeTool revokes the caller's own device (consumer tier).
/// </summary>
public class MyDeviceRevokeToolTests
{
    private readonly Mock<IMcpAuthorizationService> _authServiceMock = new();
    private readonly Mock<IServiceAvailabilityTracker> _availabilityTrackerMock = new();
    private readonly Mock<ICitizenWalletClient> _walletClientMock = new();

    private MyDeviceRevokeTool CreateTool() => new(
        _authServiceMock.Object,
        _availabilityTrackerMock.Object,
        _walletClientMock.Object,
        Mock.Of<ILogger<MyDeviceRevokeTool>>());

    private void Allow()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_my_device_revoke")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Wallet")).Returns(true);
    }

    [Fact]
    public async Task RevokeAsync_Unauthorized_ReturnsUnauthorized()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_my_device_revoke")).Returns(false);

        var result = await CreateTool().RevokeAsync(Guid.NewGuid());

        result.Status.Should().Be("Unauthorized");
        _walletClientMock.Verify(c => c.RevokeDeviceAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RevokeAsync_EmptyDeviceId_ReturnsError()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_my_device_revoke")).Returns(true);

        var result = await CreateTool().RevokeAsync(Guid.Empty);

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task RevokeAsync_Success_CallsClient()
    {
        Allow();
        var deviceId = Guid.NewGuid();
        _walletClientMock.Setup(c => c.RevokeDeviceAsync(deviceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await CreateTool().RevokeAsync(deviceId);

        result.Status.Should().Be("Success");
        result.DeviceId.Should().Be(deviceId);
        _walletClientMock.Verify(c => c.RevokeDeviceAsync(deviceId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RevokeAsync_NotOwned_ReturnsNotFound()
    {
        Allow();
        _walletClientMock.Setup(c => c.RevokeDeviceAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await CreateTool().RevokeAsync(Guid.NewGuid());

        result.Status.Should().Be("NotFound");
    }
}
