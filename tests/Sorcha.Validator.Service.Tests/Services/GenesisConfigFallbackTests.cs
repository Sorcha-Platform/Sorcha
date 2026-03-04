// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sorcha.Register.Models;
using Sorcha.ServiceClients.Register;
using StackExchange.Redis;
using Sorcha.Validator.Service.Configuration;
using Sorcha.Validator.Service.Services;
using Sorcha.Validator.Service.Services.Interfaces;

namespace Sorcha.Validator.Service.Tests.Services;

/// <summary>
/// Tests for GenesisConfigService three-tier fallback chain (Feature 048, US1):
/// Tier 1: RegisterPolicy on control record
/// Tier 2: Legacy controlBlueprint/configuration parsing
/// Tier 3: Hardcoded defaults
/// </summary>
public class GenesisConfigFallbackTests
{
    private readonly Mock<IConnectionMultiplexer> _redisMock;
    private readonly Mock<IDatabase> _databaseMock;
    private readonly Mock<IRegisterServiceClient> _registerClientMock;
    private readonly Mock<ILogger<GenesisConfigService>> _loggerMock;
    private readonly GenesisConfigService _service;

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public GenesisConfigFallbackTests()
    {
        _redisMock = new Mock<IConnectionMultiplexer>();
        _databaseMock = new Mock<IDatabase>();
        _registerClientMock = new Mock<IRegisterServiceClient>();
        _loggerMock = new Mock<ILogger<GenesisConfigService>>();

        var config = new GenesisConfigCacheConfiguration
        {
            KeyPrefix = "test:genesis:",
            DefaultTtl = TimeSpan.FromMinutes(30),
            StaleCheckInterval = TimeSpan.FromMinutes(5),
            EnableLocalCache = false,
            LocalCacheTtl = TimeSpan.FromMinutes(5),
            LocalCacheMaxEntries = 10
        };

        _redisMock.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(_databaseMock.Object);

        // Redis cache always misses — force fetch from source
        _databaseMock.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        _service = new GenesisConfigService(
            _redisMock.Object,
            _registerClientMock.Object,
            Options.Create(config),
            _loggerMock.Object);
    }

    [Fact]
    public async Task GetFullConfigAsync_WithRegisterPolicy_ReadsFromPolicy()
    {
        // Arrange — Tier 1: RegisterPolicy present on control record
        var registerId = "aabbccdd11223344aabbccdd11223344";
        var controlRecord = new RegisterControlRecord
        {
            RegisterId = registerId,
            Name = "PolicyTest",
            TenantId = "tenant-1",
            RegisterPolicy = new RegisterPolicy
            {
                Version = 1,
                Governance = new PolicyGovernanceConfig { QuorumFormula = QuorumFormula.Supermajority },
                Validators = new PolicyValidatorConfig
                {
                    RegistrationMode = RegistrationMode.Consent,
                    MinValidators = 3,
                    MaxValidators = 50,
                    OperationalTtlSeconds = 120
                },
                Consensus = new PolicyConsensusConfig
                {
                    SignatureThresholdMin = 3,
                    SignatureThresholdMax = 7,
                    MaxTransactionsPerDocket = 500,
                    DocketBuildIntervalMs = 200,
                    DocketTimeoutSeconds = 60
                },
                LeaderElection = new PolicyLeaderElectionConfig
                {
                    Mechanism = ElectionMechanism.Rotating,
                    HeartbeatIntervalMs = 2000,
                    LeaderTimeoutMs = 10000,
                    TermDurationSeconds = 120
                }
            },
            Attestations = new List<RegisterAttestation>()
        };

        SetupGenesisTransaction(registerId, controlRecord);

        // Act
        var config = await _service.GetFullConfigAsync(registerId);

        // Assert — values should come from RegisterPolicy, not defaults
        config.RegisterId.Should().Be(registerId);
        config.Consensus.SignatureThresholdMin.Should().Be(3);
        config.Consensus.SignatureThresholdMax.Should().Be(7);
        config.Consensus.MaxTransactionsPerDocket.Should().Be(500);
        config.Consensus.DocketBuildInterval.Should().Be(TimeSpan.FromMilliseconds(200));
        config.Consensus.DocketTimeout.Should().Be(TimeSpan.FromSeconds(60));
        config.Validators.RegistrationMode.Should().Be("consent");
        config.Validators.MinValidators.Should().Be(3);
        config.Validators.MaxValidators.Should().Be(50);
        config.LeaderElection.Mechanism.Should().Be("rotating");
        config.LeaderElection.HeartbeatInterval.Should().Be(TimeSpan.FromMilliseconds(2000));
        config.LeaderElection.LeaderTimeout.Should().Be(TimeSpan.FromMilliseconds(10000));
        config.LeaderElection.TermDuration.Should().Be(TimeSpan.FromSeconds(120));
    }

    [Fact]
    public async Task GetFullConfigAsync_WithoutRegisterPolicy_FallsBackToDefaults()
    {
        // Arrange — Tier 3: No RegisterPolicy, no legacy config → defaults
        var registerId = "aabbccdd11223344aabbccdd11223344";
        var controlRecord = new RegisterControlRecord
        {
            RegisterId = registerId,
            Name = "LegacyRegister",
            TenantId = "tenant-1",
            // RegisterPolicy is null (pre-Feature 048 register)
            Attestations = new List<RegisterAttestation>()
        };

        SetupGenesisTransaction(registerId, controlRecord);

        // Act
        var config = await _service.GetFullConfigAsync(registerId);

        // Assert — should get hardcoded defaults
        config.RegisterId.Should().Be(registerId);
        config.Consensus.SignatureThresholdMin.Should().Be(2);
        config.Consensus.SignatureThresholdMax.Should().Be(10);
        config.Consensus.MaxTransactionsPerDocket.Should().Be(1000);
        config.Validators.RegistrationMode.Should().Be("public");
        config.Validators.MinValidators.Should().Be(1);
        config.Validators.MaxValidators.Should().Be(100);
        config.LeaderElection.Mechanism.Should().Be("rotating");
    }

    [Fact]
    public async Task GetFullConfigAsync_NoGenesisDocket_FallsBackToDefaults()
    {
        // Arrange — no genesis docket at all
        var registerId = "aabbccdd11223344aabbccdd11223344";

        _registerClientMock
            .Setup(c => c.GetRegisterAsync(registerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sorcha.Register.Models.Register { Id = registerId, Name = "Test" });

        _registerClientMock
            .Setup(c => c.ReadDocketAsync(registerId, 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DocketModel?)null);

        // Act
        var config = await _service.GetFullConfigAsync(registerId);

        // Assert — defaults
        config.Consensus.SignatureThresholdMin.Should().Be(2);
        config.Validators.RegistrationMode.Should().Be("public");
        config.LeaderElection.Mechanism.Should().Be("rotating");
    }

    [Fact]
    public async Task GetFullConfigAsync_RegisterNotFound_FallsBackToDefaults()
    {
        // Arrange — register doesn't exist
        var registerId = "aabbccdd11223344aabbccdd11223344";

        _registerClientMock
            .Setup(c => c.GetRegisterAsync(registerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Sorcha.Register.Models.Register?)null);

        // Act
        var config = await _service.GetFullConfigAsync(registerId);

        // Assert — defaults
        config.Consensus.SignatureThresholdMin.Should().Be(2);
        config.Validators.RegistrationMode.Should().Be("public");
    }

    [Fact]
    public async Task GetFullConfigAsync_RegisterPolicy_EnumValuesSerializedCorrectly()
    {
        // Arrange — verify enum values serialize/deserialize correctly (int values)
        var registerId = "aabbccdd11223344aabbccdd11223344";
        var controlRecord = new RegisterControlRecord
        {
            RegisterId = registerId,
            Name = "EnumTest",
            TenantId = "tenant-1",
            RegisterPolicy = new RegisterPolicy
            {
                Version = 2,
                Governance = new PolicyGovernanceConfig { QuorumFormula = QuorumFormula.Unanimous },
                Validators = new PolicyValidatorConfig { RegistrationMode = RegistrationMode.Public },
                Consensus = new PolicyConsensusConfig(),
                LeaderElection = new PolicyLeaderElectionConfig { Mechanism = ElectionMechanism.Raft }
            },
            Attestations = new List<RegisterAttestation>()
        };

        SetupGenesisTransaction(registerId, controlRecord);

        // Act
        var config = await _service.GetFullConfigAsync(registerId);

        // Assert
        config.LeaderElection.Mechanism.Should().Be("raft");
        config.Validators.RegistrationMode.Should().Be("public");
        config.ControlBlueprintVersionId.Should().Be("policy-v2");
    }

    /// <summary>
    /// Sets up mock to return a genesis transaction with the given control record as payload
    /// </summary>
    private void SetupGenesisTransaction(string registerId, RegisterControlRecord controlRecord)
    {
        var register = new Sorcha.Register.Models.Register
        {
            Id = registerId,
            Name = controlRecord.Name,
            TenantId = controlRecord.TenantId
        };

        _registerClientMock
            .Setup(c => c.GetRegisterAsync(registerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(register);

        // Serialize control record as the genesis transaction payload
        var payloadJson = JsonSerializer.Serialize(controlRecord, _jsonOptions);

        var genesisTransaction = new TransactionModel
        {
            TxId = $"genesis-{registerId}",
            RegisterId = registerId,
            Payloads = new[]
            {
                new PayloadModel
                {
                    Data = payloadJson,
                    ContentType = "application/json"
                }
            }
        };

        var genesisDocket = new DocketModel
        {
            DocketId = "0",
            RegisterId = registerId,
            DocketNumber = 0,
            DocketHash = "genesis-hash",
            CreatedAt = DateTimeOffset.UtcNow,
            Transactions = new List<TransactionModel> { genesisTransaction },
            ProposerValidatorId = "system",
            MerkleRoot = "genesis-merkle"
        };

        _registerClientMock
            .Setup(c => c.ReadDocketAsync(registerId, 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(genesisDocket);
    }
}
