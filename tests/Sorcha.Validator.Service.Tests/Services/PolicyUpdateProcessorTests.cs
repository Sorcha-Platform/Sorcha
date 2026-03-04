// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sorcha.Register.Models;
using Sorcha.Validator.Service.Models;
using Sorcha.Validator.Service.Services;
using Sorcha.Validator.Service.Services.Interfaces;
using Docket = Sorcha.Validator.Service.Models.Docket;
using Transaction = Sorcha.Validator.Service.Models.Transaction;
using Signature = Sorcha.Validator.Service.Models.Signature;

namespace Sorcha.Validator.Service.Tests.Services;

/// <summary>
/// Unit tests for control.policy.update processing in ControlDocketProcessor.
/// Covers valid updates, version conflicts, validation failures, and transition enforcement.
/// </summary>
public class PolicyUpdateProcessorTests
{
    private readonly Mock<IGenesisConfigService> _mockGenesisConfig;
    private readonly Mock<IControlBlueprintVersionResolver> _mockVersionResolver;
    private readonly Mock<IValidatorRegistry> _mockValidatorRegistry;
    private readonly Mock<ILogger<ControlDocketProcessor>> _mockLogger;
    private readonly ControlDocketProcessor _processor;

    private const string TestRegisterId = "test-register-001";

    public PolicyUpdateProcessorTests()
    {
        _mockGenesisConfig = new Mock<IGenesisConfigService>();
        _mockVersionResolver = new Mock<IControlBlueprintVersionResolver>();
        _mockValidatorRegistry = new Mock<IValidatorRegistry>();
        _mockLogger = new Mock<ILogger<ControlDocketProcessor>>();

        _processor = new ControlDocketProcessor(
            _mockGenesisConfig.Object,
            _mockVersionResolver.Object,
            _mockValidatorRegistry.Object,
            _mockLogger.Object);

        SetupDefaultMocks();
    }

    private void SetupDefaultMocks()
    {
        _mockGenesisConfig
            .Setup(s => s.GetFullConfigAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDefaultGenesisConfig());

        _mockValidatorRegistry
            .Setup(r => r.GetActiveCountAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);
    }

    #region Extraction Tests

    [Fact]
    public void ExtractControlTransactions_PolicyUpdate_ExtractsCorrectly()
    {
        // Arrange
        var payload = CreateValidPolicyUpdatePayload();
        var tx = CreateTransaction("tx-001", "control.policy.update", payload);
        var docket = CreateDocket([tx]);

        // Act
        var result = _processor.ExtractControlTransactions(docket);

        // Assert
        result.Should().HaveCount(1);
        result[0].ActionType.Should().Be(ControlActionType.PolicyUpdate);
        result[0].ActionId.Should().Be("control.policy.update");
    }

    [Fact]
    public void ExtractControlTransactions_PolicyUpdate_ParsesPayload()
    {
        // Arrange
        var payload = CreateValidPolicyUpdatePayload();
        var tx = CreateTransaction("tx-001", "control.policy.update", payload);
        var docket = CreateDocket([tx]);

        // Act
        var result = _processor.ExtractControlTransactions(docket);

        // Assert
        var controlTx = result[0];
        controlTx.Payload.Should().BeOfType<PolicyUpdatePayload>();
        var policyPayload = (PolicyUpdatePayload)controlTx.Payload;
        policyPayload.Policy.Version.Should().Be(2);
        policyPayload.UpdatedBy.Should().Be("did:sorcha:admin-001");
    }

    #endregion

    #region Validation Tests

    [Fact]
    public async Task ValidateControlTransactions_ValidPolicyUpdate_ReturnsSuccess()
    {
        // Arrange
        var payload = CreateValidPolicyUpdatePayload();
        var tx = CreateTransaction("tx-001", "control.policy.update", payload);
        var docket = CreateDocket([tx]);
        var controlTransactions = _processor.ExtractControlTransactions(docket);

        // Act
        var result = await _processor.ValidateControlTransactionsAsync(TestRegisterId, controlTransactions);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateControlTransactions_MissingPolicy_ReturnsFailure()
    {
        // Arrange
        var payload = new { updatedBy = "did:sorcha:admin-001" };
        var tx = CreateTransaction("tx-001", "control.policy.update", payload);
        var docket = CreateDocket([tx]);
        var controlTransactions = _processor.ExtractControlTransactions(docket);

        // Act
        var result = await _processor.ValidateControlTransactionsAsync(TestRegisterId, controlTransactions);

        // Assert
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateControlTransactions_MissingUpdatedBy_ReturnsFailure()
    {
        // Arrange
        var policy = RegisterPolicy.CreateDefault();
        policy.Version = 2;
        var payload = new { policy, updatedBy = "" };
        var tx = CreateTransaction("tx-001", "control.policy.update", payload);
        var docket = CreateDocket([tx]);
        var controlTransactions = _processor.ExtractControlTransactions(docket);

        // Act
        var result = await _processor.ValidateControlTransactionsAsync(TestRegisterId, controlTransactions);

        // Assert
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateControlTransactions_InvalidVersion_ReturnsFailure()
    {
        // Arrange - version 0 is invalid (must be >= 1)
        var policy = RegisterPolicy.CreateDefault();
        policy.Version = 0;
        var payload = new { policy, updatedBy = "did:sorcha:admin-001" };
        var tx = CreateTransaction("tx-001", "control.policy.update", payload);
        var docket = CreateDocket([tx]);
        var controlTransactions = _processor.ExtractControlTransactions(docket);

        // Act
        var result = await _processor.ValidateControlTransactionsAsync(TestRegisterId, controlTransactions);

        // Assert
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateControlTransactions_PublicToConsentWithoutTransitionMode_ReturnsFailure()
    {
        // Arrange - changing from public to consent requires TransitionMode
        var policy = RegisterPolicy.CreateDefault();
        policy.Version = 2;
        policy.Validators.RegistrationMode = RegistrationMode.Consent;
        var payload = new { policy, updatedBy = "did:sorcha:admin-001" };
        var tx = CreateTransaction("tx-001", "control.policy.update", payload);
        var docket = CreateDocket([tx]);
        var controlTransactions = _processor.ExtractControlTransactions(docket);

        // Act
        var result = await _processor.ValidateControlTransactionsAsync(TestRegisterId, controlTransactions);

        // Assert
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateControlTransactions_PublicToConsentWithTransitionMode_ReturnsSuccess()
    {
        // Arrange
        var policy = RegisterPolicy.CreateDefault();
        policy.Version = 2;
        policy.Validators.RegistrationMode = RegistrationMode.Consent;
        var payload = new
        {
            policy,
            transitionMode = "Immediate",
            updatedBy = "did:sorcha:admin-001"
        };
        var tx = CreateTransaction("tx-001", "control.policy.update", payload);
        var docket = CreateDocket([tx]);
        var controlTransactions = _processor.ExtractControlTransactions(docket);

        // Act
        var result = await _processor.ValidateControlTransactionsAsync(TestRegisterId, controlTransactions);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateControlTransactions_MaxValidatorsLessThanMin_ReturnsFailure()
    {
        // Arrange
        var policy = RegisterPolicy.CreateDefault();
        policy.Version = 2;
        policy.Validators.MinValidators = 5;
        policy.Validators.MaxValidators = 3; // invalid: max < min
        var payload = new { policy, updatedBy = "did:sorcha:admin-001" };
        var tx = CreateTransaction("tx-001", "control.policy.update", payload);
        var docket = CreateDocket([tx]);
        var controlTransactions = _processor.ExtractControlTransactions(docket);

        // Act
        var result = await _processor.ValidateControlTransactionsAsync(TestRegisterId, controlTransactions);

        // Assert
        result.IsValid.Should().BeFalse();
    }

    #endregion

    #region Apply Tests

    [Fact]
    public async Task ApplyControlAction_PolicyUpdate_ReturnsSuccess()
    {
        // Arrange
        var payload = CreateValidPolicyUpdatePayload();
        var tx = CreateTransaction("tx-001", "control.policy.update", payload);
        var docket = CreateDocket([tx]);
        var controlTx = _processor.ExtractControlTransactions(docket)[0];

        // Act
        var result = await _processor.ApplyControlActionAsync(TestRegisterId, controlTx);

        // Assert
        result.Success.Should().BeTrue();
        result.ActionType.Should().Be(ControlActionType.PolicyUpdate);
        result.ChangeDescription.Should().Contain("version 2");
    }

    [Fact]
    public async Task ApplyControlAction_PolicyUpdateWithTransition_CallsEnforceTransition()
    {
        // Arrange
        var policy = RegisterPolicy.CreateDefault();
        policy.Version = 2;
        policy.Validators.RegistrationMode = RegistrationMode.Consent;
        policy.Validators.ApprovedValidators =
        [
            new ApprovedValidator { Did = "validator-001", PublicKey = "pk1", ApprovedAt = DateTimeOffset.UtcNow }
        ];
        var payload = new
        {
            policy,
            transitionMode = "Immediate",
            updatedBy = "did:sorcha:admin-001"
        };
        var tx = CreateTransaction("tx-001", "control.policy.update", payload);
        var docket = CreateDocket([tx]);
        var controlTx = _processor.ExtractControlTransactions(docket)[0];

        _mockValidatorRegistry
            .Setup(r => r.EnforceRegistrationModeTransitionAsync(
                TestRegisterId,
                It.IsAny<IReadOnlyList<ApprovedValidator>>(),
                TransitionMode.Immediate,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);

        // Act
        var result = await _processor.ApplyControlActionAsync(TestRegisterId, controlTx);

        // Assert
        result.Success.Should().BeTrue();
        result.ChangeDescription.Should().Contain("2 validators affected");
        _mockValidatorRegistry.Verify(r => r.EnforceRegistrationModeTransitionAsync(
            TestRegisterId,
            It.IsAny<IReadOnlyList<ApprovedValidator>>(),
            TransitionMode.Immediate,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ApplyControlAction_PolicyUpdateWithoutTransition_DoesNotCallEnforce()
    {
        // Arrange - staying on public mode, no transition needed
        var payload = CreateValidPolicyUpdatePayload();
        var tx = CreateTransaction("tx-001", "control.policy.update", payload);
        var docket = CreateDocket([tx]);
        var controlTx = _processor.ExtractControlTransactions(docket)[0];

        // Act
        var result = await _processor.ApplyControlActionAsync(TestRegisterId, controlTx);

        // Assert
        result.Success.Should().BeTrue();
        _mockValidatorRegistry.Verify(r => r.EnforceRegistrationModeTransitionAsync(
            It.IsAny<string>(),
            It.IsAny<IReadOnlyList<ApprovedValidator>>(),
            It.IsAny<TransitionMode>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessCommittedDocket_PolicyUpdate_RefreshesConfig()
    {
        // Arrange
        var payload = CreateValidPolicyUpdatePayload();
        var tx = CreateTransaction("tx-001", "control.policy.update", payload);
        var docket = CreateDocket([tx]);

        // Act
        var result = await _processor.ProcessCommittedDocketAsync(TestRegisterId, docket);

        // Assert
        result.Success.Should().BeTrue();
        result.ConfigurationUpdated.Should().BeTrue();
        _mockGenesisConfig.Verify(
            g => g.RefreshConfigAsync(TestRegisterId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region Helper Methods

    private static object CreateValidPolicyUpdatePayload()
    {
        var policy = RegisterPolicy.CreateDefault();
        policy.Version = 2;
        return new
        {
            policy,
            updatedBy = "did:sorcha:admin-001"
        };
    }

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

    private static GenesisConfiguration CreateDefaultGenesisConfig()
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
                HeartbeatInterval = TimeSpan.FromSeconds(1),
                LeaderTimeout = TimeSpan.FromSeconds(5)
            },
            LoadedAt = DateTimeOffset.UtcNow,
            CacheTtl = TimeSpan.FromMinutes(5)
        };
    }

    #endregion
}
