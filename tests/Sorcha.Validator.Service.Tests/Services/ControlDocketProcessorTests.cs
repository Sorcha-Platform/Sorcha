// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sorcha.Validator.Service.Models;
using Sorcha.Validator.Service.Services;
using Sorcha.Validator.Service.Services.Interfaces;
using ValidatorStatus = Sorcha.Validator.Service.Services.Interfaces.ValidatorStatus;

namespace Sorcha.Validator.Service.Tests.Services;

/// <summary>
/// Unit tests for ControlDocketProcessor (VAL-9.41)
/// Tests cover control transaction extraction, validation, and processing.
/// </summary>
public class ControlDocketProcessorTests
{
    private readonly Mock<IGenesisConfigService> _mockGenesisConfigService;
    private readonly Mock<IControlBlueprintVersionResolver> _mockVersionResolver;
    private readonly Mock<IValidatorRegistry> _mockValidatorRegistry;
    private readonly Mock<ILogger<ControlDocketProcessor>> _mockLogger;
    private readonly ControlDocketProcessor _processor;

    private const string TestRegisterId = "test-register-001";
    private const string TestValidatorId = "validator-001";

    public ControlDocketProcessorTests()
    {
        _mockGenesisConfigService = new Mock<IGenesisConfigService>();
        _mockVersionResolver = new Mock<IControlBlueprintVersionResolver>();
        _mockValidatorRegistry = new Mock<IValidatorRegistry>();
        _mockLogger = new Mock<ILogger<ControlDocketProcessor>>();

        _processor = new ControlDocketProcessor(
            _mockGenesisConfigService.Object,
            _mockVersionResolver.Object,
            _mockValidatorRegistry.Object,
            _mockLogger.Object);

        SetupDefaultMocks();
    }

    private void SetupDefaultMocks()
    {
        var defaultConfig = CreateDefaultGenesisConfiguration();
        _mockGenesisConfigService
            .Setup(s => s.GetFullConfigAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(defaultConfig);

        _mockValidatorRegistry
            .Setup(r => r.GetActiveCountAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);
    }

    private static GenesisConfiguration CreateDefaultGenesisConfiguration()
    {
        return new GenesisConfiguration
        {
            RegisterId = TestRegisterId,
            GenesisTransactionId = "genesis-tx-001",
            ControlBlueprintVersionId = "control-v1",
            Consensus = new ConsensusConfig
            {
                SignatureThresholdMin = 2,
                SignatureThresholdMax = 10,
                DocketTimeout = TimeSpan.FromSeconds(30),
                MaxSignaturesPerDocket = 10,
                MaxTransactionsPerDocket = 100,
                DocketBuildInterval = TimeSpan.FromSeconds(10)
            },
            Validators = new ValidatorConfig
            {
                RegistrationMode = "public",
                MinValidators = 2,
                MaxValidators = 10,
                RequireStake = false
            },
            LeaderElection = new LeaderElectionConfig
            {
                Mechanism = "rotating",
                HeartbeatInterval = TimeSpan.FromSeconds(5),
                LeaderTimeout = TimeSpan.FromSeconds(15)
            },
            LoadedAt = DateTimeOffset.UtcNow,
            CacheTtl = TimeSpan.FromMinutes(30)
        };
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullGenesisConfigService_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new ControlDocketProcessor(
            null!,
            _mockVersionResolver.Object,
            _mockValidatorRegistry.Object,
            _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullVersionResolver_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new ControlDocketProcessor(
            _mockGenesisConfigService.Object,
            null!,
            _mockValidatorRegistry.Object,
            _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullValidatorRegistry_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new ControlDocketProcessor(
            _mockGenesisConfigService.Object,
            _mockVersionResolver.Object,
            null!,
            _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new ControlDocketProcessor(
            _mockGenesisConfigService.Object,
            _mockVersionResolver.Object,
            _mockValidatorRegistry.Object,
            null!));
    }

    #endregion

    #region ExtractControlTransactions Tests

    [Fact]
    public void ExtractControlTransactions_WithNullDocket_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => _processor.ExtractControlTransactions(null!));
    }

    [Fact]
    public void ExtractControlTransactions_WithEmptyDocket_ReturnsEmptyList()
    {
        // Arrange
        var docket = CreateDocket([]);

        // Act
        var result = _processor.ExtractControlTransactions(docket);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractControlTransactions_WithNoControlTransactions_ReturnsEmptyList()
    {
        // Arrange
        var regularTx = CreateTransaction("tx-001", "regular.action", new { data = "test" });
        var docket = CreateDocket([regularTx]);

        // Act
        var result = _processor.ExtractControlTransactions(docket);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractControlTransactions_WithValidatorRegisterAction_ExtractsTransaction()
    {
        // Arrange
        var payload = new
        {
            validatorId = TestValidatorId,
            publicKey = "pubkey-001",
            endpoint = "https://validator1.example.com"
        };
        var controlTx = CreateTransaction("tx-001", "control.validator.register", payload);
        var docket = CreateDocket([controlTx]);

        // Act
        var result = _processor.ExtractControlTransactions(docket);

        // Assert
        result.Should().HaveCount(1);
        result[0].ActionType.Should().Be(ControlActionType.ValidatorRegister);
        result[0].ActionId.Should().Be("control.validator.register");
    }

    [Fact]
    public void ExtractControlTransactions_WithConfigUpdateAction_ExtractsTransaction()
    {
        // Arrange
        var payload = new
        {
            path = "consensus.signatureThreshold.min",
            newValue = 3,
            reason = "Increase minimum signatures"
        };
        var controlTx = CreateTransaction("tx-001", "control.config.update", payload);
        var docket = CreateDocket([controlTx]);

        // Act
        var result = _processor.ExtractControlTransactions(docket);

        // Assert
        result.Should().HaveCount(1);
        result[0].ActionType.Should().Be(ControlActionType.ConfigUpdate);
    }

    [Fact]
    public void ExtractControlTransactions_WithMixedTransactions_ExtractsOnlyControlTransactions()
    {
        // Arrange
        var regularTx = CreateTransaction("tx-001", "workflow.submit", new { data = "test" });
        var controlTx1 = CreateTransaction("tx-002", "control.validator.register", new
        {
            validatorId = TestValidatorId,
            publicKey = "pubkey-001",
            endpoint = "https://validator1.example.com"
        });
        var controlTx2 = CreateTransaction("tx-003", "control.config.update", new
        {
            path = "consensus.docketTimeout",
            newValue = "PT60S"
        });
        var docket = CreateDocket([regularTx, controlTx1, controlTx2]);

        // Act
        var result = _processor.ExtractControlTransactions(docket);

        // Assert
        result.Should().HaveCount(2);
        result.Select(t => t.Transaction.TransactionId).Should().Contain(["tx-002", "tx-003"]);
    }

    [Theory]
    [InlineData("control.validator.register", ControlActionType.ValidatorRegister)]
    [InlineData("control.validator.approve", ControlActionType.ValidatorApprove)]
    [InlineData("control.validator.suspend", ControlActionType.ValidatorSuspend)]
    [InlineData("control.validator.remove", ControlActionType.ValidatorRemove)]
    [InlineData("control.config.update", ControlActionType.ConfigUpdate)]
    [InlineData("control.blueprint.publish", ControlActionType.BlueprintPublish)]
    [InlineData("control.register.updateMetadata", ControlActionType.RegisterUpdateMetadata)]
    [InlineData("control.crypto.update", ControlActionType.CryptoPolicyUpdate)]
    [InlineData("control.policy.update", ControlActionType.PolicyUpdate)]
    public void ExtractControlTransactions_WithActionId_ReturnsCorrectActionType(
        string actionId,
        ControlActionType expectedType)
    {
        // Arrange
        var payload = CreateValidPayloadForActionType(expectedType);
        var controlTx = CreateTransaction("tx-001", actionId, payload);
        var docket = CreateDocket([controlTx]);

        // Act
        var result = _processor.ExtractControlTransactions(docket);

        // Assert
        result.Should().HaveCount(1);
        result[0].ActionType.Should().Be(expectedType);
    }

    [Fact]
    public void ExtractControlTransactions_WithNullActionId_SkipsTransaction()
    {
        // Arrange
        var payloadJson = JsonSerializer.Serialize(new { data = "test" });
        var payloadElement = JsonDocument.Parse(payloadJson).RootElement.Clone();
        var tx = new Transaction
        {
            TransactionId = "tx-001",
            RegisterId = TestRegisterId,
            BlueprintId = null,
            ActionId = null,
            Payload = payloadElement,
            PayloadHash = "hash-tx-001",
            CreatedAt = DateTimeOffset.UtcNow,
            Signatures =
            [
                new Signature
                {
                    PublicKey = System.Text.Encoding.UTF8.GetBytes("test-pubkey"),
                    SignatureValue = System.Text.Encoding.UTF8.GetBytes("test-signature"),
                    Algorithm = "ED25519",
                    SignedAt = DateTimeOffset.UtcNow
                }
            ]
        };
        var docket = CreateDocket([tx]);

        // Act
        var result = _processor.ExtractControlTransactions(docket);

        // Assert
        result.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Control.Validator.Register")]
    [InlineData("CONTROL.VALIDATOR.REGISTER")]
    [InlineData("Control.Config.Update")]
    public void ExtractControlTransactions_CaseInsensitiveActionId_ExtractsTransaction(string actionId)
    {
        // Arrange
        var payload = CreateValidPayloadForActionType(
            actionId.Contains("Validator", StringComparison.OrdinalIgnoreCase)
                ? ControlActionType.ValidatorRegister
                : ControlActionType.ConfigUpdate);
        var controlTx = CreateTransaction("tx-001", actionId, payload);
        var docket = CreateDocket([controlTx]);

        // Act
        var result = _processor.ExtractControlTransactions(docket);

        // Assert
        result.Should().HaveCount(1);
    }

    [Fact]
    public void ExtractControlTransactions_WithCryptoPolicyUpdateAction_ExtractsTransaction()
    {
        // Arrange
        var payload = new
        {
            version = 2,
            acceptedSignatureAlgorithms = new[] { "ED25519", "P-256" },
            requiredSignatureAlgorithms = new[] { "ED25519" },
            enforcementMode = "Strict"
        };
        var controlTx = CreateTransaction("tx-001", "control.crypto.update", payload);
        var docket = CreateDocket([controlTx]);

        // Act
        var result = _processor.ExtractControlTransactions(docket);

        // Assert
        result.Should().HaveCount(1);
        result[0].ActionType.Should().Be(ControlActionType.CryptoPolicyUpdate);
    }

    [Fact]
    public void ExtractControlTransactions_WithPolicyUpdateAction_ExtractsTransaction()
    {
        // Arrange
        var payload = new
        {
            policy = new
            {
                version = 2u,
                validators = new { registrationMode = "Public", minValidators = 1, maxValidators = 50 },
                consensus = new { signatureThresholdMin = 2, signatureThresholdMax = 5 },
                leaderElection = new { mechanism = "Rotating" }
            },
            updatedBy = "admin-001"
        };
        var controlTx = CreateTransaction("tx-001", "control.policy.update", payload);
        var docket = CreateDocket([controlTx]);

        // Act
        var result = _processor.ExtractControlTransactions(docket);

        // Assert
        result.Should().HaveCount(1);
        result[0].ActionType.Should().Be(ControlActionType.PolicyUpdate);
    }

    [Fact]
    public void ExtractControlTransactions_WithMalformedPayload_SkipsAndContinues()
    {
        // Arrange - payload missing required fields for validator.register
        var malformedTx = CreateTransaction("tx-001", "control.validator.register",
            new { unrelatedField = true });
        var validTx = CreateTransaction("tx-002", "control.config.update", new
        {
            path = "consensus.signatureThreshold.min",
            newValue = 3
        });
        var docket = CreateDocket([malformedTx, validTx]);

        // Act
        var result = _processor.ExtractControlTransactions(docket);

        // Assert - at least the valid config.update should be extracted; no exception propagates
        result.Should().HaveCountGreaterThanOrEqualTo(1);
        result.Should().Contain(t => t.ActionType == ControlActionType.ConfigUpdate);
    }

    [Fact]
    public void ExtractControlTransactions_WithUnknownControlPrefix_ReturnsEmpty()
    {
        // Arrange - action starts with "control." but is not a known action type
        var tx = CreateTransaction("tx-001", "control.unknown.action", new { data = "test" });
        var docket = CreateDocket([tx]);

        // Act
        var result = _processor.ExtractControlTransactions(docket);

        // Assert - Unknown ControlActionType is filtered out
        result.Should().BeEmpty();
    }

    #endregion

    #region IsControlDocket Tests

    [Fact]
    public void IsControlDocket_WithNullDocket_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => _processor.IsControlDocket(null!));
    }

    [Fact]
    public void IsControlDocket_WithNoControlTransactions_ReturnsFalse()
    {
        // Arrange
        var regularTx = CreateTransaction("tx-001", "workflow.submit", new { data = "test" });
        var docket = CreateDocket([regularTx]);

        // Act
        var result = _processor.IsControlDocket(docket);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsControlDocket_WithControlTransactions_ReturnsTrue()
    {
        // Arrange
        var controlTx = CreateTransaction("tx-001", "control.config.update", new
        {
            path = "consensus.docketTimeout",
            newValue = "PT60S"
        });
        var docket = CreateDocket([controlTx]);

        // Act
        var result = _processor.IsControlDocket(docket);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsControlDocket_WithEmptyDocket_ReturnsFalse()
    {
        // Arrange
        var docket = CreateDocket([]);

        // Act
        var result = _processor.IsControlDocket(docket);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsControlDocket_WithNullActionIdTransaction_ReturnsFalse()
    {
        // Arrange
        var payloadJson = JsonSerializer.Serialize(new { data = "test" });
        var payloadElement = JsonDocument.Parse(payloadJson).RootElement.Clone();
        var tx = new Transaction
        {
            TransactionId = "tx-001",
            RegisterId = TestRegisterId,
            BlueprintId = null,
            ActionId = null,
            Payload = payloadElement,
            PayloadHash = "hash-tx-001",
            CreatedAt = DateTimeOffset.UtcNow,
            Signatures =
            [
                new Signature
                {
                    PublicKey = System.Text.Encoding.UTF8.GetBytes("test-pubkey"),
                    SignatureValue = System.Text.Encoding.UTF8.GetBytes("test-signature"),
                    Algorithm = "ED25519",
                    SignedAt = DateTimeOffset.UtcNow
                }
            ]
        };
        var docket = CreateDocket([tx]);

        // Act
        var result = _processor.IsControlDocket(docket);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsControlDocket_WithCaseInsensitiveControlAction_ReturnsTrue()
    {
        // Arrange
        var controlTx = CreateTransaction("tx-001", "CONTROL.CONFIG.UPDATE", new
        {
            path = "consensus.docketTimeout",
            newValue = "PT60S"
        });
        var docket = CreateDocket([controlTx]);

        // Act
        var result = _processor.IsControlDocket(docket);

        // Assert
        result.Should().BeTrue();
    }

    #endregion

    #region ValidateControlTransactionsAsync Tests

    [Fact]
    public async Task ValidateControlTransactionsAsync_WithNullRegisterId_ThrowsArgumentNullException()
    {
        // Arrange
        var controlTransactions = new List<ControlTransaction>();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _processor.ValidateControlTransactionsAsync(null!, controlTransactions));
    }

    [Fact]
    public async Task ValidateControlTransactionsAsync_WithWhitespaceRegisterId_ThrowsArgumentException()
    {
        // Arrange
        var controlTransactions = new List<ControlTransaction>();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _processor.ValidateControlTransactionsAsync("   ", controlTransactions));
    }

    [Fact]
    public async Task ValidateControlTransactionsAsync_WithNullTransactionList_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _processor.ValidateControlTransactionsAsync(TestRegisterId, null!));
    }

    [Fact]
    public async Task ValidateControlTransactionsAsync_WithEmptyList_ReturnsSuccess()
    {
        // Arrange
        var controlTransactions = new List<ControlTransaction>();

        // Act
        var result = await _processor.ValidateControlTransactionsAsync(
            TestRegisterId, controlTransactions, CancellationToken.None);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateControlTransactionsAsync_WithValidValidatorRegistration_ReturnsSuccess()
    {
        // Arrange
        var payload = new
        {
            validatorId = TestValidatorId,
            publicKey = "pubkey-001",
            endpoint = "https://validator1.example.com"
        };
        var tx = CreateTransaction("tx-001", "control.validator.register", payload);
        var docket = CreateDocket([tx]);
        var controlTransactions = _processor.ExtractControlTransactions(docket);

        // Act
        var result = await _processor.ValidateControlTransactionsAsync(
            TestRegisterId, controlTransactions, CancellationToken.None);

        // Assert
        result.IsValid.Should().BeTrue();
        result.ValidTransactions.Should().HaveCount(1);
    }

    [Fact]
    public async Task ValidateControlTransactionsAsync_WithInvalidEndpoint_ReturnsError()
    {
        // Arrange
        var payload = new
        {
            validatorId = TestValidatorId,
            publicKey = "pubkey-001",
            endpoint = "not-a-valid-uri"
        };
        var tx = CreateTransaction("tx-001", "control.validator.register", payload);
        var docket = CreateDocket([tx]);
        var controlTransactions = _processor.ExtractControlTransactions(docket);

        // Act
        var result = await _processor.ValidateControlTransactionsAsync(
            TestRegisterId, controlTransactions, CancellationToken.None);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainKey("tx-001");
        result.Errors["tx-001"].Should().Contain(e => e.Contains("valid URI"));
    }

    [Fact]
    public async Task ValidateControlTransactionsAsync_WithMissingValidatorId_ReturnsError()
    {
        // Arrange
        var payload = new
        {
            validatorId = "",
            publicKey = "pubkey-001",
            endpoint = "https://validator1.example.com"
        };
        var tx = CreateTransaction("tx-001", "control.validator.register", payload);
        var docket = CreateDocket([tx]);
        var controlTransactions = _processor.ExtractControlTransactions(docket);

        // Act
        var result = await _processor.ValidateControlTransactionsAsync(
            TestRegisterId, controlTransactions, CancellationToken.None);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors["tx-001"].Should().Contain(e => e.Contains("ValidatorId"));
    }

    [Fact]
    public async Task ValidateControlTransactionsAsync_WithMissingPublicKey_ReturnsError()
    {
        // Arrange
        var payload = new
        {
            validatorId = TestValidatorId,
            publicKey = "",
            endpoint = "https://validator1.example.com"
        };
        var tx = CreateTransaction("tx-001", "control.validator.register", payload);
        var docket = CreateDocket([tx]);
        var controlTransactions = _processor.ExtractControlTransactions(docket);

        // Act
        var result = await _processor.ValidateControlTransactionsAsync(
            TestRegisterId, controlTransactions, CancellationToken.None);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors["tx-001"].Should().Contain(e => e.Contains("PublicKey"));
    }

    [Fact]
    public async Task ValidateControlTransactionsAsync_ValidatorApprovalForNonexistentValidator_ReturnsError()
    {
        // Arrange
        _mockValidatorRegistry
            .Setup(r => r.GetValidatorAsync(TestRegisterId, TestValidatorId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ValidatorInfo?)null);

        var payload = new
        {
            validatorId = TestValidatorId,
            approvedBy = "admin-001"
        };
        var tx = CreateTransaction("tx-001", "control.validator.approve", payload);
        var docket = CreateDocket([tx]);
        var controlTransactions = _processor.ExtractControlTransactions(docket);

        // Act
        var result = await _processor.ValidateControlTransactionsAsync(
            TestRegisterId, controlTransactions, CancellationToken.None);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors["tx-001"].Should().Contain(e => e.Contains("not found"));
    }

    [Fact]
    public async Task ValidateControlTransactionsAsync_ValidatorApprovalForPendingValidator_ReturnsSuccess()
    {
        // Arrange
        var validatorInfo = new ValidatorInfo
        {
            ValidatorId = TestValidatorId,
            PublicKey = "pubkey-001",
            GrpcEndpoint = "https://validator1.example.com",
            Status = ValidatorStatus.Pending,
            RegisteredAt = DateTimeOffset.UtcNow
        };

        _mockValidatorRegistry
            .Setup(r => r.GetValidatorAsync(TestRegisterId, TestValidatorId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(validatorInfo);

        var payload = new
        {
            validatorId = TestValidatorId,
            approvedBy = "admin-001"
        };
        var tx = CreateTransaction("tx-001", "control.validator.approve", payload);
        var docket = CreateDocket([tx]);
        var controlTransactions = _processor.ExtractControlTransactions(docket);

        // Act
        var result = await _processor.ValidateControlTransactionsAsync(
            TestRegisterId, controlTransactions, CancellationToken.None);

        // Assert
        result.IsValid.Should().BeTrue();
        result.ValidTransactions.Should().HaveCount(1);
    }

    [Fact]
    public async Task ValidateControlTransactionsAsync_ValidatorApprovalForActiveValidator_ReturnsError()
    {
        // Arrange
        var validatorInfo = new ValidatorInfo
        {
            ValidatorId = TestValidatorId,
            PublicKey = "pubkey-001",
            GrpcEndpoint = "https://validator1.example.com",
            Status = ValidatorStatus.Active,
            RegisteredAt = DateTimeOffset.UtcNow
        };

        _mockValidatorRegistry
            .Setup(r => r.GetValidatorAsync(TestRegisterId, TestValidatorId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(validatorInfo);

        var payload = new
        {
            validatorId = TestValidatorId,
            approvedBy = "admin-001"
        };
        var tx = CreateTransaction("tx-001", "control.validator.approve", payload);
        var docket = CreateDocket([tx]);
        var controlTransactions = _processor.ExtractControlTransactions(docket);

        // Act
        var result = await _processor.ValidateControlTransactionsAsync(
            TestRegisterId, controlTransactions, CancellationToken.None);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors["tx-001"].Should().Contain(e => e.Contains("not pending"));
    }

    [Fact]
    public async Task ValidateControlTransactionsAsync_ValidatorSuspensionForActiveValidator_ReturnsSuccess()
    {
        // Arrange
        var validatorInfo = new ValidatorInfo
        {
            ValidatorId = TestValidatorId,
            PublicKey = "pubkey-001",
            GrpcEndpoint = "https://validator1.example.com",
            Status = ValidatorStatus.Active,
            RegisteredAt = DateTimeOffset.UtcNow
        };

        _mockValidatorRegistry
            .Setup(r => r.GetValidatorAsync(TestRegisterId, TestValidatorId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(validatorInfo);

        var payload = new
        {
            validatorId = TestValidatorId,
            suspendedBy = "admin-001",
            reason = "Maintenance"
        };
        var tx = CreateTransaction("tx-001", "control.validator.suspend", payload);
        var docket = CreateDocket([tx]);
        var controlTransactions = _processor.ExtractControlTransactions(docket);

        // Act
        var result = await _processor.ValidateControlTransactionsAsync(
            TestRegisterId, controlTransactions, CancellationToken.None);

        // Assert
        result.IsValid.Should().BeTrue();
        result.ValidTransactions.Should().HaveCount(1);
    }

    [Fact]
    public async Task ValidateControlTransactionsAsync_ValidatorSuspensionForSuspendedValidator_ReturnsError()
    {
        // Arrange
        var validatorInfo = new ValidatorInfo
        {
            ValidatorId = TestValidatorId,
            PublicKey = "pubkey-001",
            GrpcEndpoint = "https://validator1.example.com",
            Status = ValidatorStatus.Suspended,
            RegisteredAt = DateTimeOffset.UtcNow
        };

        _mockValidatorRegistry
            .Setup(r => r.GetValidatorAsync(TestRegisterId, TestValidatorId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(validatorInfo);

        var payload = new
        {
            validatorId = TestValidatorId,
            suspendedBy = "admin-001",
            reason = "Maintenance"
        };
        var tx = CreateTransaction("tx-001", "control.validator.suspend", payload);
        var docket = CreateDocket([tx]);
        var controlTransactions = _processor.ExtractControlTransactions(docket);

        // Act
        var result = await _processor.ValidateControlTransactionsAsync(
            TestRegisterId, controlTransactions, CancellationToken.None);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors["tx-001"].Should().Contain(e => e.Contains("not active"));
    }

    [Fact]
    public async Task ValidateControlTransactionsAsync_ValidatorSuspensionForNonexistentValidator_ReturnsError()
    {
        // Arrange
        _mockValidatorRegistry
            .Setup(r => r.GetValidatorAsync(TestRegisterId, TestValidatorId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ValidatorInfo?)null);

        var payload = new
        {
            validatorId = TestValidatorId,
            suspendedBy = "admin-001",
            reason = "Maintenance"
        };
        var tx = CreateTransaction("tx-001", "control.validator.suspend", payload);
        var docket = CreateDocket([tx]);
        var controlTransactions = _processor.ExtractControlTransactions(docket);

        // Act
        var result = await _processor.ValidateControlTransactionsAsync(
            TestRegisterId, controlTransactions, CancellationToken.None);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors["tx-001"].Should().Contain(e => e.Contains("not found"));
    }

    [Fact]
    public async Task ValidateControlTransactionsAsync_ValidatorRemovalBelowMinimum_ReturnsError()
    {
        // Arrange
        var validatorInfo = new ValidatorInfo
        {
            ValidatorId = TestValidatorId,
            PublicKey = "pubkey-001",
            GrpcEndpoint = "https://validator1.example.com",
            Status = ValidatorStatus.Active,
            RegisteredAt = DateTimeOffset.UtcNow
        };

        _mockValidatorRegistry
            .Setup(r => r.GetValidatorAsync(TestRegisterId, TestValidatorId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(validatorInfo);
        _mockValidatorRegistry
            .Setup(r => r.GetActiveCountAsync(TestRegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(2); // Equals minimum (2)

        var payload = new
        {
            validatorId = TestValidatorId,
            removedBy = "admin-001",
            reason = "No longer needed"
        };
        var tx = CreateTransaction("tx-001", "control.validator.remove", payload);
        var docket = CreateDocket([tx]);
        var controlTransactions = _processor.ExtractControlTransactions(docket);

        // Act
        var result = await _processor.ValidateControlTransactionsAsync(
            TestRegisterId, controlTransactions, CancellationToken.None);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors["tx-001"].Should().Contain(e => e.Contains("below minimum"));
    }

    [Fact]
    public async Task ValidateControlTransactionsAsync_ValidatorRemovalAboveMinimum_ReturnsSuccess()
    {
        // Arrange
        var validatorInfo = new ValidatorInfo
        {
            ValidatorId = TestValidatorId,
            PublicKey = "pubkey-001",
            GrpcEndpoint = "https://validator1.example.com",
            Status = ValidatorStatus.Active,
            RegisteredAt = DateTimeOffset.UtcNow
        };

        _mockValidatorRegistry
            .Setup(r => r.GetValidatorAsync(TestRegisterId, TestValidatorId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(validatorInfo);
        _mockValidatorRegistry
            .Setup(r => r.GetActiveCountAsync(TestRegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(5); // Well above minimum (2)

        var payload = new
        {
            validatorId = TestValidatorId,
            removedBy = "admin-001",
            reason = "No longer needed"
        };
        var tx = CreateTransaction("tx-001", "control.validator.remove", payload);
        var docket = CreateDocket([tx]);
        var controlTransactions = _processor.ExtractControlTransactions(docket);

        // Act
        var result = await _processor.ValidateControlTransactionsAsync(
            TestRegisterId, controlTransactions, CancellationToken.None);

        // Assert
        result.IsValid.Should().BeTrue();
        result.ValidTransactions.Should().HaveCount(1);
    }

    [Fact]
    public async Task ValidateControlTransactionsAsync_ConfigUpdateWithValidPath_ReturnsSuccess()
    {
        // Arrange
        var payload = new
        {
            path = "consensus.signatureThreshold.min",
            newValue = 3,
            reason = "Increase threshold"
        };
        var tx = CreateTransaction("tx-001", "control.config.update", payload);
        var docket = CreateDocket([tx]);
        var controlTransactions = _processor.ExtractControlTransactions(docket);

        // Act
        var result = await _processor.ValidateControlTransactionsAsync(
            TestRegisterId, controlTransactions, CancellationToken.None);

        // Assert
        result.IsValid.Should().BeTrue();
        result.ValidTransactions.Should().HaveCount(1);
    }

    [Fact]
    public async Task ValidateControlTransactionsAsync_ConfigUpdateWithInvalidPath_ReturnsError()
    {
        // Arrange
        var payload = new
        {
            path = "invalid.config.path",
            newValue = "something",
            reason = "Testing"
        };
        var tx = CreateTransaction("tx-001", "control.config.update", payload);
        var docket = CreateDocket([tx]);
        var controlTransactions = _processor.ExtractControlTransactions(docket);

        // Act
        var result = await _processor.ValidateControlTransactionsAsync(
            TestRegisterId, controlTransactions, CancellationToken.None);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors["tx-001"].Should().Contain(e => e.Contains("Unknown configuration path"));
    }

    [Fact]
    public async Task ValidateControlTransactionsAsync_ConfigUpdateWithMissingPath_ReturnsError()
    {
        // Arrange
        var payload = new
        {
            path = "",
            newValue = "something"
        };
        var tx = CreateTransaction("tx-001", "control.config.update", payload);
        var docket = CreateDocket([tx]);
        var controlTransactions = _processor.ExtractControlTransactions(docket);

        // Act
        var result = await _processor.ValidateControlTransactionsAsync(
            TestRegisterId, controlTransactions, CancellationToken.None);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors["tx-001"].Should().Contain(e => e.Contains("Path"));
    }

    [Fact]
    public async Task ValidateControlTransactionsAsync_BlueprintPublishWithValidPayload_ReturnsSuccess()
    {
        // Arrange
        var payload = new
        {
            blueprintId = "bp-001",
            blueprintJson = "{\"title\":\"Test Blueprint\"}",
            publishedBy = "admin-001"
        };
        var tx = CreateTransaction("tx-001", "control.blueprint.publish", payload);
        var docket = CreateDocket([tx]);
        var controlTransactions = _processor.ExtractControlTransactions(docket);

        // Act
        var result = await _processor.ValidateControlTransactionsAsync(
            TestRegisterId, controlTransactions, CancellationToken.None);

        // Assert
        result.IsValid.Should().BeTrue();
        result.ValidTransactions.Should().HaveCount(1);
    }

    [Fact]
    public async Task ValidateControlTransactionsAsync_BlueprintPublishWithMissingBlueprintId_ReturnsError()
    {
        // Arrange
        var payload = new
        {
            blueprintId = "",
            blueprintJson = "{\"title\":\"Test Blueprint\"}",
            publishedBy = "admin-001"
        };
        var tx = CreateTransaction("tx-001", "control.blueprint.publish", payload);
        var docket = CreateDocket([tx]);
        var controlTransactions = _processor.ExtractControlTransactions(docket);

        // Act
        var result = await _processor.ValidateControlTransactionsAsync(
            TestRegisterId, controlTransactions, CancellationToken.None);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors["tx-001"].Should().Contain(e => e.Contains("BlueprintId"));
    }

    [Fact]
    public async Task ValidateControlTransactionsAsync_BlueprintPublishWithInvalidJson_ReturnsError()
    {
        // Arrange
        var payload = new
        {
            blueprintId = "bp-001",
            blueprintJson = "not valid json {{{",
            publishedBy = "admin-001"
        };
        var tx = CreateTransaction("tx-001", "control.blueprint.publish", payload);
        var docket = CreateDocket([tx]);
        var controlTransactions = _processor.ExtractControlTransactions(docket);

        // Act
        var result = await _processor.ValidateControlTransactionsAsync(
            TestRegisterId, controlTransactions, CancellationToken.None);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors["tx-001"].Should().Contain(e => e.Contains("valid JSON"));
    }

    [Fact]
    public async Task ValidateControlTransactionsAsync_MetadataUpdateWithValidPayload_ReturnsSuccess()
    {
        // Arrange
        var payload = new
        {
            field = "name",
            newValue = "Updated Register Name"
        };
        var tx = CreateTransaction("tx-001", "control.register.updatemetadata", payload);
        var docket = CreateDocket([tx]);
        var controlTransactions = _processor.ExtractControlTransactions(docket);

        // Act
        var result = await _processor.ValidateControlTransactionsAsync(
            TestRegisterId, controlTransactions, CancellationToken.None);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateControlTransactionsAsync_MetadataUpdateWithInvalidField_ReturnsError()
    {
        // Arrange
        var payload = new
        {
            field = "invalidField",
            newValue = "Some value"
        };
        var tx = CreateTransaction("tx-001", "control.register.updatemetadata", payload);
        var docket = CreateDocket([tx]);
        var controlTransactions = _processor.ExtractControlTransactions(docket);

        // Act
        var result = await _processor.ValidateControlTransactionsAsync(
            TestRegisterId, controlTransactions, CancellationToken.None);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors["tx-001"].Should().Contain(e => e.Contains("Invalid field"));
    }

    [Fact]
    public async Task ValidateControlTransactionsAsync_MetadataUpdateWithMissingNewValue_ReturnsError()
    {
        // Arrange
        var payload = new
        {
            field = "name",
            newValue = ""
        };
        var tx = CreateTransaction("tx-001", "control.register.updatemetadata", payload);
        var docket = CreateDocket([tx]);
        var controlTransactions = _processor.ExtractControlTransactions(docket);

        // Act
        var result = await _processor.ValidateControlTransactionsAsync(
            TestRegisterId, controlTransactions, CancellationToken.None);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors["tx-001"].Should().Contain(e => e.Contains("NewValue"));
    }

    [Fact]
    public async Task ValidateControlTransactionsAsync_CryptoPolicyUpdateWithValidPayload_ReturnsSuccess()
    {
        // Arrange
        var payload = new
        {
            version = 2,
            acceptedSignatureAlgorithms = new[] { "ED25519", "P-256" },
            requiredSignatureAlgorithms = new[] { "ED25519" },
            enforcementMode = "Strict"
        };
        var tx = CreateTransaction("tx-001", "control.crypto.update", payload);
        var docket = CreateDocket([tx]);
        var controlTransactions = _processor.ExtractControlTransactions(docket);

        // Act
        var result = await _processor.ValidateControlTransactionsAsync(
            TestRegisterId, controlTransactions, CancellationToken.None);

        // Assert
        result.IsValid.Should().BeTrue();
        result.ValidTransactions.Should().HaveCount(1);
    }

    [Fact]
    public async Task ValidateControlTransactionsAsync_CryptoPolicyUpdateWithZeroVersion_ReturnsError()
    {
        // Arrange
        var payload = new
        {
            version = 0,
            acceptedSignatureAlgorithms = new[] { "ED25519" },
            enforcementMode = "Permissive"
        };
        var tx = CreateTransaction("tx-001", "control.crypto.update", payload);
        var docket = CreateDocket([tx]);
        var controlTransactions = _processor.ExtractControlTransactions(docket);

        // Act
        var result = await _processor.ValidateControlTransactionsAsync(
            TestRegisterId, controlTransactions, CancellationToken.None);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors["tx-001"].Should().Contain(e => e.Contains("version"));
    }

    [Fact]
    public async Task ValidateControlTransactionsAsync_CryptoPolicyUpdateWithEmptyAlgorithms_ReturnsError()
    {
        // Arrange
        var payload = new
        {
            version = 1,
            acceptedSignatureAlgorithms = Array.Empty<string>(),
            enforcementMode = "Permissive"
        };
        var tx = CreateTransaction("tx-001", "control.crypto.update", payload);
        var docket = CreateDocket([tx]);
        var controlTransactions = _processor.ExtractControlTransactions(docket);

        // Act
        var result = await _processor.ValidateControlTransactionsAsync(
            TestRegisterId, controlTransactions, CancellationToken.None);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors["tx-001"].Should().Contain(e => e.Contains("AcceptedSignatureAlgorithms"));
    }

    [Fact]
    public async Task ValidateControlTransactionsAsync_CryptoPolicyUpdateWithInvalidEnforcementMode_ReturnsError()
    {
        // Arrange
        var payload = new
        {
            version = 1,
            acceptedSignatureAlgorithms = new[] { "ED25519" },
            enforcementMode = "InvalidMode"
        };
        var tx = CreateTransaction("tx-001", "control.crypto.update", payload);
        var docket = CreateDocket([tx]);
        var controlTransactions = _processor.ExtractControlTransactions(docket);

        // Act
        var result = await _processor.ValidateControlTransactionsAsync(
            TestRegisterId, controlTransactions, CancellationToken.None);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors["tx-001"].Should().Contain(e => e.Contains("EnforcementMode"));
    }

    [Fact]
    public async Task ValidateControlTransactionsAsync_CryptoPolicyUpdateWithRequiredNotInAccepted_ReturnsError()
    {
        // Arrange
        var payload = new
        {
            version = 1,
            acceptedSignatureAlgorithms = new[] { "ED25519" },
            requiredSignatureAlgorithms = new[] { "P-256" },
            enforcementMode = "Strict"
        };
        var tx = CreateTransaction("tx-001", "control.crypto.update", payload);
        var docket = CreateDocket([tx]);
        var controlTransactions = _processor.ExtractControlTransactions(docket);

        // Act
        var result = await _processor.ValidateControlTransactionsAsync(
            TestRegisterId, controlTransactions, CancellationToken.None);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors["tx-001"].Should().Contain(e => e.Contains("not in AcceptedSignatureAlgorithms"));
    }

    [Fact]
    public async Task ValidateControlTransactionsAsync_MultipleTransactionsWithMixedValidity_ReturnsErrors()
    {
        // Arrange - one valid, one invalid
        var validTx = CreateTransaction("tx-001", "control.config.update", new
        {
            path = "consensus.signatureThreshold.min",
            newValue = 3
        });
        var invalidTx = CreateTransaction("tx-002", "control.config.update", new
        {
            path = "invalid.path",
            newValue = "something"
        });
        var docket = CreateDocket([validTx, invalidTx]);
        var controlTransactions = _processor.ExtractControlTransactions(docket);

        // Act
        var result = await _processor.ValidateControlTransactionsAsync(
            TestRegisterId, controlTransactions, CancellationToken.None);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainKey("tx-002");
        result.Errors.Should().NotContainKey("tx-001");
    }

    #endregion

    #region ProcessCommittedDocketAsync Tests

    [Fact]
    public async Task ProcessCommittedDocketAsync_WithNullRegisterId_ThrowsArgumentNullException()
    {
        // Arrange
        var docket = CreateDocket([]);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _processor.ProcessCommittedDocketAsync(null!, docket));
    }

    [Fact]
    public async Task ProcessCommittedDocketAsync_WithNullDocket_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _processor.ProcessCommittedDocketAsync(TestRegisterId, null!));
    }

    [Fact]
    public async Task ProcessCommittedDocketAsync_WithNoControlTransactions_ReturnsSuccessWithZeroActions()
    {
        // Arrange
        var regularTx = CreateTransaction("tx-001", "workflow.submit", new { data = "test" });
        var docket = CreateDocket([regularTx]);

        // Act
        var result = await _processor.ProcessCommittedDocketAsync(TestRegisterId, docket);

        // Assert
        result.Success.Should().BeTrue();
        result.ActionsApplied.Should().Be(0);
        result.ConfigurationUpdated.Should().BeFalse();
        result.ValidatorsModified.Should().BeFalse();
    }

    [Fact]
    public async Task ProcessCommittedDocketAsync_WithValidatorRegistration_RefreshesValidatorRegistry()
    {
        // Arrange
        _mockValidatorRegistry
            .Setup(r => r.RegisterAsync(
                TestRegisterId,
                It.IsAny<ValidatorRegistration>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidatorRegistrationResult.Succeeded("tx-001", 0));

        var payload = new
        {
            validatorId = TestValidatorId,
            publicKey = "pubkey-001",
            endpoint = "https://validator1.example.com"
        };
        var tx = CreateTransaction("tx-001", "control.validator.register", payload);
        var docket = CreateDocket([tx]);

        // Act
        var result = await _processor.ProcessCommittedDocketAsync(TestRegisterId, docket);

        // Assert
        result.Success.Should().BeTrue();
        result.ActionsApplied.Should().Be(1);
        result.ValidatorsModified.Should().BeTrue();
        _mockValidatorRegistry.Verify(
            r => r.RefreshAsync(TestRegisterId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessCommittedDocketAsync_WithConfigUpdate_RefreshesGenesisConfig()
    {
        // Arrange
        var payload = new
        {
            path = "consensus.signatureThreshold.min",
            newValue = 3,
            reason = "Increase minimum"
        };
        var tx = CreateTransaction("tx-001", "control.config.update", payload);
        var docket = CreateDocket([tx]);

        // Act
        var result = await _processor.ProcessCommittedDocketAsync(TestRegisterId, docket);

        // Assert
        result.Success.Should().BeTrue();
        result.ConfigurationUpdated.Should().BeTrue();
        _mockGenesisConfigService.Verify(
            s => s.RefreshConfigAsync(TestRegisterId, It.IsAny<CancellationToken>()),
            Times.Once);
        _mockVersionResolver.Verify(
            v => v.InvalidateCache(TestRegisterId),
            Times.Once);
    }

    [Fact]
    public async Task ProcessCommittedDocketAsync_RaisesControlActionAppliedEvent()
    {
        // Arrange
        ControlActionAppliedEventArgs? capturedArgs = null;
        _processor.ControlActionApplied += (sender, args) => capturedArgs = args;

        _mockValidatorRegistry
            .Setup(r => r.RegisterAsync(
                TestRegisterId,
                It.IsAny<ValidatorRegistration>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidatorRegistrationResult.Succeeded("tx-001", 0));

        var payload = new
        {
            validatorId = TestValidatorId,
            publicKey = "pubkey-001",
            endpoint = "https://validator1.example.com"
        };
        var tx = CreateTransaction("tx-001", "control.validator.register", payload);
        var docket = CreateDocket([tx]);

        // Act
        await _processor.ProcessCommittedDocketAsync(TestRegisterId, docket);

        // Assert
        capturedArgs.Should().NotBeNull();
        capturedArgs!.RegisterId.Should().Be(TestRegisterId);
        capturedArgs.TransactionId.Should().Be("tx-001");
        capturedArgs.ActionType.Should().Be(ControlActionType.ValidatorRegister);
    }

    [Fact]
    public async Task ProcessCommittedDocketAsync_WithCryptoPolicyUpdate_SetsConfigurationUpdated()
    {
        // Arrange
        var payload = new
        {
            version = 2,
            acceptedSignatureAlgorithms = new[] { "ED25519", "P-256" },
            requiredSignatureAlgorithms = new[] { "ED25519" },
            enforcementMode = "Strict"
        };
        var tx = CreateTransaction("tx-001", "control.crypto.update", payload);
        var docket = CreateDocket([tx]);

        // Act
        var result = await _processor.ProcessCommittedDocketAsync(TestRegisterId, docket);

        // Assert
        result.Success.Should().BeTrue();
        result.ConfigurationUpdated.Should().BeTrue();
        result.ValidatorsModified.Should().BeFalse();
        _mockGenesisConfigService.Verify(
            s => s.RefreshConfigAsync(TestRegisterId, It.IsAny<CancellationToken>()),
            Times.Once);
        _mockVersionResolver.Verify(
            v => v.InvalidateCache(TestRegisterId),
            Times.Once);
    }

    [Fact]
    public async Task ProcessCommittedDocketAsync_WithActionException_RecordsFailureAndContinues()
    {
        // Arrange - validator register throws, then a config update succeeds
        _mockValidatorRegistry
            .Setup(r => r.RegisterAsync(
                TestRegisterId,
                It.IsAny<ValidatorRegistration>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Registry unavailable"));

        var registerTx = CreateTransaction("tx-001", "control.validator.register", new
        {
            validatorId = TestValidatorId,
            publicKey = "pubkey-001",
            endpoint = "https://validator1.example.com"
        });
        var configTx = CreateTransaction("tx-002", "control.config.update", new
        {
            path = "consensus.signatureThreshold.min",
            newValue = 3
        });
        var docket = CreateDocket([registerTx, configTx]);

        // Act
        var result = await _processor.ProcessCommittedDocketAsync(TestRegisterId, docket);

        // Assert
        result.Success.Should().BeFalse(); // Not all actions succeeded
        result.ActionsApplied.Should().Be(1); // Config update succeeded
        result.ActionResults.Should().HaveCount(2);
        result.ActionResults[0].Success.Should().BeFalse();
        result.ActionResults[0].ErrorMessage.Should().Contain("Registry unavailable");
        result.ActionResults[1].Success.Should().BeTrue();
    }

    [Fact]
    public async Task ProcessCommittedDocketAsync_WithMultipleValidatorActions_SetsValidatorsModifiedOnce()
    {
        // Arrange
        _mockValidatorRegistry
            .Setup(r => r.RegisterAsync(
                TestRegisterId,
                It.IsAny<ValidatorRegistration>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidatorRegistrationResult.Succeeded("tx-001", 0));

        _mockValidatorRegistry
            .Setup(r => r.GetValidatorAsync(TestRegisterId, TestValidatorId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidatorInfo
            {
                ValidatorId = TestValidatorId,
                PublicKey = "pubkey-001",
                GrpcEndpoint = "https://validator1.example.com",
                Status = ValidatorStatus.Pending,
                RegisteredAt = DateTimeOffset.UtcNow
            });

        var registerTx = CreateTransaction("tx-001", "control.validator.register", new
        {
            validatorId = "validator-002",
            publicKey = "pubkey-002",
            endpoint = "https://validator2.example.com"
        });
        var approveTx = CreateTransaction("tx-002", "control.validator.approve", new
        {
            validatorId = TestValidatorId,
            approvedBy = "admin-001"
        });
        var docket = CreateDocket([registerTx, approveTx]);

        // Act
        var result = await _processor.ProcessCommittedDocketAsync(TestRegisterId, docket);

        // Assert
        result.ValidatorsModified.Should().BeTrue();
        result.ActionsApplied.Should().Be(2);
        _mockValidatorRegistry.Verify(
            r => r.RefreshAsync(TestRegisterId, It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task ProcessCommittedDocketAsync_WithNoConfigOrValidatorChanges_DoesNotRefresh()
    {
        // Arrange - blueprint publish doesn't trigger config refresh or validator refresh
        var payload = new
        {
            blueprintId = "bp-001",
            blueprintJson = "{\"title\":\"Test Blueprint\"}",
            publishedBy = "admin-001"
        };
        var tx = CreateTransaction("tx-001", "control.blueprint.publish", payload);
        var docket = CreateDocket([tx]);

        // Act
        var result = await _processor.ProcessCommittedDocketAsync(TestRegisterId, docket);

        // Assert
        result.Success.Should().BeTrue();
        result.ConfigurationUpdated.Should().BeFalse();
        result.ValidatorsModified.Should().BeFalse();
        _mockGenesisConfigService.Verify(
            s => s.RefreshConfigAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _mockValidatorRegistry.Verify(
            r => r.RefreshAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion

    #region ApplyControlActionAsync Tests

    [Fact]
    public async Task ApplyControlActionAsync_WithNullRegisterId_ThrowsArgumentNullException()
    {
        // Arrange
        var payload = new
        {
            validatorId = TestValidatorId,
            publicKey = "pubkey-001",
            endpoint = "https://validator1.example.com"
        };
        var tx = CreateTransaction("tx-001", "control.validator.register", payload);
        var docket = CreateDocket([tx]);
        var controlTx = _processor.ExtractControlTransactions(docket)[0];

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _processor.ApplyControlActionAsync(null!, controlTx));
    }

    [Fact]
    public async Task ApplyControlActionAsync_WithNullControlTransaction_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _processor.ApplyControlActionAsync(TestRegisterId, null!));
    }

    [Fact]
    public async Task ApplyControlActionAsync_ValidatorRegister_CallsValidatorRegistry()
    {
        // Arrange
        _mockValidatorRegistry
            .Setup(r => r.RegisterAsync(
                TestRegisterId,
                It.IsAny<ValidatorRegistration>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidatorRegistrationResult.Succeeded("tx-001", 5));

        var payload = new
        {
            validatorId = TestValidatorId,
            publicKey = "pubkey-001",
            endpoint = "https://validator1.example.com"
        };
        var tx = CreateTransaction("tx-001", "control.validator.register", payload);
        var docket = CreateDocket([tx]);
        var controlTx = _processor.ExtractControlTransactions(docket)[0];

        // Act
        var result = await _processor.ApplyControlActionAsync(TestRegisterId, controlTx);

        // Assert
        result.Success.Should().BeTrue();
        result.ChangeDescription.Should().Contain("registered");
        result.ChangeDescription.Should().Contain("order: 5");
    }

    [Fact]
    public async Task ApplyControlActionAsync_ValidatorRegisterFails_ReturnsFailureResult()
    {
        // Arrange
        _mockValidatorRegistry
            .Setup(r => r.RegisterAsync(
                TestRegisterId,
                It.IsAny<ValidatorRegistration>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidatorRegistrationResult.Failed("Max validators reached"));

        var payload = new
        {
            validatorId = TestValidatorId,
            publicKey = "pubkey-001",
            endpoint = "https://validator1.example.com"
        };
        var tx = CreateTransaction("tx-001", "control.validator.register", payload);
        var docket = CreateDocket([tx]);
        var controlTx = _processor.ExtractControlTransactions(docket)[0];

        // Act
        var result = await _processor.ApplyControlActionAsync(TestRegisterId, controlTx);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Max validators reached");
    }

    [Fact]
    public async Task ApplyControlActionAsync_ValidatorApprove_ReturnsSuccessWithDescription()
    {
        // Arrange
        _mockValidatorRegistry
            .Setup(r => r.GetValidatorAsync(TestRegisterId, TestValidatorId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidatorInfo
            {
                ValidatorId = TestValidatorId,
                PublicKey = "pubkey-001",
                GrpcEndpoint = "https://validator1.example.com",
                Status = ValidatorStatus.Pending,
                RegisteredAt = DateTimeOffset.UtcNow
            });

        var payload = new
        {
            validatorId = TestValidatorId,
            approvedBy = "admin-001"
        };
        var tx = CreateTransaction("tx-001", "control.validator.approve", payload);
        var docket = CreateDocket([tx]);
        var controlTx = _processor.ExtractControlTransactions(docket)[0];

        // Act
        var result = await _processor.ApplyControlActionAsync(TestRegisterId, controlTx);

        // Assert
        result.Success.Should().BeTrue();
        result.ChangeDescription.Should().Contain("approved");
        result.ChangeDescription.Should().Contain("admin-001");
        result.ActionType.Should().Be(ControlActionType.ValidatorApprove);
    }

    [Fact]
    public async Task ApplyControlActionAsync_ValidatorApproveNotFound_ReturnsFailure()
    {
        // Arrange
        _mockValidatorRegistry
            .Setup(r => r.GetValidatorAsync(TestRegisterId, TestValidatorId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ValidatorInfo?)null);

        var payload = new
        {
            validatorId = TestValidatorId,
            approvedBy = "admin-001"
        };
        var tx = CreateTransaction("tx-001", "control.validator.approve", payload);
        var docket = CreateDocket([tx]);
        var controlTx = _processor.ExtractControlTransactions(docket)[0];

        // Act
        var result = await _processor.ApplyControlActionAsync(TestRegisterId, controlTx);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not found");
    }

    [Fact]
    public async Task ApplyControlActionAsync_ValidatorSuspend_ReturnsSuccessWithDescription()
    {
        // Arrange
        var payload = new
        {
            validatorId = TestValidatorId,
            suspendedBy = "admin-001",
            reason = "Maintenance window"
        };
        var tx = CreateTransaction("tx-001", "control.validator.suspend", payload);
        var docket = CreateDocket([tx]);
        var controlTx = _processor.ExtractControlTransactions(docket)[0];

        // Act
        var result = await _processor.ApplyControlActionAsync(TestRegisterId, controlTx);

        // Assert
        result.Success.Should().BeTrue();
        result.ChangeDescription.Should().Contain("suspended");
        result.ChangeDescription.Should().Contain("admin-001");
        result.ChangeDescription.Should().Contain("indefinitely");
        result.ActionType.Should().Be(ControlActionType.ValidatorSuspend);
    }

    [Fact]
    public async Task ApplyControlActionAsync_ValidatorSuspendWithExpiry_IncludesExpiryInDescription()
    {
        // Arrange
        var suspendUntil = DateTimeOffset.UtcNow.AddHours(24);
        var payload = new
        {
            validatorId = TestValidatorId,
            suspendedBy = "admin-001",
            reason = "Temporary maintenance",
            suspendedUntil = suspendUntil
        };
        var tx = CreateTransaction("tx-001", "control.validator.suspend", payload);
        var docket = CreateDocket([tx]);
        var controlTx = _processor.ExtractControlTransactions(docket)[0];

        // Act
        var result = await _processor.ApplyControlActionAsync(TestRegisterId, controlTx);

        // Assert
        result.Success.Should().BeTrue();
        result.ChangeDescription.Should().Contain("suspended");
        result.ChangeDescription.Should().Contain("until");
    }

    [Fact]
    public async Task ApplyControlActionAsync_ValidatorRemove_ReturnsSuccessWithDescription()
    {
        // Arrange
        var payload = new
        {
            validatorId = TestValidatorId,
            removedBy = "admin-001",
            reason = "Decommissioned"
        };
        var tx = CreateTransaction("tx-001", "control.validator.remove", payload);
        var docket = CreateDocket([tx]);
        var controlTx = _processor.ExtractControlTransactions(docket)[0];

        // Act
        var result = await _processor.ApplyControlActionAsync(TestRegisterId, controlTx);

        // Assert
        result.Success.Should().BeTrue();
        result.ChangeDescription.Should().Contain("removed");
        result.ChangeDescription.Should().Contain("admin-001");
        result.ChangeDescription.Should().Contain("Decommissioned");
        result.ActionType.Should().Be(ControlActionType.ValidatorRemove);
    }

    [Fact]
    public async Task ApplyControlActionAsync_ConfigUpdate_ReturnsSuccessWithDescription()
    {
        // Arrange
        var payload = new
        {
            path = "consensus.signatureThreshold.min",
            newValue = 5,
            reason = "Increase threshold"
        };
        var tx = CreateTransaction("tx-001", "control.config.update", payload);
        var docket = CreateDocket([tx]);
        var controlTx = _processor.ExtractControlTransactions(docket)[0];

        // Act
        var result = await _processor.ApplyControlActionAsync(TestRegisterId, controlTx);

        // Assert
        result.Success.Should().BeTrue();
        result.ChangeDescription.Should().Contain("Configuration updated");
        result.ChangeDescription.Should().Contain("consensus.signatureThreshold.min");
        result.ActionType.Should().Be(ControlActionType.ConfigUpdate);
    }

    [Fact]
    public async Task ApplyControlActionAsync_BlueprintPublish_ReturnsSuccessWithDescription()
    {
        // Arrange
        var payload = new
        {
            blueprintId = "bp-001",
            blueprintJson = "{\"title\":\"Test Blueprint\"}",
            publishedBy = "admin-001"
        };
        var tx = CreateTransaction("tx-001", "control.blueprint.publish", payload);
        var docket = CreateDocket([tx]);
        var controlTx = _processor.ExtractControlTransactions(docket)[0];

        // Act
        var result = await _processor.ApplyControlActionAsync(TestRegisterId, controlTx);

        // Assert
        result.Success.Should().BeTrue();
        result.ChangeDescription.Should().Contain("Blueprint bp-001 published");
    }

    [Fact]
    public async Task ApplyControlActionAsync_BlueprintPublishWithPreviousVersion_IncludesUpdateInfo()
    {
        // Arrange
        var payload = new
        {
            blueprintId = "bp-001",
            blueprintJson = "{\"title\":\"Updated Blueprint\"}",
            publishedBy = "admin-001",
            previousVersionId = "prev-version-001"
        };
        var tx = CreateTransaction("tx-001", "control.blueprint.publish", payload);
        var docket = CreateDocket([tx]);
        var controlTx = _processor.ExtractControlTransactions(docket)[0];

        // Act
        var result = await _processor.ApplyControlActionAsync(TestRegisterId, controlTx);

        // Assert
        result.Success.Should().BeTrue();
        result.ChangeDescription.Should().Contain("update from prev-version-001");
    }

    [Fact]
    public async Task ApplyControlActionAsync_MetadataUpdate_ReturnsSuccessWithDescription()
    {
        // Arrange
        var payload = new
        {
            field = "description",
            newValue = "Updated register description"
        };
        var tx = CreateTransaction("tx-001", "control.register.updateMetadata", payload);
        var docket = CreateDocket([tx]);
        var controlTx = _processor.ExtractControlTransactions(docket)[0];

        // Act
        var result = await _processor.ApplyControlActionAsync(TestRegisterId, controlTx);

        // Assert
        result.Success.Should().BeTrue();
        result.ChangeDescription.Should().Contain("description updated");
    }

    [Fact]
    public async Task ApplyControlActionAsync_CryptoPolicyUpdate_ReturnsSuccessWithDescription()
    {
        // Arrange
        var payload = new
        {
            version = 3,
            acceptedSignatureAlgorithms = new[] { "ED25519", "P-256", "RSA-4096" },
            requiredSignatureAlgorithms = new[] { "ED25519" },
            enforcementMode = "Strict"
        };
        var tx = CreateTransaction("tx-001", "control.crypto.update", payload);
        var docket = CreateDocket([tx]);
        var controlTx = _processor.ExtractControlTransactions(docket)[0];

        // Act
        var result = await _processor.ApplyControlActionAsync(TestRegisterId, controlTx);

        // Assert
        result.Success.Should().BeTrue();
        result.ChangeDescription.Should().Contain("Crypto policy updated");
        result.ChangeDescription.Should().Contain("version 3");
        result.ChangeDescription.Should().Contain("Strict");
        result.ChangeDescription.Should().Contain("3 accepted algorithms");
        result.ActionType.Should().Be(ControlActionType.CryptoPolicyUpdate);
    }

    [Fact]
    public async Task ApplyControlActionAsync_PolicyUpdate_ReturnsSuccessWithDescription()
    {
        // Arrange
        var payload = new
        {
            policy = new
            {
                version = 2u,
                validators = new
                {
                    registrationMode = "Public",
                    approvedValidators = Array.Empty<object>(),
                    minValidators = 2,
                    maxValidators = 20
                },
                consensus = new
                {
                    signatureThresholdMin = 2,
                    signatureThresholdMax = 10,
                    maxTransactionsPerDocket = 500
                },
                leaderElection = new
                {
                    mechanism = "Rotating"
                }
            },
            updatedBy = "admin-001"
        };
        var tx = CreateTransaction("tx-001", "control.policy.update", payload);
        var docket = CreateDocket([tx]);
        var controlTx = _processor.ExtractControlTransactions(docket)[0];

        // Act
        var result = await _processor.ApplyControlActionAsync(TestRegisterId, controlTx);

        // Assert
        result.Success.Should().BeTrue();
        result.ChangeDescription.Should().Contain("policy updated");
        result.ChangeDescription.Should().Contain("admin-001");
        result.ActionType.Should().Be(ControlActionType.PolicyUpdate);
    }

    [Fact]
    public async Task ApplyControlActionAsync_UnknownActionType_ReturnsFailure()
    {
        // Arrange - create a ControlTransaction with Unknown action type directly
        var payloadJson = JsonSerializer.Serialize(new { data = "test" });
        var payloadElement = JsonDocument.Parse(payloadJson).RootElement.Clone();
        var tx = new Transaction
        {
            TransactionId = "tx-001",
            RegisterId = TestRegisterId,
            BlueprintId = "control-blueprint",
            ActionId = "control.unknown.type",
            Payload = payloadElement,
            PayloadHash = "hash-tx-001",
            CreatedAt = DateTimeOffset.UtcNow,
            Signatures =
            [
                new Signature
                {
                    PublicKey = System.Text.Encoding.UTF8.GetBytes("test-pubkey"),
                    SignatureValue = System.Text.Encoding.UTF8.GetBytes("test-signature"),
                    Algorithm = "ED25519",
                    SignedAt = DateTimeOffset.UtcNow
                }
            ]
        };

        var controlTx = new ControlTransaction
        {
            Transaction = tx,
            ActionType = ControlActionType.Unknown,
            ActionId = "control.unknown.type",
            Payload = new TestControlPayload()
        };

        // Act
        var result = await _processor.ApplyControlActionAsync(TestRegisterId, controlTx);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Unknown");
    }

    [Fact]
    public async Task ApplyControlActionAsync_RaisesEventOnSuccess()
    {
        // Arrange
        ControlActionAppliedEventArgs? capturedArgs = null;
        _processor.ControlActionApplied += (sender, args) => capturedArgs = args;

        var payload = new
        {
            field = "name",
            newValue = "New Register Name"
        };
        var tx = CreateTransaction("tx-001", "control.register.updatemetadata", payload);
        var docket = CreateDocket([tx]);
        var controlTx = _processor.ExtractControlTransactions(docket)[0];

        // Act
        var result = await _processor.ApplyControlActionAsync(TestRegisterId, controlTx);

        // Assert
        result.Success.Should().BeTrue();
        capturedArgs.Should().NotBeNull();
        capturedArgs!.RegisterId.Should().Be(TestRegisterId);
        capturedArgs.TransactionId.Should().Be("tx-001");
        capturedArgs.ActionType.Should().Be(ControlActionType.RegisterUpdateMetadata);
        capturedArgs.AppliedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
        capturedArgs.ChangeDescription.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ApplyControlActionAsync_DoesNotRaiseEventOnFailure()
    {
        // Arrange
        ControlActionAppliedEventArgs? capturedArgs = null;
        _processor.ControlActionApplied += (sender, args) => capturedArgs = args;

        _mockValidatorRegistry
            .Setup(r => r.RegisterAsync(
                TestRegisterId,
                It.IsAny<ValidatorRegistration>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidatorRegistrationResult.Failed("Registration denied"));

        var payload = new
        {
            validatorId = TestValidatorId,
            publicKey = "pubkey-001",
            endpoint = "https://validator1.example.com"
        };
        var tx = CreateTransaction("tx-001", "control.validator.register", payload);
        var docket = CreateDocket([tx]);
        var controlTx = _processor.ExtractControlTransactions(docket)[0];

        // Act
        var result = await _processor.ApplyControlActionAsync(TestRegisterId, controlTx);

        // Assert
        result.Success.Should().BeFalse();
        capturedArgs.Should().BeNull();
    }

    #endregion

    #region Helper Methods

    private static Transaction CreateTransaction(string txId, string actionId, object payload)
    {
        var payloadJson = JsonSerializer.Serialize(payload);
        var payloadElement = JsonDocument.Parse(payloadJson).RootElement.Clone();

        return new Transaction
        {
            TransactionId = txId,
            RegisterId = TestRegisterId,
            BlueprintId = "control-blueprint",
            ActionId = actionId,
            Payload = payloadElement,
            PayloadHash = $"hash-{txId}",
            CreatedAt = DateTimeOffset.UtcNow,
            Signatures =
            [
                new Signature
                {
                    PublicKey = System.Text.Encoding.UTF8.GetBytes("test-pubkey"),
                    SignatureValue = System.Text.Encoding.UTF8.GetBytes("test-signature"),
                    Algorithm = "ED25519",
                    SignedAt = DateTimeOffset.UtcNow
                }
            ]
        };
    }

    private static Docket CreateDocket(List<Transaction> transactions)
    {
        return new Docket
        {
            DocketId = $"docket-{Guid.NewGuid():N}",
            RegisterId = TestRegisterId,
            DocketNumber = 1,
            DocketHash = "test-docket-hash",
            CreatedAt = DateTimeOffset.UtcNow,
            Transactions = transactions,
            ProposerValidatorId = "validator-001",
            ProposerSignature = new Signature
            {
                PublicKey = System.Text.Encoding.UTF8.GetBytes("proposer-pubkey"),
                SignatureValue = System.Text.Encoding.UTF8.GetBytes("proposer-signature"),
                Algorithm = "ED25519",
                SignedAt = DateTimeOffset.UtcNow
            },
            MerkleRoot = "test-merkle-root"
        };
    }

    private static object CreateValidPayloadForActionType(ControlActionType actionType)
    {
        return actionType switch
        {
            ControlActionType.ValidatorRegister => new
            {
                validatorId = TestValidatorId,
                publicKey = "pubkey-001",
                endpoint = "https://validator1.example.com"
            },
            ControlActionType.ValidatorApprove => new
            {
                validatorId = TestValidatorId,
                approvedBy = "admin-001"
            },
            ControlActionType.ValidatorSuspend => new
            {
                validatorId = TestValidatorId,
                suspendedBy = "admin-001",
                reason = "Maintenance"
            },
            ControlActionType.ValidatorRemove => new
            {
                validatorId = TestValidatorId,
                removedBy = "admin-001",
                reason = "Decommissioned"
            },
            ControlActionType.ConfigUpdate => new
            {
                path = "consensus.signatureThreshold.min",
                newValue = 3,
                reason = "Increase threshold"
            },
            ControlActionType.BlueprintPublish => new
            {
                blueprintId = "bp-001",
                blueprintJson = "{\"title\":\"Test\"}",
                publishedBy = "admin-001"
            },
            ControlActionType.RegisterUpdateMetadata => new
            {
                field = "name",
                newValue = "Updated Name"
            },
            ControlActionType.CryptoPolicyUpdate => (object)new
            {
                version = 2,
                acceptedSignatureAlgorithms = new[] { "ED25519", "P-256" },
                requiredSignatureAlgorithms = new[] { "ED25519" },
                enforcementMode = "Strict"
            },
            ControlActionType.PolicyUpdate => new
            {
                policy = new
                {
                    version = 2u,
                    validators = new { registrationMode = "Public", minValidators = 1, maxValidators = 50 },
                    consensus = new { signatureThresholdMin = 2, signatureThresholdMax = 5 },
                    leaderElection = new { mechanism = "Rotating" }
                },
                updatedBy = "admin-001"
            },
            _ => new { }
        };
    }

    /// <summary>
    /// Simple test control payload for constructing ControlTransaction with Unknown type
    /// </summary>
    private record TestControlPayload : ControlPayload;

    #endregion
}
