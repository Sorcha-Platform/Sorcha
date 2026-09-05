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
/// Spec 139 US4 / MCP P0 Task 5: RegisterQueryTool reads via the typed
/// <see cref="IRegisterServiceClient"/> (route pinned, caller token forwarded), so these tests
/// mock the client rather than HTTP.
/// </summary>
public sealed class RegisterQueryToolTests
{
    private readonly Mock<IMcpAuthorizationService> _authServiceMock = new();
    private readonly Mock<IServiceAvailabilityTracker> _availabilityTrackerMock = new();
    private readonly Mock<IRegisterServiceClient> _registerClientMock = new();

    private RegisterQueryTool CreateTool() => new(
        _authServiceMock.Object,
        _availabilityTrackerMock.Object,
        _registerClientMock.Object,
        Mock.Of<ILogger<RegisterQueryTool>>());

    private void Allow()
    {
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_register_query")).Returns(true);
        _availabilityTrackerMock.Setup(x => x.IsServiceAvailable("Register")).Returns(true);
    }

    private static TransactionModel Tx(string txId, string sender, string? blueprintId = null, string? instanceId = null, uint? actionId = null) => new()
    {
        TxId = txId,
        RegisterId = "register-123",
        SenderWallet = sender,
        Signature = "sig...",
        DocketNumber = 5,
        TimeStamp = DateTime.UtcNow,
        PayloadCount = 1,
        MetaData = new TransactionMetaData
        {
            TransactionType = TransactionType.Action,
            BlueprintId = blueprintId,
            InstanceId = instanceId,
            ActionId = actionId
        }
    };

    [Fact]
    public async Task QueryRegisterAsync_WhenUnauthorized_ReturnsUnauthorizedStatus()
    {
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_register_query")).Returns(false);

        var result = await CreateTool().QueryRegisterAsync("register-123");

        result.Status.Should().Be("Unauthorized");
        result.Message.Should().Contain("sorcha:participant");
    }

    [Fact]
    public async Task QueryRegisterAsync_WithEmptyRegisterId_ReturnsError()
    {
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_register_query")).Returns(true);

        var result = await CreateTool().QueryRegisterAsync("");

        result.Status.Should().Be("Error");
        result.Message.Should().Contain("Register ID is required");
    }

    [Fact]
    public async Task QueryRegisterAsync_WhenServiceUnavailable_ReturnsUnavailableStatus()
    {
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_register_query")).Returns(true);
        _availabilityTrackerMock.Setup(x => x.IsServiceAvailable("Register")).Returns(false);

        var result = await CreateTool().QueryRegisterAsync("register-123");

        result.Status.Should().Be("Unavailable");
        result.Message.Should().Contain("Register service");
    }

    [Fact]
    public async Task QueryRegisterAsync_WithSuccessfulResponse_ReturnsRecords()
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
                    Tx("tx-1", "addr-1", blueprintId: "bp-1", instanceId: "wf-1", actionId: 1),
                    Tx("tx-2", "addr-2", blueprintId: "bp-1", instanceId: "wf-1", actionId: 2)
                ]
            });

        var result = await CreateTool().QueryRegisterAsync("register-123");

        result.Status.Should().Be("Success");
        result.Records.Should().HaveCount(2);
        result.Records[0].TransactionId.Should().Be("tx-1");
        result.Records[0].SenderWallet.Should().Be("addr-1");
        result.Records[0].DocketNumber.Should().Be((ulong)5);
        result.Records[0].BlueprintId.Should().Be("bp-1");
        result.Records[0].WorkflowInstanceId.Should().Be("wf-1");
        result.Records[0].ActionId.Should().Be(1);
        result.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task QueryRegisterAsync_WithEmptyResults_ReturnsSuccessWithEmptyList()
    {
        Allow();

        _registerClientMock
            .Setup(c => c.GetTransactionsAsync("register-123", It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionPage { Page = 1, PageSize = 20, Total = 0, Transactions = [] });

        var result = await CreateTool().QueryRegisterAsync("register-123");

        result.Status.Should().Be("Success");
        result.Records.Should().BeEmpty();
        result.Message.Should().Contain("0 record");
    }

    [Fact]
    public async Task QueryRegisterAsync_WithPagination_PassesCorrectParameters()
    {
        Allow();

        _registerClientMock
            .Setup(c => c.GetTransactionsAsync("register-123", 3, 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionPage { Page = 3, PageSize = 25, Total = 0, Transactions = [] });

        await CreateTool().QueryRegisterAsync("register-123", page: 3, pageSize: 25);

        _registerClientMock.Verify(
            c => c.GetTransactionsAsync("register-123", 3, 25, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task QueryRegisterAsync_WithPageSizeOverMax_CapsAt100()
    {
        Allow();

        _registerClientMock
            .Setup(c => c.GetTransactionsAsync("register-123", 1, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionPage { Page = 1, PageSize = 100, Total = 0, Transactions = [] });

        await CreateTool().QueryRegisterAsync("register-123", pageSize: 200);

        _registerClientMock.Verify(
            c => c.GetTransactionsAsync("register-123", 1, 100, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task QueryRegisterAsync_WithTimeout_ReturnsTimeoutStatus()
    {
        Allow();

        _registerClientMock
            .Setup(c => c.GetTransactionsAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException());

        var result = await CreateTool().QueryRegisterAsync("register-123");

        result.Status.Should().Be("Timeout");
        _availabilityTrackerMock.Verify(x => x.RecordFailure("Register"), Times.Once);
    }

    [Fact]
    public async Task QueryRegisterAsync_WithHttpException_ReturnsErrorStatus()
    {
        Allow();

        _registerClientMock
            .Setup(c => c.GetTransactionsAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var result = await CreateTool().QueryRegisterAsync("register-123");

        result.Status.Should().Be("Error");
        result.Message.Should().Contain("Connection refused");
    }

    [Fact]
    public async Task QueryRegisterAsync_RecordsSuccessOnSuccessfulResponse()
    {
        Allow();

        _registerClientMock
            .Setup(c => c.GetTransactionsAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionPage { Page = 1, PageSize = 20, Total = 0, Transactions = [] });

        await CreateTool().QueryRegisterAsync("register-123");

        _availabilityTrackerMock.Verify(x => x.RecordSuccess("Register"), Times.Once);
    }
}
