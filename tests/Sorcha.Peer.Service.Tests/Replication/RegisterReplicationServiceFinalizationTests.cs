// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sorcha.Cryptography.Core;
using Sorcha.Cryptography.Enums;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Cryptography.Utilities;
using Sorcha.Peer.Service.Communication;
using Sorcha.Peer.Service.Connection;
using Sorcha.Peer.Service.Core;
using Sorcha.Peer.Service.Discovery;
using Sorcha.Peer.Service.Observability;
using Sorcha.Peer.Service.Replication;
using Sorcha.ServiceClients.Register;

namespace Sorcha.Peer.Service.Tests.Replication;

/// <summary>
/// #1474: a replica must report sync success only once a pulled docket is actually PERSISTED,
/// not merely pulled off the wire. These tests exercise
/// <see cref="RegisterReplicationService.FinalizeAndRecordAsync"/> — the single gate both pull
/// paths (direct gRPC <c>PullFullReplicaAsync</c> and relay <c>TryRelayBatchSyncAsync</c>) route
/// through before calling <see cref="RegisterSubscription.RecordSyncSuccess"/>. A real
/// <see cref="DocketFinalizationService"/> is wired in (mirroring
/// <c>DocketFinalizationServiceTests</c>) with only <see cref="IRegisterServiceClient.WriteDocketAsync"/>
/// mocked, so these tests exercise the actual finalization gate rather than a stand-in.
/// </summary>
public class RegisterReplicationServiceFinalizationTests : IAsyncDisposable
{
    private readonly Mock<IRegisterServiceClient> _registerClientMock = new();
    private readonly RegisterCache _registerCache;
    private readonly DocketFinalizationService _finalizationService;
    private readonly RegisterReplicationService _service;
    private readonly PeerConnectionPool _connectionPool;
    private readonly PeerListManager _peerListManager;
    private readonly PeerServiceMetrics _metrics;
    private readonly PeerServiceActivitySource _activitySource;

    public RegisterReplicationServiceFinalizationTests()
    {
        var config = Options.Create(new PeerServiceConfiguration
        {
            NodeId = "test-node",
            PeerDiscovery = new PeerDiscoveryConfiguration(),
            SeedNodes = new SeedNodeConfiguration(),
            RegisterSync = new RegisterSyncConfiguration()
        });

        _peerListManager = new PeerListManager(new Mock<ILogger<PeerListManager>>().Object, config);

        var loggerFactoryMock = new Mock<ILoggerFactory>();
        loggerFactoryMock.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);

        _metrics = new PeerServiceMetrics();
        _activitySource = new PeerServiceActivitySource();

        _connectionPool = new PeerConnectionPool(
            new Mock<ILogger<PeerConnectionPool>>().Object,
            loggerFactoryMock.Object,
            _peerListManager,
            config,
            _metrics,
            _activitySource);

        _registerCache = new RegisterCache(new Mock<ILogger<RegisterCache>>().Object);

        var advertisementService = new RegisterAdvertisementService(
            new Mock<ILogger<RegisterAdvertisementService>>().Object, _peerListManager);

        var cryptoModuleMock = new Mock<ICryptoModule>();
        cryptoModuleMock
            .Setup(c => c.VerifyAsync(
                It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<byte>(),
                It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CryptoStatus.Success);

        var validatorKeyCache = new ValidatorKeyCache(new Mock<ILogger<ValidatorKeyCache>>().Object);
        var docketHasher = new DocketHasher(new HashProvider());

        var services = new ServiceCollection();
        services.AddScoped<IRegisterServiceClient>(_ => _registerClientMock.Object);
        var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();

        _finalizationService = new DocketFinalizationService(
            new Mock<ILogger<DocketFinalizationService>>().Object,
            scopeFactory,
            validatorKeyCache,
            _registerCache,
            cryptoModuleMock.Object,
            docketHasher);

        _service = new RegisterReplicationService(
            new Mock<ILogger<RegisterReplicationService>>().Object,
            _connectionPool,
            _peerListManager,
            advertisementService,
            _registerCache,
            config,
            relayCommunication: null,
            docketFinalizationService: _finalizationService,
            scopeFactory: null);
    }

    [Fact]
    public async Task FinalizeAndRecordAsync_AllDocketsFinalize_RecordsSyncSuccess()
    {
        var registerId = "reg-success";
        var docket0 = CreateValidCachedDocket(registerId, 0, null);
        var docket1 = CreateValidCachedDocket(registerId, 1, docket0.DocketHash);
        var cacheEntry = _registerCache.GetOrCreate(registerId);
        cacheEntry.AddOrUpdateDocket(docket0);
        cacheEntry.AddOrUpdateDocket(docket1);

        _registerClientMock
            .Setup(c => c.WriteDocketAsync(It.IsAny<DocketModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var subscription = new RegisterSubscription
        {
            RegisterId = registerId,
            Mode = ReplicationMode.FullReplica,
            SyncState = RegisterSyncState.Syncing
        };

        var result = await _service.FinalizeAndRecordAsync(
            registerId, subscription, cacheEntry,
            totalDockets: 2, totalTransactions: 4, sourcePeerId: "peer-1", CancellationToken.None);

        result.Success.Should().BeTrue();
        subscription.ConsecutiveFailures.Should().Be(0);
        subscription.ErrorMessage.Should().BeNull();
        subscription.LastSyncedDocketVersion.Should().Be(1);
        _registerClientMock.Verify(
            c => c.WriteDocketAsync(It.IsAny<DocketModel>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    /// <summary>
    /// The #1474 regression itself: the historical code called <c>RecordSyncSuccess</c> BEFORE the
    /// docket was ever handed to finalization, so a replica reported the watermark advanced (and,
    /// via the caller's state machine, SyncState transitioning toward FullyReplicated/"Synced")
    /// despite the Register Service refusing every write. This asserts the watermark does not move
    /// and a failure is recorded instead.
    /// </summary>
    [Fact]
    public async Task FinalizeAndRecordAsync_FinalizationFails_RecordsFailureNotSuccess()
    {
        var registerId = "reg-fail";
        var docket0 = CreateValidCachedDocket(registerId, 0, null);
        var cacheEntry = _registerCache.GetOrCreate(registerId);
        cacheEntry.AddOrUpdateDocket(docket0);

        _registerClientMock
            .Setup(c => c.WriteDocketAsync(It.IsAny<DocketModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var subscription = new RegisterSubscription
        {
            RegisterId = registerId,
            Mode = ReplicationMode.FullReplica,
            SyncState = RegisterSyncState.Syncing,
            LastSyncedDocketVersion = -1,
            ConsecutiveFailures = 0
        };

        var result = await _service.FinalizeAndRecordAsync(
            registerId, subscription, cacheEntry,
            totalDockets: 1, totalTransactions: 2, sourcePeerId: "peer-1", CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("finalization failed");
        subscription.ConsecutiveFailures.Should().Be(1);
        subscription.ErrorMessage.Should().NotBeNullOrEmpty();
        subscription.LastSyncedDocketVersion.Should().Be(-1,
            "sync success must not be recorded when the docket was never actually persisted");
    }

    [Fact]
    public async Task FinalizeAndRecordAsync_SecondDocketFails_StopsAndDoesNotFinalizeLaterDockets()
    {
        var registerId = "reg-partial";
        var docket0 = CreateValidCachedDocket(registerId, 0, null);
        var docket1 = CreateValidCachedDocket(registerId, 1, docket0.DocketHash);
        var docket2 = CreateValidCachedDocket(registerId, 2, docket1.DocketHash);
        var cacheEntry = _registerCache.GetOrCreate(registerId);
        cacheEntry.AddOrUpdateDocket(docket0);
        cacheEntry.AddOrUpdateDocket(docket1);
        cacheEntry.AddOrUpdateDocket(docket2);

        _registerClientMock
            .Setup(c => c.WriteDocketAsync(It.Is<DocketModel>(d => d.DocketNumber == 0), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _registerClientMock
            .Setup(c => c.WriteDocketAsync(It.Is<DocketModel>(d => d.DocketNumber == 1), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _registerClientMock
            .Setup(c => c.WriteDocketAsync(It.Is<DocketModel>(d => d.DocketNumber == 2), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var subscription = new RegisterSubscription
        {
            RegisterId = registerId,
            Mode = ReplicationMode.FullReplica,
            SyncState = RegisterSyncState.Syncing
        };

        var result = await _service.FinalizeAndRecordAsync(
            registerId, subscription, cacheEntry,
            totalDockets: 3, totalTransactions: 6, sourcePeerId: "peer-1", CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Docket 1");
        _registerClientMock.Verify(
            c => c.WriteDocketAsync(It.Is<DocketModel>(d => d.DocketNumber == 0), It.IsAny<CancellationToken>()),
            Times.Once);
        _registerClientMock.Verify(
            c => c.WriteDocketAsync(It.Is<DocketModel>(d => d.DocketNumber == 1), It.IsAny<CancellationToken>()),
            Times.Once);
        _registerClientMock.Verify(
            c => c.WriteDocketAsync(It.Is<DocketModel>(d => d.DocketNumber == 2), It.IsAny<CancellationToken>()),
            Times.Never,
            "docket 2 must not be attempted once docket 1's write fails — chain order matters");
    }

    [Fact]
    public async Task FinalizeAndRecordAsync_NoFinalizationService_RecordsSuccessOnPullAlone()
    {
        // Optional dependency omitted (unit-test double / forward-only caller) — there is nothing to
        // persist through this path, so the pull itself is reported as success. This preserves the
        // pre-#1474 behaviour for that configuration; it is the finalizer-present case that changed.
        var registerId = "reg-no-finalizer";
        var serviceWithoutFinalizer = new RegisterReplicationService(
            new Mock<ILogger<RegisterReplicationService>>().Object,
            _connectionPool,
            _peerListManager,
            new RegisterAdvertisementService(new Mock<ILogger<RegisterAdvertisementService>>().Object, _peerListManager),
            _registerCache);

        var cacheEntry = _registerCache.GetOrCreate(registerId);
        var subscription = new RegisterSubscription
        {
            RegisterId = registerId,
            Mode = ReplicationMode.FullReplica,
            SyncState = RegisterSyncState.Syncing
        };

        var result = await serviceWithoutFinalizer.FinalizeAndRecordAsync(
            registerId, subscription, cacheEntry,
            totalDockets: 0, totalTransactions: 0, sourcePeerId: null, CancellationToken.None);

        result.Success.Should().BeTrue();
        subscription.ConsecutiveFailures.Should().Be(0);
    }

    /// <summary>
    /// Builds a docket whose data + hash + signature all pass <see cref="DocketFinalizationService"/>'s
    /// verification steps, so the finalization outcome is driven purely by the mocked
    /// <see cref="IRegisterServiceClient.WriteDocketAsync"/> result. Mirrors
    /// <c>DocketFinalizationServiceTests.CreateValidCachedDocket</c> — every docket shares the same
    /// proposer public key so the roster extracted from docket 0 (legacy ProposerSignature fallback)
    /// authorizes every later docket in the same chain.
    /// </summary>
    private static CachedDocket CreateValidCachedDocket(
        string registerId,
        long version,
        string? previousHash)
    {
        var createdAt = DateTimeOffset.UtcNow;
        var merkleRoot = "abcd1234abcd1234abcd1234abcd1234abcd1234abcd1234abcd1234abcd1234";

        var hashInput = new
        {
            RegisterId = registerId,
            DocketNumber = version,
            PreviousHash = previousHash ?? string.Empty,
            MerkleRoot = merkleRoot,
            Timestamp = createdAt.ToUnixTimeMilliseconds()
        };

        var hashJson = JsonSerializer.Serialize(hashInput, new JsonSerializerOptions
        {
            PropertyNamingPolicy = null,
            WriteIndented = false
        });

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(hashJson));
        var docketHash = Convert.ToHexString(hashBytes).ToLowerInvariant();

        var docketData = JsonSerializer.Serialize(new
        {
            DocketNumber = version,
            RegisterId = registerId,
            MerkleRoot = merkleRoot,
            CreatedAt = createdAt,
            ProposerValidatorId = "validator-1",
            ProposerSignature = new
            {
                PublicKey = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5 }),
                SignatureValue = Convert.ToBase64String(new byte[] { 10, 20, 30 }),
                Algorithm = "ED25519",
                SignedAt = createdAt
            }
        });

        return new CachedDocket
        {
            RegisterId = registerId,
            Version = version,
            Data = Encoding.UTF8.GetBytes(docketData),
            DocketHash = docketHash,
            PreviousHash = previousHash,
            TransactionIds = new List<string> { $"tx-{version}-a", $"tx-{version}-b" },
            CreatedAt = createdAt
        };
    }

    public async ValueTask DisposeAsync()
    {
        await _connectionPool.DisposeAsync();
        _peerListManager.Dispose();
        _metrics.Dispose();
        _activitySource.Dispose();
    }
}
