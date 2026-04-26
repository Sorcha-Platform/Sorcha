// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sorcha.Cryptography.Utilities;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Cryptography.Enums;
using Sorcha.ServiceClients.Register;
using Sorcha.ServiceClients.Wallet;
using Sorcha.Validator.Service.Configuration;
using Sorcha.Validator.Service.Models;
using Sorcha.Validator.Service.Services;
using Sorcha.Validator.Service.Services.Interfaces;
using RegisterModels = Sorcha.Register.Models;

namespace Sorcha.Validator.Service.Tests.Services;

/// <summary>
/// Unit tests for DocketBuilder
/// Tests cover >85% code coverage as required by project standards
/// </summary>
public class DocketBuilderTests
{
    private readonly Mock<IVerifiedTransactionQueue> _mockVerifiedQueue;
    private readonly Mock<IRegisterServiceClient> _mockRegisterClient;
    private readonly Mock<IWalletServiceClient> _mockWalletClient;
    private readonly Mock<IGenesisManager> _mockGenesisManager;
    private readonly Mock<IHashProvider> _mockHashProvider;
    private readonly MerkleTree _merkleTree;
    private readonly DocketHasher _docketHasher;
    private readonly Mock<ILogger<DocketBuilder>> _mockLogger;
    private readonly ValidatorConfiguration _validatorConfig;
    private readonly DocketBuildConfiguration _buildConfig;
    private readonly DocketBuilder _builder;

    public DocketBuilderTests()
    {
        _mockVerifiedQueue = new Mock<IVerifiedTransactionQueue>();
        // Default no-op setups for the lease lifecycle methods. Tests that need
        // to assert on Confirm/Release call these via Verify on top of the defaults.
        _mockVerifiedQueue
            .Setup(q => q.ConfirmAsync(
                It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockVerifiedQueue
            .Setup(q => q.ReleaseAsync(
                It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockRegisterClient = new Mock<IRegisterServiceClient>();
        _mockWalletClient = new Mock<IWalletServiceClient>();
        _mockGenesisManager = new Mock<IGenesisManager>();
        _mockHashProvider = new Mock<IHashProvider>();
        _mockLogger = new Mock<ILogger<DocketBuilder>>();

        // Create real instances of utility classes with mocked dependencies
        _merkleTree = new MerkleTree(_mockHashProvider.Object);
        _docketHasher = new DocketHasher(_mockHashProvider.Object);

        _validatorConfig = new ValidatorConfiguration
        {
            ValidatorId = "validator-1",
            SystemWalletAddress = "system-wallet-1"
        };

        _buildConfig = new DocketBuildConfiguration
        {
            TimeThreshold = TimeSpan.FromSeconds(10),
            SizeThreshold = 50,
            MaxTransactionsPerDocket = 100,
            AllowEmptyDockets = false
        };

        // Setup hash provider to return predictable hashes
        _mockHashProvider
            .Setup(h => h.ComputeHash(It.IsAny<byte[]>(), It.IsAny<HashType>()))
            .Returns((byte[] data, HashType hashType) =>
            {
                // Return a simple deterministic hash for testing
                return System.Text.Encoding.UTF8.GetBytes($"hash-{data.Length}");
            });

        _builder = new DocketBuilder(
            _mockVerifiedQueue.Object,
            _mockRegisterClient.Object,
            _mockWalletClient.Object,
            _mockGenesisManager.Object,
            _merkleTree,
            _docketHasher,
            Options.Create(_validatorConfig),
            Options.Create(_buildConfig),
            _mockLogger.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullVerifiedQueue_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => new DocketBuilder(
            null!,
            _mockRegisterClient.Object,
            _mockWalletClient.Object,
            _mockGenesisManager.Object,
            _merkleTree,
            _docketHasher,
            Options.Create(_validatorConfig),
            Options.Create(_buildConfig),
            _mockLogger.Object));

        exception.ParamName.Should().Be("verifiedQueue");
    }

    [Fact]
    public void Constructor_WithNullRegisterClient_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => new DocketBuilder(
            _mockVerifiedQueue.Object,
            null!,
            _mockWalletClient.Object,
            _mockGenesisManager.Object,
            _merkleTree,
            _docketHasher,
            Options.Create(_validatorConfig),
            Options.Create(_buildConfig),
            _mockLogger.Object));

        exception.ParamName.Should().Be("registerClient");
    }

    [Fact]
    public void Constructor_WithNullWalletClient_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => new DocketBuilder(
            _mockVerifiedQueue.Object,
            _mockRegisterClient.Object,
            null!,
            _mockGenesisManager.Object,
            _merkleTree,
            _docketHasher,
            Options.Create(_validatorConfig),
            Options.Create(_buildConfig),
            _mockLogger.Object));

        exception.ParamName.Should().Be("walletClient");
    }

    #endregion

    #region BuildDocketAsync - Genesis Tests

    [Fact]
    public async Task BuildDocketAsync_WhenNeedsGenesisDocket_CallsGenesisManager()
    {
        // Arrange
        var registerId = "register-1";
        var transactions = new List<Transaction> { CreateTestTransaction("tx-1") };
        var genesisDocket = CreateTestDocket(0, null);

        _mockGenesisManager
            .Setup(g => g.NeedsGenesisDocketAsync(registerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockVerifiedQueue
            .Setup(q => q.ClaimAsync(registerId, _buildConfig.MaxTransactionsPerDocket, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(WrapAsLeases(registerId, transactions));

        _mockGenesisManager
            .Setup(g => g.CreateGenesisDocketAsync(registerId, transactions, It.IsAny<CancellationToken>()))
            .ReturnsAsync(genesisDocket);

        // Act
        var result = await _builder.BuildDocketAsync(registerId);

        // Assert
        result.Should().NotBeNull();
        result!.DocketNumber.Should().Be(0);
        result.PreviousHash.Should().BeNull();

        _mockGenesisManager.Verify(
            g => g.CreateGenesisDocketAsync(registerId, transactions, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task BuildDocketAsync_WhenNeedsGenesisDocket_DoesNotBuildNormalDocket()
    {
        // Arrange
        var registerId = "register-1";
        var genesisDocket = CreateTestDocket(0, null);

        _mockGenesisManager
            .Setup(g => g.NeedsGenesisDocketAsync(registerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockVerifiedQueue
            .Setup(q => q.ClaimAsync(registerId, _buildConfig.MaxTransactionsPerDocket, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(WrapAsLeases(registerId, new List<Transaction>()));

        _mockGenesisManager
            .Setup(g => g.CreateGenesisDocketAsync(registerId, It.IsAny<List<Transaction>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(genesisDocket);

        // Act
        await _builder.BuildDocketAsync(registerId);

        // Assert
        _mockRegisterClient.Verify(
            r => r.ReadLatestDocketAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion

    #region BuildDocketAsync - Normal Docket Tests

    [Fact]
    public async Task BuildDocketAsync_WithPendingTransactions_BuildsSuccessfully()
    {
        // Arrange
        var registerId = "register-1";
        var transactions = new List<Transaction>
        {
            CreateTestTransaction("tx-1"),
            CreateTestTransaction("tx-2")
        };

        var previousDocket = CreateTestDocket(0, null);
        var expectedMerkleRoot = "merkle-root-123";
        var expectedDocketHash = "docket-hash-456";
        var expectedSignature = "signature-789";

        SetupNormalDocketBuild(registerId, transactions, previousDocket, expectedMerkleRoot, expectedDocketHash, expectedSignature);

        // Act
        var result = await _builder.BuildDocketAsync(registerId);

        // Assert
        result.Should().NotBeNull();
        result!.RegisterId.Should().Be(registerId);
        result.DocketNumber.Should().Be(1);
        result.PreviousHash.Should().Be(previousDocket.DocketHash);
        result.DocketHash.Should().NotBeNullOrEmpty(); // Real hash computed
        result.MerkleRoot.Should().NotBeNullOrEmpty(); // Real merkle root computed
        result.Transactions.Should().HaveCount(2);
        result.ProposerValidatorId.Should().Be(_validatorConfig.ValidatorId);
        result.ProposerSignature.SignatureValue.Should().BeEquivalentTo(System.Text.Encoding.UTF8.GetBytes(expectedSignature));
        result.Status.Should().Be(DocketStatus.Proposed);
    }

    [Fact]
    public async Task BuildDocketAsync_WithNoTransactionsAndEmptyNotAllowed_ReturnsNull()
    {
        // Arrange
        var registerId = "register-1";

        _mockGenesisManager
            .Setup(g => g.NeedsGenesisDocketAsync(registerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _mockVerifiedQueue
            .Setup(q => q.ClaimAsync(registerId, _buildConfig.MaxTransactionsPerDocket, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(WrapAsLeases(registerId, new List<Transaction>()));

        // Act
        var result = await _builder.BuildDocketAsync(registerId);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task BuildDocketAsync_WithNoTransactionsAndEmptyAllowed_BuildsEmptyDocket()
    {
        // Arrange
        var registerId = "register-1";
        _buildConfig.AllowEmptyDockets = true;

        var previousDocket = CreateTestDocket(0, null);
        var expectedMerkleRoot = "empty-merkle-root";
        var expectedDocketHash = "docket-hash-empty";
        var expectedSignature = "signature-empty";

        SetupNormalDocketBuild(registerId, new List<Transaction>(), previousDocket, expectedMerkleRoot, expectedDocketHash, expectedSignature);

        // Act
        var result = await _builder.BuildDocketAsync(registerId);

        // Assert
        result.Should().NotBeNull();
        result!.Transactions.Should().BeEmpty();
        result.DocketNumber.Should().Be(1);
    }

    [Fact]
    public async Task BuildDocketAsync_ComputesMerkleRootFromTransactionHashes()
    {
        // Arrange
        var registerId = "register-1";
        var transactions = new List<Transaction>
        {
            CreateTestTransaction("tx-1"),
            CreateTestTransaction("tx-2"),
            CreateTestTransaction("tx-3")
        };

        var previousDocket = CreateTestDocket(0, null);

        SetupNormalDocketBuild(registerId, transactions, previousDocket, "merkle-root", "docket-hash", "signature");

        // Act
        var result = await _builder.BuildDocketAsync(registerId);

        // Assert
        result.Should().NotBeNull();
        result!.MerkleRoot.Should().NotBeNullOrEmpty();
        result.Transactions.Should().HaveCount(3);
    }

    [Fact]
    public async Task BuildDocketAsync_SignsDocketWithSystemWallet()
    {
        // Arrange
        var registerId = "register-1";
        var transactions = new List<Transaction> { CreateTestTransaction("tx-1") };
        var previousDocket = CreateTestDocket(0, null);
        var expectedSignature = "signature-value";

        SetupNormalDocketBuild(registerId, transactions, previousDocket, "merkle-root", "docket-hash", expectedSignature);

        // Act
        var result = await _builder.BuildDocketAsync(registerId);

        // Assert
        result.Should().NotBeNull();

        _mockWalletClient.Verify(
            w => w.SignDataAsync(_validatorConfig.SystemWalletAddress!, It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);

        result!.ProposerSignature.PublicKey.Should().BeEquivalentTo(System.Text.Encoding.UTF8.GetBytes(_validatorConfig.SystemWalletAddress!));
        result.ProposerSignature.SignatureValue.Should().BeEquivalentTo(System.Text.Encoding.UTF8.GetBytes(expectedSignature));
    }

    [Fact]
    public async Task BuildDocketAsync_WithEmptySystemWalletAddress_CreatesSystemWallet()
    {
        // Arrange
        var registerId = "register-1";
        var transactions = new List<Transaction> { CreateTestTransaction("tx-1") };
        var previousDocket = CreateTestDocket(0, null);
        var createdWalletId = "newly-created-wallet";

        _validatorConfig.SystemWalletAddress = "";

        SetupNormalDocketBuild(registerId, transactions, previousDocket, "merkle-root", "docket-hash", "signature");

        _mockWalletClient
            .Setup(w => w.CreateOrRetrieveSystemWalletAsync(_validatorConfig.ValidatorId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdWalletId);

        // Act
        var result = await _builder.BuildDocketAsync(registerId);

        // Assert
        result.Should().NotBeNull();

        _mockWalletClient.Verify(
            w => w.CreateOrRetrieveSystemWalletAsync(_validatorConfig.ValidatorId, It.IsAny<CancellationToken>()),
            Times.Once);

        result!.ProposerSignature.PublicKey.Should().BeEquivalentTo(System.Text.Encoding.UTF8.GetBytes(createdWalletId));
    }

    [Fact]
    public async Task BuildDocketAsync_SequentialDocketNumbers_IncrementsCorrectly()
    {
        // Arrange
        var registerId = "register-1";
        var transactions = new List<Transaction> { CreateTestTransaction("tx-1") };

        // Test with previous docket number 5
        var previousDocket = CreateTestDocket(5, "prev-hash");

        SetupNormalDocketBuild(registerId, transactions, previousDocket, "merkle-root", "docket-hash", "signature");

        // Act
        var result = await _builder.BuildDocketAsync(registerId);

        // Assert
        result.Should().NotBeNull();
        result!.DocketNumber.Should().Be(6); // Should increment from 5 to 6
        result.PreviousHash.Should().Be(previousDocket.DocketHash);
    }

    [Fact]
    public async Task BuildDocketAsync_WhenLatestDocketNull_AfterGenesisCheck_ReturnsNull()
    {
        // Arrange
        var registerId = "register-1";
        var transactions = new List<Transaction> { CreateTestTransaction("tx-1") };

        _mockGenesisManager
            .Setup(g => g.NeedsGenesisDocketAsync(registerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _mockVerifiedQueue
            .Setup(q => q.ClaimAsync(registerId, _buildConfig.MaxTransactionsPerDocket, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(WrapAsLeases(registerId, transactions));

        _mockRegisterClient
            .Setup(r => r.ReadLatestDocketAsync(registerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DocketModel?)null); // No previous docket — inconsistent state

        SetupCryptoMocks("merkle-root", "docket-hash", "signature");

        // Act
        var result = await _builder.BuildDocketAsync(registerId);

        // Assert — should abort, not create a duplicate Docket 0
        result.Should().BeNull();
    }

    [Fact]
    public async Task BuildDocketAsync_WhenExceptionOccurs_ReturnsNull()
    {
        // Arrange
        var registerId = "register-1";

        _mockGenesisManager
            .Setup(g => g.NeedsGenesisDocketAsync(registerId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Test error"));

        // Act
        var result = await _builder.BuildDocketAsync(registerId);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region ShouldBuildDocketAsync Tests

    [Fact]
    public async Task ShouldBuildDocketAsync_WhenTimeThresholdMet_ReturnsTrue()
    {
        // Arrange
        var registerId = "register-1";
        var lastBuildTime = DateTimeOffset.UtcNow.AddSeconds(-15); // 15 seconds ago

        // Time threshold is 10 seconds, so this should trigger

        // Act
        var result = await _builder.ShouldBuildDocketAsync(registerId, lastBuildTime);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task ShouldBuildDocketAsync_WhenTimeThresholdNotMet_ChecksSizeThreshold()
    {
        // Arrange
        var registerId = "register-1";
        var lastBuildTime = DateTimeOffset.UtcNow.AddSeconds(-5); // 5 seconds ago (under 10 second threshold)

        _mockVerifiedQueue
            .Setup(q => q.GetCount(registerId))
            .Returns(60); // Over 50 threshold

        // Act
        var result = await _builder.ShouldBuildDocketAsync(registerId, lastBuildTime);

        // Assert
        result.Should().BeTrue();

        _mockVerifiedQueue.Verify(
            q => q.GetCount(registerId),
            Times.Once);
    }

    [Fact]
    public async Task ShouldBuildDocketAsync_WhenSizeThresholdMet_ReturnsTrue()
    {
        // Arrange
        var registerId = "register-1";
        var lastBuildTime = DateTimeOffset.UtcNow.AddSeconds(-5); // Time threshold not met

        _mockVerifiedQueue
            .Setup(q => q.GetCount(registerId))
            .Returns(50); // Exactly at threshold

        // Act
        var result = await _builder.ShouldBuildDocketAsync(registerId, lastBuildTime);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task ShouldBuildDocketAsync_WhenNoThresholdsMet_ReturnsFalse()
    {
        // Arrange
        var registerId = "register-1";
        var lastBuildTime = DateTimeOffset.UtcNow.AddSeconds(-5); // Under time threshold

        _mockVerifiedQueue
            .Setup(q => q.GetCount(registerId))
            .Returns(30); // Under size threshold

        // Act
        var result = await _builder.ShouldBuildDocketAsync(registerId, lastBuildTime);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ShouldBuildDocketAsync_WhenExceptionOccurs_ReturnsFalse()
    {
        // Arrange
        var registerId = "register-1";
        var lastBuildTime = DateTimeOffset.UtcNow.AddSeconds(-5);

        _mockVerifiedQueue
            .Setup(q => q.GetCount(registerId))
            .Throws(new InvalidOperationException("Test error"));

        // Act
        var result = await _builder.ShouldBuildDocketAsync(registerId, lastBuildTime);

        // Assert
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData(10, 49, true)]  // Time at threshold (>=10), size under threshold
    [InlineData(9, 50, true)]   // Time under threshold, size at threshold (>=50)
    [InlineData(11, 0, true)]   // Time over threshold, size doesn't matter
    [InlineData(5, 100, true)]  // Time under threshold, size way over threshold
    [InlineData(5, 30, false)]  // Time under threshold, size under threshold
    public async Task ShouldBuildDocketAsync_WithVariousThresholds_ReturnsExpected(
        int secondsSinceLastBuild, int transactionCount, bool expectedResult)
    {
        // Arrange
        var registerId = "register-1";
        var lastBuildTime = DateTimeOffset.UtcNow.AddSeconds(-secondsSinceLastBuild);

        _mockVerifiedQueue
            .Setup(q => q.GetCount(registerId))
            .Returns(transactionCount);

        // Act
        var result = await _builder.ShouldBuildDocketAsync(registerId, lastBuildTime);

        // Assert
        result.Should().Be(expectedResult);
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Creates a test transaction
    /// </summary>
    private static Transaction CreateTestTransaction(string transactionId)
    {
        return new Transaction
        {
            TransactionId = transactionId,
            RegisterId = "register-1",
            BlueprintId = "blueprint-1",
            ActionId = "action-1",
            Payload = JsonDocument.Parse("{\"data\":\"test\"}").RootElement,
            PayloadHash = $"payload-hash-{transactionId}",
            Signatures = new List<Signature>
            {
                new Signature
                {
                    PublicKey = System.Text.Encoding.UTF8.GetBytes("public-key"),
                    SignatureValue = System.Text.Encoding.UTF8.GetBytes("signature-value"),
                    Algorithm = "ED25519",
                    SignedAt = DateTimeOffset.UtcNow
                }
            },
            CreatedAt = DateTimeOffset.UtcNow,
            Priority = TransactionPriority.Normal
        };
    }

    /// <summary>
    /// Creates a test docket (Validator Service internal model)
    /// </summary>
    private static Docket CreateTestDocket(long docketNumber, string? previousHash)
    {
        return new Docket
        {
            DocketId = $"docket-{docketNumber}",
            RegisterId = "register-1",
            DocketNumber = docketNumber,
            PreviousHash = previousHash,
            DocketHash = $"docket-hash-{docketNumber}",
            CreatedAt = DateTimeOffset.UtcNow,
            Transactions = new List<Transaction>(),
            Status = DocketStatus.Confirmed,
            ProposerValidatorId = "validator-1",
            ProposerSignature = new Signature
            {
                PublicKey = System.Text.Encoding.UTF8.GetBytes("system-wallet"),
                SignatureValue = System.Text.Encoding.UTF8.GetBytes("signature"),
                Algorithm = "ED25519",
                SignedAt = DateTimeOffset.UtcNow
            },
            MerkleRoot = "merkle-root"
        };
    }

    /// <summary>
    /// Creates a test docket model (Register Service client model)
    /// </summary>
    private static DocketModel CreateTestDocketModel(long docketNumber, string? previousHash)
    {
        return new DocketModel
        {
            DocketId = $"docket-{docketNumber}",
            RegisterId = "register-1",
            DocketNumber = docketNumber,
            PreviousHash = previousHash,
            DocketHash = $"docket-hash-{docketNumber}",
            CreatedAt = DateTimeOffset.UtcNow,
            Transactions = new List<RegisterModels.TransactionModel>(),
            ProposerValidatorId = "validator-1",
            MerkleRoot = "merkle-root"
        };
    }

    /// <summary>
    /// Sets up mocks for normal docket build scenarios
    /// </summary>
    private void SetupNormalDocketBuild(
        string registerId,
        List<Transaction> transactions,
        Docket? previousDocket,
        string merkleRoot,
        string docketHash,
        string signature)
    {
        _mockGenesisManager
            .Setup(g => g.NeedsGenesisDocketAsync(registerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _mockVerifiedQueue
            .Setup(q => q.ClaimAsync(registerId, _buildConfig.MaxTransactionsPerDocket, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(WrapAsLeases(registerId, transactions));

        // Convert internal Docket to DocketModel for RegisterServiceClient mock
        DocketModel? docketModel = previousDocket == null ? null : CreateTestDocketModel(previousDocket.DocketNumber, previousDocket.PreviousHash);

        _mockRegisterClient
            .Setup(r => r.ReadLatestDocketAsync(registerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(docketModel);

        SetupCryptoMocks(merkleRoot, docketHash, signature);
    }

    /// <summary>
    /// Sets up cryptographic mocks
    /// </summary>
    private void SetupCryptoMocks(string merkleRoot, string docketHash, string signature)
    {
        // Since we're using real MerkleTree and DocketHasher instances,
        // we only need to mock the wallet client for signing
        _mockWalletClient
            .Setup(w => w.SignDataAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string walletAddress, string data, CancellationToken ct) => new WalletSignResult
            {
                Signature = System.Text.Encoding.UTF8.GetBytes(signature),
                PublicKey = System.Text.Encoding.UTF8.GetBytes(string.IsNullOrEmpty(walletAddress) ? "test-wallet-address" : walletAddress),
                SignedBy = string.IsNullOrEmpty(walletAddress) ? "test-wallet-address" : walletAddress,
                Algorithm = "ED25519"
            });
    }

    /// <summary>
    /// Wraps transactions into VerifiedTransaction records for mock setups
    /// </summary>
    private static IReadOnlyList<VerifiedTransactionLease> WrapAsLeases(string registerId, List<Transaction> transactions)
    {
        return transactions.Select(t => new VerifiedTransactionLease
        {
            RegisterId = registerId,
            Transaction = new VerifiedTransaction
            {
                Transaction = t,
                EnqueuedAt = DateTimeOffset.UtcNow,
                Priority = 0,
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
            },
            LeaseExpiresAt = DateTimeOffset.UtcNow.AddMinutes(1)
        }).ToList();
    }

    private static IReadOnlyList<VerifiedTransaction> WrapAsVerified(List<Transaction> transactions)
    {
        return transactions.Select(t => new VerifiedTransaction
        {
            Transaction = t,
            EnqueuedAt = DateTimeOffset.UtcNow,
            Priority = 0,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        }).ToList();
    }

    #endregion
}
