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
/// Feature 140 Wave 3: PendingApplicationsTool reads the caller's pending-application
/// notice via the typed <see cref="ICitizenWalletClient"/> (consumer tier).
/// </summary>
public class PendingApplicationsToolTests
{
    private readonly Mock<IMcpAuthorizationService> _authServiceMock = new();
    private readonly Mock<IServiceAvailabilityTracker> _availabilityTrackerMock = new();
    private readonly Mock<ICitizenWalletClient> _walletClientMock = new();

    private PendingApplicationsTool CreateTool() => new(
        _authServiceMock.Object,
        _availabilityTrackerMock.Object,
        _walletClientMock.Object,
        Mock.Of<ILogger<PendingApplicationsTool>>());

    private void Allow()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_pending_applications")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Wallet")).Returns(true);
    }

    [Fact]
    public async Task GetAsync_Unauthorized_ReturnsUnauthorized()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_pending_applications")).Returns(false);

        var result = await CreateTool().GetAsync();

        result.Status.Should().Be("Unauthorized");
        _walletClientMock.Verify(c => c.GetPendingApplicationAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetAsync_NonePending_ReturnsSuccessWithNoPending()
    {
        Allow();
        _walletClientMock.Setup(c => c.GetPendingApplicationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PendingApplicationResponse { Notice = null });

        var result = await CreateTool().GetAsync();

        result.Status.Should().Be("Success");
        result.HasPending.Should().BeFalse();
        result.Label.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_Pending_ReturnsNotice()
    {
        Allow();
        var setAt = DateTimeOffset.UtcNow;
        _walletClientMock.Setup(c => c.GetPendingApplicationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PendingApplicationResponse
            {
                Notice = new PendingApplicationNoticeDto { Label = "Assured Identity", SetAt = setAt }
            });

        var result = await CreateTool().GetAsync();

        result.Status.Should().Be("Success");
        result.HasPending.Should().BeTrue();
        result.Label.Should().Be("Assured Identity");
        result.SetAt.Should().Be(setAt);
    }
}
