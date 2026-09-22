// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.McpServer.Tools.Participant;
using Sorcha.Register.Models;
using Sorcha.ServiceClients.Blueprint;
using Sorcha.ServiceClients.Register;
using Sorcha.ServiceClients.Register.Models;
using Sorcha.ServiceClients.Shared;

namespace Sorcha.McpServer.Tests.Tools.Participant;

/// <summary>
/// #1707 — a bounded wait an agent can call instead of relaying a ledger event through a human.
/// The poll interval is injected tiny (10ms) so "met after a delay" and "times out" tests do not
/// actually sleep for anywhere near the real 1s/25s defaults.
/// </summary>
public sealed class AwaitConditionToolTests
{
    private readonly Mock<IMcpAuthorizationService> _auth = new();
    private readonly Mock<IServiceAvailabilityTracker> _availability = new();
    private readonly Mock<IBlueprintServiceClient> _blueprint = new();
    private readonly Mock<IRegisterServiceClient> _register = new();
    private readonly AwaitConditionTool _tool;

    public AwaitConditionToolTests()
    {
        _tool = new AwaitConditionTool(
            _auth.Object, _availability.Object, _blueprint.Object, _register.Object,
            Mock.Of<ILogger<AwaitConditionTool>>(), pollInterval: TimeSpan.FromMilliseconds(10));
    }

    private void Allow()
    {
        _auth.Setup(x => x.CanInvokeTool("sorcha_await_condition")).Returns(true);
        _availability.Setup(x => x.IsServiceAvailable("Register")).Returns(true);
        _availability.Setup(x => x.IsServiceAvailable("Blueprint")).Returns(true);
    }

    // -------------------------------------------------------------------------
    // Cross-cutting validation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AwaitAsync_WhenUnauthorized_ReturnsUnauthorized()
    {
        _auth.Setup(x => x.CanInvokeTool("sorcha_await_condition")).Returns(false);

        var result = await _tool.AwaitAsync("ParticipantActive", registerId: "reg-1", participantName: "buyer");

        result.Status.Should().Be("Unauthorized");
    }

    [Fact]
    public async Task AwaitAsync_WithUnknownCondition_ReturnsError()
    {
        Allow();

        var result = await _tool.AwaitAsync("NotARealCondition");

        result.Status.Should().Be("Error");
        result.Message.Should().Contain("NotARealCondition");
    }

    [Fact]
    public async Task AwaitAsync_ParticipantActive_WithoutRegisterId_ReturnsError()
    {
        Allow();

        var result = await _tool.AwaitAsync("ParticipantActive", participantName: "buyer");

        result.Status.Should().Be("Error");
        result.Message.Should().Contain("registerId");
    }

    [Fact]
    public async Task AwaitAsync_ParticipantActive_WithoutParticipantName_ReturnsError()
    {
        Allow();

        var result = await _tool.AwaitAsync("ParticipantActive", registerId: "reg-1");

        result.Status.Should().Be("Error");
        result.Message.Should().Contain("participantName");
    }

    [Fact]
    public async Task AwaitAsync_InstanceReachesAction_WithoutWorkflowInstanceId_ReturnsError()
    {
        Allow();

        var result = await _tool.AwaitAsync("InstanceReachesAction");

        result.Status.Should().Be("Error");
        result.Message.Should().Contain("workflowInstanceId");
    }

    [Fact]
    public async Task AwaitAsync_InstanceReachesAction_WithNonIntegerActionId_ReturnsError()
    {
        Allow();

        var result = await _tool.AwaitAsync(
            "InstanceReachesAction", workflowInstanceId: "wf-1", actionId: "not-a-number");

        result.Status.Should().Be("Error");
        result.Message.Should().Contain("not-a-number");
    }

    [Fact]
    public async Task AwaitAsync_TransactionSeals_WithoutTransactionId_ReturnsError()
    {
        Allow();

        var result = await _tool.AwaitAsync("TransactionSeals", registerId: "reg-1");

        result.Status.Should().Be("Error");
        result.Message.Should().Contain("transactionId");
    }

    [Fact]
    public async Task AwaitAsync_WhenServiceUnavailable_ReturnsUnavailable()
    {
        _auth.Setup(x => x.CanInvokeTool("sorcha_await_condition")).Returns(true);
        _availability.Setup(x => x.IsServiceAvailable("Register")).Returns(false);

        var result = await _tool.AwaitAsync("ParticipantActive", registerId: "reg-1", participantName: "buyer");

        result.Status.Should().Be("Unavailable");
    }

    // -------------------------------------------------------------------------
    // ParticipantActive
    // -------------------------------------------------------------------------

    private static PublishedParticipantRecord ActiveRecord(string name, string wallet) => new()
    {
        ParticipantId = "pid-1", ParticipantName = name, OrganizationName = "Org",
        Status = "Active", Version = 1, LatestTxId = "tx-1",
        Addresses = [new ParticipantAddressInfo { WalletAddress = wallet, PublicKey = "pk", Algorithm = "ED25519", Primary = true }],
    };

    private static ParticipantPage Page(params PublishedParticipantRecord[] records) =>
        new() { Page = 1, PageSize = 200, Total = records.Length, Participants = [.. records] };

    [Fact]
    public async Task ParticipantActive_MetImmediately_ReturnsMet()
    {
        Allow();
        _register.Setup(r => r.GetRegisterAsync("reg-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sorcha.Register.Models.Register { Id = "reg-1", Name = "Reg" });
        _register.Setup(r => r.GetPublishedParticipantsAsync(
                "reg-1", It.IsAny<int>(), It.IsAny<int>(), "active", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Page(ActiveRecord("buyer", "wallet-abc")));

        var result = await _tool.AwaitAsync("ParticipantActive", registerId: "reg-1", participantName: "buyer");

        result.Status.Should().Be("Success");
        result.Outcome.Should().Be("Met");
        result.Message.Should().Contain("buyer").And.Contain("Active").And.Contain("wallet-abc");
        _register.Verify(r => r.GetPublishedParticipantsAsync(
            "reg-1", It.IsAny<int>(), It.IsAny<int>(), "active", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ParticipantActive_MetAfterDelay_ReturnsMet()
    {
        Allow();
        _register.Setup(r => r.GetRegisterAsync("reg-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sorcha.Register.Models.Register { Id = "reg-1", Name = "Reg" });
        _register.SetupSequence(r => r.GetPublishedParticipantsAsync(
                "reg-1", It.IsAny<int>(), It.IsAny<int>(), "active", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Page())
            .ReturnsAsync(Page(ActiveRecord("buyer", "wallet-abc")));

        var result = await _tool.AwaitAsync(
            "ParticipantActive", registerId: "reg-1", participantName: "buyer", timeoutSeconds: 5);

        result.Outcome.Should().Be("Met");
        _register.Verify(r => r.GetPublishedParticipantsAsync(
            "reg-1", It.IsAny<int>(), It.IsAny<int>(), "active", It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ParticipantActive_NeverPublished_TimesOutAsNotYet()
    {
        Allow();
        _register.Setup(r => r.GetRegisterAsync("reg-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sorcha.Register.Models.Register { Id = "reg-1", Name = "Reg" });
        _register.Setup(r => r.GetPublishedParticipantsAsync(
                "reg-1", It.IsAny<int>(), It.IsAny<int>(), "active", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Page());

        var result = await _tool.AwaitAsync(
            "ParticipantActive", registerId: "reg-1", participantName: "buyer", timeoutSeconds: 1);

        result.Status.Should().Be("Success");
        result.Outcome.Should().Be("NotYet");
        result.Message.Should().Contain("Timed out").And.Contain("sorcha_await_condition again");
    }

    [Fact]
    public async Task ParticipantActive_RegisterDoesNotExist_ReturnsUnreachable()
    {
        Allow();
        _register.Setup(r => r.GetRegisterAsync("ghost-reg", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Sorcha.Register.Models.Register?)null);

        var result = await _tool.AwaitAsync(
            "ParticipantActive", registerId: "ghost-reg", participantName: "buyer", timeoutSeconds: 5);

        result.Status.Should().Be("Success");
        result.Outcome.Should().Be("Unreachable");
        result.Message.Should().Contain("does not exist");
        _register.Verify(r => r.GetPublishedParticipantsAsync(
            It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // -------------------------------------------------------------------------
    // InstanceReachesAction
    // -------------------------------------------------------------------------

    private static ServiceReadResult InstanceBody(
        string id, object state, int[] currentActionIds, string registerId = "reg-1") =>
        new(HttpStatusCode.OK, JsonSerializer.Serialize(new
        {
            id,
            registerId,
            state,
            currentActionIds,
        }));

    [Fact]
    public async Task InstanceReachesAction_MetImmediately_ReturnsMet()
    {
        Allow();
        _blueprint.Setup(b => b.GetWorkflowStatusAsync("wf-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(InstanceBody("wf-1", 0, [2]));

        var result = await _tool.AwaitAsync("InstanceReachesAction", workflowInstanceId: "wf-1", actionId: "2");

        result.Outcome.Should().Be("Met");
        result.Message.Should().Contain("action 2");
        _register.Verify(r => r.GetTransactionsByInstanceIdAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task InstanceReachesAction_MetAfterDelay_ReturnsMet()
    {
        Allow();
        _blueprint.SetupSequence(b => b.GetWorkflowStatusAsync("wf-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(InstanceBody("wf-1", 0, [1]))
            .ReturnsAsync(InstanceBody("wf-1", 0, [2]));
        _register.Setup(r => r.GetTransactionsByInstanceIdAsync("reg-1", "wf-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await _tool.AwaitAsync(
            "InstanceReachesAction", workflowInstanceId: "wf-1", actionId: "2", timeoutSeconds: 5);

        result.Outcome.Should().Be("Met");
    }

    [Fact]
    public async Task InstanceReachesAction_NeverReached_TimesOutAsNotYet()
    {
        Allow();
        _blueprint.Setup(b => b.GetWorkflowStatusAsync("wf-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(InstanceBody("wf-1", 0, [1]));
        _register.Setup(r => r.GetTransactionsByInstanceIdAsync("reg-1", "wf-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await _tool.AwaitAsync(
            "InstanceReachesAction", workflowInstanceId: "wf-1", actionId: "2", timeoutSeconds: 1);

        result.Status.Should().Be("Success");
        result.Outcome.Should().Be("NotYet");
        result.Message.Should().Contain("Timed out").And.Contain("sorcha_await_condition again");
    }

    [Fact]
    public async Task InstanceReachesAction_InstanceRejected_ReturnsUnreachable()
    {
        Allow();
        // InstanceState: Active=0, Completed=1, Rejected=2, TimedOut=3, Cancelled=4.
        _blueprint.Setup(b => b.GetWorkflowStatusAsync("wf-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(InstanceBody("wf-1", 2, Array.Empty<int>()));

        var result = await _tool.AwaitAsync(
            "InstanceReachesAction", workflowInstanceId: "wf-1", actionId: "2", timeoutSeconds: 5);

        result.Status.Should().Be("Success");
        result.Outcome.Should().Be("Unreachable");
        result.Message.Should().Contain("Rejected");
    }

    [Fact]
    public async Task InstanceReachesAction_InstanceCompletedWithoutReachingAction_ReturnsUnreachable()
    {
        Allow();
        _blueprint.Setup(b => b.GetWorkflowStatusAsync("wf-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(InstanceBody("wf-1", 1, Array.Empty<int>()));

        var result = await _tool.AwaitAsync(
            "InstanceReachesAction", workflowInstanceId: "wf-1", actionId: "2", timeoutSeconds: 5);

        result.Status.Should().Be("Success");
        result.Outcome.Should().Be("Unreachable");
        result.Message.Should().Contain("completed");
    }

    [Fact]
    public async Task InstanceReachesAction_AlreadyAdvancedPastAction_ReturnsUnreachable()
    {
        Allow();
        // Active, but action 2 is no longer current — action 3 is. History shows action 2 already sealed.
        _blueprint.Setup(b => b.GetWorkflowStatusAsync("wf-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(InstanceBody("wf-1", 0, [3]));
        _register.Setup(r => r.GetTransactionsByInstanceIdAsync("reg-1", "wf-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new TransactionModel
            {
                TxId = new string('a', 64),
                RegisterId = "reg-1",
                SenderWallet = "wallet-1",
                Signature = "sig",
                MetaData = new TransactionMetaData { RegisterId = "reg-1", InstanceId = "wf-1", ActionId = 2 },
            }]);

        var result = await _tool.AwaitAsync(
            "InstanceReachesAction", workflowInstanceId: "wf-1", actionId: "2", timeoutSeconds: 5);

        result.Status.Should().Be("Success");
        result.Outcome.Should().Be("Unreachable");
        result.Message.Should().Contain("advanced past");
    }

    [Fact]
    public async Task InstanceReachesAction_InstanceNotFound_ReturnsUnreachable()
    {
        Allow();
        _blueprint.Setup(b => b.GetWorkflowStatusAsync("ghost-wf", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceReadResult(HttpStatusCode.NotFound, null));

        var result = await _tool.AwaitAsync(
            "InstanceReachesAction", workflowInstanceId: "ghost-wf", actionId: "2", timeoutSeconds: 5);

        result.Status.Should().Be("Success");
        result.Outcome.Should().Be("Unreachable");
        result.Message.Should().Contain("No workflow instance");
    }

    [Fact]
    public async Task InstanceReachesAction_NoActionId_MetOnCompletion()
    {
        Allow();
        _blueprint.Setup(b => b.GetWorkflowStatusAsync("wf-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(InstanceBody("wf-1", 1, Array.Empty<int>()));

        var result = await _tool.AwaitAsync("InstanceReachesAction", workflowInstanceId: "wf-1");

        result.Outcome.Should().Be("Met");
        result.Message.Should().Contain("completed");
    }

    // -------------------------------------------------------------------------
    // TransactionSeals
    // -------------------------------------------------------------------------

    private static TransactionModel Transaction(ulong? docketNumber) => new()
    {
        TxId = new string('b', 64),
        RegisterId = "reg-1",
        SenderWallet = "wallet-1",
        Signature = "sig",
        DocketNumber = docketNumber,
    };

    [Fact]
    public async Task TransactionSeals_MetImmediately_ReturnsMet()
    {
        Allow();
        _register.Setup(r => r.GetTransactionAsync("reg-1", "tx-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Transaction(42));

        var result = await _tool.AwaitAsync("TransactionSeals", registerId: "reg-1", transactionId: "tx-1");

        result.Outcome.Should().Be("Met");
        result.Message.Should().Contain("docket 42");
    }

    [Fact]
    public async Task TransactionSeals_MetAfterDelay_ReturnsMet()
    {
        Allow();
        _register.SetupSequence(r => r.GetTransactionAsync("reg-1", "tx-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Transaction(null))
            .ReturnsAsync(Transaction(99));

        var result = await _tool.AwaitAsync(
            "TransactionSeals", registerId: "reg-1", transactionId: "tx-1", timeoutSeconds: 5);

        result.Outcome.Should().Be("Met");
        result.Message.Should().Contain("docket 99");
    }

    [Fact]
    public async Task TransactionSeals_NeverSeals_TimesOutAsNotYet()
    {
        Allow();
        _register.Setup(r => r.GetTransactionAsync("reg-1", "tx-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Transaction(null));

        var result = await _tool.AwaitAsync(
            "TransactionSeals", registerId: "reg-1", transactionId: "tx-1", timeoutSeconds: 1);

        result.Status.Should().Be("Success");
        result.Outcome.Should().Be("NotYet");
        result.Message.Should().Contain("has not sealed yet").And.Contain("Timed out");
    }

    [Fact]
    public async Task TransactionSeals_TransactionNotFound_ReturnsUnreachable()
    {
        Allow();
        _register.Setup(r => r.GetTransactionAsync("reg-1", "ghost-tx", It.IsAny<CancellationToken>()))
            .ReturnsAsync((TransactionModel?)null);

        var result = await _tool.AwaitAsync(
            "TransactionSeals", registerId: "reg-1", transactionId: "ghost-tx", timeoutSeconds: 5);

        result.Status.Should().Be("Success");
        result.Outcome.Should().Be("Unreachable");
        result.Message.Should().Contain("was not found");
    }
}
