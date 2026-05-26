// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.McpServer.Tools.Admin;
using Sorcha.ServiceClients.Peer;

namespace Sorcha.McpServer.Tests.Tools.Admin;

/// <summary>
/// Feature 140 Wave 1: RegisterSubscribeTool routes through the typed <see cref="IPeerServiceClient"/>.
/// </summary>
public class RegisterSubscribeToolTests
{
    private readonly Mock<IMcpAuthorizationService> _authServiceMock = new();
    private readonly Mock<IServiceAvailabilityTracker> _availabilityTrackerMock = new();
    private readonly Mock<IPeerServiceClient> _peerClientMock = new();

    private RegisterSubscribeTool CreateTool() => new(
        _authServiceMock.Object,
        _availabilityTrackerMock.Object,
        _peerClientMock.Object,
        Mock.Of<ILogger<RegisterSubscribeTool>>());

    private void Allow()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_register_subscribe")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Peer")).Returns(true);
    }

    [Fact]
    public async Task SubscribeAsync_Unauthorized_ReturnsUnauthorized()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_register_subscribe")).Returns(false);

        var result = await CreateTool().SubscribeAsync("reg-1");

        result.Status.Should().Be("Unauthorized");
        _peerClientMock.Verify(c => c.SubscribeToRegisterAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SubscribeAsync_EmptyRegisterId_ReturnsError()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_register_subscribe")).Returns(true);

        var result = await CreateTool().SubscribeAsync("");

        result.Status.Should().Be("Error");
        result.Message.Should().Contain("required");
    }

    [Fact]
    public async Task SubscribeAsync_InvalidMode_ReturnsError()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_register_subscribe")).Returns(true);

        var result = await CreateTool().SubscribeAsync("reg-1", "bogus");

        result.Status.Should().Be("Error");
        result.Message.Should().Contain("full-replica");
    }

    [Fact]
    public async Task SubscribeAsync_Unavailable_ReturnsUnavailable()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_register_subscribe")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Peer")).Returns(false);

        var result = await CreateTool().SubscribeAsync("reg-1");

        result.Status.Should().Be("Unavailable");
    }

    [Fact]
    public async Task SubscribeAsync_Success_CallsClientAndReturnsSuccess()
    {
        Allow();

        var result = await CreateTool().SubscribeAsync("reg-1", "forward-only");

        result.Status.Should().Be("Success");
        result.RegisterId.Should().Be("reg-1");
        result.Mode.Should().Be("forward-only");
        _peerClientMock.Verify(c => c.SubscribeToRegisterAsync("reg-1", "forward-only", It.IsAny<CancellationToken>()), Times.Once);
        _availabilityTrackerMock.Verify(a => a.RecordSuccess("Peer"), Times.Once);
    }

    [Fact]
    public async Task SubscribeAsync_DefaultMode_IsFullReplica()
    {
        Allow();

        var result = await CreateTool().SubscribeAsync("reg-1");

        result.Status.Should().Be("Success");
        result.Mode.Should().Be("full-replica");
        _peerClientMock.Verify(c => c.SubscribeToRegisterAsync("reg-1", "full-replica", It.IsAny<CancellationToken>()), Times.Once);
    }
}
