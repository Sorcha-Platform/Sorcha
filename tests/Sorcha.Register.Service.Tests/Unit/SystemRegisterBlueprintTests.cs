// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Register.Core.Events;
using Sorcha.Register.Core.Managers;
using Sorcha.Register.Core.Storage;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Constants;
using Sorcha.Register.Service.Services;
using Sorcha.ServiceClients.SystemWallet;
using Sorcha.ServiceClients.Validator;
using Sorcha.Blueprint.Models.Canonical;
using Xunit;

namespace Sorcha.Register.Service.Tests.Unit;

/// <summary>
/// Unit tests for blueprint operations in SystemRegisterService (ledger-backed).
/// Covers querying, publishing, and existence checks against the system register.
/// </summary>
public class SystemRegisterBlueprintTests
{
    private readonly Mock<IRegisterRepository> _mockRepository;
    private readonly Mock<IValidatorServiceClient> _mockValidatorClient;
    private readonly Mock<ISystemWalletSigningService> _mockSigningService;
    private readonly Mock<IHashProvider> _mockHashProvider;
    private readonly Mock<ILogger<SystemRegisterService>> _mockLogger;
    private readonly SystemRegisterService _service;

    public SystemRegisterBlueprintTests()
    {
        _mockRepository = new Mock<IRegisterRepository>();
        RegisterMockHelpers.StubTransactionsByTypeReadThrough(_mockRepository);
        var mockEventPublisher = new Mock<IEventPublisher>();
        _mockValidatorClient = new Mock<IValidatorServiceClient>();
        _mockSigningService = new Mock<ISystemWalletSigningService>();
        _mockHashProvider = new Mock<IHashProvider>();
        _mockLogger = new Mock<ILogger<SystemRegisterService>>();

        var registerManager = new RegisterManager(_mockRepository.Object, mockEventPublisher.Object);
        var transactionManager = new TransactionManager(_mockRepository.Object, mockEventPublisher.Object);

        // Default: system register exists
        _mockRepository
            .Setup(r => r.GetRegisterAsync(SystemRegisterConstants.SystemRegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sorcha.Register.Models.Register
            {
                Id = SystemRegisterConstants.SystemRegisterId,
                Name = SystemRegisterConstants.SystemRegisterName,
                Height = 0,
                Status = Sorcha.Register.Models.Enums.RegisterStatus.Online
            });

        // Default: no transactions
        _mockRepository
            .Setup(r => r.GetTransactionsAsync(SystemRegisterConstants.SystemRegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransactionModel>().AsQueryable());

        // Default hash provider returns 32 bytes
        _mockHashProvider
            .Setup(h => h.ComputeHash(It.IsAny<byte[]>(), It.IsAny<Sorcha.Cryptography.Enums.HashType>()))
            .Returns(new byte[32]);

        _service = new SystemRegisterService(
            _mockLogger.Object,
            registerManager,
            transactionManager,
            _mockValidatorClient.Object,
            _mockSigningService.Object,
            _mockHashProvider.Object);
    }

    #region GetAllBlueprintsAsync Tests

    [Fact]
    public async Task GetAllBlueprintsAsync_NoTransactions_ReturnsEmptyList()
    {
        // Act
        var result = await _service.GetAllBlueprintsAsync();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllBlueprintsAsync_WithBlueprintTransactions_ReturnsEntries()
    {
        // Arrange
        var transactions = new List<TransactionModel>
        {
            CreateBlueprintTransaction("bp-1", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            CreateBlueprintTransaction("bp-2", new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc))
        };

        _mockRepository
            .Setup(r => r.GetTransactionsAsync(SystemRegisterConstants.SystemRegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transactions.AsQueryable());

        // Act
        var result = await _service.GetAllBlueprintsAsync();

        // Assert
        result.Should().HaveCount(2);
        result[0].BlueprintId.Should().Be("bp-1");
        result[0].Version.Should().Be(1);
        result[1].BlueprintId.Should().Be("bp-2");
        // 1, not 2: Version counts publications OF THIS blueprint. It used to be a position in the
        // combined transaction list, which is how register-governance-v1 reported v5 on a node that
        // had published it exactly once (#1515), and what makes
        // GET /blueprints/{id}/versions/{version} answerable at all.
        result[1].Version.Should().Be(1);
    }

    [Fact]
    public async Task GetAllBlueprintsAsync_IgnoresNonBlueprintTransactions()
    {
        // Arrange — mix of blueprint and non-blueprint transactions
        var transactions = new List<TransactionModel>
        {
            CreateBlueprintTransaction("bp-1", DateTime.UtcNow),
            new TransactionModel
            {
                TxId = new string('f', 64),
                RegisterId = SystemRegisterConstants.SystemRegisterId,
                SenderWallet = "system",
                MetaData = new TransactionMetaData
                {
                    TransactionType = Sorcha.Register.Models.Enums.TransactionType.Control,
                    TrackingData = new Dictionary<string, string>
                    {
                        ["transactionType"] = "Genesis"
                    }
                },
                Payloads = Array.Empty<PayloadModel>()
            }
        };

        _mockRepository
            .Setup(r => r.GetTransactionsAsync(SystemRegisterConstants.SystemRegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transactions.AsQueryable());

        // Act
        var result = await _service.GetAllBlueprintsAsync();

        // Assert
        result.Should().HaveCount(1);
        result[0].BlueprintId.Should().Be("bp-1");
    }

    #endregion

    #region GetBlueprintAsync Tests

    [Fact]
    public async Task GetBlueprintAsync_ExistingBlueprint_ReturnsEntry()
    {
        // Arrange
        var transactions = new List<TransactionModel>
        {
            CreateBlueprintTransaction("bp-1", DateTime.UtcNow)
        };

        _mockRepository
            .Setup(r => r.GetTransactionsAsync(SystemRegisterConstants.SystemRegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transactions.AsQueryable());

        // Act
        var result = await _service.GetBlueprintAsync("bp-1");

        // Assert
        result.Should().NotBeNull();
        result!.BlueprintId.Should().Be("bp-1");
    }

    [Fact]
    public async Task GetBlueprintAsync_NonExistent_ReturnsNull()
    {
        // Act
        var result = await _service.GetBlueprintAsync("nonexistent");

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region BlueprintExistsAsync Tests

    [Fact]
    public async Task BlueprintExistsAsync_ExistingActiveBlueprint_ReturnsTrue()
    {
        // Arrange
        var transactions = new List<TransactionModel>
        {
            CreateBlueprintTransaction("active-bp", DateTime.UtcNow)
        };

        _mockRepository
            .Setup(r => r.GetTransactionsAsync(SystemRegisterConstants.SystemRegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transactions.AsQueryable());

        // Act
        var result = await _service.BlueprintExistsAsync("active-bp");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task BlueprintExistsAsync_NonExistentBlueprint_ReturnsFalse()
    {
        // Act
        var result = await _service.BlueprintExistsAsync("nonexistent");

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region PublishBlueprintAsync Tests

    [Fact]
    public async Task PublishBlueprintAsync_ValidBlueprint_SubmitsToValidator()
    {
        // Arrange
        var blueprintJson = JsonSerializer.Deserialize<JsonElement>("""{"title": "Test Blueprint"}""");

        _mockSigningService
            .Setup(s => s.SignAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemSignResult
            {
                Signature = new byte[64],
                PublicKey = new byte[32],
                Algorithm = "ED25519",
                WalletAddress = "system-wallet-addr"
            });

        _mockValidatorClient
            .Setup(v => v.SubmitTransactionAsync(It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionSubmissionResult
            {
                Success = true,
                TransactionId = "test-tx-id",
                RegisterId = SystemRegisterConstants.SystemRegisterId
            });

        // Act
        var result = await _service.PublishBlueprintAsync(
            "test-blueprint-v1", blueprintJson, "admin-001");

        // Assert
        result.BlueprintId.Should().Be("test-blueprint-v1");
        result.PublishedBy.Should().Be("admin-001");
        result.IsActive.Should().BeTrue();
        result.PublicationTransactionId.Should().NotBeNullOrEmpty();

        _mockValidatorClient.Verify(v => v.SubmitTransactionAsync(
            It.Is<TransactionSubmission>(s =>
                s.RegisterId == SystemRegisterConstants.SystemRegisterId &&
                s.Metadata != null &&
                s.Metadata["transactionType"] == "BlueprintPublish" &&
                s.Metadata["BlueprintId"] == "test-blueprint-v1"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PublishBlueprintAsync_UsesThePublicationIdAsTheTransactionId_SoRecoveryProvenancePasses()
    {
        // Feature 195 — the system register is a register, and a blueprint published to it is a
        // definition like any other. Its transaction id must therefore BE the publication id of the
        // canonical definition, because that is what recovery recomputes and compares against.
        //
        // Was Feature 138 US4, which asserted a separately-sealed `contentHash` in TrackingData.
        // Corrected rather than deleted: the property is unchanged — a seeded system blueprint must
        // survive recovery, or the genesis blueprints never re-enter the published store after a
        // restart and governance stops resolving (the #1466 shape). What changed is that the digest
        // and the identity are now one value, so there is no sibling field left to disagree.
        const string blueprintStr = """{"title":"Register Creation","actions":[]}""";
        var blueprintJson = JsonSerializer.Deserialize<JsonElement>(blueprintStr);

        _mockSigningService
            .Setup(s => s.SignAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemSignResult
            {
                Signature = new byte[64],
                PublicKey = new byte[32],
                Algorithm = "ED25519",
                WalletAddress = "system-wallet-addr"
            });

        TransactionSubmission? captured = null;
        _mockValidatorClient
            .Setup(v => v.SubmitTransactionAsync(It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()))
            .Callback<TransactionSubmission, CancellationToken>((s, _) => captured = s)
            .ReturnsAsync(new TransactionSubmissionResult
            {
                Success = true,
                TransactionId = "test-tx-id",
                RegisterId = SystemRegisterConstants.SystemRegisterId
            });

        await _service.PublishBlueprintAsync(
            "register-creation-v1", blueprintJson, "system",
            new Dictionary<string, string> { ["seedReason"] = "bootstrap" });

        captured.Should().NotBeNull();

        // Exactly what BlueprintRecoveryService.TryVerifyProvenance recomputes.
        var expected = BlueprintPublicationId.ComputeFromDefinition(
            SystemRegisterConstants.SystemRegisterId, "register-creation-v1", blueprintStr);

        captured!.TransactionId.Should().Be(
            expected,
            "recovery recomputes the publication id from the bytes it receives and compares it to " +
            "the transaction's own id — a system blueprint whose id is derived any other way fails " +
            "provenance on every restart");

        captured.Metadata.Should().NotContainKey("contentHash",
            "the transaction id IS the digest now; a sibling field would be a second source of truth " +
            "that could disagree with it");
    }

    [Fact]
    public async Task PublishBlueprintAsync_ValidatorRejects_ThrowsInvalidOperation()
    {
        // Arrange
        var blueprintJson = JsonSerializer.Deserialize<JsonElement>("""{"title": "Test"}""");

        _mockSigningService
            .Setup(s => s.SignAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemSignResult
            {
                Signature = new byte[64],
                PublicKey = new byte[32],
                Algorithm = "ED25519",
                WalletAddress = "system-wallet-addr"
            });

        _mockValidatorClient
            .Setup(v => v.SubmitTransactionAsync(It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionSubmissionResult
            {
                Success = false,
                ErrorMessage = "Validation failed"
            });

        // Act
        var act = () => _service.PublishBlueprintAsync(
            "test-bp", blueprintJson, "admin-001");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Blueprint publish failed*");
    }

    [Fact]
    public async Task PublishBlueprintAsync_NullBlueprintId_ThrowsArgumentException()
    {
        var blueprintJson = JsonSerializer.Deserialize<JsonElement>("""{"title": "Test"}""");

        var act = () => _service.PublishBlueprintAsync(
            null!, blueprintJson, "admin-001");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task PublishBlueprintAsync_NullPublishedBy_ThrowsArgumentException()
    {
        var blueprintJson = JsonSerializer.Deserialize<JsonElement>("""{"title": "Test"}""");

        var act = () => _service.PublishBlueprintAsync(
            "test-bp", blueprintJson, null!);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    #endregion

    #region GetCurrentVersionAsync Tests

    [Fact]
    public async Task GetCurrentVersionAsync_NoRegister_ReturnsZero()
    {
        // Arrange — register does not exist
        _mockRepository
            .Setup(r => r.GetRegisterAsync(SystemRegisterConstants.SystemRegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Sorcha.Register.Models.Register?)null);

        // Act
        var result = await _service.GetCurrentVersionAsync();

        // Assert
        result.Should().Be(0);
    }

    [Fact]
    public async Task GetCurrentVersionAsync_WithBlueprints_ReturnsCount()
    {
        // Arrange
        var transactions = new List<TransactionModel>
        {
            CreateBlueprintTransaction("bp-1", DateTime.UtcNow),
            CreateBlueprintTransaction("bp-2", DateTime.UtcNow.AddMinutes(1))
        };

        _mockRepository
            .Setup(r => r.GetTransactionsAsync(SystemRegisterConstants.SystemRegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transactions.AsQueryable());

        // Act
        var result = await _service.GetCurrentVersionAsync();

        // Assert
        result.Should().Be(2);
    }

    #endregion

    /// <summary>
    /// Creates a mock blueprint publish transaction
    /// </summary>
    private static TransactionModel CreateBlueprintTransaction(string blueprintId, DateTime timestamp)
    {
        var payloadData = System.Buffers.Text.Base64Url.EncodeToString(
            System.Text.Encoding.UTF8.GetBytes($"{{\"title\": \"{blueprintId}\"}}"));

        return new TransactionModel
        {
            TxId = $"tx-{blueprintId}-{timestamp.Ticks}".PadRight(64, '0')[..64],
            RegisterId = SystemRegisterConstants.SystemRegisterId,
            SenderWallet = "system",
            TimeStamp = timestamp,
            MetaData = new TransactionMetaData
            {
                RegisterId = SystemRegisterConstants.SystemRegisterId,
                TransactionType = Sorcha.Register.Models.Enums.TransactionType.Control,
                BlueprintId = blueprintId,
                TrackingData = new Dictionary<string, string>
                {
                    ["transactionType"] = "BlueprintPublish",
                    ["BlueprintId"] = blueprintId,
                    ["publishedBy"] = "system"
                }
            },
            PayloadCount = 1,
            Payloads = new[]
            {
                new PayloadModel
                {
                    Data = payloadData,
                    Hash = "fakehash",
                    ContentType = "application/json",
                    ContentEncoding = "base64url"
                }
            },
            Signature = "system-signature"
        };
    }
}
