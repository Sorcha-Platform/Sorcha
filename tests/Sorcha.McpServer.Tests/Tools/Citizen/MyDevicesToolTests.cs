// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.CitizenWallet.Abstractions.Models;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.McpServer.Tools.Citizen;
using Sorcha.ServiceClients.CitizenWallet;

namespace Sorcha.McpServer.Tests.Tools.Citizen;

/// <summary>
/// Feature 140 Wave 3: MyDevicesTool lists the caller's own enrolled devices (consumer tier).
/// </summary>
public class MyDevicesToolTests
{
    private readonly Mock<IMcpAuthorizationService> _authServiceMock = new();
    private readonly Mock<IServiceAvailabilityTracker> _availabilityTrackerMock = new();
    private readonly Mock<ICitizenWalletClient> _walletClientMock = new();

    private MyDevicesTool CreateTool() => new(
        _authServiceMock.Object,
        _availabilityTrackerMock.Object,
        _walletClientMock.Object,
        Mock.Of<ILogger<MyDevicesTool>>());

    private void Allow()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_my_devices")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Wallet")).Returns(true);
    }

    [Fact]
    public async Task ListAsync_Unauthorized_ReturnsUnauthorized()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_my_devices")).Returns(false);

        var result = await CreateTool().ListAsync();

        result.Status.Should().Be("Unauthorized");
        _walletClientMock.Verify(c => c.ListDevicesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ListAsync_Success_MapsDevices()
    {
        Allow();
        var deviceId = Guid.NewGuid();
        _walletClientMock.Setup(c => c.ListDevicesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeviceListResponse
            {
                Devices =
                [
                    new DeviceSummary
                    {
                        DeviceId = deviceId,
                        Label = "Pixel 9",
                        Platform = "Android",
                        Status = DeviceStatus.Active,
                        EnrolledAt = DateTimeOffset.UtcNow,
                        DelegationExpiresAt = DateTimeOffset.UtcNow.AddDays(30)
                    }
                ]
            });

        var result = await CreateTool().ListAsync();

        result.Status.Should().Be("Success");
        result.Devices.Should().ContainSingle();
        result.Devices[0].DeviceId.Should().Be(deviceId);
        result.Devices[0].Status.Should().Be("Active");
    }

    [Fact]
    public async Task ListAsync_Timeout_ReturnsTimeout()
    {
        Allow();
        _walletClientMock.Setup(c => c.ListDevicesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException());

        var result = await CreateTool().ListAsync();

        result.Status.Should().Be("Timeout");
    }
}
