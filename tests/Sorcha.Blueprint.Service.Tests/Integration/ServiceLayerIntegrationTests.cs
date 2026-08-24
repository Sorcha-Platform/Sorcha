// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sorcha.ServiceClients.Wallet;
using Sorcha.ServiceClients.Register;
using Sorcha.Blueprint.Service.Services.Implementation;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.Cryptography.Interfaces;
using System.Text;
using System.Text.Json;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;
using ActionModel = Sorcha.Blueprint.Models.Action;
using ParticipantModel = Sorcha.Blueprint.Models.Participant;

namespace Sorcha.Blueprint.Service.Tests.Integration;

/// <summary>
/// Integration tests for the service layer components working together
/// </summary>
public class ServiceLayerIntegrationTests
{

    /// <summary>
    /// A stand-in definition pin (Feature 195). Publishing assigns a real publication id;
    /// these tests only need a stable, non-empty one, because the resolver now REQUIRES a pin
    /// rather than falling back to whatever definition this node holds latest.
    /// </summary>
    private const string TestDefinitionPin = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private readonly IActionResolverService _actionResolver;
    private readonly IPayloadResolverService _payloadResolver;
    private readonly ITransactionBuilderService _transactionBuilder;
    private readonly Mock<IBlueprintStore> _mockBlueprintStore;
    private readonly Mock<IPublishedBlueprintStore> _mockPublishedStore;
    private readonly Mock<IWalletServiceClient> _mockWalletClient;
    private readonly Mock<IRegisterServiceClient> _mockRegisterClient;
    private readonly IDistributedCache _cache;

    public ServiceLayerIntegrationTests()
    {
        // Setup real distributed cache (in-memory implementation)
        var cacheOptions = Options.Create(new MemoryDistributedCacheOptions());
        _cache = new MemoryDistributedCache(cacheOptions);

        // Setup mock blueprint store
        _mockBlueprintStore = new Mock<IBlueprintStore>();
        _mockPublishedStore = new Mock<IPublishedBlueprintStore>();

        // Setup mock service clients
        _mockWalletClient = new Mock<IWalletServiceClient>();
        _mockRegisterClient = new Mock<IRegisterServiceClient>();

        // Setup wallet encryption mock to return prefixed data
        _mockWalletClient
            .Setup(x => x.EncryptPayloadAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string wallet, byte[] data, CancellationToken _) =>
                Encoding.UTF8.GetBytes($"ENCRYPTED_FOR:{wallet}:{Encoding.UTF8.GetString(data)}"));

        // Create real services
        var actionResolverLogger = Mock.Of<ILogger<ActionResolverService>>();
        _actionResolver = new ActionResolverService(
            _mockBlueprintStore.Object,
            _mockPublishedStore.Object,
            _cache,
            actionResolverLogger);

        var payloadResolverLogger = Mock.Of<ILogger<PayloadResolverService>>();
        _payloadResolver = new PayloadResolverService(
            payloadResolverLogger,
            _mockWalletClient.Object,
            _mockRegisterClient.Object);

        // Create transaction builder with mocks
        var mockCryptoModule = new Mock<ICryptoModule>();
        var mockHashProvider = new Mock<IHashProvider>();
        var mockSymmetricCrypto = new Mock<ISymmetricCrypto>();
        var transactionBuilderLogger = Mock.Of<ILogger<TransactionBuilderService>>();
        _transactionBuilder = new TransactionBuilderService(
            mockCryptoModule.Object,
            mockHashProvider.Object,
            mockSymmetricCrypto.Object,
            transactionBuilderLogger);
    }

    [Fact]
    public async Task EndToEnd_ActionSubmission_WorksCorrectly()
    {
        // Arrange: Create a blueprint with participants and actions
        var blueprint = new BlueprintModel
        {
            Id = "loan-application-bp",
            Title = "Loan Application",
            Participants = new List<ParticipantModel>
            {
                new ParticipantModel { Id = "applicant", Name = "Applicant", WalletAddress = "wallet-applicant" },
                new ParticipantModel { Id = "loan-officer", Name = "Loan Officer", WalletAddress = "wallet-loan-officer" }
            },
            Actions = new List<ActionModel>
            {
                new ActionModel
                {
                    Id = 1,
                    Title = "Submit Application",
                    Sender = "applicant"
                },
                new ActionModel
                {
                    Id = 2,
                    Title = "Review Application",
                    Sender = "loan-officer"
                }
            }
        };

        _mockPublishedStore.Setup(x => x.GetVersionsAsync("loan-application-bp"))
            .ReturnsAsync(new[]
            {
                new PublishedBlueprint
                {
                    BlueprintId = "loan-application-bp",
                    PublicationTxId = TestDefinitionPin,
                    Blueprint = blueprint
                }
            });

        // Act 1: Resolve the blueprint and action
        var resolvedBlueprint = await _actionResolver.GetBlueprintAsync("loan-application-bp", TestDefinitionPin);
        var action = _actionResolver.GetActionDefinition(resolvedBlueprint!, "1");

        // Assert 1: Blueprint and action resolved
        resolvedBlueprint.Should().NotBeNull();
        action.Should().NotBeNull();
        action!.Title.Should().Be("Submit Application");

        // Act 2: Resolve participant wallets
        var participantIds = new[] { "applicant", "loan-officer" };
        var wallets = await _actionResolver.ResolveParticipantWalletsAsync(
            resolvedBlueprint!,
            participantIds);

        // Assert 2: Wallets resolved
        wallets.Should().HaveCount(2);
        wallets.Should().ContainKey("applicant");
        wallets.Should().ContainKey("loan-officer");

        // Act 3: Create encrypted payloads
        var disclosureResults = new Dictionary<string, object>
        {
            ["applicant"] = new { name = "John Doe", amount = 50000 },
            ["loan-officer"] = new { name = "John Doe", amount = 50000, creditScore = 720 }
        };

        var encryptedPayloads = await _payloadResolver.CreateEncryptedPayloadsAsync(
            disclosureResults,
            wallets,
            wallets["applicant"]);

        // Assert 3: Payloads encrypted
        encryptedPayloads.Should().HaveCount(2);
        encryptedPayloads.Should().ContainKey(wallets["applicant"]);
        encryptedPayloads.Should().ContainKey(wallets["loan-officer"]);

        // Act 4: Build transaction
        var transaction = await _transactionBuilder.BuildActionTransactionAsync(
            blueprint.Id!,
            "1",
            null,
            null,
            encryptedPayloads,
            wallets["applicant"],
            "register-1");

        // Assert 4: Transaction built correctly
        transaction.Should().NotBeNull();
        // Note: SenderWallet is only set during signing, not during build
        transaction.Recipients.Should().HaveCount(2);
        transaction.RegisterId.Should().Be("register-1");

        var metadata = JsonSerializer.Deserialize<JsonElement>(transaction.Metadata!);
        metadata.GetProperty("blueprintId").GetString().Should().Be("loan-application-bp");
        metadata.GetProperty("actionId").GetString().Should().Be("1");
    }

    [Fact]
    public async Task Integration_CachingWorks_AcrossMultipleCalls()
    {
        // Arrange
        var blueprint = new BlueprintModel
        {
            Id = "cached-bp",
            Title = "Cached Blueprint"
        };

        _mockPublishedStore.Setup(x => x.GetVersionsAsync("cached-bp"))
            .ReturnsAsync(new[]
            {
                new PublishedBlueprint
                {
                    BlueprintId = "cached-bp",
                    PublicationTxId = TestDefinitionPin,
                    Blueprint = blueprint
                }
            });

        // Act: Get blueprint twice
        var first = await _actionResolver.GetBlueprintAsync("cached-bp", TestDefinitionPin);
        var second = await _actionResolver.GetBlueprintAsync("cached-bp", TestDefinitionPin);

        // Assert: Store was only called once (second call used cache)
        first.Should().NotBeNull();
        second.Should().NotBeNull();
        first!.Title.Should().Be(second!.Title);
        _mockPublishedStore.Verify(x => x.GetVersionsAsync("cached-bp"), Times.Once,
            "the second call is served from the definition-keyed cache");
    }

    [Fact]
    public async Task Integration_PayloadAndTransaction_WithMultipleParticipants()
    {
        // Arrange
        var participantWallets = new Dictionary<string, string>
        {
            ["p1"] = "wallet-1",
            ["p2"] = "wallet-2",
            ["p3"] = "wallet-3"
        };

        var disclosureResults = new Dictionary<string, object>
        {
            ["p1"] = new { field1 = "value1" },
            ["p2"] = new { field1 = "value1", field2 = "value2" },
            ["p3"] = new { field1 = "value1", field2 = "value2", field3 = "value3" }
        };

        // Act: Create payloads
        var payloads = await _payloadResolver.CreateEncryptedPayloadsAsync(
            disclosureResults,
            participantWallets,
            "sender-wallet");

        // Build transaction
        var transaction = await _transactionBuilder.BuildActionTransactionAsync(
            "blueprint-id",
            "1",
            null,
            null,
            payloads,
            "sender-wallet",
            "register-1");

        // Assert
        payloads.Should().HaveCount(3);
        transaction.Recipients.Should().HaveCount(3);
        transaction.Recipients.Should().Contain("wallet-1");
        transaction.Recipients.Should().Contain("wallet-2");
        transaction.Recipients.Should().Contain("wallet-3");
    }

    [Fact]
    public async Task Integration_RejectionTransaction_BuildsCorrectly()
    {
        // Arrange
        var originalTxHash = "tx-to-reject";
        var reason = "Data validation failed";

        // Act
        var rejection = await _transactionBuilder.BuildRejectionTransactionAsync(
            originalTxHash,
            reason,
            "wallet-sender",
            "register-1");

        // Assert
        rejection.Should().NotBeNull();
        rejection.PreviousTxHash.Should().Be(originalTxHash);

        var metadata = JsonSerializer.Deserialize<JsonElement>(rejection.Metadata!);
        metadata.GetProperty("type").GetString().Should().Be("rejection");
        metadata.GetProperty("reason").GetString().Should().Be(reason);
    }

    [Fact]
    public async Task Integration_FileTransactions_WithMultipleFiles()
    {
        // Arrange
        var files = new List<FileAttachment>
        {
            new FileAttachment(
                "invoice.pdf",
                "application/pdf",
                Encoding.UTF8.GetBytes("PDF invoice content")),
            new FileAttachment(
                "receipt.jpg",
                "image/jpeg",
                Encoding.UTF8.GetBytes("JPG receipt content")),
            new FileAttachment(
                "notes.txt",
                "text/plain",
                Encoding.UTF8.GetBytes("Additional notes"))
        };

        var parentTxHash = "parent-tx-hash";

        // Act
        var fileTransactions = await _transactionBuilder.BuildFileTransactionsAsync(
            files,
            parentTxHash,
            "wallet-sender",
            "register-1");

        // Assert
        fileTransactions.Should().HaveCount(3);

        foreach (var tx in fileTransactions)
        {
            tx.PreviousTxHash.Should().Be(parentTxHash);
            var metadata = JsonSerializer.Deserialize<JsonElement>(tx.Metadata!);
            metadata.GetProperty("type").GetString().Should().Be("file");
            metadata.GetProperty("parentTransactionHash").GetString().Should().Be(parentTxHash);
        }

        // Verify file names
        var metadatas = fileTransactions.Select(tx =>
            JsonSerializer.Deserialize<JsonElement>(tx.Metadata!)).ToList();

        metadatas[0].GetProperty("fileName").GetString().Should().Be("invoice.pdf");
        metadatas[1].GetProperty("fileName").GetString().Should().Be("receipt.jpg");
        metadatas[2].GetProperty("fileName").GetString().Should().Be("notes.txt");
    }

    [Fact]
    public async Task Integration_CompleteWorkflowSimulation()
    {
        // This test simulates a complete workflow from blueprint resolution to transaction creation
        // Arrange: Setup a purchase order workflow
        var blueprint = new BlueprintModel
        {
            Id = "purchase-order-workflow",
            Title = "Purchase Order Workflow",
            Participants = new List<ParticipantModel>
            {
                new ParticipantModel { Id = "buyer", Name = "Buyer Company", WalletAddress = "wallet-buyer" },
                new ParticipantModel { Id = "seller", Name = "Seller Company", WalletAddress = "wallet-seller" },
                new ParticipantModel { Id = "logistics", Name = "Logistics Provider", WalletAddress = "wallet-logistics" }
            },
            Actions = new List<ActionModel>
            {
                new ActionModel { Id = 1, Title = "Create Order", Sender = "buyer" },
                new ActionModel { Id = 2, Title = "Accept Order", Sender = "seller" },
                new ActionModel { Id = 3, Title = "Ship Order", Sender = "logistics" }
            }
        };

        _mockPublishedStore.Setup(x => x.GetVersionsAsync("purchase-order-workflow"))
            .ReturnsAsync(new[]
            {
                new PublishedBlueprint
                {
                    BlueprintId = "purchase-order-workflow",
                    PublicationTxId = TestDefinitionPin,
                    Blueprint = blueprint
                }
            });

        // Step 1: Buyer creates order
        var resolvedBp = await _actionResolver.GetBlueprintAsync("purchase-order-workflow", TestDefinitionPin);
        var createOrderAction = _actionResolver.GetActionDefinition(resolvedBp!, "1");
        var wallets = await _actionResolver.ResolveParticipantWalletsAsync(
            resolvedBp!,
            new[] { "buyer", "seller", "logistics" });

        var orderData = new Dictionary<string, object>
        {
            ["buyer"] = new { items = new[] { "Widget A", "Widget B" }, total = 1000 },
            ["seller"] = new { items = new[] { "Widget A", "Widget B" }, total = 1000 },
            ["logistics"] = new { deliveryAddress = "123 Main St" }
        };

        var orderPayloads = await _payloadResolver.CreateEncryptedPayloadsAsync(
            orderData,
            wallets,
            wallets["buyer"]);

        var orderTx = await _transactionBuilder.BuildActionTransactionAsync(
            blueprint.Id!,
            "1",
            null, // New instance
            null,
            orderPayloads,
            wallets["buyer"],
            "register-1");

        // Assert Step 1
        orderTx.Should().NotBeNull();
        var orderMetadata = JsonSerializer.Deserialize<JsonElement>(orderTx.Metadata!);
        orderMetadata.GetProperty("actionId").GetString().Should().Be("1");
        var instanceId = orderMetadata.GetProperty("instanceId").GetString();
        instanceId.Should().NotBeNullOrEmpty();

        // Step 2: Seller accepts order (using same instance)
        var acceptOrderAction = _actionResolver.GetActionDefinition(resolvedBp!, "2");
        var acceptData = new Dictionary<string, object>
        {
            ["buyer"] = new { status = "accepted", estimatedDelivery = "5 days" },
            ["seller"] = new { status = "accepted", estimatedDelivery = "5 days", internalNotes = "Priority order" },
            ["logistics"] = new { deliveryScheduled = true }
        };

        var acceptPayloads = await _payloadResolver.CreateEncryptedPayloadsAsync(
            acceptData,
            wallets,
            wallets["seller"]);

        // Simulate getting previous TX hash (would come from register service)
        var previousTxHash = "simulated-order-tx-hash";

        var acceptTx = await _transactionBuilder.BuildActionTransactionAsync(
            blueprint.Id!,
            "2",
            instanceId, // Same instance
            previousTxHash,
            acceptPayloads,
            wallets["seller"],
            "register-1");

        // Assert Step 2
        acceptTx.Should().NotBeNull();
        acceptTx.PreviousTxHash.Should().Be(previousTxHash);
        var acceptMetadata = JsonSerializer.Deserialize<JsonElement>(acceptTx.Metadata!);
        acceptMetadata.GetProperty("instanceId").GetString().Should().Be(instanceId);
        acceptMetadata.GetProperty("actionId").GetString().Should().Be("2");

        // Verify workflow continuity
        orderTx.RegisterId.Should().Be(acceptTx.RegisterId);
    }
}
