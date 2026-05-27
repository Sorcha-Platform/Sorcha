// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.McpServer.Tools.Citizen;
using Sorcha.ServiceClients.CitizenWallet;

namespace Sorcha.McpServer.Tests.Tools.Citizen;

/// <summary>
/// Feature 140 Wave 3: MyDeviceRenameTool renames the caller's own device (consumer tier).
/// </summary>
public class MyDeviceRenameToolTests
{
    private readonly Mock<IMcpAuthorizationService> _authServiceMock = new();
    private readonly Mock<IServiceAvailabilityTracker> _availabilityTrackerMock = new();
    private readonly Mock<ICitizenWalletClient> _walletClientMock = new();

    private MyDeviceRenameTool CreateTool() => new(
        _authServiceMock.Object,
        _availabilityTrackerMock.Object,
        _walletClientMock.Object,
        Mock.Of<ILogger<MyDeviceRenameTool>>());

    private void Allow()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_my_device_rename")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Wallet")).Returns(true);
    }

    [Fact]
    public async Task RenameAsync_Unauthorized_ReturnsUnauthorized()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_my_device_rename")).Returns(false);

        var result = await CreateTool().RenameAsync(Guid.NewGuid(), "New label");

        result.Status.Should().Be("Unauthorized");
        _walletClientMock.Verify(c => c.RenameDeviceAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RenameAsync_EmptyDeviceId_ReturnsError()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_my_device_rename")).Returns(true);

        var result = await CreateTool().RenameAsync(Guid.Empty, "New label");

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task RenameAsync_EmptyLabel_ReturnsError()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_my_device_rename")).Returns(true);

        var result = await CreateTool().RenameAsync(Guid.NewGuid(), "  ");

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task RenameAsync_Success_CallsClient()
    {
        Allow();
        var deviceId = Guid.NewGuid();
        _walletClientMock.Setup(c => c.RenameDeviceAsync(deviceId, "New label", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await CreateTool().RenameAsync(deviceId, "New label");

        result.Status.Should().Be("Success");
        result.DeviceId.Should().Be(deviceId);
        result.Label.Should().Be("New label");
        _walletClientMock.Verify(c => c.RenameDeviceAsync(deviceId, "New label", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RenameAsync_NotOwned_ReturnsNotFound()
    {
        Allow();
        _walletClientMock.Setup(c => c.RenameDeviceAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await CreateTool().RenameAsync(Guid.NewGuid(), "New label");

        result.Status.Should().Be("NotFound");
    }
}
