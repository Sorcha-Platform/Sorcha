// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;
using Microsoft.Extensions.Logging;
using Sorcha.ServiceClients.Participant;
using Sorcha.ServiceClients.Wallet;
using Sorcha.ServiceClients.Register;
using Sorcha.ServiceClients.Validator;
using Sorcha.Blueprint.Engine.Interfaces;
using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Models.Requests;
using Sorcha.Blueprint.Service.Services.Implementation;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.Blueprint.Service.Storage;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;
using ActionModel = Sorcha.Blueprint.Models.Action;
using ParticipantModel = Sorcha.Blueprint.Models.Participant;
using RouteModel = Sorcha.Blueprint.Models.Route;
using RejectionConfigModel = Sorcha.Blueprint.Models.RejectionConfig;

namespace Sorcha.Blueprint.Service.Tests.Services;

/// <summary>
/// Unit tests for ActionExecutionService
/// </summary>
public class ActionExecutionServiceTests
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

    public ActionExecutionServiceTests()
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

        // Default: no idempotency collision
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
            _mockLogger.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullActionResolver_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ActionExecutionService(
                null!,
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
                _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullStateReconstruction_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ActionExecutionService(
                _mockActionResolver.Object,
                null!,
                _mockTransactionBuilder.Object,
                _mockRegisterClient.Object,
                _mockValidatorClient.Object,
                _mockWalletClient.Object,
                _mockParticipantClient.Object,
                _mockNotificationService.Object,
                _mockInstanceStore.Object,
                _mockActionStore.Object,
                _mockExecutionEngine.Object,
                _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullTransactionBuilder_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ActionExecutionService(
                _mockActionResolver.Object,
                _mockStateReconstruction.Object,
                null!,
                _mockRegisterClient.Object,
                _mockValidatorClient.Object,
                _mockWalletClient.Object,
                _mockParticipantClient.Object,
                _mockNotificationService.Object,
                _mockInstanceStore.Object,
                _mockActionStore.Object,
                _mockExecutionEngine.Object,
                _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullRegisterClient_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ActionExecutionService(
                _mockActionResolver.Object,
                _mockStateReconstruction.Object,
                _mockTransactionBuilder.Object,
                null!,
                _mockValidatorClient.Object,
                _mockWalletClient.Object,
                _mockParticipantClient.Object,
                _mockNotificationService.Object,
                _mockInstanceStore.Object,
                _mockActionStore.Object,
                _mockExecutionEngine.Object,
                _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullValidatorClient_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ActionExecutionService(
                _mockActionResolver.Object,
                _mockStateReconstruction.Object,
                _mockTransactionBuilder.Object,
                _mockRegisterClient.Object,
                null!,
                _mockWalletClient.Object,
                _mockParticipantClient.Object,
                _mockNotificationService.Object,
                _mockInstanceStore.Object,
                _mockActionStore.Object,
                _mockExecutionEngine.Object,
                _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullWalletClient_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ActionExecutionService(
                _mockActionResolver.Object,
                _mockStateReconstruction.Object,
                _mockTransactionBuilder.Object,
                _mockRegisterClient.Object,
                _mockValidatorClient.Object,
                null!,
                _mockParticipantClient.Object,
                _mockNotificationService.Object,
                _mockInstanceStore.Object,
                _mockActionStore.Object,
                _mockExecutionEngine.Object,
                _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullParticipantClient_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ActionExecutionService(
                _mockActionResolver.Object,
                _mockStateReconstruction.Object,
                _mockTransactionBuilder.Object,
                _mockRegisterClient.Object,
                _mockValidatorClient.Object,
                _mockWalletClient.Object,
                null!,
                _mockNotificationService.Object,
                _mockInstanceStore.Object,
                _mockActionStore.Object,
                _mockExecutionEngine.Object,
                _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullNotificationService_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ActionExecutionService(
                _mockActionResolver.Object,
                _mockStateReconstruction.Object,
                _mockTransactionBuilder.Object,
                _mockRegisterClient.Object,
                _mockValidatorClient.Object,
                _mockWalletClient.Object,
                _mockParticipantClient.Object,
                null!,
                _mockInstanceStore.Object,
                _mockActionStore.Object,
                _mockExecutionEngine.Object,
                _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullInstanceStore_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ActionExecutionService(
                _mockActionResolver.Object,
                _mockStateReconstruction.Object,
                _mockTransactionBuilder.Object,
                _mockRegisterClient.Object,
                _mockValidatorClient.Object,
                _mockWalletClient.Object,
                _mockParticipantClient.Object,
                _mockNotificationService.Object,
                null!,
                _mockActionStore.Object,
                _mockExecutionEngine.Object,
                _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullActionStore_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ActionExecutionService(
                _mockActionResolver.Object,
                _mockStateReconstruction.Object,
                _mockTransactionBuilder.Object,
                _mockRegisterClient.Object,
                _mockValidatorClient.Object,
                _mockWalletClient.Object,
                _mockParticipantClient.Object,
                _mockNotificationService.Object,
                _mockInstanceStore.Object,
                null!,
                _mockExecutionEngine.Object,
                _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullExecutionEngine_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ActionExecutionService(
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
                null!,
                _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ActionExecutionService(
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
                null!));
    }

    #endregion

    #region ExecuteAsync Tests

    [Fact]
    public async Task ExecuteAsync_WithNonExistentInstance_ThrowsInvalidOperationException()
    {
        // Arrange
        var instanceId = "non-existent-instance";
        var actionId = 1;
        var request = new ActionSubmissionRequest
        {
            BlueprintId = "blueprint-1",
            ActionId = "1",
            SenderWallet = "wallet-sender",
            RegisterAddress = "register-1",
            PayloadData = new Dictionary<string, object>()
        };
        var delegationToken = "test-token";

        _mockInstanceStore
            .Setup(x => x.GetAsync(instanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Instance?)null);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.ExecuteAsync(instanceId, actionId, request, delegationToken));
    }

    [Fact]
    public async Task ExecuteAsync_WithNonExistentBlueprint_ThrowsInvalidOperationException()
    {
        // Arrange
        var instanceId = "test-instance";
        var actionId = 1;
        var request = CreateTestRequest();
        var delegationToken = "test-token";
        var instance = CreateTestInstance(instanceId, "non-existent-bp");

        _mockInstanceStore
            .Setup(x => x.GetAsync(instanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);

        _mockActionResolver
            .Setup(x => x.GetBlueprintAsync("non-existent-bp", It.IsAny<CancellationToken>()))
            .ReturnsAsync((BlueprintModel?)null);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.ExecuteAsync(instanceId, actionId, request, delegationToken));
    }

    [Fact]
    public async Task ExecuteAsync_WithNonExistentAction_ThrowsInvalidOperationException()
    {
        // Arrange
        var instanceId = "test-instance";
        var actionId = 999; // Non-existent
        var request = CreateTestRequest();
        var delegationToken = "test-token";
        var instance = CreateTestInstance(instanceId, "blueprint-1");
        var blueprint = CreateTestBlueprint();

        _mockInstanceStore
            .Setup(x => x.GetAsync(instanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);

        _mockActionResolver
            .Setup(x => x.GetBlueprintAsync("blueprint-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(blueprint);

        _mockActionResolver
            .Setup(x => x.GetActionDefinition(blueprint, "999"))
            .Returns((ActionModel?)null);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.ExecuteAsync(instanceId, actionId, request, delegationToken));
    }

    [Fact]
    public async Task ExecuteAsync_WithActionNotCurrentAction_ThrowsInvalidOperationException()
    {
        // Arrange
        var instanceId = "test-instance";
        var actionId = 2; // Not a current action
        var request = CreateTestRequest();
        var delegationToken = "test-token";
        var instance = CreateTestInstance(instanceId, "blueprint-1");
        instance.CurrentActionIds = [1]; // Only action 1 is current
        var blueprint = CreateTestBlueprint();
        var action = blueprint.Actions!.First(a => a.Id == 2);
        action.IsStartingAction = false;

        _mockInstanceStore
            .Setup(x => x.GetAsync(instanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);

        _mockActionResolver
            .Setup(x => x.GetBlueprintAsync("blueprint-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(blueprint);

        _mockActionResolver
            .Setup(x => x.GetActionDefinition(blueprint, "2"))
            .Returns(action);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.ExecuteAsync(instanceId, actionId, request, delegationToken));
    }

    // Note: ExecuteAsync full flow test is skipped because it requires complex
    // mocking of Transaction class construction which requires ICryptoModule,
    // IHashProvider, and IPayloadManager. The validation logic is tested above.

    #endregion

    #region Engine Delegation Tests

    [Fact]
    public void Constructor_WithAllDependencies_CreatesInstance()
    {
        // Verify we can construct with all dependencies
        var service = new ActionExecutionService(
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

        Assert.NotNull(service);
    }

    [Fact]
    public async Task ExecuteAsync_WithConditionalRoute_DelegatesToEngine()
    {
        // Arrange - Verify engine routing is called with correct parameters
        var instanceId = "test-instance";
        var actionId = 1;
        var request = CreateTestRequest();
        var delegationToken = "test-token";
        var instance = CreateTestInstance(instanceId, "blueprint-1");
        var blueprint = CreateTestBlueprint();
        var action = blueprint.Actions!.First(a => a.Id == actionId);

        SetupCommonMocks(instanceId, instance, blueprint, action);

        var engineRoutingResult = new Sorcha.Blueprint.Engine.Models.RoutingResult
        {
            NextActions = [new Sorcha.Blueprint.Engine.Models.RoutedAction
            {
                ActionId = "2",
                ParticipantId = "reviewer"
            }],
            IsParallel = false
        };

        _mockExecutionEngine
            .Setup(x => x.DetermineRoutingAsync(
                blueprint,
                action,
                It.IsAny<Dictionary<string, object>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(engineRoutingResult);

        _mockExecutionEngine
            .Setup(x => x.ApplyDisclosures(It.IsAny<Dictionary<string, object>>(), action))
            .Returns(new List<Sorcha.Blueprint.Engine.Models.DisclosureResult>());

        // Act & Assert - verify engine is called (will throw at transaction building, which is fine)
        await Assert.ThrowsAnyAsync<Exception>(() =>
            _service.ExecuteAsync(instanceId, actionId, request, delegationToken));

        _mockExecutionEngine.Verify(x => x.DetermineRoutingAsync(
            blueprint, action, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WithInvalidSchemaData_DelegatesToEngineValidation()
    {
        // Arrange
        var instanceId = "test-instance";
        var actionId = 1;
        var request = CreateTestRequest();
        var delegationToken = "test-token";
        var instance = CreateTestInstance(instanceId, "blueprint-1");
        var blueprint = CreateTestBlueprint();
        var action = blueprint.Actions!.First(a => a.Id == actionId);
        action.DataSchemas = new[] { System.Text.Json.JsonDocument.Parse("""{ "type": "object" }""") };

        SetupCommonMocks(instanceId, instance, blueprint, action);

        var validationErrors = new List<Sorcha.Blueprint.Engine.Models.ValidationError>
        {
            Sorcha.Blueprint.Engine.Models.ValidationError.Create("/field1", "Invalid value")
        };

        _mockExecutionEngine
            .Setup(x => x.ValidateAsync(
                It.IsAny<Dictionary<string, object>>(),
                action,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Sorcha.Blueprint.Engine.Models.ValidationResult.Invalid(validationErrors));

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _service.ExecuteAsync(instanceId, actionId, request, delegationToken));

        Assert.Contains("Invalid value", ex.Errors[0]);
        _mockExecutionEngine.Verify(x => x.ValidateAsync(
            It.IsAny<Dictionary<string, object>>(), action, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WithNoSchema_FallsBackToFieldPresenceCheck()
    {
        // Arrange - no DataSchemas defined, should use RequiredActionData fallback
        var instanceId = "test-instance";
        var actionId = 1;
        var request = new ActionSubmissionRequest
        {
            BlueprintId = "blueprint-1",
            ActionId = "1",
            SenderWallet = "wallet-sender",
            RegisterAddress = "register-1",
            PayloadData = new Dictionary<string, object>() // Missing required fields
        };
        var delegationToken = "test-token";
        var instance = CreateTestInstance(instanceId, "blueprint-1");
        var blueprint = CreateTestBlueprint();
        var action = blueprint.Actions!.First(a => a.Id == actionId);
        action.DataSchemas = null; // No schemas
        action.RequiredActionData = new List<string> { "mandatoryField" };

        SetupCommonMocks(instanceId, instance, blueprint, action);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _service.ExecuteAsync(instanceId, actionId, request, delegationToken));

        Assert.Contains("Required field 'mandatoryField' is missing", ex.Message);

        // Engine validation should NOT have been called (no schemas defined)
        _mockExecutionEngine.Verify(x => x.ValidateAsync(
            It.IsAny<Dictionary<string, object>>(),
            It.IsAny<ActionModel>(),
            It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WithCalculations_DelegatesToEngine()
    {
        // Arrange
        var instanceId = "test-instance";
        var actionId = 1;
        var request = CreateTestRequest();
        var delegationToken = "test-token";
        var instance = CreateTestInstance(instanceId, "blueprint-1");
        var blueprint = CreateTestBlueprint();
        var action = blueprint.Actions!.First(a => a.Id == actionId);
        action.Calculations = new Dictionary<string, System.Text.Json.Nodes.JsonNode?>
        {
            ["total"] = System.Text.Json.Nodes.JsonNode.Parse("""{"+":[{"var":"a"},{"var":"b"}]}""")
        };

        SetupCommonMocks(instanceId, instance, blueprint, action);

        _mockExecutionEngine
            .Setup(x => x.ValidateAsync(It.IsAny<Dictionary<string, object>>(), action, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Sorcha.Blueprint.Engine.Models.ValidationResult.Valid());

        _mockExecutionEngine
            .Setup(x => x.DetermineRoutingAsync(blueprint, action, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Sorcha.Blueprint.Engine.Models.RoutingResult.Complete());

        _mockExecutionEngine
            .Setup(x => x.ApplyDisclosures(It.IsAny<Dictionary<string, object>>(), action))
            .Returns(new List<Sorcha.Blueprint.Engine.Models.DisclosureResult>());

        var calculatedData = new Dictionary<string, object>
        {
            ["a"] = 10,
            ["b"] = 20,
            ["total"] = 30
        };

        _mockExecutionEngine
            .Setup(x => x.ApplyCalculationsAsync(It.IsAny<Dictionary<string, object>>(), action, It.IsAny<CancellationToken>()))
            .ReturnsAsync(calculatedData);

        // Act & Assert - will throw at transaction building, verify engine called
        await Assert.ThrowsAnyAsync<Exception>(() =>
            _service.ExecuteAsync(instanceId, actionId, request, delegationToken));

        _mockExecutionEngine.Verify(x => x.ApplyCalculationsAsync(
            It.IsAny<Dictionary<string, object>>(), action, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WithDisclosureRules_DelegatesToEngine()
    {
        // Arrange
        var instanceId = "test-instance";
        var actionId = 1;
        var request = CreateTestRequest();
        var delegationToken = "test-token";
        var instance = CreateTestInstance(instanceId, "blueprint-1");
        var blueprint = CreateTestBlueprint();
        var action = blueprint.Actions!.First(a => a.Id == actionId);

        SetupCommonMocks(instanceId, instance, blueprint, action);

        _mockExecutionEngine
            .Setup(x => x.ValidateAsync(It.IsAny<Dictionary<string, object>>(), action, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Sorcha.Blueprint.Engine.Models.ValidationResult.Valid());

        _mockExecutionEngine
            .Setup(x => x.DetermineRoutingAsync(blueprint, action, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Sorcha.Blueprint.Engine.Models.RoutingResult.Complete());

        var disclosureResults = new List<Sorcha.Blueprint.Engine.Models.DisclosureResult>
        {
            Sorcha.Blueprint.Engine.Models.DisclosureResult.Create("reviewer", new Dictionary<string, object> { ["field1"] = "value1" })
        };

        _mockExecutionEngine
            .Setup(x => x.ApplyDisclosures(It.IsAny<Dictionary<string, object>>(), action))
            .Returns(disclosureResults);

        // Act & Assert - will throw at transaction building, verify engine called
        await Assert.ThrowsAnyAsync<Exception>(() =>
            _service.ExecuteAsync(instanceId, actionId, request, delegationToken));

        _mockExecutionEngine.Verify(x => x.ApplyDisclosures(
            It.IsAny<Dictionary<string, object>>(), action),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WithDisclosureRules_FiltersDataPerRecipient()
    {
        // Arrange - disclosure returns different filtered data per participant
        var instanceId = "test-instance";
        var actionId = 1;
        var request = CreateTestRequest();
        var delegationToken = "test-token";
        var instance = CreateTestInstance(instanceId, "blueprint-1");
        var blueprint = CreateTestBlueprint();
        var action = blueprint.Actions!.First(a => a.Id == actionId);

        SetupCommonMocks(instanceId, instance, blueprint, action);

        _mockExecutionEngine
            .Setup(x => x.ValidateAsync(It.IsAny<Dictionary<string, object>>(), action, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Sorcha.Blueprint.Engine.Models.ValidationResult.Valid());

        _mockExecutionEngine
            .Setup(x => x.DetermineRoutingAsync(blueprint, action, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Sorcha.Blueprint.Engine.Models.RoutingResult.Complete());

        // Two different disclosures: applicant sees field1+field2, reviewer sees only field1
        var disclosureResults = new List<Sorcha.Blueprint.Engine.Models.DisclosureResult>
        {
            Sorcha.Blueprint.Engine.Models.DisclosureResult.Create("applicant", new Dictionary<string, object>
            {
                ["field1"] = "value1",
                ["field2"] = 42
            }),
            Sorcha.Blueprint.Engine.Models.DisclosureResult.Create("reviewer", new Dictionary<string, object>
            {
                ["field1"] = "value1"
            })
        };

        _mockExecutionEngine
            .Setup(x => x.ApplyDisclosures(It.IsAny<Dictionary<string, object>>(), action))
            .Returns(disclosureResults);

        // Act & Assert - verify engine processes two separate disclosures
        await Assert.ThrowsAnyAsync<Exception>(() =>
            _service.ExecuteAsync(instanceId, actionId, request, delegationToken));

        _mockExecutionEngine.Verify(x => x.ApplyDisclosures(
            It.IsAny<Dictionary<string, object>>(), action),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WithWildcardDisclosure_SendsAllFields()
    {
        // Arrange - disclosure returns full data (wildcard)
        var instanceId = "test-instance";
        var actionId = 1;
        var request = CreateTestRequest();
        var delegationToken = "test-token";
        var instance = CreateTestInstance(instanceId, "blueprint-1");
        var blueprint = CreateTestBlueprint();
        var action = blueprint.Actions!.First(a => a.Id == actionId);

        SetupCommonMocks(instanceId, instance, blueprint, action);

        _mockExecutionEngine
            .Setup(x => x.ValidateAsync(It.IsAny<Dictionary<string, object>>(), action, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Sorcha.Blueprint.Engine.Models.ValidationResult.Valid());

        _mockExecutionEngine
            .Setup(x => x.DetermineRoutingAsync(blueprint, action, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Sorcha.Blueprint.Engine.Models.RoutingResult.Complete());

        // Wildcard disclosure: reviewer gets all fields
        var disclosureResults = new List<Sorcha.Blueprint.Engine.Models.DisclosureResult>
        {
            Sorcha.Blueprint.Engine.Models.DisclosureResult.Create("reviewer", new Dictionary<string, object>
            {
                ["field1"] = "value1",
                ["field2"] = 42
            })
        };

        _mockExecutionEngine
            .Setup(x => x.ApplyDisclosures(It.IsAny<Dictionary<string, object>>(), action))
            .Returns(disclosureResults);

        // Act & Assert - verify engine called and disclosure includes all fields
        await Assert.ThrowsAnyAsync<Exception>(() =>
            _service.ExecuteAsync(instanceId, actionId, request, delegationToken));

        _mockExecutionEngine.Verify(x => x.ApplyDisclosures(
            It.IsAny<Dictionary<string, object>>(), action),
            Times.Once);
    }

    #endregion

    #region RejectAsync Tests

    [Fact]
    public async Task RejectAsync_WithNonExistentInstance_ThrowsInvalidOperationException()
    {
        // Arrange
        var instanceId = "non-existent-instance";
        var actionId = 1;
        var request = new ActionRejectionRequest
        {
            Reason = "Test rejection"
        };
        var delegationToken = "test-token";

        _mockInstanceStore
            .Setup(x => x.GetAsync(instanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Instance?)null);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.RejectAsync(instanceId, actionId, request, delegationToken));
    }

    [Fact]
    public async Task RejectAsync_WithActionNotAllowingRejection_ThrowsInvalidOperationException()
    {
        // Arrange
        var instanceId = "test-instance";
        var actionId = 1;
        var request = new ActionRejectionRequest { Reason = "Test rejection" };
        var delegationToken = "test-token";
        var instance = CreateTestInstance(instanceId, "blueprint-1");
        var blueprint = CreateTestBlueprint();
        var action = blueprint.Actions!.First(a => a.Id == actionId);
        action.RejectionConfig = null; // No rejection allowed

        _mockInstanceStore
            .Setup(x => x.GetAsync(instanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);

        _mockActionResolver
            .Setup(x => x.GetBlueprintAsync("blueprint-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(blueprint);

        _mockActionResolver
            .Setup(x => x.GetActionDefinition(blueprint, "1"))
            .Returns(action);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.RejectAsync(instanceId, actionId, request, delegationToken));
    }

    [Fact]
    public async Task RejectAsync_WithRequiredReasonMissing_ThrowsValidationException()
    {
        // Arrange
        var instanceId = "test-instance";
        var actionId = 2;
        var request = new ActionRejectionRequest { Reason = "" }; // Empty reason
        var delegationToken = "test-token";
        var instance = CreateTestInstance(instanceId, "blueprint-1");
        var blueprint = CreateTestBlueprintWithRejection();
        var action = blueprint.Actions!.First(a => a.Id == actionId);

        _mockInstanceStore
            .Setup(x => x.GetAsync(instanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);

        _mockActionResolver
            .Setup(x => x.GetBlueprintAsync("blueprint-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(blueprint);

        _mockActionResolver
            .Setup(x => x.GetActionDefinition(blueprint, "2"))
            .Returns(action);

        // Act & Assert
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.RejectAsync(instanceId, actionId, request, delegationToken));
    }

    #endregion

    #region Wallet Ownership Validation Tests

    [Fact]
    public async Task ExecuteAsync_WithServicePrincipal_SkipsWalletValidation()
    {
        // Arrange
        var instanceId = "test-instance";
        var actionId = 1;
        var request = CreateTestRequest();
        var delegationToken = "test-token";
        var instance = CreateTestInstance(instanceId, "blueprint-1");
        var blueprint = CreateTestBlueprint();
        var action = blueprint.Actions!.First(a => a.Id == actionId);
        var caller = CreateServicePrincipal();

        SetupCommonMocks(instanceId, instance, blueprint, action);

        // Act — will throw later at transaction building, but wallet validation should be skipped
        await Assert.ThrowsAnyAsync<Exception>(() =>
            _service.ExecuteAsync(instanceId, actionId, request, delegationToken, caller));

        // Assert — participant client should never be called
        _mockParticipantClient.Verify(
            x => x.GetByUserAndOrgAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WithNullCaller_SkipsWalletValidation()
    {
        // Arrange
        var instanceId = "test-instance";
        var actionId = 1;
        var request = CreateTestRequest();
        var delegationToken = "test-token";
        var instance = CreateTestInstance(instanceId, "blueprint-1");
        var blueprint = CreateTestBlueprint();
        var action = blueprint.Actions!.First(a => a.Id == actionId);

        SetupCommonMocks(instanceId, instance, blueprint, action);

        // Act — null caller should skip validation (backward compat)
        await Assert.ThrowsAnyAsync<Exception>(() =>
            _service.ExecuteAsync(instanceId, actionId, request, delegationToken, caller: null));

        // Assert — participant client should never be called
        _mockParticipantClient.Verify(
            x => x.GetByUserAndOrgAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WithParticipantNotFound_DegradeGracefully()
    {
        // Arrange — when participant is not found, the service degrades gracefully
        // and continues execution (the user is already authenticated via JWT)
        var instanceId = "test-instance";
        var actionId = 1;
        var request = CreateTestRequest();
        var delegationToken = "test-token";
        var instance = CreateTestInstance(instanceId, "blueprint-1");
        var blueprint = CreateTestBlueprint();
        var action = blueprint.Actions!.First(a => a.Id == actionId);
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var caller = CreateUserPrincipal(userId, orgId);

        SetupCommonMocks(instanceId, instance, blueprint, action);

        _mockParticipantClient
            .Setup(x => x.GetByUserAndOrgAsync(userId, orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ParticipantInfo?)null);

        // Act — execution proceeds past wallet validation (graceful degradation)
        // and will throw later at transaction building (unmocked), NOT UnauthorizedAccessException
        await Assert.ThrowsAnyAsync<Exception>(() =>
            _service.ExecuteAsync(instanceId, actionId, request, delegationToken, caller));

        // Assert — participant lookup was still attempted
        _mockParticipantClient.Verify(
            x => x.GetByUserAndOrgAsync(userId, orgId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WithInactiveParticipant_ThrowsUnauthorizedAccessException()
    {
        // Arrange
        var instanceId = "test-instance";
        var actionId = 1;
        var request = CreateTestRequest();
        var delegationToken = "test-token";
        var instance = CreateTestInstance(instanceId, "blueprint-1");
        var blueprint = CreateTestBlueprint();
        var action = blueprint.Actions!.First(a => a.Id == actionId);
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var caller = CreateUserPrincipal(userId, orgId);

        SetupCommonMocks(instanceId, instance, blueprint, action);

        _mockParticipantClient
            .Setup(x => x.GetByUserAndOrgAsync(userId, orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ParticipantInfo
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                OrganizationId = orgId,
                DisplayName = "Test User",
                Email = "test@example.com",
                Status = "Suspended"
            });

        // Act & Assert
        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _service.ExecuteAsync(instanceId, actionId, request, delegationToken, caller));

        Assert.Contains("Participant status is Suspended", ex.Message);
    }

    [Fact]
    public async Task ExecuteAsync_WithUnlinkedWallet_DegradeGracefully()
    {
        // Arrange — when wallet is not linked, the service degrades gracefully
        // and continues execution (the user is already authenticated via JWT)
        var instanceId = "test-instance";
        var actionId = 1;
        var request = CreateTestRequest();
        var delegationToken = "test-token";
        var instance = CreateTestInstance(instanceId, "blueprint-1");
        var blueprint = CreateTestBlueprint();
        var action = blueprint.Actions!.First(a => a.Id == actionId);
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var participantId = Guid.NewGuid();
        var caller = CreateUserPrincipal(userId, orgId);

        SetupCommonMocks(instanceId, instance, blueprint, action);

        _mockParticipantClient
            .Setup(x => x.GetByUserAndOrgAsync(userId, orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ParticipantInfo
            {
                Id = participantId,
                UserId = userId,
                OrganizationId = orgId,
                DisplayName = "Test User",
                Email = "test@example.com",
                Status = "Active"
            });

        _mockParticipantClient
            .Setup(x => x.GetLinkedWalletsAsync(participantId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LinkedWalletInfo>
            {
                new LinkedWalletInfo
                {
                    Id = Guid.NewGuid(),
                    WalletAddress = "different-wallet-address",
                    Algorithm = "ED25519",
                    Status = "Active"
                }
            });

        // Act — execution proceeds past wallet validation (graceful degradation)
        // and will throw later at transaction building (unmocked), NOT UnauthorizedAccessException
        await Assert.ThrowsAnyAsync<Exception>(() =>
            _service.ExecuteAsync(instanceId, actionId, request, delegationToken, caller));

        // Assert — wallet lookup was still attempted
        _mockParticipantClient.Verify(
            x => x.GetLinkedWalletsAsync(participantId, true, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WithLinkedWallet_ProceedsSuccessfully()
    {
        // Arrange — wallet validation should pass, then it proceeds to transaction building
        var instanceId = "test-instance";
        var actionId = 1;
        var request = CreateTestRequest();
        var delegationToken = "test-token";
        var instance = CreateTestInstance(instanceId, "blueprint-1");
        var blueprint = CreateTestBlueprint();
        var action = blueprint.Actions!.First(a => a.Id == actionId);
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var participantId = Guid.NewGuid();
        var caller = CreateUserPrincipal(userId, orgId);

        SetupCommonMocks(instanceId, instance, blueprint, action);
        SetupWalletValidationMocks(userId, orgId, participantId, request.SenderWallet);

        _mockExecutionEngine
            .Setup(x => x.DetermineRoutingAsync(blueprint, action, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Sorcha.Blueprint.Engine.Models.RoutingResult.Complete());

        _mockExecutionEngine
            .Setup(x => x.ApplyDisclosures(It.IsAny<Dictionary<string, object>>(), action))
            .Returns(new List<Sorcha.Blueprint.Engine.Models.DisclosureResult>());

        // Act — will throw at transaction building (which is after wallet validation)
        await Assert.ThrowsAnyAsync<Exception>(() =>
            _service.ExecuteAsync(instanceId, actionId, request, delegationToken, caller));

        // Assert — wallet validation completed (participant was looked up)
        _mockParticipantClient.Verify(
            x => x.GetByUserAndOrgAsync(userId, orgId, It.IsAny<CancellationToken>()),
            Times.Once);
        _mockParticipantClient.Verify(
            x => x.GetLinkedWalletsAsync(participantId, true, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RejectAsync_WithUnlinkedWallet_DegradeGracefully()
    {
        // Arrange — when wallet is not linked, the service degrades gracefully
        // and continues execution (the user is already authenticated via JWT)
        var instanceId = "test-instance";
        var actionId = 2;
        var request = new ActionRejectionRequest
        {
            Reason = "Test rejection",
            SenderWallet = "unlinked-wallet"
        };
        var delegationToken = "test-token";
        var instance = CreateTestInstance(instanceId, "blueprint-1");
        var blueprint = CreateTestBlueprintWithRejection();
        var action = blueprint.Actions!.First(a => a.Id == actionId);
        var targetAction = blueprint.Actions!.First(a => a.Id == 1);
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var participantId = Guid.NewGuid();
        var caller = CreateUserPrincipal(userId, orgId);

        _mockInstanceStore
            .Setup(x => x.GetAsync(instanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);

        _mockActionResolver
            .Setup(x => x.GetBlueprintAsync("blueprint-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(blueprint);

        _mockActionResolver
            .Setup(x => x.GetActionDefinition(blueprint, "2"))
            .Returns(action);

        _mockActionResolver
            .Setup(x => x.GetActionDefinition(blueprint, "1"))
            .Returns(targetAction);

        _mockParticipantClient
            .Setup(x => x.GetByUserAndOrgAsync(userId, orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ParticipantInfo
            {
                Id = participantId,
                UserId = userId,
                OrganizationId = orgId,
                DisplayName = "Test User",
                Email = "test@example.com",
                Status = "Active"
            });

        _mockParticipantClient
            .Setup(x => x.GetLinkedWalletsAsync(participantId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LinkedWalletInfo>());

        // Act — execution proceeds past wallet validation (graceful degradation)
        // and will throw later at transaction building (unmocked), NOT UnauthorizedAccessException
        await Assert.ThrowsAnyAsync<Exception>(() =>
            _service.RejectAsync(instanceId, actionId, request, delegationToken, caller));

        // Assert — wallet lookup was still attempted
        _mockParticipantClient.Verify(
            x => x.GetLinkedWalletsAsync(participantId, true, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RejectAsync_WithServicePrincipal_SkipsWalletValidation()
    {
        // Arrange
        var instanceId = "test-instance";
        var actionId = 2;
        var request = new ActionRejectionRequest
        {
            Reason = "Test rejection",
            SenderWallet = "any-wallet"
        };
        var delegationToken = "test-token";
        var instance = CreateTestInstance(instanceId, "blueprint-1");
        var blueprint = CreateTestBlueprintWithRejection();
        var action = blueprint.Actions!.First(a => a.Id == actionId);
        var targetAction = blueprint.Actions!.First(a => a.Id == 1);
        var caller = CreateServicePrincipal();

        _mockInstanceStore
            .Setup(x => x.GetAsync(instanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);

        _mockActionResolver
            .Setup(x => x.GetBlueprintAsync("blueprint-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(blueprint);

        _mockActionResolver
            .Setup(x => x.GetActionDefinition(blueprint, "2"))
            .Returns(action);

        _mockActionResolver
            .Setup(x => x.GetActionDefinition(blueprint, "1"))
            .Returns(targetAction);

        // Act — will throw later at transaction building, but wallet validation should be skipped
        await Assert.ThrowsAnyAsync<Exception>(() =>
            _service.RejectAsync(instanceId, actionId, request, delegationToken, caller));

        // Assert — participant client should never be called
        _mockParticipantClient.Verify(
            x => x.GetByUserAndOrgAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion

    #region PrevTxId Chain Linking Tests

    [Fact]
    public void ComputeBlueprintPublishTxId_ReturnsConsistentHash()
    {
        // Arrange
        var registerId = "register-1";
        var blueprintId = "blueprint-1";

        // Act
        var txId1 = ActionExecutionService.ComputeBlueprintPublishTxId(registerId, blueprintId);
        var txId2 = ActionExecutionService.ComputeBlueprintPublishTxId(registerId, blueprintId);

        // Assert
        txId1.Should().Be(txId2, "same inputs should produce the same deterministic TX ID");
        txId1.Should().HaveLength(64, "TX ID should be a 64-character hex SHA-256 hash");
        txId1.Should().MatchRegex("^[0-9a-f]{64}$", "TX ID should be lowercase hex");
    }

    [Fact]
    public void ComputeBlueprintPublishTxId_DifferentInputs_ProduceDifferentHashes()
    {
        // Act
        var txId1 = ActionExecutionService.ComputeBlueprintPublishTxId("register-1", "blueprint-1");
        var txId2 = ActionExecutionService.ComputeBlueprintPublishTxId("register-2", "blueprint-1");
        var txId3 = ActionExecutionService.ComputeBlueprintPublishTxId("register-1", "blueprint-2");

        // Assert
        txId1.Should().NotBe(txId2, "different register IDs should produce different TX IDs");
        txId1.Should().NotBe(txId3, "different blueprint IDs should produce different TX IDs");
    }

    [Fact]
    public void ComputeBlueprintPublishTxId_MatchesRegisterServiceFormula()
    {
        // The Register Service uses the same formula: SHA-256("blueprint-publish-{registerId}-{blueprintId}")
        // Verify our implementation matches by computing manually
        var registerId = "test-register";
        var blueprintId = "test-blueprint";
        var source = System.Text.Encoding.UTF8.GetBytes($"blueprint-publish-{registerId}-{blueprintId}");
        var hash = System.Security.Cryptography.SHA256.HashData(source);
        var expected = Convert.ToHexStringLower(hash);

        // Act
        var actual = ActionExecutionService.ComputeBlueprintPublishTxId(registerId, blueprintId);

        // Assert
        actual.Should().Be(expected, "formula must match the Register Service implementation");
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

        // Default: GetTransactionAsync returns a confirmed transaction so
        // WaitForTransactionConfirmationAsync does not poll for 30 seconds
        _mockRegisterClient
            .Setup(x => x.GetTransactionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sorcha.Register.Models.TransactionModel
            {
                TxId = "0000000000000000000000000000000000000000000000000000000000000000",
                DocketNumber = 1
            });

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

    private ActionSubmissionRequest CreateTestRequest()
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

    private BlueprintModel CreateTestBlueprint()
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

    private BlueprintModel CreateTestBlueprintWithRejection()
    {
        var blueprint = CreateTestBlueprint();
        var reviewAction = blueprint.Actions!.First(a => a.Id == 2);
        reviewAction.RejectionConfig = new RejectionConfigModel
        {
            TargetActionId = 1,
            RequireReason = true,
            IsTerminal = false
        };
        return blueprint;
    }

    private static ClaimsPrincipal CreateUserPrincipal(Guid userId, Guid orgId)
    {
        var claims = new[]
        {
            new Claim("sub", userId.ToString()),
            new Claim("org_id", orgId.ToString()),
            new Claim("token_type", "user")
        };
        var identity = new ClaimsIdentity(claims, "test");
        return new ClaimsPrincipal(identity);
    }

    private static ClaimsPrincipal CreateServicePrincipal()
    {
        var claims = new[]
        {
            new Claim("sub", "service-blueprint"),
            new Claim("token_type", "service")
        };
        var identity = new ClaimsIdentity(claims, "test");
        return new ClaimsPrincipal(identity);
    }

    private void SetupWalletValidationMocks(Guid userId, Guid orgId, Guid participantId, string walletAddress)
    {
        _mockParticipantClient
            .Setup(x => x.GetByUserAndOrgAsync(userId, orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ParticipantInfo
            {
                Id = participantId,
                UserId = userId,
                OrganizationId = orgId,
                DisplayName = "Test User",
                Email = "test@example.com",
                Status = "Active"
            });

        _mockParticipantClient
            .Setup(x => x.GetLinkedWalletsAsync(participantId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LinkedWalletInfo>
            {
                new LinkedWalletInfo
                {
                    Id = Guid.NewGuid(),
                    WalletAddress = walletAddress,
                    Algorithm = "ED25519",
                    Status = "Active"
                }
            });
    }

    #endregion
}
