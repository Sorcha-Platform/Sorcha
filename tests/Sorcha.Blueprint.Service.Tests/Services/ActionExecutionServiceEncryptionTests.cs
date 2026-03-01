// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.ServiceClients.Participant;
using Sorcha.ServiceClients.Wallet;
using Sorcha.ServiceClients.Register;
using Sorcha.ServiceClients.Register.Models;
using Sorcha.ServiceClients.Validator;
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
/// Tests for encryption pipeline integration in ActionExecutionService.
/// Verifies T018: encryption pipeline called after disclosure, encrypted transaction path,
/// legacy plaintext fallback, encryption failure handling, and skipped recipient warnings.
/// </summary>
public class ActionExecutionServiceEncryptionTests
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

    public ActionExecutionServiceEncryptionTests()
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
            credentialVerifier: null,
            confirmationOptions: null,
            statusListManager: null,
            encryptionPipeline: _mockEncryptionPipeline.Object);
    }

    private ActionExecutionService CreateServiceWithoutEncryption()
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
            _mockLogger.Object);
    }

    #region Encryption Pipeline Integration Tests

    [Fact]
    public async Task ExecuteAsync_WithExternalRecipientKeys_CallsEncryptionPipeline()
    {
        // Arrange
        var service = CreateServiceWithEncryption();
        var instanceId = "test-instance";
        var actionId = 1;
        var recipientKey = Convert.ToBase64String(new byte[32]);
        var request = CreateTestRequestWithExternalKeys(recipientKey);
        var delegationToken = "test-token";
        var instance = CreateTestInstance(instanceId, "blueprint-1");
        var blueprint = CreateTestBlueprint();
        var action = blueprint.Actions!.First(a => a.Id == actionId);

        SetupCommonMocks(instanceId, instance, blueprint, action);
        SetupRoutingAndDisclosure(blueprint, action);
        SetupFullTransactionFlow(instance);

        _mockEncryptionPipeline
            .Setup(x => x.EncryptDisclosedPayloadsAsync(
                It.IsAny<DisclosureGroup[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(EncryptionResult.Succeeded(CreateTestEncryptedGroups()));

        // Act
        var result = await service.ExecuteAsync(instanceId, actionId, request, delegationToken);

        // Assert
        result.Should().NotBeNull();
        _mockEncryptionPipeline.Verify(x => x.EncryptDisclosedPayloadsAsync(
            It.Is<DisclosureGroup[]>(g => g.Length > 0),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WithExternalRecipientKeys_UsesEncryptedTransactionPath()
    {
        // Arrange
        var service = CreateServiceWithEncryption();
        var instanceId = "test-instance";
        var actionId = 1;
        var recipientKey = Convert.ToBase64String(new byte[32]);
        var request = CreateTestRequestWithExternalKeys(recipientKey);
        var delegationToken = "test-token";
        var instance = CreateTestInstance(instanceId, "blueprint-1");
        var blueprint = CreateTestBlueprint();
        var action = blueprint.Actions!.First(a => a.Id == actionId);

        SetupCommonMocks(instanceId, instance, blueprint, action);
        SetupRoutingAndDisclosure(blueprint, action);

        var encryptedGroups = CreateTestEncryptedGroups();
        _mockEncryptionPipeline
            .Setup(x => x.EncryptDisclosedPayloadsAsync(
                It.IsAny<DisclosureGroup[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(EncryptionResult.Succeeded(encryptedGroups));

        SetupFullTransactionFlow(instance);

        // Act
        var result = await service.ExecuteAsync(instanceId, actionId, request, delegationToken);

        // Assert - the result should contain a transaction ID (encrypted path was used)
        result.TransactionId.Should().NotBeNullOrEmpty();

        // Verify BuildActionTransactionAsync (plaintext overload) was NOT called directly
        // by checking that the encrypted pipeline was invoked
        _mockEncryptionPipeline.Verify(x => x.EncryptDisclosedPayloadsAsync(
            It.IsAny<DisclosureGroup[]>(),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WithoutExternalRecipientKeys_UsesLegacyPlaintextPath()
    {
        // Arrange
        var service = CreateServiceWithEncryption();
        var instanceId = "test-instance";
        var actionId = 1;
        var request = CreateTestRequest(); // No external keys
        var delegationToken = "test-token";
        var instance = CreateTestInstance(instanceId, "blueprint-1");
        var blueprint = CreateTestBlueprint();
        var action = blueprint.Actions!.First(a => a.Id == actionId);

        SetupCommonMocks(instanceId, instance, blueprint, action);
        SetupRoutingAndDisclosure(blueprint, action);
        SetupFullTransactionFlow(instance);

        // Act
        var result = await service.ExecuteAsync(instanceId, actionId, request, delegationToken);

        // Assert - encryption pipeline should NOT have been called
        _mockEncryptionPipeline.Verify(x => x.EncryptDisclosedPayloadsAsync(
            It.IsAny<DisclosureGroup[]>(),
            It.IsAny<CancellationToken>()),
            Times.Never);

        // Transaction should still succeed (plaintext path)
        result.TransactionId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_WithNullEncryptionPipeline_UsesLegacyPlaintextPath()
    {
        // Arrange - service created WITHOUT encryption pipeline
        var service = CreateServiceWithoutEncryption();
        var instanceId = "test-instance";
        var actionId = 1;
        var recipientKey = Convert.ToBase64String(new byte[32]);
        var request = CreateTestRequestWithExternalKeys(recipientKey); // Has keys but no pipeline
        var delegationToken = "test-token";
        var instance = CreateTestInstance(instanceId, "blueprint-1");
        var blueprint = CreateTestBlueprint();
        var action = blueprint.Actions!.First(a => a.Id == actionId);

        SetupCommonMocks(instanceId, instance, blueprint, action);
        SetupRoutingAndDisclosure(blueprint, action);
        SetupFullTransactionFlow(instance);

        // Act
        var result = await service.ExecuteAsync(instanceId, actionId, request, delegationToken);

        // Assert - encryption pipeline is null, so plaintext path is used
        result.TransactionId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_WithEncryptionFailure_ThrowsInvalidOperationException()
    {
        // Arrange
        var service = CreateServiceWithEncryption();
        var instanceId = "test-instance";
        var actionId = 1;
        var recipientKey = Convert.ToBase64String(new byte[32]);
        var request = CreateTestRequestWithExternalKeys(recipientKey);
        var delegationToken = "test-token";
        var instance = CreateTestInstance(instanceId, "blueprint-1");
        var blueprint = CreateTestBlueprint();
        var action = blueprint.Actions!.First(a => a.Id == actionId);

        SetupCommonMocks(instanceId, instance, blueprint, action);
        SetupRoutingAndDisclosure(blueprint, action);
        // Need register mock to return blueprint publish TX for starting action confirmation
        SetupFullTransactionFlow(instance);

        _mockEncryptionPipeline
            .Setup(x => x.EncryptDisclosedPayloadsAsync(
                It.IsAny<DisclosureGroup[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(EncryptionResult.Failed("AES key wrap failed", "wallet-sender"));

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ExecuteAsync(instanceId, actionId, request, delegationToken));

        ex.Message.Should().Contain("Encryption failed");
        ex.Message.Should().Contain("wallet-sender");
        ex.Message.Should().Contain("AES key wrap failed");
    }

    [Fact]
    public async Task ExecuteAsync_WithSkippedRecipients_LogsWarningAndContinues()
    {
        // Arrange
        var service = CreateServiceWithEncryption();
        var instanceId = "test-instance";
        var actionId = 1;
        var recipientKey = Convert.ToBase64String(new byte[32]);
        var request = CreateTestRequestWithExternalKeys(recipientKey);
        var delegationToken = "test-token";
        var instance = CreateTestInstance(instanceId, "blueprint-1");
        var blueprint = CreateTestBlueprint();
        var action = blueprint.Actions!.First(a => a.Id == actionId);

        SetupCommonMocks(instanceId, instance, blueprint, action);
        SetupRoutingAndDisclosure(blueprint, action);

        var encryptedGroups = CreateTestEncryptedGroups();
        var skippedRecipients = new List<string> { "wallet-unknown" };
        _mockEncryptionPipeline
            .Setup(x => x.EncryptDisclosedPayloadsAsync(
                It.IsAny<DisclosureGroup[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(EncryptionResult.Succeeded(encryptedGroups, skippedRecipients));

        SetupFullTransactionFlow(instance);

        // Act
        var result = await service.ExecuteAsync(instanceId, actionId, request, delegationToken);

        // Assert - should succeed despite skipped recipients
        result.TransactionId.Should().NotBeNullOrEmpty();

        // Verify warning was logged (check that LogWarning was called)
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Skipped")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_WithUnrecognizedAlgorithm_SkipsRecipient()
    {
        // Arrange
        var service = CreateServiceWithEncryption();
        var instanceId = "test-instance";
        var actionId = 1;
        var request = new ActionSubmissionRequest
        {
            BlueprintId = "blueprint-1",
            ActionId = "1",
            SenderWallet = "wallet-sender",
            RegisterAddress = "register-1",
            PayloadData = new Dictionary<string, object>
            {
                ["field1"] = "value1",
                ["field2"] = 42
            },
            ExternalRecipientKeys = new Dictionary<string, ExternalKeyInfo>
            {
                ["wallet-sender"] = new ExternalKeyInfo
                {
                    PublicKey = Convert.ToBase64String(new byte[32]),
                    Algorithm = "INVALID_ALGO" // Unrecognized algorithm
                }
            }
        };
        var delegationToken = "test-token";
        var instance = CreateTestInstance(instanceId, "blueprint-1");
        var blueprint = CreateTestBlueprint();
        var action = blueprint.Actions!.First(a => a.Id == actionId);

        SetupCommonMocks(instanceId, instance, blueprint, action);
        SetupRoutingAndDisclosure(blueprint, action);
        SetupFullTransactionFlow(instance);

        // Act
        var result = await service.ExecuteAsync(instanceId, actionId, request, delegationToken);

        // Assert - encryption pipeline should NOT be called (no valid recipients)
        _mockEncryptionPipeline.Verify(x => x.EncryptDisclosedPayloadsAsync(
            It.IsAny<DisclosureGroup[]>(),
            It.IsAny<CancellationToken>()),
            Times.Never);

        // Should still succeed with plaintext path
        result.TransactionId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Constructor_WithNullEncryptionPipeline_CreatesInstance()
    {
        // Verify the encryption pipeline is truly optional
        var service = CreateServiceWithoutEncryption();
        service.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithEncryptionPipeline_CreatesInstance()
    {
        // Verify the service can be constructed with the encryption pipeline
        var service = CreateServiceWithEncryption();
        service.Should().NotBeNull();
    }

    #endregion

    #region Helper Methods

    private void SetupCommonMocks(string instanceId, Instance instance, BlueprintModel blueprint, ActionModel action)
    {
        _mockInstanceStore
            .Setup(x => x.GetAsync(instanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);

        _mockActionResolver
            .Setup(x => x.GetBlueprintAsync(instance.BlueprintId, It.IsAny<CancellationToken>()))
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

        // Default: validation passes when no schemas
        _mockExecutionEngine
            .Setup(x => x.ValidateAsync(
                It.IsAny<Dictionary<string, object>>(),
                action,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Sorcha.Blueprint.Engine.Models.ValidationResult.Valid());

        // Default: no calculations
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
            .Setup(x => x.DetermineRoutingAsync(
                blueprint,
                action,
                It.IsAny<Dictionary<string, object>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Sorcha.Blueprint.Engine.Models.RoutingResult.Complete());

        _mockExecutionEngine
            .Setup(x => x.ApplyDisclosures(It.IsAny<Dictionary<string, object>>(), action))
            .Returns(new List<Sorcha.Blueprint.Engine.Models.DisclosureResult>());
    }

    private void SetupFullTransactionFlow(Instance instance)
    {
        // Mock wallet signing
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
                SignedBy = "wallet-sender",
                Algorithm = "ED25519"
            });

        // Mock validator acceptance
        _mockValidatorClient
            .Setup(x => x.SubmitTransactionAsync(
                It.IsAny<TransactionSubmission>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionSubmissionResult
            {
                Success = true,
                TransactionId = "abc123def456abc123def456abc123def456abc123def456abc123def456abc12345"
            });

        // Mock transaction confirmation (return a confirmed transaction)
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

        // Mock instance update
        _mockInstanceStore
            .Setup(x => x.UpdateAsync(
                It.IsAny<Instance>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Instance inst, CancellationToken _) => inst);
    }

    private static ActionSubmissionRequest CreateTestRequest()
    {
        return new ActionSubmissionRequest
        {
            BlueprintId = "blueprint-1",
            ActionId = "1",
            SenderWallet = "wallet-sender",
            RegisterAddress = "register-1",
            PayloadData = new Dictionary<string, object>
            {
                ["field1"] = "value1",
                ["field2"] = 42
            }
        };
    }

    private static ActionSubmissionRequest CreateTestRequestWithExternalKeys(string publicKeyBase64)
    {
        return new ActionSubmissionRequest
        {
            BlueprintId = "blueprint-1",
            ActionId = "1",
            SenderWallet = "wallet-sender",
            RegisterAddress = "register-1",
            PayloadData = new Dictionary<string, object>
            {
                ["field1"] = "value1",
                ["field2"] = 42
            },
            ExternalRecipientKeys = new Dictionary<string, ExternalKeyInfo>
            {
                ["wallet-sender"] = new ExternalKeyInfo
                {
                    PublicKey = publicKeyBase64,
                    Algorithm = "ED25519"
                }
            }
        };
    }

    private Instance CreateTestInstance(string instanceId, string blueprintId)
    {
        return new Instance
        {
            Id = instanceId,
            BlueprintId = blueprintId,
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
                        WalletAddress = "wallet-sender",
                        EncryptedKey = new byte[48],
                        Algorithm = WalletNetworks.ED25519
                    }
                ]
            }
        ];
    }

    #endregion
}
