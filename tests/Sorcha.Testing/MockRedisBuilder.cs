// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using Moq;
using StackExchange.Redis;

namespace Sorcha.Testing;

/// <summary>
/// Builds mock <see cref="IConnectionMultiplexer"/> instances for integration tests.
/// Two modes are provided:
///  * <see cref="Stateful"/>  — strings / sets / sorted sets are backed by
///                              in-memory dictionaries so round-trips work.
///  * <see cref="Stub"/>      — every operation returns a success/empty value;
///                              intended for services that only need Redis to
///                              exist in the container, not to behave correctly.
/// </summary>
public static class MockRedisBuilder
{
    /// <summary>
    /// Create a stub multiplexer that silently accepts writes and returns
    /// empty reads. Sufficient for services that only use Redis as a cache
    /// fallback or pub/sub sink in tests.
    /// </summary>
    public static IConnectionMultiplexer Stub()
    {
        var mockDatabase = new Mock<IDatabase>();
        mockDatabase.Setup(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        mockDatabase.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);
        mockDatabase.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        mockDatabase.Setup(d => d.SetAddAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        mockDatabase.Setup(d => d.SetContainsAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(false);
        mockDatabase.Setup(d => d.PublishAsync(It.IsAny<RedisChannel>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(0);

        var mockSubscriber = new Mock<ISubscriber>();
        mockSubscriber.Setup(s => s.SubscribeAsync(It.IsAny<RedisChannel>(), It.IsAny<Action<RedisChannel, RedisValue>>(), It.IsAny<CommandFlags>()))
            .Returns(Task.CompletedTask);

        var mockMultiplexer = new Mock<IConnectionMultiplexer>();
        mockMultiplexer.Setup(m => m.IsConnected).Returns(true);
        mockMultiplexer.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(mockDatabase.Object);
        mockMultiplexer.Setup(m => m.GetSubscriber(It.IsAny<object>()))
            .Returns(mockSubscriber.Object);
        return mockMultiplexer.Object;
    }

    /// <summary>
    /// Create a stateful multiplexer whose strings, sets and sorted sets are
    /// backed by in-memory dictionaries. Useful for services that read back
    /// values they write (e.g. nonces, rate-limit counters).
    /// </summary>
    public static IConnectionMultiplexer Stateful()
    {
        var mockDatabase = new Mock<IDatabase>();

        var stringStore = new ConcurrentDictionary<string, RedisValue>();

        mockDatabase
            .Setup(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisKey key, RedisValue value, TimeSpan? _, When _, CommandFlags _) =>
            {
                stringStore[key.ToString()] = value;
                return true;
            });

        mockDatabase
            .Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisKey key, CommandFlags _) =>
                stringStore.TryGetValue(key.ToString(), out var value) ? value : RedisValue.Null);

        mockDatabase
            .Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisKey key, CommandFlags _) =>
                stringStore.TryRemove(key.ToString(), out RedisValue _));

        mockDatabase
            .Setup(d => d.KeyExistsAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisKey key, CommandFlags _) => stringStore.ContainsKey(key.ToString()));

        var setStore = new ConcurrentDictionary<string, HashSet<RedisValue>>();

        mockDatabase
            .Setup(d => d.SetAddAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisKey key, RedisValue value, CommandFlags _) =>
            {
                var set = setStore.GetOrAdd(key.ToString(), _ => new HashSet<RedisValue>());
                return set.Add(value);
            });

        mockDatabase
            .Setup(d => d.SetContainsAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisKey key, RedisValue value, CommandFlags _) =>
                setStore.TryGetValue(key.ToString(), out var set) && set.Contains(value));

        mockDatabase
            .Setup(d => d.SetMembersAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisKey key, CommandFlags _) =>
                setStore.TryGetValue(key.ToString(), out var set)
                    ? set.ToArray()
                    : Array.Empty<RedisValue>());

        var sortedSetStore = new ConcurrentDictionary<string, SortedDictionary<double, RedisValue>>();

        mockDatabase
            .Setup(d => d.SortedSetAddAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<double>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisKey key, RedisValue value, double score, When _, CommandFlags _) =>
            {
                var sortedSet = sortedSetStore.GetOrAdd(key.ToString(), _ => new SortedDictionary<double, RedisValue>());
                sortedSet[score] = value;
                return true;
            });

        var mockSubscriber = new Mock<ISubscriber>();
        mockSubscriber
            .Setup(s => s.SubscribeAsync(It.IsAny<RedisChannel>(), It.IsAny<Action<RedisChannel, RedisValue>>(), It.IsAny<CommandFlags>()))
            .Returns(Task.CompletedTask);

        mockDatabase
            .Setup(d => d.PublishAsync(It.IsAny<RedisChannel>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(0);

        var mockMultiplexer = new Mock<IConnectionMultiplexer>();
        mockMultiplexer.Setup(m => m.IsConnected).Returns(true);
        mockMultiplexer.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(mockDatabase.Object);
        mockMultiplexer.Setup(m => m.GetSubscriber(It.IsAny<object>())).Returns(mockSubscriber.Object);
        return mockMultiplexer.Object;
    }
}
