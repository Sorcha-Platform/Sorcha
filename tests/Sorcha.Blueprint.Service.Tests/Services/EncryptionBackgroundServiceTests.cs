// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Threading.Channels;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Blueprint.Models;
using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Services.Implementation;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.Blueprint.Service.Storage;
using Sorcha.ServiceClients.Register;
using Sorcha.ServiceClients.Validator;
using Sorcha.ServiceClients.Wallet;
using Sorcha.TransactionHandler.Encryption;
using Sorcha.TransactionHandler.Encryption.Models;
using EncryptionOpStatus = Sorcha.Blueprint.Service.Models.EncryptionOperationStatus;

namespace Sorcha.Blueprint.Service.Tests.Services;

public class EncryptionBackgroundServiceTests
{
    private readonly Mock<INotificationService> _notificationService = new();
    private readonly Mock<IEncryptionPipelineService> _encryptionPipeline = new();
    private readonly Mock<ITransactionBuilderService> _transactionBuilder = new();
    private readonly Mock<IWalletServiceClient> _walletClient = new();
    private readonly Mock<IValidatorServiceClient> _validatorClient = new();
    private readonly Mock<IActionResolverService> _actionResolver = new();
    private readonly Mock<IInstanceStore> _instanceStore = new();
    private readonly Mock<IEncryptionOperationStore> _operationStore = new();
    // SUT now resolves IRegisterServiceClient from scope to look up the register's
    // bloom filter / recipient routing during encryption. Without registering this,
    // the pipeline throws on resolution and encryption fails before the success path.
    private readonly Mock<IRegisterServiceClient> _registerClient = new();
    private readonly Mock<IEncryptionInboxWriter> _encryptionInboxWriter = new();

    private EncryptionBackgroundService CreateService(Channel<EncryptionWorkItem> channel)
    {
        var services = new ServiceCollection();
        services.AddSingleton(_notificationService.Object);
        services.AddSingleton(_encryptionPipeline.Object);
        services.AddSingleton(_transactionBuilder.Object);
        services.AddSingleton(_walletClient.Object);
        services.AddSingleton(_validatorClient.Object);
        services.AddSingleton(_actionResolver.Object);
        services.AddSingleton(_instanceStore.Object);
        services.AddSingleton(_registerClient.Object);
        services.AddSingleton(_encryptionInboxWriter.Object);
        // SUT now also resolves IOptions<TransactionConfirmationOptions> from scope.
        services.AddSingleton<Microsoft.Extensions.Options.IOptions<Sorcha.Blueprint.Service.Models.TransactionConfirmationOptions>>(
            Microsoft.Extensions.Options.Options.Create(new Sorcha.Blueprint.Service.Models.TransactionConfirmationOptions()));
        var serviceProvider = services.BuildServiceProvider();

        return new EncryptionBackgroundService(
            channel,
            _operationStore.Object,
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<EncryptionBackgroundService>.Instance);
    }

    private EncryptionWorkItem CreateTestWorkItem(string operationId = "op123")
    {
        return new EncryptionWorkItem
        {
            OperationId = operationId,
            InstanceId = "inst-1",
            BlueprintId = "bp-1",
            ActionId = 1,
            SenderWallet = "wallet-sender-001",
            RegisterId = "reg-1",
            DisclosureGroups = [new DisclosureGroup
            {
                GroupId = "g1",
                DisclosedFields = ["field1"],
                FilteredPayload = new Dictionary<string, object> { ["field1"] = "value1" },
                Recipients = [new RecipientInfo
                {
                    WalletAddress = "wallet-recipient-001",
                    PublicKey = new byte[32],
                    Algorithm = Sorcha.Cryptography.Enums.WalletNetworks.ED25519,
                    Source = KeySource.Register
                }]
            }],
            PayloadWithCalculations = new Dictionary<string, object> { ["field1"] = "value1" },
            DisclosedPayloads = new Dictionary<string, Dictionary<string, object>>
            {
                ["wallet-recipient-001"] = new Dictionary<string, object> { ["field1"] = "value1" }
            },
            PreviousTransactionId = "prev-tx-hash",
            DelegationToken = "delegation-token",
            RoutingResult = new RoutingResult(),
            MergedData = new Dictionary<string, object> { ["field1"] = "value1" },
            AllowedAccumulatedFields = []
        };
    }

    private void SetupSuccessfulPipeline()
    {
        var testOperation = new EncryptionOperation
        {
            OperationId = "op123",
            BlueprintId = "bp-1",
            ActionId = "1",
            InstanceId = "inst-1",
            SubmittingWalletAddress = "wallet-sender-001"
        };

        _operationStore.Setup(s => s.GetByIdAsync(It.IsAny<string>()))
            .ReturnsAsync(testOperation);
        _operationStore.Setup(s => s.UpdateAsync(It.IsAny<EncryptionOperation>()))
            .ReturnsAsync((EncryptionOperation op) => op);

        // CheckSizeLimit must return within-limit so the pipeline proceeds past the pre-flight check
        _encryptionPipeline.Setup(e => e.CheckSizeLimit(
                It.IsAny<DisclosureGroup[]>(), It.IsAny<long>()))
            .Returns((true, 1024L, 4 * 1024 * 1024L));

        _encryptionPipeline.Setup(e => e.EncryptDisclosedPayloadsAsync(
                It.IsAny<DisclosureGroup[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(EncryptionResult.Succeeded([new EncryptedPayloadGroup
            {
                GroupId = "g1",
                DisclosedFields = ["field1"],
                Ciphertext = new byte[64],
                Nonce = new byte[12],
                PlaintextHash = new byte[32],
                EncryptionAlgorithm = Sorcha.Cryptography.Enums.EncryptionType.AES_GCM,
                WrappedKeys = [new WrappedKey
                {
                    WalletAddress = "wallet-recipient-001",
                    EncryptedKey = new byte[48],
                    Algorithm = Sorcha.Cryptography.Enums.WalletNetworks.ED25519
                }]
            }]));

        _instanceStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Instance
            {
                Id = "inst-1",
                BlueprintId = "bp-1",
                BlueprintDefinitionTxId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", // Feature 195: an instance must carry its definition pin, or execution has nothing to resolve or chain from
                BlueprintVersion = 1,
                RegisterId = "reg-1",
                TenantId = "tenant-1",
                State = InstanceState.Active,
                ParticipantWallets = new Dictionary<string, string> { ["p1"] = "wallet-sender-001" }
            });

        _actionResolver.Setup(a => a.GetBlueprintAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Blueprint.Models.Blueprint
            {
                Title = "Test Blueprint",
                Participants = [new Participant { Id = "p1", Name = "Test" }],
                Actions = [new Blueprint.Models.Action { Id = 1, Title = "Action 1" }]
            });

        _actionResolver.Setup(a => a.GetActionDefinition(It.IsAny<Blueprint.Models.Blueprint>(), "1"))
            .Returns(new Blueprint.Models.Action { Id = 1, Title = "Action 1" });

        // BuildEncryptedActionTransactionAsync is a static extension method — it runs directly
        // on the mock without needing setup. It serializes and hashes internally.

        _walletClient.Setup(w => w.SignTransactionAsync(
                It.IsAny<string>(), It.IsAny<byte[]>(),
                It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WalletSignResult
            {
                Signature = new byte[64],
                PublicKey = new byte[32],
                SignedBy = "wallet-sender-001",
                Algorithm = "ed25519"
            });

        _validatorClient.Setup(v => v.SubmitTransactionAsync(
                It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionSubmissionResult
            {
                Success = true,
                TransactionId = "abc123def456abc123def456abc123def456abc123def456abc123def456abcd"
            });

    }

    [Fact]
    public async Task ProcessWorkItem_SuccessfulEncryption_SendsProgressAndComplete()
    {
        // Arrange
        var channel = Channel.CreateUnbounded<EncryptionWorkItem>();
        var service = CreateService(channel);
        var workItem = CreateTestWorkItem();
        SetupSuccessfulPipeline();

        // Act
        await service.ProcessWorkItemAsync(workItem, CancellationToken.None);

        // Assert — progress notifications sent for each step (thin EncryptionSignal)
        _notificationService.Verify(n => n.NotifyEncryptionProgressAsync(
            "wallet-sender-001",
            It.Is<EncryptionSignal>(s => s.PercentComplete == 10 && s.Status == "encrypting"),
            It.IsAny<CancellationToken>()), Times.Once);

        _notificationService.Verify(n => n.NotifyEncryptionProgressAsync(
            "wallet-sender-001",
            It.Is<EncryptionSignal>(s => s.PercentComplete == 30 && s.Status == "encrypting"),
            It.IsAny<CancellationToken>()), Times.Once);

        _notificationService.Verify(n => n.NotifyEncryptionProgressAsync(
            "wallet-sender-001",
            It.Is<EncryptionSignal>(s => s.PercentComplete == 60 && s.Status == "encrypting"),
            It.IsAny<CancellationToken>()), Times.Once);

        _notificationService.Verify(n => n.NotifyEncryptionProgressAsync(
            "wallet-sender-001",
            It.Is<EncryptionSignal>(s => s.PercentComplete == 80 && s.Status == "encrypting"),
            It.IsAny<CancellationToken>()), Times.Once);

        // Assert — completion notification sent
        _notificationService.Verify(n => n.NotifyEncryptionCompleteAsync(
            "wallet-sender-001",
            It.Is<EncryptionSignal>(s =>
                s.OperationId == "op123" &&
                s.PercentComplete == 100 &&
                s.Status == "complete"),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessWorkItem_EncryptionFailure_SendsFailedNotification()
    {
        // Arrange
        var channel = Channel.CreateUnbounded<EncryptionWorkItem>();
        var service = CreateService(channel);
        var workItem = CreateTestWorkItem();

        var testOperation = new EncryptionOperation
        {
            OperationId = "op123",
            BlueprintId = "bp-1",
            ActionId = "1",
            InstanceId = "inst-1",
            SubmittingWalletAddress = "wallet-sender-001"
        };
        _operationStore.Setup(s => s.GetByIdAsync("op123")).ReturnsAsync(testOperation);
        _operationStore.Setup(s => s.UpdateAsync(It.IsAny<EncryptionOperation>()))
            .ReturnsAsync((EncryptionOperation op) => op);

        // CheckSizeLimit must pass so the pipeline reaches the encryption step
        _encryptionPipeline.Setup(e => e.CheckSizeLimit(
                It.IsAny<DisclosureGroup[]>(), It.IsAny<long>()))
            .Returns((true, 1024L, 4 * 1024 * 1024L));

        _encryptionPipeline.Setup(e => e.EncryptDisclosedPayloadsAsync(
                It.IsAny<DisclosureGroup[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(EncryptionResult.Failed("P-256 key error", "wallet-recipient-001"));

        // Act
        await service.ProcessWorkItemAsync(workItem, CancellationToken.None);

        // Assert
        _notificationService.Verify(n => n.NotifyEncryptionFailedAsync(
            "wallet-sender-001",
            It.Is<EncryptionSignal>(s =>
                s.OperationId == "op123" &&
                s.Status == "failed"),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once);

        // Operation store should be updated with failure (the same object is mutated across steps)
        _operationStore.Verify(s => s.UpdateAsync(It.Is<EncryptionOperation>(op =>
            op.Status == EncryptionOpStatus.Failed &&
            op.Error != null &&
            op.FailedRecipient == "wallet-recipient-001")), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ProcessWorkItem_CancellationToken_StopsProcessing()
    {
        // Arrange
        var channel = Channel.CreateUnbounded<EncryptionWorkItem>();
        var service = CreateService(channel);
        var workItem = CreateTestWorkItem();

        var cts = new CancellationTokenSource();
        cts.Cancel();

        var testOperation = new EncryptionOperation
        {
            OperationId = "op123",
            BlueprintId = "bp-1",
            ActionId = "1",
            InstanceId = "inst-1",
            SubmittingWalletAddress = "wallet-sender-001"
        };
        _operationStore.Setup(s => s.GetByIdAsync("op123")).ReturnsAsync(testOperation);
        _operationStore.Setup(s => s.UpdateAsync(It.IsAny<EncryptionOperation>()))
            .ReturnsAsync((EncryptionOperation op) => op);

        // CheckSizeLimit must pass so the pipeline reaches the encryption step
        _encryptionPipeline.Setup(e => e.CheckSizeLimit(
                It.IsAny<DisclosureGroup[]>(), It.IsAny<long>()))
            .Returns((true, 1024L, 4 * 1024 * 1024L));

        _encryptionPipeline.Setup(e => e.EncryptDisclosedPayloadsAsync(
                It.IsAny<DisclosureGroup[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.ProcessWorkItemAsync(workItem, cts.Token));

        // Should update operation status to Failed
        _operationStore.Verify(s => s.UpdateAsync(It.Is<EncryptionOperation>(op =>
            op.Status == EncryptionOpStatus.Failed)), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ProcessWorkItem_UpdatesOperationStore_AtEachStep()
    {
        // Arrange
        var channel = Channel.CreateUnbounded<EncryptionWorkItem>();
        var service = CreateService(channel);
        var workItem = CreateTestWorkItem();
        SetupSuccessfulPipeline();

        var updateCalls = new List<EncryptionOperation>();
        _operationStore.Setup(s => s.UpdateAsync(It.IsAny<EncryptionOperation>()))
            .Callback<EncryptionOperation>(op => updateCalls.Add(new EncryptionOperation
            {
                OperationId = op.OperationId,
                BlueprintId = op.BlueprintId,
                ActionId = op.ActionId,
                InstanceId = op.InstanceId,
                SubmittingWalletAddress = op.SubmittingWalletAddress,
                Status = op.Status,
                CurrentStep = op.CurrentStep,
                StepName = op.StepName,
                PercentComplete = op.PercentComplete,
                TransactionHash = op.TransactionHash
            }))
            .ReturnsAsync((EncryptionOperation op) => op);

        // Act
        await service.ProcessWorkItemAsync(workItem, CancellationToken.None);

        // Assert — at least 5 updates (4 steps + completion)
        updateCalls.Should().HaveCountGreaterThanOrEqualTo(5);

        // Verify the progression of statuses
        updateCalls.Should().Contain(op => op.Status == EncryptionOpStatus.ResolvingKeys);
        updateCalls.Should().Contain(op => op.Status == EncryptionOpStatus.Encrypting);
        updateCalls.Should().Contain(op => op.Status == EncryptionOpStatus.BuildingTransaction);
        updateCalls.Should().Contain(op => op.Status == EncryptionOpStatus.Submitting);
        updateCalls.Should().Contain(op => op.Status == EncryptionOpStatus.Complete);

        // Final update should have 100% completion
        var lastUpdate = updateCalls.Last();
        lastUpdate.PercentComplete.Should().Be(100);
        lastUpdate.TransactionHash.Should().NotBeNullOrEmpty();
    }

}
