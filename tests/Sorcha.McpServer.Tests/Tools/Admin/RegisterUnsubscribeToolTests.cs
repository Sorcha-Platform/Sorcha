// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.McpServer.Tools.Admin;
using Sorcha.ServiceClients.Peer;

namespace Sorcha.McpServer.Tests.Tools.Admin;

/// <summary>
/// Feature 140 Wave 1: RegisterUnsubscribeTool routes through the typed <see cref="IPeerServiceClient"/>.
/// </summary>
public class RegisterUnsubscribeToolTests
{
    private readonly Mock<IMcpAuthorizationService> _authServiceMock = new();
    private readonly Mock<IServiceAvailabilityTracker> _availabilityTrackerMock = new();
    private readonly Mock<IPeerServiceClient> _peerClientMock = new();

    private RegisterUnsubscribeTool CreateTool() => new(
        _authServiceMock.Object,
        _availabilityTrackerMock.Object,
        _peerClientMock.Object,
        Mock.Of<ILogger<RegisterUnsubscribeTool>>());

    private void Allow()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_register_unsubscribe")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Peer")).Returns(true);
    }

    [Fact]
    public async Task UnsubscribeAsync_Unauthorized_ReturnsUnauthorized()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_register_unsubscribe")).Returns(false);

        var result = await CreateTool().UnsubscribeAsync("reg-1");

        result.Status.Should().Be("Unauthorized");
        _peerClientMock.Verify(c => c.UnsubscribeFromRegisterAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UnsubscribeAsync_EmptyRegisterId_ReturnsError()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_register_unsubscribe")).Returns(true);

        var result = await CreateTool().UnsubscribeAsync("  ");

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task UnsubscribeAsync_Unavailable_ReturnsUnavailable()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_register_unsubscribe")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Peer")).Returns(false);

        var result = await CreateTool().UnsubscribeAsync("reg-1");

        result.Status.Should().Be("Unavailable");
    }

    [Fact]
    public async Task UnsubscribeAsync_Success_CallsClientAndReturnsSuccess()
    {
        Allow();

        var result = await CreateTool().UnsubscribeAsync("reg-1");

        result.Status.Should().Be("Success");
        result.RegisterId.Should().Be("reg-1");
        _peerClientMock.Verify(c => c.UnsubscribeFromRegisterAsync("reg-1", It.IsAny<CancellationToken>()), Times.Once);
        _availabilityTrackerMock.Verify(a => a.RecordSuccess("Peer"), Times.Once);
    }
}
