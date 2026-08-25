// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Services.Implementation;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.Cryptography.Interfaces;
using Sorcha.TransactionHandler.Enums;
using System.Text;
using System.Text.Json;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;
using ActionModel = Sorcha.Blueprint.Models.Action;
using ParticipantModel = Sorcha.Blueprint.Models.Participant;

namespace Sorcha.Blueprint.Service.Tests.Services;

public class TransactionBuilderServiceTests
{
    private readonly Mock<ICryptoModule> _mockCryptoModule;
    private readonly Mock<IHashProvider> _mockHashProvider;
    private readonly Mock<ISymmetricCrypto> _mockSymmetricCrypto;
    private readonly Mock<ILogger<TransactionBuilderService>> _mockLogger;
    private readonly TransactionBuilderService _service;

    public TransactionBuilderServiceTests()
    {
        _mockCryptoModule = new Mock<ICryptoModule>();
        _mockHashProvider = new Mock<IHashProvider>();
        _mockSymmetricCrypto = new Mock<ISymmetricCrypto>();
        _mockLogger = new Mock<ILogger<TransactionBuilderService>>();
        _service = new TransactionBuilderService(
            _mockCryptoModule.Object,
            _mockHashProvider.Object,
            _mockSymmetricCrypto.Object,
            _mockLogger.Object);
    }

    [Fact]
    public async Task BuildActionTransactionAsync_WithValidData_CreatesTransaction()
    {
        // Arrange
        var blueprintId = "blueprint-1";
        var actionId = "action-1";
        var instanceId = "instance-123";
        var previousTxHash = "prev-tx-hash";
        var encryptedPayloads = new Dictionary<string, byte[]>
        {
            ["wallet-alice"] = Encoding.UTF8.GetBytes("encrypted-data-alice"),
            ["wallet-bob"] = Encoding.UTF8.GetBytes("encrypted-data-bob")
        };
        var senderWallet = "wallet-sender";
        var registerAddress = "register-1";

        // Act
        var result = await _service.BuildActionTransactionAsync(
            blueprintId,
            actionId,
            instanceId,
            previousTxHash,
            encryptedPayloads,
            senderWallet,
            registerAddress);

        // Assert
        result.Should().NotBeNull();
        // Note: SenderWallet is only set during signing, not during build
        result.Recipients.Should().HaveCount(2);
        result.Recipients.Should().Contain("wallet-alice");
        result.Recipients.Should().Contain("wallet-bob");
        result.PreviousTxHash.Should().Be(previousTxHash);
        result.RegisterId.Should().Be(registerAddress);
        result.Version.Should().Be(TransactionVersion.V1);

        // Verify metadata
        result.Metadata.Should().NotBeNullOrEmpty();
        var metadata = JsonSerializer.Deserialize<JsonElement>(result.Metadata!);
        metadata.GetProperty("type").GetString().Should().Be("action");
        metadata.GetProperty("blueprintId").GetString().Should().Be(blueprintId);
        metadata.GetProperty("actionId").GetString().Should().Be(actionId);
        metadata.GetProperty("instanceId").GetString().Should().Be(instanceId);
    }

    [Fact]
    public async Task BuildActionTransactionAsync_WithoutInstanceId_GeneratesNew()
    {
        // Arrange
        var blueprintId = "blueprint-1";
        var actionId = "action-1";
        var encryptedPayloads = new Dictionary<string, byte[]>
        {
            ["wallet-alice"] = Encoding.UTF8.GetBytes("data")
        };
        var senderWallet = "wallet-sender";
        var registerAddress = "register-1";

        // Act
        var result = await _service.BuildActionTransactionAsync(
            blueprintId,
            actionId,
            null, // No instance ID
            null,
            encryptedPayloads,
            senderWallet,
            registerAddress);

        // Assert
        result.Should().NotBeNull();
        var metadata = JsonSerializer.Deserialize<JsonElement>(result.Metadata!);
        var instanceId = metadata.GetProperty("instanceId").GetString();
        instanceId.Should().NotBeNullOrEmpty();
        Guid.TryParse(instanceId, out _).Should().BeTrue();
    }

    [Fact]
    public async Task BuildActionTransactionAsync_WithNullBlueprintId_ThrowsArgumentException()
    {
        // Arrange
        var encryptedPayloads = new Dictionary<string, byte[]> { ["wallet"] = new byte[1] };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.BuildActionTransactionAsync(
                null!,
                "action-1",
                null,
                null,
                encryptedPayloads,
                "wallet-sender",
                "register-1"));
    }

    [Fact]
    public async Task BuildActionTransactionAsync_WithNullActionId_ThrowsArgumentException()
    {
        // Arrange
        var encryptedPayloads = new Dictionary<string, byte[]> { ["wallet"] = new byte[1] };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.BuildActionTransactionAsync(
                "blueprint-1",
                null!,
                null,
                null,
                encryptedPayloads,
                "wallet-sender",
                "register-1"));
    }

    [Fact]
    public async Task BuildActionTransactionAsync_WithEmptyPayloads_ThrowsArgumentException()
    {
        // Arrange
        var emptyPayloads = new Dictionary<string, byte[]>();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.BuildActionTransactionAsync(
                "blueprint-1",
                "action-1",
                null,
                null,
                emptyPayloads,
                "wallet-sender",
                "register-1"));
    }

    [Fact]
    public async Task BuildRejectionTransactionAsync_WithValidData_CreatesRejectionTransaction()
    {
        // Arrange
        var originalTxHash = "original-tx-hash";
        var reason = "Invalid data provided";
        var senderWallet = "wallet-sender";
        var registerAddress = "register-1";

        // Act
        var result = await _service.BuildRejectionTransactionAsync(
            originalTxHash,
            reason,
            senderWallet,
            registerAddress);

        // Assert
        result.Should().NotBeNull();
        // Note: SenderWallet is only set during signing, not during build
        result.Recipients.Should().BeEmpty();
        result.PreviousTxHash.Should().Be(originalTxHash);
        result.RegisterId.Should().Be(registerAddress);

        // Verify metadata
        var metadata = JsonSerializer.Deserialize<JsonElement>(result.Metadata!);
        metadata.GetProperty("type").GetString().Should().Be("rejection");
        metadata.GetProperty("rejectedTransactionHash").GetString().Should().Be(originalTxHash);
        metadata.GetProperty("reason").GetString().Should().Be(reason);
    }

    [Fact]
    public async Task BuildRejectionTransactionAsync_WithNullOriginalTxHash_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.BuildRejectionTransactionAsync(
                null!,
                "reason",
                "wallet-sender",
                "register-1"));
    }

    [Fact]
    public async Task BuildRejectionTransactionAsync_WithNullReason_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.BuildRejectionTransactionAsync(
                "tx-hash",
                null!,
                "wallet-sender",
                "register-1"));
    }

    [Fact]
    public async Task BuildFileTransactionsAsync_WithValidFiles_CreatesFileTransactions()
    {
        // Arrange
        var files = new List<FileAttachment>
        {
            new FileAttachment("document.pdf", "application/pdf", Encoding.UTF8.GetBytes("PDF content")),
            new FileAttachment("image.jpg", "image/jpeg", Encoding.UTF8.GetBytes("JPG content"))
        };
        var parentTxHash = "parent-tx-hash";
        var senderWallet = "wallet-sender";
        var registerAddress = "register-1";

        // Act
        var result = await _service.BuildFileTransactionsAsync(
            files,
            parentTxHash,
            senderWallet,
            registerAddress);

        // Assert
        result.Should().HaveCount(2);

        var pdfTx = result[0];
        pdfTx.PreviousTxHash.Should().Be(parentTxHash);
        // Note: SenderWallet is only set during signing, not during build

        var pdfMetadata = JsonSerializer.Deserialize<JsonElement>(pdfTx.Metadata!);
        pdfMetadata.GetProperty("type").GetString().Should().Be("file");
        pdfMetadata.GetProperty("fileName").GetString().Should().Be("document.pdf");
        pdfMetadata.GetProperty("contentType").GetString().Should().Be("application/pdf");
        pdfMetadata.GetProperty("size").GetInt32().Should().Be(11);

        var jpgTx = result[1];
        var jpgMetadata = JsonSerializer.Deserialize<JsonElement>(jpgTx.Metadata!);
        jpgMetadata.GetProperty("fileName").GetString().Should().Be("image.jpg");
    }

    [Fact]
    public async Task BuildFileTransactionsAsync_WithEmptyFiles_ReturnsEmptyList()
    {
        // Arrange
        var files = new List<FileAttachment>();
        var parentTxHash = "parent-tx-hash";
        var senderWallet = "wallet-sender";
        var registerAddress = "register-1";

        // Act
        var result = await _service.BuildFileTransactionsAsync(
            files,
            parentTxHash,
            senderWallet,
            registerAddress);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task BuildFileTransactionsAsync_WithEmptyFileContent_SkipsFile()
    {
        // Arrange
        var files = new List<FileAttachment>
        {
            new FileAttachment("valid.pdf", "application/pdf", Encoding.UTF8.GetBytes("content")),
            new FileAttachment("empty.txt", "text/plain", Array.Empty<byte>()) // Empty file
        };
        var parentTxHash = "parent-tx-hash";
        var senderWallet = "wallet-sender";
        var registerAddress = "register-1";

        // Act
        var result = await _service.BuildFileTransactionsAsync(
            files,
            parentTxHash,
            senderWallet,
            registerAddress);

        // Assert
        result.Should().HaveCount(1);
        var metadata = JsonSerializer.Deserialize<JsonElement>(result[0].Metadata!);
        metadata.GetProperty("fileName").GetString().Should().Be("valid.pdf");
    }

    [Fact]
    public async Task BuildFileTransactionsAsync_WithNullFiles_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _service.BuildFileTransactionsAsync(
                null!,
                "parent-tx",
                "wallet",
                "register"));
    }

    [Fact]
    public async Task BuildFileTransactionsAsync_WithNullParentTxHash_ThrowsArgumentException()
    {
        // Arrange
        var files = new List<FileAttachment>
        {
            new FileAttachment("file.txt", "text/plain", Encoding.UTF8.GetBytes("content"))
        };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.BuildFileTransactionsAsync(
                files,
                null!,
                "wallet",
                "register"));
    }

    [Fact]
    public async Task BuildActionTransactionAsync_IncludesTimestamp()
    {
        // Arrange
        var beforeTime = DateTime.UtcNow;
        var encryptedPayloads = new Dictionary<string, byte[]>
        {
            ["wallet"] = Encoding.UTF8.GetBytes("data")
        };

        // Act
        var result = await _service.BuildActionTransactionAsync(
            "blueprint-1",
            "action-1",
            null,
            null,
            encryptedPayloads,
            "wallet-sender",
            "register-1");

        var afterTime = DateTime.UtcNow;

        // Assert
        result.Timestamp.Should().NotBeNull();
        result.Timestamp.Should().BeOnOrAfter(beforeTime);
        result.Timestamp.Should().BeOnOrBefore(afterTime);
    }

    #region Extension Method Tests

    [Fact]
    public async Task BuildActionTransactionAsync_Extension_ProducesNonEmptyTransactionData()
    {
        // Arrange
        var blueprint = new BlueprintModel
        {
            Id = "bp-1",
            Title = "Test",
            Participants = [new ParticipantModel { Id = "p1", Name = "P1" }],
            Actions = [new ActionModel { Id = 1, Title = "Submit", Sender = "p1" }]
        };
        var instance = new Instance
        {
            Id = "inst-1",
            BlueprintId = "bp-1",
            BlueprintDefinitionTxId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", // Feature 195: an instance must carry its definition pin, or execution has nothing to resolve or chain from
            BlueprintVersion = 1,
            RegisterId = "reg-1",
            TenantId = "tenant-1",
            State = InstanceState.Active,
            CurrentActionIds = [1],
            ParticipantWallets = new Dictionary<string, string> { ["p1"] = "wallet-1" }
        };
        var action = blueprint.Actions.First();
        var payloadData = new Dictionary<string, object> { ["amount"] = 100 };
        var disclosedPayloads = new Dictionary<string, Dictionary<string, object>>
        {
            ["wallet-1"] = new() { ["amount"] = 100 }
        };

        // Act
        var result = await _service.BuildActionTransactionAsync(
            blueprint, instance, action, payloadData, disclosedPayloads, null);

        // Assert
        result.TransactionData.Should().NotBeEmpty();
        result.TxId.Should().NotBeNullOrEmpty();
        result.TransactionType.Should().Be("action");
        result.Metadata["blueprintId"].Should().Be("bp-1");
        result.Metadata["actionId"].Should().Be(1);
        result.Metadata["instanceId"].Should().Be("inst-1");

        // Verify transaction data is valid JSON containing the disclosed payloads
        var json = JsonSerializer.Deserialize<JsonElement>(result.TransactionData);
        json.GetProperty("type").GetString().Should().Be("action");
        json.GetProperty("blueprintId").GetString().Should().Be("bp-1");
    }

    [Fact]
    public async Task BuildRejectionTransactionAsync_Extension_ProducesNonEmptyTransactionData()
    {
        // Arrange
        var blueprint = new BlueprintModel
        {
            Id = "bp-1",
            Title = "Test",
            Participants = [new ParticipantModel { Id = "p1", Name = "P1" }],
            Actions = [new ActionModel { Id = 1, Title = "Submit", Sender = "p1" }]
        };
        var instance = new Instance
        {
            Id = "inst-1",
            BlueprintId = "bp-1",
            BlueprintDefinitionTxId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", // Feature 195: an instance must carry its definition pin, or execution has nothing to resolve or chain from
            BlueprintVersion = 1,
            RegisterId = "reg-1",
            TenantId = "tenant-1",
            State = InstanceState.Active,
            CurrentActionIds = [1],
            ParticipantWallets = new Dictionary<string, string> { ["p1"] = "wallet-1" }
        };
        var action = blueprint.Actions.First();
        var rejectionData = new Dictionary<string, object>
        {
            ["rejectionReason"] = "Data incomplete"
        };

        // Act
        var result = await _service.BuildRejectionTransactionAsync(
            blueprint, instance, action, rejectionData, "prev-tx-123");

        // Assert
        result.TransactionData.Should().NotBeEmpty();
        result.TxId.Should().NotBeNullOrEmpty();
        result.TransactionType.Should().Be("rejection");
        result.Metadata["rejectionReason"].Should().Be("Data incomplete");
        result.Metadata["previousTxId"].Should().Be("prev-tx-123");

        // Verify transaction data is valid JSON containing the rejection data
        var json = JsonSerializer.Deserialize<JsonElement>(result.TransactionData);
        json.GetProperty("type").GetString().Should().Be("rejection");
    }

    [Fact]
    public async Task BuildActionTransactionAsync_Extension_IncludesDisclosedPayloadsInData()
    {
        // Arrange
        var blueprint = new BlueprintModel
        {
            Id = "bp-1",
            Title = "Test",
            Participants = [new ParticipantModel { Id = "p1", Name = "P1" }],
            Actions = [new ActionModel { Id = 1, Title = "Submit", Sender = "p1" }]
        };
        var instance = new Instance
        {
            Id = "inst-1",
            BlueprintId = "bp-1",
            BlueprintDefinitionTxId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", // Feature 195: an instance must carry its definition pin, or execution has nothing to resolve or chain from
            BlueprintVersion = 1,
            RegisterId = "reg-1",
            TenantId = "tenant-1",
            State = InstanceState.Active,
            CurrentActionIds = [1],
            ParticipantWallets = new Dictionary<string, string> { ["p1"] = "wallet-1" }
        };
        var action = blueprint.Actions.First();
        var payloadData = new Dictionary<string, object> { ["field1"] = "value1", ["field2"] = 42 };
        var disclosedPayloads = new Dictionary<string, Dictionary<string, object>>
        {
            ["wallet-reviewer"] = new() { ["field1"] = "value1" },
            ["wallet-approver"] = new() { ["field1"] = "value1", ["field2"] = 42 }
        };

        // Act
        var result = await _service.BuildActionTransactionAsync(
            blueprint, instance, action, payloadData, disclosedPayloads, null);

        // Assert
        result.TransactionData.Should().NotBeEmpty();
        var json = JsonSerializer.Deserialize<JsonElement>(result.TransactionData);
        json.GetProperty("payloads").Should().NotBeNull();
    }

    #endregion
}
