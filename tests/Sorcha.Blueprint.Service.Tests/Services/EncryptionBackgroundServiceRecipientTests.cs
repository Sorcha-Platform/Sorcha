// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Threading.Channels;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Blueprint.Models;
using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Services.Implementation;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.Blueprint.Service.Storage;
using Sorcha.ServiceClients.Validator;
using Sorcha.ServiceClients.Wallet;
using Sorcha.TransactionHandler.Encryption;
using Sorcha.TransactionHandler.Encryption.Models;
using EncryptionOpStatus = Sorcha.Blueprint.Service.Models.EncryptionOperationStatus;

namespace Sorcha.Blueprint.Service.Tests.Services;

/// <summary>
/// Integration-style tests verifying that <see cref="EncryptionBackgroundService"/>
/// stores per-recipient progress in the operation store during processing.
/// Per-recipient SignalR signals were removed; recipients are now pull-back only.
/// </summary>
public class EncryptionBackgroundServiceRecipientTests
{
    private readonly Mock<INotificationService> _notificationService = new();
    private readonly Mock<IEncryptionPipelineService> _encryptionPipeline = new();
    private readonly Mock<ITransactionBuilderService> _transactionBuilder = new();
    private readonly Mock<IWalletServiceClient> _walletClient = new();
    private readonly Mock<IValidatorServiceClient> _validatorClient = new();
    private readonly Mock<IActionResolverService> _actionResolver = new();
    private readonly Mock<IInstanceStore> _instanceStore = new();
    private readonly Mock<IEncryptionOperationStore> _operationStore = new();
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
        services.AddSingleton(_encryptionInboxWriter.Object);
        var serviceProvider = services.BuildServiceProvider();

        return new EncryptionBackgroundService(
            channel,
            _operationStore.Object,
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<EncryptionBackgroundService>.Instance);
    }

    private EncryptionWorkItem CreateTestWorkItem(string operationId = "op-recip-bg")
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
                DisclosedFields = ["/name", "/email"],
                FilteredPayload = new Dictionary<string, object> { ["name"] = "Alice", ["email"] = "a@b.com" },
                Recipients =
                [
                    new RecipientInfo
                    {
                        WalletAddress = "ws1qalice",
                        PublicKey = new byte[32],
                        Algorithm = Sorcha.Cryptography.Enums.WalletNetworks.ED25519,
                        Source = KeySource.Register,
                        DisplayName = "Alice"
                    },
                    new RecipientInfo
                    {
                        WalletAddress = "ws1qbob",
                        PublicKey = new byte[32],
                        Algorithm = Sorcha.Cryptography.Enums.WalletNetworks.NISTP256,
                        Source = KeySource.Register,
                        DisplayName = "Bob"
                    },
                    new RecipientInfo
                    {
                        WalletAddress = "ws1qcarol",
                        PublicKey = new byte[32],
                        Algorithm = Sorcha.Cryptography.Enums.WalletNetworks.RSA4096,
                        Source = KeySource.Register,
                        DisplayName = "Carol"
                    }
                ]
            }],
            PayloadWithCalculations = new Dictionary<string, object> { ["name"] = "Alice", ["email"] = "a@b.com" },
            DisclosedPayloads = new Dictionary<string, Dictionary<string, object>>
            {
                ["ws1qalice"] = new() { ["name"] = "Alice", ["email"] = "a@b.com" },
                ["ws1qbob"] = new() { ["name"] = "Alice", ["email"] = "a@b.com" },
                ["ws1qcarol"] = new() { ["name"] = "Alice", ["email"] = "a@b.com" }
            },
            PreviousTransactionId = "prev-tx-hash",
            DelegationToken = "delegation-token",
            RoutingResult = new RoutingResult(),
            MergedData = new Dictionary<string, object> { ["name"] = "Alice" },
            AllowedAccumulatedFields = []
        };
    }

    private void SetupSuccessfulPipelineWithRecipientProgress()
    {
        var testOperation = new EncryptionOperation
        {
            OperationId = "op-recip-bg",
            BlueprintId = "bp-1",
            ActionId = "1",
            InstanceId = "inst-1",
            SubmittingWalletAddress = "wallet-sender-001"
        };

        _operationStore.Setup(s => s.GetByIdAsync(It.IsAny<string>()))
            .ReturnsAsync(testOperation);
        _operationStore.Setup(s => s.UpdateAsync(It.IsAny<EncryptionOperation>()))
            .ReturnsAsync((EncryptionOperation op) => op);

        _encryptionPipeline.Setup(e => e.CheckSizeLimit(
                It.IsAny<DisclosureGroup[]>(), It.IsAny<long>()))
            .Returns((true, 2048L, 4 * 1024 * 1024L));

        // Return encryption result WITH RecipientProgressEntries
        var encryptionResult = new EncryptionResult
        {
            Success = true,
            Groups = [new EncryptedPayloadGroup
            {
                GroupId = "g1",
                DisclosedFields = ["/name", "/email"],
                Ciphertext = new byte[64],
                Nonce = new byte[12],
                PlaintextHash = new byte[32],
                EncryptionAlgorithm = Sorcha.Cryptography.Enums.EncryptionType.XCHACHA20_POLY1305,
                WrappedKeys =
                [
                    new WrappedKey { WalletAddress = "ws1qalice", EncryptedKey = new byte[48], Algorithm = Sorcha.Cryptography.Enums.WalletNetworks.ED25519 },
                    new WrappedKey { WalletAddress = "ws1qbob", EncryptedKey = new byte[48], Algorithm = Sorcha.Cryptography.Enums.WalletNetworks.NISTP256 },
                    new WrappedKey { WalletAddress = "ws1qcarol", EncryptedKey = new byte[48], Algorithm = Sorcha.Cryptography.Enums.WalletNetworks.RSA4096 }
                ]
            }],
            RecipientProgressEntries =
            [
                new RecipientProgress { WalletAddress = "ws1qalice", DisplayName = "Alice", DisclosedFields = ["/name", "/email"], GroupId = "g1", Status = RecipientProgressStatus.Secured },
                new RecipientProgress { WalletAddress = "ws1qbob", DisplayName = "Bob", DisclosedFields = ["/name", "/email"], GroupId = "g1", Status = RecipientProgressStatus.Secured },
                new RecipientProgress { WalletAddress = "ws1qcarol", DisplayName = "Carol", DisclosedFields = ["/name", "/email"], GroupId = "g1", Status = RecipientProgressStatus.Secured }
            ]
        };

        _encryptionPipeline.Setup(e => e.EncryptDisclosedPayloadsAsync(
                It.IsAny<DisclosureGroup[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(encryptionResult);

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
    public async Task ProcessWorkItem_WithRecipientProgress_StoresRecipientsInOperationStore()
    {
        // Arrange
        var channel = Channel.CreateUnbounded<EncryptionWorkItem>();
        var service = CreateService(channel);
        var workItem = CreateTestWorkItem();
        SetupSuccessfulPipelineWithRecipientProgress();

        // Act
        await service.ProcessWorkItemAsync(workItem, CancellationToken.None);

        // Assert — operation store was updated with per-recipient status
        _operationStore.Verify(s => s.UpdateAsync(It.Is<EncryptionOperation>(op =>
            op.Recipients.Count == 3 &&
            op.Recipients.Any(r => r.Name == "Alice") &&
            op.Recipients.Any(r => r.Name == "Bob") &&
            op.Recipients.Any(r => r.Name == "Carol"))), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ProcessWorkItem_WithRecipientProgress_StillEmitsStepLevelProgress()
    {
        // Arrange
        var channel = Channel.CreateUnbounded<EncryptionWorkItem>();
        var service = CreateService(channel);
        var workItem = CreateTestWorkItem();
        SetupSuccessfulPipelineWithRecipientProgress();

        // Act
        await service.ProcessWorkItemAsync(workItem, CancellationToken.None);

        // Assert — step-level NotifyEncryptionProgressAsync still called for all 4 steps
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
    }

    [Fact]
    public async Task ProcessWorkItem_EncryptionFails_RecipientStatusStillStored()
    {
        // Arrange — pipeline returns failure with some recipient progress
        var channel = Channel.CreateUnbounded<EncryptionWorkItem>();
        var service = CreateService(channel);
        var workItem = CreateTestWorkItem();

        var testOperation = new EncryptionOperation
        {
            OperationId = "op-recip-bg",
            BlueprintId = "bp-1",
            ActionId = "1",
            InstanceId = "inst-1",
            SubmittingWalletAddress = "wallet-sender-001"
        };
        _operationStore.Setup(s => s.GetByIdAsync(It.IsAny<string>())).ReturnsAsync(testOperation);
        _operationStore.Setup(s => s.UpdateAsync(It.IsAny<EncryptionOperation>()))
            .ReturnsAsync((EncryptionOperation op) => op);

        _encryptionPipeline.Setup(e => e.CheckSizeLimit(
                It.IsAny<DisclosureGroup[]>(), It.IsAny<long>()))
            .Returns((true, 1024L, 4 * 1024 * 1024L));

        var failureResult = new EncryptionResult
        {
            Success = false,
            Error = "Key wrapping failed for ws1qbob",
            FailedRecipient = "ws1qbob",
            RecipientProgressEntries =
            [
                new RecipientProgress { WalletAddress = "ws1qalice", DisplayName = "Alice", DisclosedFields = ["/name"], GroupId = "g1", Status = RecipientProgressStatus.Secured },
                new RecipientProgress { WalletAddress = "ws1qbob", DisplayName = "Bob", DisclosedFields = ["/name"], GroupId = "g1", Status = RecipientProgressStatus.Failed, ErrorMessage = "Invalid key" }
            ]
        };

        _encryptionPipeline.Setup(e => e.EncryptDisclosedPayloadsAsync(
                It.IsAny<DisclosureGroup[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(failureResult);

        // Act
        await service.ProcessWorkItemAsync(workItem, CancellationToken.None);

        // Assert — recipient status stored in operation store even on failure
        _operationStore.Verify(s => s.UpdateAsync(It.Is<EncryptionOperation>(op =>
            op.Recipients.Count == 2 &&
            op.Recipients.Any(r => r.Name == "Alice" && r.Status == "secured") &&
            op.Recipients.Any(r => r.Name == "Bob" && r.Status == "failed"))), Times.AtLeastOnce);

        // Assert — failure notification also sent via EncryptionSignal
        _notificationService.Verify(n => n.NotifyEncryptionFailedAsync(
            "wallet-sender-001",
            It.Is<EncryptionSignal>(s => s.Status == "failed"),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
