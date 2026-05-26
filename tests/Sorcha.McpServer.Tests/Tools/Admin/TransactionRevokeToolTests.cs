// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.McpServer.Tools.Admin;
using Sorcha.ServiceClients.Register;

namespace Sorcha.McpServer.Tests.Tools.Admin;

/// <summary>
/// Feature 140 Wave 1: TransactionRevokeTool routes through the typed <see cref="IRegisterServiceClient"/>.
/// </summary>
public class TransactionRevokeToolTests
{
    private readonly Mock<IMcpAuthorizationService> _authServiceMock = new();
    private readonly Mock<IServiceAvailabilityTracker> _availabilityTrackerMock = new();
    private readonly Mock<IRegisterServiceClient> _registerClientMock = new();

    private TransactionRevokeTool CreateTool() => new(
        _authServiceMock.Object,
        _availabilityTrackerMock.Object,
        _registerClientMock.Object,
        Mock.Of<ILogger<TransactionRevokeTool>>());

    private void Allow()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_transaction_revoke")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Register")).Returns(true);
    }

    [Fact]
    public async Task RevokeAsync_Unauthorized_ReturnsUnauthorized()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_transaction_revoke")).Returns(false);

        var result = await CreateTool().RevokeAsync("reg-1", "tx-1", "Erroneous");

        result.Status.Should().Be("Unauthorized");
        _registerClientMock.Verify(c => c.RevokeTransactionAsync(
            It.IsAny<string>(), It.IsAny<RevokeTransactionClientRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RevokeAsync_MissingArgs_ReturnsError()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_transaction_revoke")).Returns(true);

        var result = await CreateTool().RevokeAsync("reg-1", "", "Erroneous");

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task RevokeAsync_InvalidReason_ReturnsError()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_transaction_revoke")).Returns(true);

        var result = await CreateTool().RevokeAsync("reg-1", "tx-1", "Nonsense");

        result.Status.Should().Be("Error");
        result.Message.Should().Contain("Reason must be");
    }

    [Fact]
    public async Task RevokeAsync_SupersededWithoutReplacement_ReturnsError()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_transaction_revoke")).Returns(true);

        var result = await CreateTool().RevokeAsync("reg-1", "tx-1", "Superseded");

        result.Status.Should().Be("Error");
        result.Message.Should().Contain("supersededByTxId");
    }

    [Fact]
    public async Task RevokeAsync_Unavailable_ReturnsUnavailable()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_transaction_revoke")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Register")).Returns(false);

        var result = await CreateTool().RevokeAsync("reg-1", "tx-1", "Erroneous");

        result.Status.Should().Be("Unavailable");
    }

    [Fact]
    public async Task RevokeAsync_ClientReturnsNull_ReturnsError()
    {
        Allow();
        _registerClientMock
            .Setup(c => c.RevokeTransactionAsync("reg-1", It.IsAny<RevokeTransactionClientRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RevokeTransactionResult?)null);

        var result = await CreateTool().RevokeAsync("reg-1", "tx-1", "Erroneous");

        result.Status.Should().Be("Error");
        result.Message.Should().Contain("not accepted");
        _availabilityTrackerMock.Verify(a => a.RecordSuccess("Register"), Times.Once);
    }

    [Fact]
    public async Task RevokeAsync_Success_ReturnsRevocationTxId()
    {
        Allow();
        _registerClientMock
            .Setup(c => c.RevokeTransactionAsync("reg-1", It.IsAny<RevokeTransactionClientRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RevokeTransactionResult
            {
                RevocationTxId = "rev-42",
                OriginalTxId = "tx-1",
                Status = "submitted"
            });

        var result = await CreateTool().RevokeAsync("reg-1", "tx-1", "Compromised", signerWalletAddress: "ws1abc");

        result.Status.Should().Be("Success");
        result.RevocationTxId.Should().Be("rev-42");
        result.OriginalTxId.Should().Be("tx-1");
        _registerClientMock.Verify(c => c.RevokeTransactionAsync(
            "reg-1",
            It.Is<RevokeTransactionClientRequest>(r => r.OriginalTxId == "tx-1" && r.Reason == "Compromised" && r.SignerWalletAddress == "ws1abc"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RevokeAsync_SupersededWithReplacement_Succeeds()
    {
        Allow();
        _registerClientMock
            .Setup(c => c.RevokeTransactionAsync("reg-1", It.IsAny<RevokeTransactionClientRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RevokeTransactionResult { RevocationTxId = "rev-7", OriginalTxId = "tx-1", Status = "submitted" });

        var result = await CreateTool().RevokeAsync("reg-1", "tx-1", "Superseded", supersededByTxId: "tx-2");

        result.Status.Should().Be("Success");
        _registerClientMock.Verify(c => c.RevokeTransactionAsync(
            "reg-1",
            It.Is<RevokeTransactionClientRequest>(r => r.SupersededByTxId == "tx-2"),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
