// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Blueprint.Engine.Interfaces;
using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Models.Requests;
using Sorcha.Blueprint.Service.Services.Implementation;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.Blueprint.Service.Storage;
using Sorcha.ServiceClients.Participant;
using Sorcha.ServiceClients.Register;
using Sorcha.ServiceClients.Validator;
using Sorcha.ServiceClients.Wallet;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;
using ActionModel = Sorcha.Blueprint.Models.Action;
using ParticipantModel = Sorcha.Blueprint.Models.Participant;
using RouteModel = Sorcha.Blueprint.Models.Route;

namespace Sorcha.Blueprint.Service.Tests.Services;

/// <summary>
/// Tests for starting action wallet binding logic in ActionExecutionService (US1).
/// Starting actions accept any wallet and bind it to the participant role for instance lifetime.
/// </summary>
public class ActionExecutionStartingActionTests
{
    private readonly Mock<IActionResolverService> _mockActionResolver;
    private readonly Mock<IStateReconstructionService> _mockStateReconstruction;
    private readonly Mock<ITransactionBuilderService> _mockTransactionBuilder;
    private readonly Mock<IRegisterServiceClient> _mockRegisterClient;
    private readonly Mock<IValidatorServiceClient> _mockValidatorClient;
    private readonly Mock<IWalletServiceClient> _mockWalletClient;
    private readonly Mock<IParticipantServiceClient> _mockParticipantClient;
    private readonly Mock<INotificationService> _mockNotificationService;
    private readonly Mock<IInstanceStore> _mockInstanceStore;
    private readonly Mock<IActionStore> _mockActionStore;
    private readonly Mock<IExecutionEngine> _mockExecutionEngine;
    private readonly Mock<ILogger<ActionExecutionService>> _mockLogger;
    private readonly ActionExecutionService _service;

    public ActionExecutionStartingActionTests()
    {
        _mockActionResolver = new Mock<IActionResolverService>();
        _mockStateReconstruction = new Mock<IStateReconstructionService>();
        _mockTransactionBuilder = new Mock<ITransactionBuilderService>();
        _mockRegisterClient = new Mock<IRegisterServiceClient>();
        _mockValidatorClient = new Mock<IValidatorServiceClient>();
        _mockWalletClient = new Mock<IWalletServiceClient>();
        _mockParticipantClient = new Mock<IParticipantServiceClient>();
        _mockNotificationService = new Mock<INotificationService>();
        _mockInstanceStore = new Mock<IInstanceStore>();
        _mockActionStore = new Mock<IActionStore>();
        _mockExecutionEngine = new Mock<IExecutionEngine>();
        _mockLogger = new Mock<ILogger<ActionExecutionService>>();

        _mockActionStore.Setup(s => s.GetByIdempotencyKeyAsync(It.IsAny<string>()))
            .ReturnsAsync((string?)null);

        _service = new ActionExecutionService(
            _mockActionResolver.Object,
            _mockStateReconstruction.Object,
            _mockTransactionBuilder.Object,
            _mockRegisterClient.Object,
            _mockValidatorClient.Object,
            _mockWalletClient.Object,
            _mockParticipantClient.Object,
            _mockNotificationService.Object,
            _mockInstanceStore.Object,
            _mockActionStore.Object,
            _mockExecutionEngine.Object,
            _mockLogger.Object,
            new ConfigurationBuilder().Build());
    }

    [Fact]
    public async Task ExecuteAsync_StartingAction_BindsWalletToParticipant()
    {
        // Arrange: starting action, citizen has no pre-bound wallet
        var instanceId = "inst-001";
        var actionId = 0;
        var senderWallet = "ws11q-citizen-new";
        var instance = CreateInstance(instanceId, participantWallets: new());
        var blueprint = CreateBlueprintWithStartingAction();
        var action = blueprint.Actions!.First(a => a.Id == actionId);
        var request = CreateRequest(senderWallet);

        SetupMocks(instanceId, instance, blueprint, action);

        // Act — will throw at transaction building (expected), but binding should happen first
        await Assert.ThrowsAnyAsync<Exception>(() =>
            _service.ExecuteAsync(instanceId, actionId, request, "token"));

        // Assert: wallet was bound to the "citizen" participant
        instance.ParticipantWallets.Should().ContainKey("citizen");
        instance.ParticipantWallets["citizen"].Should().Be(senderWallet);

        // Instance was updated to persist the binding
        _mockInstanceStore.Verify(s => s.UpdateAsync(instance, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_StartingAction_SameWalletResubmit_Succeeds()
    {
        // Arrange: citizen already bound to this wallet (resubmission)
        var instanceId = "inst-002";
        var actionId = 0;
        var senderWallet = "ws11q-citizen-bound";
        var instance = CreateInstance(instanceId, participantWallets: new()
        {
            ["citizen"] = senderWallet // already bound to same wallet
        });
        var blueprint = CreateBlueprintWithStartingAction();
        var action = blueprint.Actions!.First(a => a.Id == actionId);
        var request = CreateRequest(senderWallet);

        SetupMocks(instanceId, instance, blueprint, action);

        // Act — should NOT throw due to binding conflict
        await Assert.ThrowsAnyAsync<Exception>(() =>
            _service.ExecuteAsync(instanceId, actionId, request, "token"));

        // Assert: binding unchanged, no error from binding logic
        instance.ParticipantWallets["citizen"].Should().Be(senderWallet);
    }

    [Fact]
    public async Task ExecuteAsync_StartingAction_DifferentWalletRebind_Rejects()
    {
        // Arrange: citizen already bound to a different wallet (FR-008 immutability)
        var instanceId = "inst-003";
        var actionId = 0;
        var instance = CreateInstance(instanceId, participantWallets: new()
        {
            ["citizen"] = "ws11q-citizen-original"
        });
        var blueprint = CreateBlueprintWithStartingAction();
        var action = blueprint.Actions!.First(a => a.Id == actionId);
        var request = CreateRequest("ws11q-different-wallet");

        SetupMocks(instanceId, instance, blueprint, action);

        // Act & Assert: should throw due to immutable binding conflict
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.ExecuteAsync(instanceId, actionId, request, "token"));

        ex.Message.Should().Contain("already bound");
        ex.Message.Should().Contain("immutable");
    }

    [Fact]
    public async Task ExecuteAsync_NonStartingAction_DoesNotBind()
    {
        // Arrange: non-starting action should NOT trigger binding
        var instanceId = "inst-004";
        var actionId = 1;
        var instance = CreateInstance(instanceId, participantWallets: new()
        {
            ["citizen"] = "ws11q-citizen"
        });
        instance.CurrentActionIds = [1];
        var blueprint = CreateBlueprintWithStartingAction();
        var action = blueprint.Actions!.First(a => a.Id == 1); // non-starting action
        var request = CreateRequest("ws11q-reviewer");

        SetupMocks(instanceId, instance, blueprint, action);

        // Act
        await Assert.ThrowsAnyAsync<Exception>(() =>
            _service.ExecuteAsync(instanceId, actionId, request, "token"));

        // Assert: no new binding created for reviewer (they're already pre-bound)
        instance.ParticipantWallets.Should().NotContainKey("reviewer");
    }

    [Fact]
    public async Task ExecuteAsync_OpenStartingAction_MissingParticipantProfile_IsAllowed_AndBinds()
    {
        // Feature 103 / #911: an OPEN starting action (Sender participant has no hardcoded wallet)
        // accepts a walk-in submitter whose consumer token carries org_id (F136) but who has no
        // participant profile yet. SEC-006 must NOT fail-closed on the missing profile — the wallet
        // is late-bound right after the ownership check. The participant client returns null (default).
        var instanceId = "inst-open-911";
        var actionId = 0;
        var senderWallet = "ws11q-citizen-walkin";
        var instance = CreateInstance(instanceId, participantWallets: new());
        var blueprint = CreateBlueprintWithStartingAction();
        var action = blueprint.Actions!.First(a => a.Id == actionId);
        var request = CreateRequest(senderWallet);
        SetupMocks(instanceId, instance, blueprint, action);

        var caller = CreateConsumerCaller(Guid.NewGuid(), Guid.NewGuid());

        // Act — proceeds past the SEC-006 ownership check (no 403) and binds; later steps throw.
        var ex = await Assert.ThrowsAnyAsync<Exception>(() =>
            _service.ExecuteAsync(instanceId, actionId, request, "token", caller));

        // Assert — NOT the participant-profile rejection; the bind happened (proves it cleared 4b).
        (ex is UnauthorizedAccessException && ex.Message.Contains("No participant profile"))
            .Should().BeFalse("open-participant walk-in submission must not be rejected for a missing participant profile (#911)");
        instance.ParticipantWallets.Should().ContainKey("citizen");
        instance.ParticipantWallets["citizen"].Should().Be(senderWallet);
    }

    [Fact]
    public async Task ExecuteAsync_NonOpenAction_MissingParticipantProfile_RejectsFailClosed()
    {
        // Regression guard: SEC-006 still fails closed on a missing participant for a NON-open action
        // (a designated participant with a hardcoded wallet), so the #911 fix doesn't weaken the gate.
        var instanceId = "inst-closed-911";
        var actionId = 1; // reviewer (hardcoded wallet) — not an open starting action
        var instance = CreateInstance(instanceId, participantWallets: new() { ["citizen"] = "ws11q-citizen" });
        instance.CurrentActionIds = [1];
        var blueprint = CreateBlueprintWithStartingAction();
        var action = blueprint.Actions!.First(a => a.Id == 1);
        var request = CreateRequest("ws11q-reviewer");
        SetupMocks(instanceId, instance, blueprint, action);

        var caller = CreateConsumerCaller(Guid.NewGuid(), Guid.NewGuid());

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _service.ExecuteAsync(instanceId, actionId, request, "token", caller));
        ex.Message.Should().Contain("No participant profile");
    }

    #region Helpers

    private static ClaimsPrincipal CreateConsumerCaller(Guid userId, Guid orgId) =>
        new(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim("org_id", orgId.ToString())
            ],
            authenticationType: "test"));

    private void SetupMocks(string instanceId, Instance instance, BlueprintModel blueprint, ActionModel action)
    {
        _mockInstanceStore
            .Setup(x => x.GetAsync(instanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);

        _mockInstanceStore
            .Setup(x => x.UpdateAsync(It.IsAny<Instance>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);

        _mockActionResolver
            .Setup(x => x.GetBlueprintAsync(instance.BlueprintId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(blueprint);

        _mockActionResolver
            .Setup(x => x.GetActionDefinition(blueprint, action.Id.ToString()))
            .Returns(action);

        _mockStateReconstruction
            .Setup(x => x.ReconstructAsync(
                blueprint, instanceId, action.Id, instance.RegisterId,
                It.IsAny<string>(), instance.ParticipantWallets, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccumulatedState());

        _mockRegisterClient
            .Setup(x => x.GetTransactionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sorcha.Register.Models.TransactionModel
            {
                TxId = "0000000000000000000000000000000000000000000000000000000000000000",
                DocketNumber = 1
            });

        _mockExecutionEngine
            .Setup(x => x.ValidateAsync(It.IsAny<Dictionary<string, object>>(), action, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Sorcha.Blueprint.Engine.Models.ValidationResult.Valid());

        _mockExecutionEngine
            .Setup(x => x.ApplyCalculationsAsync(It.IsAny<Dictionary<string, object>>(), action, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object>());
    }

    private static ActionSubmissionRequest CreateRequest(string senderWallet) => new()
    {
        BlueprintId = "bp-001",
        ActionId = "0",
        SenderWallet = senderWallet,
        RegisterAddress = "reg-001",
        PayloadData = new Dictionary<string, object> { ["name"] = "Test" }
    };

    private static Instance CreateInstance(string id, Dictionary<string, string>? participantWallets = null) => new()
    {
        Id = id,
        BlueprintId = "bp-001",
        BlueprintDefinitionTxId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", // Feature 195: execution resolves and chains by the pin
        BlueprintVersion = 1,
        RegisterId = "reg-001",
        TenantId = "tenant-001",
        State = InstanceState.Active,
        CurrentActionIds = [0],
        ParticipantWallets = participantWallets ?? new()
    };

    private static BlueprintModel CreateBlueprintWithStartingAction() => new()
    {
        Id = "bp-001",
        Title = "Test Blueprint",
        Participants =
        [
            new ParticipantModel { Id = "citizen", Name = "Citizen" }, // No wallet — dynamic binding
            new ParticipantModel { Id = "reviewer", Name = "Reviewer", WalletAddress = "ws11q-reviewer" }
        ],
        Actions =
        [
            new ActionModel
            {
                Id = 0,
                Title = "Apply",
                Sender = "citizen",
                IsStartingAction = true,
                Routes = [new RouteModel { NextActionIds = [1] }]
            },
            new ActionModel
            {
                Id = 1,
                Title = "Review",
                Sender = "reviewer"
            }
        ]
    };

    #endregion
}
