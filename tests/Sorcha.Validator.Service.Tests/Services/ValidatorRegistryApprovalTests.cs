// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using StackExchange.Redis;
using Sorcha.ServiceClients.Register;
using Sorcha.Validator.Service.Configuration;
using Sorcha.Validator.Service.Services;
using Sorcha.Validator.Service.Services.Interfaces;
using ValidatorStatus = Sorcha.Validator.Service.Services.Interfaces.ValidatorStatus;

namespace Sorcha.Validator.Service.Tests.Services;

/// <summary>
/// Unit tests for ValidatorRegistry approval flow (VAL-9.39)
/// Tests consent mode validator approval and rejection functionality
/// </summary>
public class ValidatorRegistryApprovalTests
{
    private readonly Mock<IConnectionMultiplexer> _redisMock;
    private readonly Mock<IDatabase> _databaseMock;
    private readonly Mock<IServer> _serverMock;
    private readonly Mock<IRegisterServiceClient> _registerClientMock;
    private readonly Mock<IGenesisConfigService> _genesisConfigMock;
    private readonly Mock<ILogger<ValidatorRegistry>> _loggerMock;
    private readonly ValidatorRegistryConfiguration _config;
    private readonly ValidatorRegistry _registry;
    private readonly JsonSerializerOptions _jsonOptions;

    private const string TestRegisterId = "test-register-1";
    private const string TestValidatorId = "0x1234567890abcdef";
    private const string TestApprover = "0xowner123";

    public ValidatorRegistryApprovalTests()
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
            EnableLocalCache = false, // Disable local cache for tests
            LocalCacheMaxEntries = 10,
            MaxRetries = 3,
            RetryDelay = TimeSpan.FromMilliseconds(100)
        };

        _redisMock.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(_databaseMock.Object);

        _redisMock.Setup(r => r.GetEndPoints(It.IsAny<bool>()))
            .Returns([new IPEndPoint(IPAddress.Loopback, 6379)]);

        _redisMock.Setup(r => r.GetServer(It.IsAny<EndPoint>(), It.IsAny<object>()))
            .Returns(_serverMock.Object);

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        _registry = new ValidatorRegistry(
            _redisMock.Object,
            _registerClientMock.Object,
            _genesisConfigMock.Object,
            Options.Create(_config),
            _loggerMock.Object);
    }

    #region GetPendingValidatorsAsync Tests

    [Fact]
    public async Task GetPendingValidatorsAsync_WithNullRegisterId_ThrowsArgumentException()
    {
        // Act
        var act = () => _registry.GetPendingValidatorsAsync(null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetPendingValidatorsAsync_WithEmptyRegisterId_ThrowsArgumentException()
    {
        // Act
        var act = () => _registry.GetPendingValidatorsAsync("");

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetPendingValidatorsAsync_NoPendingValidators_ReturnsEmptyList()
    {
        // Arrange
        _databaseMock.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        _serverMock.Setup(s => s.KeysAsync(
                It.IsAny<int>(),
                It.IsAny<RedisValue>(),
                It.IsAny<int>(),
                It.IsAny<long>(),
                It.IsAny<int>(),
                It.IsAny<CommandFlags>()))
            .Returns(AsyncEnumerable.Empty<RedisKey>());

        // Act
        var result = await _registry.GetPendingValidatorsAsync(TestRegisterId);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPendingValidatorsAsync_WithPendingValidators_ReturnsPendingOnly()
    {
        // Arrange
        var validators = new List<ValidatorInfo>
        {
            CreateValidatorInfo("val-1", ValidatorStatus.Pending),
            CreateValidatorInfo("val-2", ValidatorStatus.Active),
            CreateValidatorInfo("val-3", ValidatorStatus.Pending),
            CreateValidatorInfo("val-4", ValidatorStatus.Suspended)
        };

        var json = JsonSerializer.Serialize(validators, _jsonOptions);
        _databaseMock.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(json);

        // Act
        var result = await _registry.GetPendingValidatorsAsync(TestRegisterId);

        // Assert
        result.Should().HaveCount(2);
        result.All(v => v.Status == ValidatorStatus.Pending).Should().BeTrue();
        result.Select(v => v.ValidatorId).Should().Contain(["val-1", "val-3"]);
    }

    [Fact]
    public async Task GetPendingValidatorsAsync_OrdersByRegisteredAt()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;
        var validators = new List<ValidatorInfo>
        {
            CreateValidatorInfo("val-late", ValidatorStatus.Pending, now),
            CreateValidatorInfo("val-early", ValidatorStatus.Pending, now.AddHours(-2)),
            CreateValidatorInfo("val-middle", ValidatorStatus.Pending, now.AddHours(-1))
        };

        var json = JsonSerializer.Serialize(validators, _jsonOptions);
        _databaseMock.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(json);

        // Act
        var result = await _registry.GetPendingValidatorsAsync(TestRegisterId);

        // Assert
        result.Should().HaveCount(3);
        result[0].ValidatorId.Should().Be("val-early");
        result[1].ValidatorId.Should().Be("val-middle");
        result[2].ValidatorId.Should().Be("val-late");
    }

    #endregion

    #region ApproveValidatorAsync Tests

    [Fact]
    public async Task ApproveValidatorAsync_WithNullRegisterId_ThrowsArgumentException()
    {
        // Arrange
        var request = new ValidatorApprovalRequest
        {
            ValidatorId = TestValidatorId,
            ApprovedBy = TestApprover
        };

        // Act
        var act = () => _registry.ApproveValidatorAsync(null!, request);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ApproveValidatorAsync_WithNullRequest_ThrowsArgumentNullException()
    {
        // Act
        var act = () => _registry.ApproveValidatorAsync(TestRegisterId, null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ApproveValidatorAsync_InPublicMode_ReturnsFailure()
    {
        // Arrange
        SetupGenesisConfig(isPublicRegistration: true);

        var request = new ValidatorApprovalRequest
        {
            ValidatorId = TestValidatorId,
            ApprovedBy = TestApprover
        };

        // Act
        var result = await _registry.ApproveValidatorAsync(TestRegisterId, request);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("public registration mode");
    }

    [Fact]
    public async Task ApproveValidatorAsync_ValidatorNotFound_ReturnsFailure()
    {
        // Arrange
        SetupGenesisConfig(isPublicRegistration: false);
        SetupEmptyValidatorCache();

        var request = new ValidatorApprovalRequest
        {
            ValidatorId = "nonexistent",
            ApprovedBy = TestApprover
        };

        // Act
        var result = await _registry.ApproveValidatorAsync(TestRegisterId, request);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not found");
    }

    [Fact]
    public async Task ApproveValidatorAsync_ValidatorNotPending_ReturnsFailure()
    {
        // Arrange
        SetupGenesisConfig(isPublicRegistration: false);
        SetupValidatorInCache(TestValidatorId, ValidatorStatus.Active);

        var request = new ValidatorApprovalRequest
        {
            ValidatorId = TestValidatorId,
            ApprovedBy = TestApprover
        };

        // Act
        var result = await _registry.ApproveValidatorAsync(TestRegisterId, request);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not pending");
    }

    [Fact]
    public async Task ApproveValidatorAsync_MaxValidatorsReached_ReturnsFailure()
    {
        // Arrange
        SetupGenesisConfig(isPublicRegistration: false, maxValidators: 3);
        SetupValidatorInCache(TestValidatorId, ValidatorStatus.Pending);

        // Setup 3 active validators already
        var activeValidators = new List<ValidatorInfo>
        {
            CreateValidatorInfo("v1", ValidatorStatus.Active),
            CreateValidatorInfo("v2", ValidatorStatus.Active),
            CreateValidatorInfo("v3", ValidatorStatus.Active),
            CreateValidatorInfo(TestValidatorId, ValidatorStatus.Pending)
        };
        SetupValidatorsInCache(activeValidators);

        var request = new ValidatorApprovalRequest
        {
            ValidatorId = TestValidatorId,
            ApprovedBy = TestApprover
        };

        // Act
        var result = await _registry.ApproveValidatorAsync(TestRegisterId, request);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Maximum validators");
    }

    [Fact]
    public async Task ApproveValidatorAsync_ValidPendingValidator_ReturnsSuccess()
    {
        // Arrange
        SetupGenesisConfig(isPublicRegistration: false, maxValidators: 10);
        SetupValidatorInCache(TestValidatorId, ValidatorStatus.Pending);

        _databaseMock.Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<bool>(),
                It.IsAny<When>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        _databaseMock.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        var request = new ValidatorApprovalRequest
        {
            ValidatorId = TestValidatorId,
            ApprovedBy = TestApprover,
            ApprovalNotes = "Approved after review"
        };

        // Act
        var result = await _registry.ApproveValidatorAsync(TestRegisterId, request);

        // Assert
        result.Success.Should().BeTrue();
        result.TransactionId.Should().NotBeNullOrEmpty();
        result.OrderIndex.Should().NotBeNull();
        result.ApprovedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ApproveValidatorAsync_RaisesValidatorListChangedEvent()
    {
        // Arrange
        SetupGenesisConfig(isPublicRegistration: false, maxValidators: 10);
        SetupValidatorInCache(TestValidatorId, ValidatorStatus.Pending);

        _databaseMock.Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<bool>(),
                It.IsAny<When>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        _databaseMock.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        ValidatorListChangedEventArgs? eventArgs = null;
        _registry.ValidatorListChanged += (sender, args) => eventArgs = args;

        var request = new ValidatorApprovalRequest
        {
            ValidatorId = TestValidatorId,
            ApprovedBy = TestApprover
        };

        // Act
        await _registry.ApproveValidatorAsync(TestRegisterId, request);

        // Assert
        eventArgs.Should().NotBeNull();
        eventArgs!.RegisterId.Should().Be(TestRegisterId);
        eventArgs.ValidatorId.Should().Be(TestValidatorId);
        eventArgs.ChangeType.Should().Be(ValidatorListChangeType.ValidatorApproved);
    }

    #endregion

    #region RejectValidatorAsync Tests

    [Fact]
    public async Task RejectValidatorAsync_WithNullRegisterId_ThrowsArgumentException()
    {
        // Act
        var act = () => _registry.RejectValidatorAsync(null!, TestValidatorId, "reason", TestApprover);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task RejectValidatorAsync_WithNullValidatorId_ThrowsArgumentException()
    {
        // Act
        var act = () => _registry.RejectValidatorAsync(TestRegisterId, null!, "reason", TestApprover);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task RejectValidatorAsync_WithNullReason_ThrowsArgumentException()
    {
        // Act
        var act = () => _registry.RejectValidatorAsync(TestRegisterId, TestValidatorId, null!, TestApprover);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task RejectValidatorAsync_WithNullRejectedBy_ThrowsArgumentException()
    {
        // Act
        var act = () => _registry.RejectValidatorAsync(TestRegisterId, TestValidatorId, "reason", null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task RejectValidatorAsync_ValidatorNotFound_ReturnsFalse()
    {
        // Arrange
        SetupEmptyValidatorCache();

        // Act
        var result = await _registry.RejectValidatorAsync(
            TestRegisterId, "nonexistent", "Not trusted", TestApprover);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task RejectValidatorAsync_ValidatorNotPending_ReturnsFalse()
    {
        // Arrange
        SetupValidatorInCache(TestValidatorId, ValidatorStatus.Active);

        // Act
        var result = await _registry.RejectValidatorAsync(
            TestRegisterId, TestValidatorId, "Not trusted", TestApprover);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task RejectValidatorAsync_ValidPendingValidator_ReturnsTrue()
    {
        // Arrange
        SetupValidatorInCache(TestValidatorId, ValidatorStatus.Pending);

        _databaseMock.Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<bool>(),
                It.IsAny<When>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        _databaseMock.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        _databaseMock.Setup(d => d.StringGetAsync(It.Is<RedisKey>(k => k.ToString().Contains(":order")), It.IsAny<CommandFlags>()))
            .ReturnsAsync(JsonSerializer.Serialize(new List<string> { TestValidatorId }, _jsonOptions));

        // Act
        var result = await _registry.RejectValidatorAsync(
            TestRegisterId, TestValidatorId, "Failed security review", TestApprover);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task RejectValidatorAsync_RaisesValidatorListChangedEvent()
    {
        // Arrange
        SetupValidatorInCache(TestValidatorId, ValidatorStatus.Pending);

        _databaseMock.Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<bool>(),
                It.IsAny<When>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        _databaseMock.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        _databaseMock.Setup(d => d.StringGetAsync(It.Is<RedisKey>(k => k.ToString().Contains(":order")), It.IsAny<CommandFlags>()))
            .ReturnsAsync(JsonSerializer.Serialize(new List<string> { TestValidatorId }, _jsonOptions));

        ValidatorListChangedEventArgs? eventArgs = null;
        _registry.ValidatorListChanged += (sender, args) => eventArgs = args;

        // Act
        await _registry.RejectValidatorAsync(
            TestRegisterId, TestValidatorId, "Not trusted", TestApprover);

        // Assert
        eventArgs.Should().NotBeNull();
        eventArgs!.RegisterId.Should().Be(TestRegisterId);
        eventArgs.ValidatorId.Should().Be(TestValidatorId);
        eventArgs.ChangeType.Should().Be(ValidatorListChangeType.ValidatorRejected);
    }

    [Fact]
    public async Task RejectValidatorAsync_UpdatesValidatorToRemovedStatus()
    {
        // Arrange
        SetupValidatorInCache(TestValidatorId, ValidatorStatus.Pending);

        // StackExchange.Redis 2.11+ resolves StringSetAsync(key, value, timespan)
        // to the Expiration-based overload: StringSetAsync(RedisKey, RedisValue, Expiration, ValueCondition, CommandFlags)
        string? storedJson = null;
        _databaseMock
            .Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<Expiration>(),
                It.IsAny<ValueCondition>(),
                It.IsAny<CommandFlags>()))
            .Callback<RedisKey, RedisValue, Expiration, ValueCondition, CommandFlags>((k, v, e, vc, c) =>
            {
                if (k.ToString().Contains($":validator:{TestValidatorId}"))
                    storedJson = v.ToString();
            })
            .ReturnsAsync(true);

        _databaseMock.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        _databaseMock.Setup(d => d.StringGetAsync(It.Is<RedisKey>(k => k.ToString().Contains(":order")), It.IsAny<CommandFlags>()))
            .ReturnsAsync(JsonSerializer.Serialize(new List<string> { TestValidatorId }, _jsonOptions));

        // Act
        await _registry.RejectValidatorAsync(
            TestRegisterId, TestValidatorId, "Failed review", TestApprover);

        // Assert
        storedJson.Should().NotBeNullOrEmpty();
        var storedValidator = JsonSerializer.Deserialize<ValidatorInfo>(storedJson!, _jsonOptions);
        storedValidator.Should().NotBeNull();
        storedValidator!.Status.Should().Be(ValidatorStatus.Removed);
        storedValidator.Metadata.Should().ContainKey("rejectedBy");
        storedValidator.Metadata.Should().ContainKey("rejectionReason");
    }

    #endregion

    #region Helper Methods

    private ValidatorInfo CreateValidatorInfo(
        string validatorId,
        ValidatorStatus status,
        DateTimeOffset? registeredAt = null)
    {
        return new ValidatorInfo
        {
            ValidatorId = validatorId,
            PublicKey = $"0x{validatorId}pubkey",
            GrpcEndpoint = $"https://{validatorId}.example.com:7004",
            Status = status,
            RegisteredAt = registeredAt ?? DateTimeOffset.UtcNow,
            OrderIndex = 0
        };
    }

    private void SetupGenesisConfig(bool isPublicRegistration, int maxValidators = 100)
    {
        var config = new ValidatorConfig
        {
            RegistrationMode = isPublicRegistration ? "public" : "consent",
            MinValidators = 3,
            MaxValidators = maxValidators,
            RequireStake = false,
            StakeAmount = null
        };

        _genesisConfigMock.Setup(g => g.GetValidatorConfigAsync(TestRegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(config);
    }

    private void SetupEmptyValidatorCache()
    {
        _databaseMock.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        _serverMock.Setup(s => s.KeysAsync(
                It.IsAny<int>(),
                It.IsAny<RedisValue>(),
                It.IsAny<int>(),
                It.IsAny<long>(),
                It.IsAny<int>(),
                It.IsAny<CommandFlags>()))
            .Returns(AsyncEnumerable.Empty<RedisKey>());
    }

    private void SetupValidatorInCache(string validatorId, ValidatorStatus status)
    {
        var validator = CreateValidatorInfo(validatorId, status);
        var validatorJson = JsonSerializer.Serialize(validator, _jsonOptions);

        // Setup individual validator lookup
        _databaseMock.Setup(d => d.StringGetAsync(
                It.Is<RedisKey>(k => k.ToString().Contains($":validator:{validatorId}")),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(validatorJson);

        // Setup list lookup returning single validator
        var validators = new List<ValidatorInfo> { validator };
        _databaseMock.Setup(d => d.StringGetAsync(
                It.Is<RedisKey>(k => k.ToString().Contains(":list")),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(JsonSerializer.Serialize(validators, _jsonOptions));

        _serverMock.Setup(s => s.KeysAsync(
                It.IsAny<int>(),
                It.IsAny<RedisValue>(),
                It.IsAny<int>(),
                It.IsAny<long>(),
                It.IsAny<int>(),
                It.IsAny<CommandFlags>()))
            .Returns(AsyncEnumerable.Empty<RedisKey>());
    }

    private void SetupValidatorsInCache(List<ValidatorInfo> validators)
    {
        var json = JsonSerializer.Serialize(validators, _jsonOptions);

        _databaseMock.Setup(d => d.StringGetAsync(
                It.Is<RedisKey>(k => k.ToString().Contains(":list")),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(json);

        foreach (var validator in validators)
        {
            var validatorJson = JsonSerializer.Serialize(validator, _jsonOptions);
            _databaseMock.Setup(d => d.StringGetAsync(
                    It.Is<RedisKey>(k => k.ToString().Contains($":validator:{validator.ValidatorId}")),
                    It.IsAny<CommandFlags>()))
                .ReturnsAsync(validatorJson);
        }

        _serverMock.Setup(s => s.KeysAsync(
                It.IsAny<int>(),
                It.IsAny<RedisValue>(),
                It.IsAny<int>(),
                It.IsAny<long>(),
                It.IsAny<int>(),
                It.IsAny<CommandFlags>()))
            .Returns(AsyncEnumerable.Empty<RedisKey>());
    }

    #endregion
}
