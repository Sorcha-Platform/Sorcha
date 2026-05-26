// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.McpServer.Tools.Admin;
using Sorcha.Register.Models.Enums;
using Sorcha.Register.Models.LocalRelationship;
using Sorcha.ServiceClients.Register;

namespace Sorcha.McpServer.Tests.Tools.Admin;

/// <summary>
/// Feature 140 Wave 1: RegisterSyncStateTool routes through the typed <see cref="IRegisterServiceClient"/>.
/// </summary>
public class RegisterSyncStateToolTests
{
    private readonly Mock<IMcpAuthorizationService> _authServiceMock = new();
    private readonly Mock<IServiceAvailabilityTracker> _availabilityTrackerMock = new();
    private readonly Mock<IRegisterServiceClient> _registerClientMock = new();

    private RegisterSyncStateTool CreateTool() => new(
        _authServiceMock.Object,
        _availabilityTrackerMock.Object,
        _registerClientMock.Object,
        Mock.Of<ILogger<RegisterSyncStateTool>>());

    private void Allow()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_register_sync_state")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Register")).Returns(true);
    }

    [Fact]
    public async Task GetSyncStateAsync_Unauthorized_ReturnsUnauthorized()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_register_sync_state")).Returns(false);

        var result = await CreateTool().GetSyncStateAsync("reg-1");

        result.Status.Should().Be("Unauthorized");
        result.SyncState.Should().BeNull();
    }

    [Fact]
    public async Task GetSyncStateAsync_EmptyRegisterId_ReturnsError()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_register_sync_state")).Returns(true);

        var result = await CreateTool().GetSyncStateAsync("");

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task GetSyncStateAsync_ClientReturnsNull_ReturnsNotFound()
    {
        Allow();
        _registerClientMock
            .Setup(c => c.GetSyncStateAsync("reg-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RegisterSyncStateView?)null);

        var result = await CreateTool().GetSyncStateAsync("reg-1");

        result.Status.Should().Be("NotFound");
        _availabilityTrackerMock.Verify(a => a.RecordSuccess("Register"), Times.Once);
    }

    [Fact]
    public async Task GetSyncStateAsync_Success_ReturnsView()
    {
        Allow();
        var view = new RegisterSyncStateView(
            "reg-1", RegisterSyncState.CaughtUp, 42, 42, 3, DateTimeOffset.UtcNow, false, null, null);
        _registerClientMock
            .Setup(c => c.GetSyncStateAsync("reg-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(view);

        var result = await CreateTool().GetSyncStateAsync("reg-1");

        result.Status.Should().Be("Success");
        result.SyncState.Should().NotBeNull();
        result.SyncState!.State.Should().Be(RegisterSyncState.CaughtUp);
        result.SyncState.LocalHeight.Should().Be(42);
    }
}
