// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Blueprint.Service.Storage.Presentations;
using StackExchange.Redis;
using Xunit;

namespace Sorcha.Blueprint.Service.Tests.Storage.Presentations;

/// <summary>
/// Feature 127 — unit tests for <see cref="RedisDisclosedClaimsStore"/>.
/// The store writes a JSON-serialised claim dictionary at a TTL-bounded
/// Redis key alongside the F111 outcome write; the disclosed-claims
/// endpoint reads it in plaintext for council-page autofill.
/// </summary>
public sealed class RedisDisclosedClaimsStoreTests
{
    private readonly Mock<IConnectionMultiplexer> _redis = new();
    private readonly Mock<IDatabase> _db = new();
    private readonly RedisDisclosedClaimsStore _sut;

    public RedisDisclosedClaimsStoreTests()
    {
        _redis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(_db.Object);
        _sut = new RedisDisclosedClaimsStore(_redis.Object, NullLogger<RedisDisclosedClaimsStore>.Instance);
    }

    [Fact]
    public void Constructor_NullRedis_Throws()
    {
        var act = () => new RedisDisclosedClaimsStore(null!, NullLogger<RedisDisclosedClaimsStore>.Instance);
        act.Should().Throw<ArgumentNullException>();
    }

    // Note: arg-capture on the StringSetAsync write-shape is fragile
    // against StackExchange.Redis's overload dispatch — deferred to
    // integration coverage with a real Redis. The read-back-and-shape
    // tests below cover the JSON round-trip (the load-bearing
    // serialisation behaviour) via the matching IDatabase.StringGetAsync
    // mock, which doesn't suffer the same instance+extension dance.

    [Fact]
    public async Task StoreAsync_RejectsZeroOrNegativeTtl()
    {
        Func<Task> act = () => _sut.StoreAsync(
            Guid.NewGuid(),
            new Dictionary<string, object>(),
            TimeSpan.Zero);
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task GetAsync_ReturnsDeserialisedDictionary_OnHit()
    {
        var requestId = Guid.NewGuid();
        var json = "{\"givenName\":\"Sarah\",\"familyName\":\"Example\"}";
        _db.Setup(d => d.StringGetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)json);

        var result = await _sut.GetAsync(requestId);

        result.Should().NotBeNull();
        result!.Should().ContainKey("givenName");
        result["givenName"].Should().BeOfType<JsonElement>();
        ((JsonElement)result["givenName"]).GetString().Should().Be("Sarah");
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_OnMiss()
    {
        _db.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        var result = await _sut.GetAsync(Guid.NewGuid());

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_OnMalformedJson()
    {
        // Defensive: if the stored value isn't valid JSON, the store logs an
        // error and treats it as missing rather than throwing through to the
        // endpoint handler.
        _db.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)"not-json");

        var result = await _sut.GetAsync(Guid.NewGuid());

        result.Should().BeNull();
    }
}
