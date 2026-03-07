// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using StackExchange.Redis;
using Sorcha.Register.Models;
using Sorcha.ServiceClients.Register;
using Sorcha.Validator.Service.Configuration;
using Sorcha.Validator.Service.Services;
using Sorcha.Validator.Service.Services.Interfaces;

namespace Sorcha.Validator.Service.Tests.Services;

/// <summary>
/// Unit tests for ValidatorRegistry consent-mode registration gating (Feature 048, US3):
/// - Approved validator → allowed to register
/// - Unapproved validator → rejected
/// - Public mode → no check (all validators allowed)
/// </summary>
public class ValidatorRegistryConsentTests
{
    private readonly Mock<IConnectionMultiplexer> _redisMock;
    private readonly Mock<IDatabase> _databaseMock;
    private readonly Mock<IServer> _serverMock;
    private readonly Mock<IRegisterServiceClient> _registerClientMock;
    private readonly Mock<IGenesisConfigService> _genesisConfigMock;
    private readonly Mock<ILogger<ValidatorRegistry>> _loggerMock;
    private readonly ValidatorRegistryConfiguration _config;
    private readonly ValidatorRegistry _registry;

    private const string TestRegisterId = "aabbccdd11223344aabbccdd11223344";
    private const string ApprovedValidatorDid = "did:sorcha:approved-validator-1";
    private const string ApprovedPublicKey = "dGVzdC1wdWJsaWMta2V5LTE="; // base64
    private const string UnapprovedValidatorDid = "did:sorcha:unapproved-validator-1";
    private const string UnapprovedPublicKey = "dGVzdC1wdWJsaWMta2V5LTI=";

    public ValidatorRegistryConsentTests()
    {
        _redisMock = new Mock<IConnectionMultiplexer>();
        _databaseMock = new Mock<IDatabase>();
        _serverMock = new Mock<IServer>();
        _registerClientMock = new Mock<IRegisterServiceClient>();
        _genesisConfigMock = new Mock<IGenesisConfigService>();
        _loggerMock = new Mock<ILogger<ValidatorRegistry>>();

        _config = new ValidatorRegistryConfiguration
        {
            KeyPrefix = "test:validators:",
            CacheTtl = TimeSpan.FromMinutes(30),
            LocalCacheTtl = TimeSpan.FromMinutes(5),
            EnableLocalCache = false,
            LocalCacheMaxEntries = 10,
            MaxRetries = 1,
            RetryDelay = TimeSpan.FromMilliseconds(10)
        };

        _redisMock.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(_databaseMock.Object);

        _redisMock.Setup(r => r.GetEndPoints(It.IsAny<bool>()))
            .Returns([new IPEndPoint(IPAddress.Loopback, 6379)]);

        _redisMock.Setup(r => r.GetServer(It.IsAny<EndPoint>(), It.IsAny<object>()))
            .Returns(_serverMock.Object);

        // Default: no existing validators in Redis
        _databaseMock.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        // Default: no keys found on scan
        _serverMock.Setup(s => s.KeysAsync(
                It.IsAny<int>(), It.IsAny<RedisValue>(), It.IsAny<int>(),
                It.IsAny<long>(), It.IsAny<int>(), It.IsAny<CommandFlags>()))
            .Returns(EmptyAsyncEnumerable());

        // Default: Redis writes succeed
        _databaseMock.Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(), It.IsAny<bool>(),
                It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        _databaseMock.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey[]>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(1);

        _databaseMock.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        // Default: SubmitTransactionAsync succeeds
        _registerClientMock
            .Setup(c => c.SubmitTransactionAsync(
                It.IsAny<string>(), It.IsAny<TransactionModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionModel { TxId = "tx-consent-test" });

        _registry = new ValidatorRegistry(
            _redisMock.Object,
            _registerClientMock.Object,
            _genesisConfigMock.Object,
            Options.Create(_config),
            _loggerMock.Object);
    }

    [Fact]
    public async Task RegisterAsync_ConsentMode_ApprovedValidator_Succeeds()
    {
        // Arrange — consent mode with approved validator on the list
        SetupConsentMode();
        SetupApprovedValidatorsList(ApprovedValidatorDid, ApprovedPublicKey);

        var registration = new ValidatorRegistration
        {
            ValidatorId = ApprovedValidatorDid,
            PublicKey = ApprovedPublicKey,
            GrpcEndpoint = "https://validator1:5001"
        };

        // Act
        var result = await _registry.RegisterAsync(TestRegisterId, registration);

        // Assert
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task RegisterAsync_ConsentMode_UnapprovedValidator_Rejected()
    {
        // Arrange — consent mode, validator NOT on the approved list
        SetupConsentMode();
        SetupApprovedValidatorsList(ApprovedValidatorDid, ApprovedPublicKey);

        var registration = new ValidatorRegistration
        {
            ValidatorId = UnapprovedValidatorDid,
            PublicKey = UnapprovedPublicKey,
            GrpcEndpoint = "https://validator2:5001"
        };

        // Act
        var result = await _registry.RegisterAsync(TestRegisterId, registration);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not on the approved list");
    }

    [Fact]
    public async Task RegisterAsync_PublicMode_AnyValidator_Succeeds()
    {
        // Arrange — public mode, no consent check needed
        SetupPublicMode();

        var registration = new ValidatorRegistration
        {
            ValidatorId = UnapprovedValidatorDid,
            PublicKey = UnapprovedPublicKey,
            GrpcEndpoint = "https://validator2:5001"
        };

        // Act
        var result = await _registry.RegisterAsync(TestRegisterId, registration);

        // Assert
        result.Success.Should().BeTrue();
        // Note: GetRegisterPolicyAsync may be called by ResolveOperationalTtlAsync for TTL resolution,
        // but it should NOT be called for consent-mode gating in public mode.
        // We verify consent check was skipped by confirming registration succeeded
        // without setting up an approved validators list.
    }

    [Fact]
    public async Task RegisterAsync_ConsentMode_EmptyApprovedList_RejectsAll()
    {
        // Arrange — consent mode with empty approved list
        SetupConsentMode();
        SetupEmptyApprovedValidatorsList();

        var registration = new ValidatorRegistration
        {
            ValidatorId = ApprovedValidatorDid,
            PublicKey = ApprovedPublicKey,
            GrpcEndpoint = "https://validator1:5001"
        };

        // Act
        var result = await _registry.RegisterAsync(TestRegisterId, registration);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not on the approved list");
    }

    [Fact]
    public async Task RegisterAsync_ConsentMode_MatchByPublicKey_Succeeds()
    {
        // Arrange — approved list has a different DID but same public key
        SetupConsentMode();
        SetupApprovedValidatorsList("did:sorcha:different-did", ApprovedPublicKey);

        var registration = new ValidatorRegistration
        {
            ValidatorId = "did:sorcha:my-validator",
            PublicKey = ApprovedPublicKey, // matches by public key
            GrpcEndpoint = "https://validator1:5001"
        };

        // Act
        var result = await _registry.RegisterAsync(TestRegisterId, registration);

        // Assert
        result.Success.Should().BeTrue();
    }

    private void SetupConsentMode()
    {
        _genesisConfigMock
            .Setup(g => g.GetValidatorConfigAsync(TestRegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidatorConfig
            {
                RegistrationMode = "consent",
                MinValidators = 1,
                MaxValidators = 100,
                RequireStake = false
            });
    }

    private void SetupPublicMode()
    {
        _genesisConfigMock
            .Setup(g => g.GetValidatorConfigAsync(TestRegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidatorConfig
            {
                RegistrationMode = "public",
                MinValidators = 1,
                MaxValidators = 100,
                RequireStake = false
            });
    }

    private void SetupApprovedValidatorsList(string approvedDid, string approvedKey)
    {
        _registerClientMock
            .Setup(c => c.GetRegisterPolicyAsync(TestRegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RegisterPolicyResponse
            {
                RegisterId = TestRegisterId,
                Policy = new RegisterPolicy
                {
                    Version = 1,
                    Validators = new PolicyValidatorConfig
                    {
                        RegistrationMode = RegistrationMode.Consent,
                        ApprovedValidators =
                        [
                            new ApprovedValidator
                            {
                                Did = approvedDid,
                                PublicKey = approvedKey,
                                ApprovedAt = DateTimeOffset.UtcNow.AddDays(-1)
                            }
                        ]
                    }
                },
                IsDefault = false
            });
    }

    private void SetupEmptyApprovedValidatorsList()
    {
        _registerClientMock
            .Setup(c => c.GetRegisterPolicyAsync(TestRegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RegisterPolicyResponse
            {
                RegisterId = TestRegisterId,
                Policy = new RegisterPolicy
                {
                    Version = 1,
                    Validators = new PolicyValidatorConfig
                    {
                        RegistrationMode = RegistrationMode.Consent,
                        ApprovedValidators = []
                    }
                },
                IsDefault = false
            });
    }

    private static async IAsyncEnumerable<RedisKey> EmptyAsyncEnumerable()
    {
        await Task.CompletedTask;
        yield break;
    }
}
