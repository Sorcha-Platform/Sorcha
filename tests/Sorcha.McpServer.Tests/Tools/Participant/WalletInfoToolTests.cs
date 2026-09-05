// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.McpServer.Tools.Participant;
using ClientWalletInfo = Sorcha.ServiceClients.Wallet.WalletInfo;

namespace Sorcha.McpServer.Tests.Tools.Participant;

/// <summary>
/// Spec 139 US4: WalletInfoTool reads via the typed <see cref="IWalletServiceClient"/>
/// (route pinned, caller token forwarded), so these tests mock the client rather than HTTP.
/// </summary>
public sealed class WalletInfoToolTests
{
    private readonly Mock<IMcpAuthorizationService> _authServiceMock = new();
    private readonly Mock<IServiceAvailabilityTracker> _availabilityTrackerMock = new();
    private readonly Mock<Sorcha.ServiceClients.Wallet.IWalletServiceClient> _walletClientMock = new();

    private WalletInfoTool CreateTool() => new(
        _authServiceMock.Object,
        _availabilityTrackerMock.Object,
        _walletClientMock.Object,
        Mock.Of<ILogger<WalletInfoTool>>());

    private void Allow()
    {
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_wallet_info")).Returns(true);
        _availabilityTrackerMock.Setup(x => x.IsServiceAvailable("Wallet")).Returns(true);
    }

    [Fact]
    public async Task GetWalletInfoAsync_WhenUnauthorized_ReturnsUnauthorizedStatus()
    {
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_wallet_info")).Returns(false);

        var result = await CreateTool().GetWalletInfoAsync("addr-1");

        result.Status.Should().Be("Unauthorized");
        result.Message.Should().Contain("consumer- or platform-tier");
    }

    [Fact]
    public async Task GetWalletInfoAsync_EmptyAddress_ReturnsErrorStatus()
    {
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_wallet_info")).Returns(true);

        var result = await CreateTool().GetWalletInfoAsync("");

        result.Status.Should().Be("Error");
        result.Message.Should().Contain("required");
    }

    [Fact]
    public async Task GetWalletInfoAsync_WhenServiceUnavailable_ReturnsUnavailableStatus()
    {
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_wallet_info")).Returns(true);
        _availabilityTrackerMock.Setup(x => x.IsServiceAvailable("Wallet")).Returns(false);

        var result = await CreateTool().GetWalletInfoAsync("addr-1");

        result.Status.Should().Be("Unavailable");
        result.Message.Should().Contain("Wallet service");
    }

    [Fact]
    public async Task GetWalletInfoAsync_WithSuccessfulResponse_ReturnsWalletInfo()
    {
        Allow();

        _walletClientMock
            .Setup(c => c.GetWalletAsync("addr1qx4j7", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClientWalletInfo
            {
                Address = "addr1qx4j7",
                Name = "wallet-123",
                PublicKey = "ABCDpublickey...",
                Algorithm = "ED25519",
                Status = "Active",
                Owner = "user-1",
                Tenant = "tenant-1",
                CreatedAt = DateTime.UtcNow.AddMonths(-1)
            });

        var result = await CreateTool().GetWalletInfoAsync("addr1qx4j7");

        result.Status.Should().Be("Success");
        result.Wallet.Should().NotBeNull();
        result.Wallet!.WalletId.Should().Be("wallet-123");
        result.Wallet.PrimaryAddress.Should().Be("addr1qx4j7");
        result.Wallet.Algorithm.Should().Be("ED25519");
        result.Wallet.StatusValue.Should().Be("Active");
        result.Wallet.Addresses.Should().HaveCount(1);
        result.Wallet.Addresses[0].IsDefault.Should().BeTrue();
        _availabilityTrackerMock.Verify(x => x.RecordSuccess("Wallet"), Times.Once);
    }

    [Fact]
    public async Task GetWalletInfoAsync_NotFound_ReturnsNotFoundStatus()
    {
        Allow();

        _walletClientMock
            .Setup(c => c.GetWalletAsync("missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ClientWalletInfo?)null);

        var result = await CreateTool().GetWalletInfoAsync("missing");

        result.Status.Should().Be("NotFound");
        result.Message.Should().Contain("missing");
        result.Wallet.Should().BeNull();
    }

    [Fact]
    public async Task GetWalletInfoAsync_WithTimeout_ReturnsTimeoutStatus()
    {
        Allow();

        _walletClientMock
            .Setup(c => c.GetWalletAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException());

        var result = await CreateTool().GetWalletInfoAsync("addr-1");

        result.Status.Should().Be("Timeout");
        _availabilityTrackerMock.Verify(x => x.RecordFailure("Wallet"), Times.Once);
    }

    [Fact]
    public async Task GetWalletInfoAsync_WithHttpException_ReturnsErrorStatus()
    {
        Allow();

        _walletClientMock
            .Setup(c => c.GetWalletAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var result = await CreateTool().GetWalletInfoAsync("addr-1");

        result.Status.Should().Be("Error");
        result.Message.Should().Contain("Connection refused");
    }
}
