// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.McpServer.Tools.Participant;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Enums;
using Sorcha.ServiceClients.Register;

namespace Sorcha.McpServer.Tests.Tools.Participant;

/// <summary>
/// Spec 139 US4: TransactionHistoryTool reads via the typed <see cref="IRegisterServiceClient"/>
/// (routes pinned, caller token forwarded), so these tests mock the client rather than HTTP.
/// </summary>
public sealed class TransactionHistoryToolTests
{
    private readonly Mock<IMcpAuthorizationService> _authServiceMock = new();
    private readonly Mock<IServiceAvailabilityTracker> _availabilityTrackerMock = new();
    private readonly Mock<IRegisterServiceClient> _registerClientMock = new();

    private TransactionHistoryTool CreateTool() => new(
        _authServiceMock.Object,
        _availabilityTrackerMock.Object,
        _registerClientMock.Object,
        Mock.Of<ILogger<TransactionHistoryTool>>());

    private void Allow()
    {
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_transaction_history")).Returns(true);
        _availabilityTrackerMock.Setup(x => x.IsServiceAvailable("Register")).Returns(true);
    }

    private static TransactionModel Tx(string txId, string register, string? instance, uint? action, string sender) => new()
    {
        TxId = txId,
        RegisterId = register,
        SenderWallet = sender,
        Signature = "sig...",
        TimeStamp = DateTime.UtcNow,
        MetaData = new TransactionMetaData
        {
            TransactionType = TransactionType.Action,
            InstanceId = instance,
            ActionId = action
        }
    };

    [Fact]
    public async Task GetTransactionHistoryAsync_WhenUnauthorized_ReturnsUnauthorizedStatus()
    {
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_transaction_history")).Returns(false);

        var result = await CreateTool().GetTransactionHistoryAsync("reg-1");

        result.Status.Should().Be("Unauthorized");
        result.Message.Should().Contain("consumer- or platform-tier");
    }

    [Fact]
    public async Task GetTransactionHistoryAsync_EmptyRegisterId_ReturnsErrorStatus()
    {
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_transaction_history")).Returns(true);

        var result = await CreateTool().GetTransactionHistoryAsync("");

        result.Status.Should().Be("Error");
        result.Message.Should().Contain("required");
    }

    [Fact]
    public async Task GetTransactionHistoryAsync_WhenServiceUnavailable_ReturnsUnavailableStatus()
    {
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_transaction_history")).Returns(true);
        _availabilityTrackerMock.Setup(x => x.IsServiceAvailable("Register")).Returns(false);

        var result = await CreateTool().GetTransactionHistoryAsync("reg-1");

        result.Status.Should().Be("Unavailable");
        result.Message.Should().Contain("Register service");
    }

    [Fact]
    public async Task GetTransactionHistoryAsync_RegisterWide_ReturnsPagedTransactions()
    {
        Allow();

        _registerClientMock
            .Setup(c => c.GetTransactionsAsync("register-123", 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionPage
            {
                Page = 1,
                PageSize = 20,
                Total = 2,
                Transactions =
                [
                    Tx("tx-1", "register-123", "wf-1", 1, "addr-1"),
                    Tx("tx-2", "register-123", "wf-1", 2, "addr-2")
                ]
            });

        var result = await CreateTool().GetTransactionHistoryAsync("register-123");

        result.Status.Should().Be("Success");
        result.Transactions.Should().HaveCount(2);
        result.Transactions[0].TransactionId.Should().Be("tx-1");
        result.Transactions[0].TransactionType.Should().Be("Action");
        result.Transactions[0].WorkflowInstanceId.Should().Be("wf-1");
        result.Transactions[0].ActionId.Should().Be(1);
        result.Transactions[0].Submitter.Should().Be("addr-1");
        result.TotalCount.Should().Be(2);
        _availabilityTrackerMock.Verify(x => x.RecordSuccess("Register"), Times.Once);
    }

    [Fact]
    public async Task GetTransactionHistoryAsync_EmptyHistory_ReturnsSuccessWithEmptyList()
    {
        Allow();

        _registerClientMock
            .Setup(c => c.GetTransactionsAsync("register-123", It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionPage { Page = 1, PageSize = 20, Total = 0, Transactions = [] });

        var result = await CreateTool().GetTransactionHistoryAsync("register-123");

        result.Status.Should().Be("Success");
        result.Transactions.Should().BeEmpty();
        result.Message.Should().Contain("0 transaction");
    }

    [Fact]
    public async Task GetTransactionHistoryAsync_WithWorkflowFilter_UsesInstanceRead()
    {
        Allow();

        _registerClientMock
            .Setup(c => c.GetTransactionsByInstanceIdAsync("reg-1", "wf-456", It.IsAny<CancellationToken>()))
            .ReturnsAsync([Tx("tx-1", "reg-1", "wf-456", 1, "addr-1")]);

        var result = await CreateTool().GetTransactionHistoryAsync("reg-1", workflowInstanceId: "wf-456");

        result.Status.Should().Be("Success");
        result.Transactions.Should().HaveCount(1);
        result.Transactions[0].WorkflowInstanceId.Should().Be("wf-456");
        _registerClientMock.Verify(
            c => c.GetTransactionsByInstanceIdAsync("reg-1", "wf-456", It.IsAny<CancellationToken>()), Times.Once);
        _registerClientMock.Verify(
            c => c.GetTransactionsAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetTransactionHistoryAsync_WithPagination_PassesCorrectParameters()
    {
        Allow();

        _registerClientMock
            .Setup(c => c.GetTransactionsAsync("reg-1", 3, 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionPage { Page = 3, PageSize = 25, Total = 0, Transactions = [] });

        await CreateTool().GetTransactionHistoryAsync("reg-1", page: 3, pageSize: 25);

        _registerClientMock.Verify(
            c => c.GetTransactionsAsync("reg-1", 3, 25, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetTransactionHistoryAsync_WithTimeout_ReturnsTimeoutStatus()
    {
        Allow();

        _registerClientMock
            .Setup(c => c.GetTransactionsAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException());

        var result = await CreateTool().GetTransactionHistoryAsync("reg-1");

        result.Status.Should().Be("Timeout");
        _availabilityTrackerMock.Verify(x => x.RecordFailure("Register"), Times.Once);
    }

    [Fact]
    public async Task GetTransactionHistoryAsync_WithHttpException_ReturnsErrorStatus()
    {
        Allow();

        _registerClientMock
            .Setup(c => c.GetTransactionsAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var result = await CreateTool().GetTransactionHistoryAsync("reg-1");

        result.Status.Should().Be("Error");
        result.Message.Should().Contain("Connection refused");
    }
}
