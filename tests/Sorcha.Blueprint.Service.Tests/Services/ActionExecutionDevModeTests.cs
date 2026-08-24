// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sorcha.ServiceClients.Participant;
using Sorcha.ServiceClients.Register;
using Sorcha.ServiceClients.Register.Models;
using Sorcha.ServiceClients.Validator;
using Sorcha.ServiceClients.Wallet;
using Sorcha.Blueprint.Engine.Interfaces;
using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Models.Requests;
using Sorcha.Blueprint.Service.Services.Implementation;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.Blueprint.Service.Storage;
using Sorcha.Cryptography.Enums;
using Sorcha.TransactionHandler.Encryption;
using Sorcha.TransactionHandler.Encryption.Models;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;
using ActionModel = Sorcha.Blueprint.Models.Action;
using ParticipantModel = Sorcha.Blueprint.Models.Participant;
using RouteModel = Sorcha.Blueprint.Models.Route;

namespace Sorcha.Blueprint.Service.Tests.Services;

/// <summary>
/// Tests for DevMode behavior in ActionExecutionService.
/// When a register has DevMode=true, the encryption pipeline should be skipped.
/// When DevMode=false, the encryption pipeline should be invoked normally.
/// Disclosure filtering must still apply regardless of DevMode setting.
/// </summary>
public class ActionExecutionDevModeTests
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
    private readonly Mock<IEncryptionPipelineService> _mockEncryptionPipeline;

    public ActionExecutionDevModeTests()
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
        _mockEncryptionPipeline = new Mock<IEncryptionPipelineService>();

        // Default: no idempotency collision
        _mockActionStore.Setup(s => s.GetByIdempotencyKeyAsync(It.IsAny<string>()))
            .ReturnsAsync((string?)null);

        // Default: register returns empty batch response (no published keys)
        _mockRegisterClient
            .Setup(x => x.ResolvePublicKeysBatchAsync(
                It.IsAny<string>(),
                It.IsAny<BatchPublicKeyRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BatchPublicKeyResponse
            {
                Resolved = new Dictionary<string, PublicKeyResolution>(),
                NotFound = [],
                Revoked = []
            });
    }

    private ActionExecutionService CreateServiceWithEncryption()
    {
        return new ActionExecutionService(
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
            new ConfigurationBuilder().Build(),
            credentialVerifier: null,
            confirmationOptions: null,
            statusListManager: null,
            encryptionPipeline: _mockEncryptionPipeline.Object);
    }

    [Fact]
    public async Task ExecuteAsync_RegisterDevModeTrue_SkipsEncryptionPipeline()
    {
        // Arrange
        var service = CreateServiceWithEncryption();
        var instanceId = "devmode-instance";
        var actionId = 1;
        var recipientKey = Convert.ToBase64String(new byte[32]);
        var request = CreateRequestWithExternalKeys(recipientKey);
        var instance = CreateTestInstance(instanceId);
        var blueprint = CreateTestBlueprint();
        var action = blueprint.Actions!.First(a => a.Id == actionId);

        SetupCommonMocks(instanceId, instance, blueprint, action);
        SetupRoutingAndDisclosure(blueprint, action);
        SetupFullTransactionFlow(instance);

        // Register returns DevMode=true
        _mockRegisterClient
            .Setup(x => x.GetRegisterAsync(instance.RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sorcha.Register.Models.Register
            {
                Id = instance.RegisterId,
                Name = "DevMode Register",
                DevMode = true
            });

        // Act
        var result = await service.ExecuteAsync(instanceId, actionId, request, "test-token");

        // Assert — encryption pipeline must NOT be called when DevMode is true
        _mockEncryptionPipeline.Verify(x => x.EncryptDisclosedPayloadsAsync(
            It.IsAny<DisclosureGroup[]>(),
            It.IsAny<CancellationToken>()),
            Times.Never);

        result.TransactionId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_RegisterDevModeFalse_CallsEncryptionPipeline()
    {
        // Arrange
        var service = CreateServiceWithEncryption();
        var instanceId = "encrypted-instance";
        var actionId = 1;
        var recipientKey = Convert.ToBase64String(new byte[32]);
        var request = CreateRequestWithExternalKeys(recipientKey);
        var instance = CreateTestInstance(instanceId);
        var blueprint = CreateTestBlueprint();
        var action = blueprint.Actions!.First(a => a.Id == actionId);

        SetupCommonMocks(instanceId, instance, blueprint, action);
        SetupRoutingAndDisclosure(blueprint, action);
        SetupFullTransactionFlow(instance);

        // Register returns DevMode=false
        _mockRegisterClient
            .Setup(x => x.GetRegisterAsync(instance.RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sorcha.Register.Models.Register
            {
                Id = instance.RegisterId,
                Name = "Encrypted Register",
                DevMode = false
            });

        _mockEncryptionPipeline
            .Setup(x => x.EncryptDisclosedPayloadsAsync(
                It.IsAny<DisclosureGroup[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(EncryptionResult.Succeeded(CreateTestEncryptedGroups()));

        // Act
        var result = await service.ExecuteAsync(instanceId, actionId, request, "test-token");

        // Assert — encryption pipeline MUST be called when DevMode is false
        _mockEncryptionPipeline.Verify(x => x.EncryptDisclosedPayloadsAsync(
            It.Is<DisclosureGroup[]>(g => g.Length > 0),
            It.IsAny<CancellationToken>()),
            Times.Once);

        result.TransactionId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_RegisterDevModeTrue_StillAppliesDisclosureFiltering()
    {
        // Arrange
        var service = CreateServiceWithEncryption();
        var instanceId = "devmode-disclosure-instance";
        var actionId = 1;
        var recipientKey = Convert.ToBase64String(new byte[32]);
        var request = CreateRequestWithExternalKeys(recipientKey);
        var instance = CreateTestInstance(instanceId);
        var blueprint = CreateTestBlueprint();
        var action = blueprint.Actions!.First(a => a.Id == actionId);

        SetupCommonMocks(instanceId, instance, blueprint, action);
        SetupRoutingAndDisclosure(blueprint, action);
        SetupFullTransactionFlow(instance);

        // Register returns DevMode=true
        _mockRegisterClient
            .Setup(x => x.GetRegisterAsync(instance.RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sorcha.Register.Models.Register
            {
                Id = instance.RegisterId,
                Name = "DevMode Disclosure Register",
                DevMode = true
            });

        // Act
        var result = await service.ExecuteAsync(instanceId, actionId, request, "test-token");

        // Assert — disclosure filtering engine must still be invoked even in DevMode
        _mockExecutionEngine.Verify(x => x.ApplyDisclosures(
            It.IsAny<Dictionary<string, object>>(),
            action),
            Times.Once);

        // Encryption should be skipped
        _mockEncryptionPipeline.Verify(x => x.EncryptDisclosedPayloadsAsync(
            It.IsAny<DisclosureGroup[]>(),
            It.IsAny<CancellationToken>()),
            Times.Never);

        result.TransactionId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_RegisterLookupFails_DefaultsToEncryptedPath()
    {
        // Arrange
        var service = CreateServiceWithEncryption();
        var instanceId = "fallback-instance";
        var actionId = 1;
        var recipientKey = Convert.ToBase64String(new byte[32]);
        var request = CreateRequestWithExternalKeys(recipientKey);
        var instance = CreateTestInstance(instanceId);
        var blueprint = CreateTestBlueprint();
        var action = blueprint.Actions!.First(a => a.Id == actionId);

        SetupCommonMocks(instanceId, instance, blueprint, action);
        SetupRoutingAndDisclosure(blueprint, action);
        SetupFullTransactionFlow(instance);

        // Register lookup throws
        _mockRegisterClient
            .Setup(x => x.GetRegisterAsync(instance.RegisterId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Service unavailable"));

        _mockEncryptionPipeline
            .Setup(x => x.EncryptDisclosedPayloadsAsync(
                It.IsAny<DisclosureGroup[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(EncryptionResult.Succeeded(CreateTestEncryptedGroups()));

        // Act
        var result = await service.ExecuteAsync(instanceId, actionId, request, "test-token");

        // Assert — when register lookup fails, default is registerDevMode=false,
        // so encryption pipeline should be called (encrypted path)
        _mockEncryptionPipeline.Verify(x => x.EncryptDisclosedPayloadsAsync(
            It.Is<DisclosureGroup[]>(g => g.Length > 0),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_DevMode_ReturnsAsync202AndDoesNotAdvanceInstanceState()
    {
        // Feature 145: the submit path is now single-async. It returns 202 (IsAsync, empty
        // NextActions, not complete) and NEVER advances instance control state — the
        // InstanceProjector folds the sealed docket to advance CurrentActionIds. Here we prove
        // the submitter left CurrentActionIds untouched at [1]; the only instance write it makes
        // is the pre-submit instanceReference seed, which preserves the current actions.
        var service = CreateServiceWithEncryption();
        var instanceId = "devmode-async-instance";
        var actionId = 1;
        var request = CreateRequestWithExternalKeys(Convert.ToBase64String(new byte[32]));
        var instance = CreateTestInstance(instanceId);
        var blueprint = CreateTestBlueprint();
        var action = blueprint.Actions!.First(a => a.Id == actionId);

        SetupCommonMocks(instanceId, instance, blueprint, action);
        SetupRoutingAndDisclosure(blueprint, action);
        SetupFullTransactionFlow(instance);

        // Capture the CurrentActionIds of every instance write the submit path performs.
        var writtenCurrentActionIds = new List<List<int>>();
        _mockInstanceStore
            .Setup(x => x.UpdateAsync(It.IsAny<Instance>(), It.IsAny<CancellationToken>()))
            .Callback<Instance, CancellationToken>((inst, _) => writtenCurrentActionIds.Add([.. inst.CurrentActionIds]))
            .ReturnsAsync((Instance inst, CancellationToken _) => inst);

        _mockRegisterClient
            .Setup(x => x.GetRegisterAsync(instance.RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sorcha.Register.Models.Register
            {
                Id = instance.RegisterId,
                Name = "DevMode Register",
                DevMode = true
            });

        // Act
        var result = await service.ExecuteAsync(instanceId, actionId, request, "test-token");

        // Assert — the 202 async contract (contracts/submission-response.md)
        result.IsAsync.Should().BeTrue();
        result.NextActions.Should().BeEmpty();
        result.IsComplete.Should().BeFalse();
        result.TransactionId.Should().NotBeNullOrEmpty();

        // Assert — the submitter never advanced control state. Every write it made kept
        // CurrentActionIds at [1]; advancing past it is the InstanceProjector's responsibility.
        writtenCurrentActionIds.Should().OnlyContain(ids => ids.SequenceEqual(new[] { 1 }));
    }

    #region Helper Methods

    private void SetupCommonMocks(string instanceId, Instance instance, BlueprintModel blueprint, ActionModel action)
    {
        _mockInstanceStore
            .Setup(x => x.GetAsync(instanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);

        _mockActionResolver
            .Setup(x => x.GetBlueprintAsync(instance.BlueprintId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(blueprint);

        _mockActionResolver
            .Setup(x => x.GetActionDefinition(blueprint, action.Id.ToString()))
            .Returns(action);

        _mockStateReconstruction
            .Setup(x => x.ReconstructAsync(
                blueprint,
                instanceId,
                action.Id,
                instance.RegisterId,
                It.IsAny<string>(),
                instance.ParticipantWallets,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccumulatedState());

        _mockExecutionEngine
            .Setup(x => x.ValidateAsync(
                It.IsAny<Dictionary<string, object>>(),
                action,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Sorcha.Blueprint.Engine.Models.ValidationResult.Valid());

        _mockExecutionEngine
            .Setup(x => x.ApplyCalculationsAsync(
                It.IsAny<Dictionary<string, object>>(),
                action,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object>());
    }

    private void SetupRoutingAndDisclosure(BlueprintModel blueprint, ActionModel action)
    {
        _mockExecutionEngine
            .Setup(x => x.DetermineRoutingWithMappingAsync(
                blueprint,
                action,
                It.IsAny<Dictionary<string, object>>(),
                It.IsAny<System.Text.Json.Nodes.JsonObject?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Sorcha.Blueprint.Engine.Models.RoutingResult.Complete());

        _mockExecutionEngine
            .Setup(x => x.ApplyDisclosures(It.IsAny<Dictionary<string, object>>(), action))
            .Returns(new List<Sorcha.Blueprint.Engine.Models.DisclosureResult>());
    }

    private void SetupFullTransactionFlow(Instance instance)
    {
        _mockWalletClient
            .Setup(x => x.SignTransactionAsync(
                It.IsAny<string>(),
                It.IsAny<byte[]>(),
                It.IsAny<string?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WalletSignResult
            {
                Signature = new byte[64],
                PublicKey = new byte[32],
                SignedBy = "wallet-applicant",
                Algorithm = "ED25519"
            });

        _mockValidatorClient
            .Setup(x => x.SubmitTransactionAsync(
                It.IsAny<TransactionSubmission>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionSubmissionResult
            {
                Success = true,
                TransactionId = "abc123def456abc123def456abc123def456abc123def456abc123def456abc12345"
            });

        _mockRegisterClient
            .Setup(x => x.GetTransactionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sorcha.Register.Models.TransactionModel
            {
                TxId = "abc123def456abc123def456abc123def456abc123def456abc123def456abc12345",
                DocketNumber = 1
            });

        _mockInstanceStore
            .Setup(x => x.UpdateAsync(
                It.IsAny<Instance>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Instance inst, CancellationToken _) => inst);
    }

    private static ActionSubmissionRequest CreateRequestWithExternalKeys(string publicKeyBase64)
    {
        return new ActionSubmissionRequest
        {
            BlueprintId = "blueprint-1",
            ActionId = "1",
            SenderWallet = "wallet-applicant",
            RegisterAddress = "register-1",
            PayloadData = new Dictionary<string, object>
            {
                ["field1"] = "value1",
                ["field2"] = 42
            },
            ExternalRecipientKeys = new Dictionary<string, ExternalKeyInfo>
            {
                ["wallet-applicant"] = new ExternalKeyInfo
                {
                    PublicKey = publicKeyBase64,
                    Algorithm = "ED25519"
                }
            }
        };
    }

    private static Instance CreateTestInstance(string instanceId)
    {
        return new Instance
        {
            Id = instanceId,
            BlueprintId = "blueprint-1",
            BlueprintDefinitionTxId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", // Feature 195: an instance must carry its definition pin, or execution has nothing to resolve or chain from
            BlueprintVersion = 1,
            RegisterId = "register-1",
            TenantId = "test-tenant",
            State = InstanceState.Active,
            CurrentActionIds = [1],
            ParticipantWallets = new Dictionary<string, string>
            {
                ["applicant"] = "wallet-applicant",
                ["reviewer"] = "wallet-reviewer"
            }
        };
    }

    private static BlueprintModel CreateTestBlueprint()
    {
        return new BlueprintModel
        {
            Id = "blueprint-1",
            Title = "Test Blueprint",
            Participants = new List<ParticipantModel>
            {
                new ParticipantModel { Id = "applicant", Name = "Applicant", WalletAddress = "wallet-applicant" },
                new ParticipantModel { Id = "reviewer", Name = "Reviewer", WalletAddress = "wallet-reviewer" }
            },
            Actions = new List<ActionModel>
            {
                new ActionModel
                {
                    Id = 1,
                    Title = "Submit Application",
                    Sender = "applicant",
                    IsStartingAction = true,
                    Routes = new List<RouteModel>
                    {
                        new RouteModel { NextActionIds = new List<int> { 2 } }
                    }
                },
                new ActionModel
                {
                    Id = 2,
                    Title = "Review Application",
                    Sender = "reviewer"
                }
            }
        };
    }

    private static EncryptedPayloadGroup[] CreateTestEncryptedGroups()
    {
        return
        [
            new EncryptedPayloadGroup
            {
                GroupId = "test-group-id",
                DisclosedFields = ["field1", "field2"],
                Ciphertext = new byte[64],
                Nonce = new byte[12],
                PlaintextHash = new byte[32],
                EncryptionAlgorithm = EncryptionType.AES_GCM,
                WrappedKeys =
                [
                    new WrappedKey
                    {
                        WalletAddress = "wallet-applicant",
                        EncryptedKey = new byte[48],
                        Algorithm = WalletNetworks.ED25519
                    }
                ]
            }
        ];
    }

    #endregion
}
