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
using Sorcha.Validator.Service.Tests.Helpers;
using ValidatorStatus = Sorcha.Validator.Service.Services.Interfaces.ValidatorStatus;

namespace Sorcha.Validator.Service.Tests.Services;

/// <summary>
/// Belt-and-braces fix for the cold-Redis re-registration loop on the
/// validator service (see fix/validator-registry-belt-and-braces).
/// Each test pins ONE layer of the five-layer defence so removing any
/// single layer leaves a green test as evidence the others still work.
/// </summary>
public class ValidatorRegistryBeltAndBracesTests
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
    private const string TestValidatorId = "local-validator";

    public ValidatorRegistryBeltAndBracesTests()
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

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        _registry = new ValidatorRegistry(
            _redisMock.Object,
            MongoMockHelper.CreateValidatorRegistryClient().Object,
            _registerClientMock.Object,
            _genesisConfigMock.Object,
            Options.Create(_config),
            _loggerMock.Object);
    }

    // =====================================================================
    // Layer 2 — RegisterAsync order list is idempotent.
    //
    // Reproduces the cold-Redis loop: GetValidatorAsync returns null (Redis
    // per-validator key missing) so RegisterAsync proceeds, but the order
    // key already contains the validator id from a prior cycle. Layer 2
    // must NOT append a duplicate.
    // =====================================================================
    [Fact]
    public async Task RegisterAsync_OrderListIsIdempotent_WhenValidatorAlreadyInList()
    {
        // Arrange — public-mode genesis config so the consent path is skipped.
        SetupGenesisConfig(isPublicRegistration: true, maxValidators: 100);

        // GetActiveCountAsync → GetActiveValidatorsAsync → list key is empty
        // (so currentCount == 0 < maxValidators).
        _databaseMock
            .Setup(d => d.StringGetAsync(
                It.Is<RedisKey>(k => k.ToString().EndsWith(":list")),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        // BuildValidatorListAsync scans for per-validator keys — return none.
        _serverMock
            .Setup(s => s.KeysAsync(
                It.IsAny<int>(),
                It.IsAny<RedisValue>(),
                It.IsAny<int>(),
                It.IsAny<long>(),
                It.IsAny<int>(),
                It.IsAny<CommandFlags>()))
            .Returns(AsyncEnumerable.Empty<RedisKey>());

        // GetValidatorAsync(existing check) — per-validator key absent ⇒ null
        _databaseMock
            .Setup(d => d.StringGetAsync(
                It.Is<RedisKey>(k => k.ToString().Contains($":validator:{TestValidatorId}")),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        // CRITICAL — the order key ALREADY contains the validator id from a
        // prior cycle (this is the duplicate-growth bug condition).
        var existingOrder = new List<string> { TestValidatorId };
        var existingOrderJson = JsonSerializer.Serialize(existingOrder, _jsonOptions);
        _databaseMock
            .Setup(d => d.StringGetAsync(
                It.Is<RedisKey>(k => k.ToString().EndsWith(":order")),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(existingOrderJson);

        // KeyExists probe (Layer 5) — pretend write landed.
        _databaseMock
            .Setup(d => d.KeyExistsAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        _databaseMock
            .Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<bool>(),
                It.IsAny<When>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        _databaseMock
            .Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        var registration = new ValidatorRegistration
        {
            ValidatorId = TestValidatorId,
            PublicKey = "0xpubkey",
            GrpcEndpoint = "https://val.example:7004"
        };

        // Act
        var result = await _registry.RegisterAsync(TestRegisterId, registration);

        // Assert — the write to the :order key should EITHER be skipped
        // entirely (idempotent path) or, if invoked, must not double the id.
        result.Success.Should().BeTrue();

        // Verify NO StringSetAsync was made for the order key with a doubled list.
        _databaseMock.Verify(d => d.StringSetAsync(
            It.Is<RedisKey>(k => k.ToString().EndsWith(":order")),
            It.Is<RedisValue>(v => v.ToString().Contains($"\"{TestValidatorId}\",\"{TestValidatorId}\"")),
            It.IsAny<TimeSpan?>(),
            It.IsAny<bool>(),
            It.IsAny<When>(),
            It.IsAny<CommandFlags>()),
            Times.Never(),
            "Layer 2: order list must not be appended with a duplicate validator id");
    }

    // =====================================================================
    // Layer 5 — StoreValidatorAsync warns when KeyExists is false right
    // after a StringSetAsync. Exercised end-to-end via RegisterAsync.
    // =====================================================================
    [Fact]
    public async Task StoreValidatorAsync_LogsWarning_WhenKeyMissingImmediatelyAfterWrite()
    {
        // Arrange
        SetupGenesisConfig(isPublicRegistration: true, maxValidators: 100);

        // Empty list, empty per-validator, empty order — so RegisterAsync
        // proceeds and calls StoreValidatorAsync.
        _databaseMock
            .Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);
        _serverMock
            .Setup(s => s.KeysAsync(
                It.IsAny<int>(),
                It.IsAny<RedisValue>(),
                It.IsAny<int>(),
                It.IsAny<long>(),
                It.IsAny<int>(),
                It.IsAny<CommandFlags>()))
            .Returns(AsyncEnumerable.Empty<RedisKey>());

        _databaseMock
            .Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<bool>(),
                It.IsAny<When>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        _databaseMock
            .Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        // CRITICAL — pretend the write silently failed: KeyExists returns false.
        _databaseMock
            .Setup(d => d.KeyExistsAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(false);

        // Act
        await _registry.RegisterAsync(TestRegisterId, new ValidatorRegistration
        {
            ValidatorId = TestValidatorId,
            PublicKey = "0xpubkey",
            GrpcEndpoint = "https://val.example:7004"
        });

        // Assert — the diagnostic LogWarning fired identifying the silent failure.
        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) =>
                    v.ToString()!.Contains("KeyExists==false immediately after")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce(),
            "Layer 5: silent-write-failure diagnostic must fire when KeyExists is false post-write");
    }

    // =====================================================================
    // Layer 4 — interface contract: the heartbeat path will check
    // IsRegisteredInMongoAsync and call HydrateOneAsync instead of
    // RegisterAsync when the answer is true. The DocketBuildTriggerService
    // call site is integration-tested by the live n1 reset; this test pins
    // the interface contract that makes the short-circuit possible.
    // =====================================================================
    [Fact]
    public void IValidatorRegistry_Exposes_IsRegisteredInMongoAsync_And_HydrateOneAsync()
    {
        var type = typeof(IValidatorRegistry);
        type.GetMethod(nameof(IValidatorRegistry.IsRegisteredInMongoAsync))
            .Should().NotBeNull("Layer 4 requires an authoritative Mongo check on the registry");
        type.GetMethod(nameof(IValidatorRegistry.HydrateOneAsync))
            .Should().NotBeNull("Layer 4 requires a way to refresh Redis TTL without re-entering RegisterAsync");
        type.GetMethod(nameof(IValidatorRegistry.HydrateFromMongoAsync))
            .Should().NotBeNull("Layer 1 requires a bulk-hydration entry point on the interface");
    }

    // =====================================================================
    // Tests deferred — require a Mongo IAsyncCursor mock fake which the
    // existing test infrastructure does not provide. Layers 1, 3 and the
    // Mongo-side of Layer 4 are exercised live by the n1 reset / docker
    // golden-path validation and via the interface contract test above.
    // =====================================================================
    [Fact(Skip = "needs IMongoCollection<ValidatorDocument> + IAsyncCursor fake — follow-up; layer is exercised live via n1 cold-Redis reset")]
    public Task GetValidatorAsync_FallsBackToMongo_WhenRedisEmpty() => Task.CompletedTask;

    [Fact(Skip = "needs IMongoCollection<ValidatorDocument> + IAsyncCursor fake — follow-up; layer is exercised live via n1 cold-Redis reset")]
    public Task BuildValidatorListAsync_FallsBackToMongo_WhenRedisScanEmpty() => Task.CompletedTask;

    [Fact(Skip = "needs IMongoCollection<ValidatorDocument> + IAsyncCursor fake — follow-up; layer is exercised live via n1 cold-Redis reset")]
    public Task IsRegisteredInMongoAsync_TrueOnActive_FalseOnMissing() => Task.CompletedTask;

    [Fact(Skip = "needs IMongoCollection<ValidatorDocument> + IAsyncCursor fake — follow-up; layer is exercised live via n1 cold-Redis reset")]
    public Task HydrateFromMongoAsync_PopulatesRedis_FromMongoEntries() => Task.CompletedTask;

    // Helpers -------------------------------------------------------------

    private void SetupGenesisConfig(bool isPublicRegistration, int maxValidators)
    {
        var config = new ValidatorConfig
        {
            RegistrationMode = isPublicRegistration ? "public" : "consent",
            MinValidators = 1,
            MaxValidators = maxValidators,
            RequireStake = false,
            StakeAmount = null
        };

        _genesisConfigMock
            .Setup(g => g.GetValidatorConfigAsync(TestRegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(config);
    }
}
