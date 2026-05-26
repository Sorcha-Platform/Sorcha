// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.McpServer.Tools.Admin;
using Sorcha.Register.Models;
using Sorcha.ServiceClients.Register;

namespace Sorcha.McpServer.Tests.Tools.Admin;

/// <summary>
/// Feature 140 Wave 1: TransactionStatusTool routes through the typed <see cref="IRegisterServiceClient"/>.
/// </summary>
public class TransactionStatusToolTests
{
    private readonly Mock<IMcpAuthorizationService> _authServiceMock = new();
    private readonly Mock<IServiceAvailabilityTracker> _availabilityTrackerMock = new();
    private readonly Mock<IRegisterServiceClient> _registerClientMock = new();

    private TransactionStatusTool CreateTool() => new(
        _authServiceMock.Object,
        _availabilityTrackerMock.Object,
        _registerClientMock.Object,
        Mock.Of<ILogger<TransactionStatusTool>>());

    private void Allow()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_transaction_status")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Register")).Returns(true);
    }

    [Fact]
    public async Task GetStatusAsync_Unauthorized_ReturnsUnauthorized()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_transaction_status")).Returns(false);

        var result = await CreateTool().GetStatusAsync("reg-1", "tx-1");

        result.Status.Should().Be("Unauthorized");
        result.Lifecycle.Should().BeNull();
    }

    [Fact]
    public async Task GetStatusAsync_MissingArgs_ReturnsError()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_transaction_status")).Returns(true);

        var result = await CreateTool().GetStatusAsync("reg-1", "");

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task GetStatusAsync_ClientReturnsNull_ReturnsNotFound()
    {
        Allow();
        _registerClientMock
            .Setup(c => c.GetTransactionStatusAsync("reg-1", "tx-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((TransactionStatusResponse?)null);

        var result = await CreateTool().GetStatusAsync("reg-1", "tx-1");

        result.Status.Should().Be("NotFound");
        _availabilityTrackerMock.Verify(a => a.RecordSuccess("Register"), Times.Once);
    }

    [Fact]
    public async Task GetStatusAsync_Revoked_ReturnsSuccessWithLifecycle()
    {
        Allow();
        var status = new TransactionStatusResponse
        {
            TransactionId = "tx-1",
            Status = TransactionLifecycleStatus.Revoked,
            RevocationTxId = "rev-9",
            Reason = RevocationReason.Compromised,
            RevokedAt = DateTimeOffset.UtcNow
        };
        _registerClientMock
            .Setup(c => c.GetTransactionStatusAsync("reg-1", "tx-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(status);

        var result = await CreateTool().GetStatusAsync("reg-1", "tx-1");

        result.Status.Should().Be("Success");
        result.Lifecycle.Should().NotBeNull();
        result.Lifecycle!.Status.Should().Be(TransactionLifecycleStatus.Revoked);
        result.Lifecycle.RevocationTxId.Should().Be("rev-9");
    }
}
