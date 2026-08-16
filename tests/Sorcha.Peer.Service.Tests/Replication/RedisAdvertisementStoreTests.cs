// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Peer.Service.Core;
using Sorcha.Peer.Service.Replication;
using StackExchange.Redis;

namespace Sorcha.Peer.Service.Tests.Replication;

public class RedisAdvertisementStoreTests
{
    private readonly Mock<IConnectionMultiplexer> _mockRedis;
    private readonly Mock<IDatabase> _mockDb;
    private readonly RedisAdvertisementStore _store;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public RedisAdvertisementStoreTests()
    {
        _mockRedis = new Mock<IConnectionMultiplexer>();
        _mockDb = new Mock<IDatabase>();

        _mockRedis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(_mockDb.Object);

        _store = new RedisAdvertisementStore(
            _mockRedis.Object,
            new Mock<ILogger<RedisAdvertisementStore>>().Object);
    }

    [Fact]
    public void Constructor_NullRedis_ThrowsArgumentNullException()
    {
        Action act = () => new RedisAdvertisementStore(null!, new Mock<ILogger<RedisAdvertisementStore>>().Object);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        Action act = () => new RedisAdvertisementStore(_mockRedis.Object, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    #region SetLocalAsync

    [Fact]
    public async Task SetLocalAsync_SerializesAndSetsWithTtl()
    {
        var ad = new LocalRegisterAdvertisement
        {
            RegisterId = "reg-1",
            SyncState = RegisterSyncState.Active,
            LatestVersion = 42,
            LatestDocketVersion = 7,
            IsPublic = true
        };

        await _store.SetLocalAsync(ad);

        _mockDb.Verify(db => db.StringSetAsync(
            (RedisKey)"peer:advert:local:reg-1",
            It.Is<RedisValue>(v => v.ToString().Contains("reg-1")),
            It.IsAny<Expiration>(),
            It.IsAny<ValueCondition>(),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task SetLocalAsync_AddsToLocalIndex()
    {
        var ad = new LocalRegisterAdvertisement
        {
            RegisterId = "reg-1",
            SyncState = RegisterSyncState.Active
        };

        await _store.SetLocalAsync(ad);

        _mockDb.Verify(db => db.SetAddAsync(
            (RedisKey)"peer:advert:local:_index",
            (RedisValue)"reg-1",
            It.IsAny<CommandFlags>()), Times.Once);
        _mockDb.Verify(db => db.KeyExpireAsync(
            (RedisKey)"peer:advert:local:_index",
            TimeSpan.FromMinutes(5),
            It.IsAny<ExpireWhen>(),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task SetLocalAsync_RedisUnavailable_LogsWarningAndDoesNotThrow()
    {
        _mockDb.Setup(db => db.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<Expiration>(),
                It.IsAny<ValueCondition>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, CommandFlags.None, "Redis down"));

        var ad = new LocalRegisterAdvertisement
        {
            RegisterId = "reg-1",
            SyncState = RegisterSyncState.Active
        };

        var act = () => _store.SetLocalAsync(ad);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SetLocalAsync_CancellationRequested_ThrowsOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var ad = new LocalRegisterAdvertisement { RegisterId = "reg-1", SyncState = RegisterSyncState.Active };

        var act = () => _store.SetLocalAsync(ad, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    #endregion

    #region SetRemoteAsync

    [Fact]
    public async Task SetRemoteAsync_UsesCompositeKey()
    {
        var info = new PeerRegisterInfo
        {
            RegisterId = "reg-1",
            SyncState = RegisterSyncState.FullyReplicated,
            LatestVersion = 100,
            IsPublic = true
        };

        await _store.SetRemoteAsync("peer-42", info);

        _mockDb.Verify(db => db.StringSetAsync(
            (RedisKey)"peer:advert:remote:peer-42:reg-1",
            It.IsAny<RedisValue>(),
            It.IsAny<Expiration>(),
            It.IsAny<ValueCondition>(),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task SetRemoteAsync_UpdatesIndexSets()
    {
        var info = new PeerRegisterInfo
        {
            RegisterId = "reg-1",
            SyncState = RegisterSyncState.Active,
            IsPublic = true
        };

        await _store.SetRemoteAsync("peer-42", info);

        // Per-peer index
        _mockDb.Verify(db => db.SetAddAsync(
            (RedisKey)"peer:advert:remote:peer-42:_index",
            (RedisValue)"reg-1",
            It.IsAny<CommandFlags>()), Times.Once);
        _mockDb.Verify(db => db.KeyExpireAsync(
            (RedisKey)"peer:advert:remote:peer-42:_index",
            TimeSpan.FromMinutes(5),
            It.IsAny<ExpireWhen>(),
            It.IsAny<CommandFlags>()), Times.Once);

        // Master peer set
        _mockDb.Verify(db => db.SetAddAsync(
            (RedisKey)"peer:advert:remote:_peers",
            (RedisValue)"peer-42",
            It.IsAny<CommandFlags>()), Times.Once);
        _mockDb.Verify(db => db.KeyExpireAsync(
            (RedisKey)"peer:advert:remote:_peers",
            TimeSpan.FromMinutes(5),
            It.IsAny<ExpireWhen>(),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    #endregion

    #region RemoveLocalAsync

    [Fact]
    public async Task RemoveLocalAsync_DeletesKeyAndRemovesFromIndex()
    {
        await _store.RemoveLocalAsync("reg-1");

        _mockDb.Verify(db => db.KeyDeleteAsync(
            (RedisKey)"peer:advert:local:reg-1",
            It.IsAny<CommandFlags>()), Times.Once);
        _mockDb.Verify(db => db.SetRemoveAsync(
            (RedisKey)"peer:advert:local:_index",
            (RedisValue)"reg-1",
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task RemoveLocalAsync_RedisUnavailable_DoesNotThrow()
    {
        _mockDb.Setup(db => db.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, CommandFlags.None, "down"));

        var act = () => _store.RemoveLocalAsync("reg-1");
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region GetAllLocalAsync

    [Fact]
    public async Task GetAllLocalAsync_DeserializesAllEntries()
    {
        var ad1 = new LocalRegisterAdvertisement { RegisterId = "reg-1", SyncState = RegisterSyncState.Active, IsPublic = true };
        var ad2 = new LocalRegisterAdvertisement { RegisterId = "reg-2", SyncState = RegisterSyncState.FullyReplicated, IsPublic = true };

        // Mock SMEMBERS on local index
        _mockDb.Setup(db => db.SetMembersAsync((RedisKey)"peer:advert:local:_index", It.IsAny<CommandFlags>()))
            .ReturnsAsync([
                (RedisValue)"reg-1",
                (RedisValue)"reg-2"
            ]);

        // Mock batch StringGetAsync
        _mockDb.Setup(db => db.StringGetAsync(
                It.Is<RedisKey[]>(k => k.Length == 2),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync([
                (RedisValue)JsonSerializer.Serialize(ad1, JsonOptions),
                (RedisValue)JsonSerializer.Serialize(ad2, JsonOptions)
            ]);

        var results = await _store.GetAllLocalAsync();

        results.Should().HaveCount(2);
        results.Select(r => r.RegisterId).Should().BeEquivalentTo(["reg-1", "reg-2"]);
    }

    [Fact]
    public async Task GetAllLocalAsync_EmptyIndex_ReturnsEmpty()
    {
        _mockDb.Setup(db => db.SetMembersAsync((RedisKey)"peer:advert:local:_index", It.IsAny<CommandFlags>()))
            .ReturnsAsync([]);

        var results = await _store.GetAllLocalAsync();
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllLocalAsync_RedisUnavailable_ReturnsEmpty()
    {
        _mockDb.Setup(db => db.SetMembersAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, CommandFlags.None, "down"));

        var results = await _store.GetAllLocalAsync();
        results.Should().BeEmpty();
    }

    #endregion

    #region GetAllRemoteAsync

    [Fact]
    public async Task GetAllRemoteAsync_GroupsByPeerId()
    {
        var info1 = new PeerRegisterInfo { RegisterId = "reg-1", SyncState = RegisterSyncState.Active, IsPublic = true };
        var info2 = new PeerRegisterInfo { RegisterId = "reg-2", SyncState = RegisterSyncState.FullyReplicated, IsPublic = true };

        // Mock master peer set
        _mockDb.Setup(db => db.SetMembersAsync((RedisKey)"peer:advert:remote:_peers", It.IsAny<CommandFlags>()))
            .ReturnsAsync([(RedisValue)"peer-A"]);

        // Mock per-peer index
        _mockDb.Setup(db => db.SetMembersAsync((RedisKey)"peer:advert:remote:peer-A:_index", It.IsAny<CommandFlags>()))
            .ReturnsAsync([
                (RedisValue)"reg-1",
                (RedisValue)"reg-2"
            ]);

        // Mock batch read
        _mockDb.Setup(db => db.StringGetAsync(
                It.Is<RedisKey[]>(k => k.Length == 2),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync([
                (RedisValue)JsonSerializer.Serialize(info1, JsonOptions),
                (RedisValue)JsonSerializer.Serialize(info2, JsonOptions)
            ]);

        var results = await _store.GetAllRemoteAsync();

        results.Should().ContainKey("peer-A");
        results["peer-A"].Should().HaveCount(2);
    }

    [Fact]
    public async Task GetAllRemoteAsync_NoPeers_ReturnsEmpty()
    {
        _mockDb.Setup(db => db.SetMembersAsync((RedisKey)"peer:advert:remote:_peers", It.IsAny<CommandFlags>()))
            .ReturnsAsync([]);

        var results = await _store.GetAllRemoteAsync();
        results.Should().BeEmpty();
    }

    #endregion

    #region RemoveLocalExceptAsync

    [Fact]
    public async Task RemoveLocalExceptAsync_RemovesKeysNotInSet()
    {
        // Mock SMEMBERS on local index
        _mockDb.Setup(db => db.SetMembersAsync((RedisKey)"peer:advert:local:_index", It.IsAny<CommandFlags>()))
            .ReturnsAsync([
                (RedisValue)"reg-1",
                (RedisValue)"reg-2",
                (RedisValue)"reg-3"
            ]);

        var keepSet = new HashSet<string> { "reg-1" };
        var removed = await _store.RemoveLocalExceptAsync(keepSet);

        removed.Should().Be(2);
        _mockDb.Verify(db => db.KeyDeleteAsync((RedisKey)"peer:advert:local:reg-2", It.IsAny<CommandFlags>()), Times.Once);
        _mockDb.Verify(db => db.KeyDeleteAsync((RedisKey)"peer:advert:local:reg-3", It.IsAny<CommandFlags>()), Times.Once);
        _mockDb.Verify(db => db.KeyDeleteAsync((RedisKey)"peer:advert:local:reg-1", It.IsAny<CommandFlags>()), Times.Never);

        // Verify SREM on index for removed entries
        _mockDb.Verify(db => db.SetRemoveAsync(
            (RedisKey)"peer:advert:local:_index",
            (RedisValue)"reg-2",
            It.IsAny<CommandFlags>()), Times.Once);
        _mockDb.Verify(db => db.SetRemoveAsync(
            (RedisKey)"peer:advert:local:_index",
            (RedisValue)"reg-3",
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task RemoveLocalExceptAsync_AllInKeepSet_RemovesNothing()
    {
        _mockDb.Setup(db => db.SetMembersAsync((RedisKey)"peer:advert:local:_index", It.IsAny<CommandFlags>()))
            .ReturnsAsync([(RedisValue)"reg-1"]);

        var removed = await _store.RemoveLocalExceptAsync(new HashSet<string> { "reg-1" });

        removed.Should().Be(0);
    }

    #endregion

    #region RemoveRemoteByPeerAsync

    [Fact]
    public async Task RemoveRemoteByPeerAsync_DeletesAllKeysForPeer()
    {
        // Mock per-peer index
        _mockDb.Setup(db => db.SetMembersAsync((RedisKey)"peer:advert:remote:peer-A:_index", It.IsAny<CommandFlags>()))
            .ReturnsAsync([
                (RedisValue)"reg-1",
                (RedisValue)"reg-2"
            ]);

        await _store.RemoveRemoteByPeerAsync("peer-A");

        // Data keys deleted in batch
        _mockDb.Verify(db => db.KeyDeleteAsync(
            It.Is<RedisKey[]>(k => k.Length == 2),
            It.IsAny<CommandFlags>()), Times.Once);

        // Per-peer index deleted
        _mockDb.Verify(db => db.KeyDeleteAsync(
            (RedisKey)"peer:advert:remote:peer-A:_index",
            It.IsAny<CommandFlags>()), Times.Once);

        // Removed from master peer set
        _mockDb.Verify(db => db.SetRemoveAsync(
            (RedisKey)"peer:advert:remote:_peers",
            (RedisValue)"peer-A",
            It.IsAny<CommandFlags>()), Times.Once);
    }

    #endregion
}
