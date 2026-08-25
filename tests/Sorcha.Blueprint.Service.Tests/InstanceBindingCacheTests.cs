// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Services;
using Sorcha.Blueprint.Service.Storage;

namespace Sorcha.Blueprint.Service.Tests;

/// <summary>
/// Tests for <see cref="InstanceBindingCache"/> — the Redis read-through cache that
/// sits in front of <see cref="IInstanceStore"/> for the hot-path binding lookup.
///
/// Test contract: specs/103-verified-citizen-v2/contracts/instance-binding-cache.md
/// § Tests required.
/// </summary>
public class InstanceBindingCacheTests
{
    private readonly IDistributedCache _cache;
    private readonly Mock<IInstanceStore> _instanceStore;
    private readonly InstanceBindingCache _sut;

    public InstanceBindingCacheTests()
    {
        // Microsoft.Extensions.Caching.Memory.MemoryDistributedCache is the canonical
        // in-memory fake for IDistributedCache — round-trips serialised bytes, honours
        // absolute/sliding expiration, doesn't need Redis.
        _cache = new MemoryDistributedCache(
            Options.Create(new MemoryDistributedCacheOptions()));

        _instanceStore = new Mock<IInstanceStore>();

        var logger = new Mock<ILogger<InstanceBindingCache>>().Object;
        var options = Options.Create(new InstanceBindingCacheOptions
        {
            SlidingExpiration = TimeSpan.FromHours(1),
            KeyPrefix = "test"
        });

        _sut = new InstanceBindingCache(_cache, _instanceStore.Object, options, logger);
    }

    [Fact]
    public async Task GetAsync_WhenCacheHit_ReturnsCachedBindings()
    {
        // Arrange — seed the cache directly
        var instanceId = Guid.NewGuid().ToString();
        var expected = new Dictionary<string, string>
        {
            ["citizen"] = "ws1qcitizenwallet",
            ["assessor"] = "ws1qassessorwallet"
        };
        await _sut.SetAsync(instanceId, expected);

        // Act
        var result = await _sut.GetAsync(instanceId);

        // Assert
        result.Should().NotBeNull();
        result!.Count.Should().Be(2);
        result["citizen"].Should().Be("ws1qcitizenwallet");
        result["assessor"].Should().Be("ws1qassessorwallet");
        _instanceStore.Verify(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never,
            "a cache hit must NOT fall through to the instance store");
    }

    [Fact]
    public async Task GetAsync_WhenCacheMissButInstanceStoreHas_FallsThroughAndWritesBackToCache()
    {
        // Arrange — empty cache, instance store has the bindings
        var instanceId = Guid.NewGuid().ToString();
        var instance = new Instance
        {
            Id = instanceId,
            BlueprintId = "bp-test",
            BlueprintDefinitionTxId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", // Feature 195: an instance must carry its definition pin, or execution has nothing to resolve or chain from
            BlueprintVersion = 1,
            RegisterId = "reg-test",
            TenantId = "tenant-test",
            ParticipantWallets = new Dictionary<string, string>
            {
                ["citizen"] = "ws1qcitizenwallet",
                ["assessor"] = "ws1qassessorwallet"
            }
        };
        _instanceStore
            .Setup(s => s.GetAsync(instanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);

        // Act
        var result = await _sut.GetAsync(instanceId);

        // Assert — value returned, and cache now populated
        result.Should().NotBeNull();
        result!["citizen"].Should().Be("ws1qcitizenwallet");
        _instanceStore.Verify(s => s.GetAsync(instanceId, It.IsAny<CancellationToken>()), Times.Once);

        // Second call should be a cache hit (instance store called only once total)
        var secondResult = await _sut.GetAsync(instanceId);
        secondResult.Should().NotBeNull();
        _instanceStore.Verify(s => s.GetAsync(instanceId, It.IsAny<CancellationToken>()), Times.Once,
            "the write-through from the first call should make the second call a cache hit");
    }

    [Fact]
    public async Task GetAsync_WhenBothCacheAndInstanceStoreMiss_ReturnsNull()
    {
        // Arrange
        var instanceId = Guid.NewGuid().ToString();
        _instanceStore
            .Setup(s => s.GetAsync(instanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Instance?)null);

        // Act
        var result = await _sut.GetAsync(instanceId);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_WhenInstanceExistsButHasNoBindings_ReturnsEmpty()
    {
        // Arrange — instance exists but no late-bound participants yet (e.g. empty
        // instance just created, before the first action submission)
        var instanceId = Guid.NewGuid().ToString();
        var instance = new Instance
        {
            Id = instanceId,
            BlueprintId = "bp-test",
            BlueprintDefinitionTxId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", // Feature 195: an instance must carry its definition pin, or execution has nothing to resolve or chain from
            BlueprintVersion = 1,
            RegisterId = "reg-test",
            TenantId = "tenant-test",
            ParticipantWallets = new Dictionary<string, string>()
        };
        _instanceStore
            .Setup(s => s.GetAsync(instanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);

        // Act
        var result = await _sut.GetAsync(instanceId);

        // Assert — the result should be a materialised empty dictionary, not null.
        // Distinguishing "unknown instance" (null) from "known but empty" (empty dict)
        // matters so that the caller doesn't re-query the store every time for an
        // instance that legitimately has no bindings yet.
        result.Should().NotBeNull();
        result!.Count.Should().Be(0);
    }

    [Fact]
    public async Task SetAsync_WritesBindingsToCache()
    {
        // Arrange
        var instanceId = Guid.NewGuid().ToString();
        var bindings = new Dictionary<string, string>
        {
            ["citizen"] = "ws1qcitizenwallet"
        };

        // Act
        await _sut.SetAsync(instanceId, bindings);

        // Assert — a direct GetAsync should return the same bindings without hitting
        // the instance store
        var result = await _sut.GetAsync(instanceId);
        result.Should().NotBeNull();
        result!["citizen"].Should().Be("ws1qcitizenwallet");
        _instanceStore.Verify(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SetAsync_OverwritesExistingCachedValue()
    {
        // Arrange
        var instanceId = Guid.NewGuid().ToString();
        await _sut.SetAsync(instanceId, new Dictionary<string, string> { ["citizen"] = "old" });

        // Act — write through with an updated map (even though binding is immutable
        // in practice, the cache should accept the new value; the caller enforces
        // immutability at the business-logic layer)
        await _sut.SetAsync(instanceId, new Dictionary<string, string>
        {
            ["citizen"] = "old",
            ["assessor"] = "new"
        });

        // Assert
        var result = await _sut.GetAsync(instanceId);
        result.Should().NotBeNull();
        result!.Count.Should().Be(2);
        result["assessor"].Should().Be("new");
    }

    [Fact]
    public async Task InvalidateAsync_RemovesEntryFromCache()
    {
        // Arrange
        var instanceId = Guid.NewGuid().ToString();
        await _sut.SetAsync(instanceId, new Dictionary<string, string> { ["citizen"] = "ws1q" });
        _instanceStore
            .Setup(s => s.GetAsync(instanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Instance?)null);

        // Act
        await _sut.InvalidateAsync(instanceId);

        // Assert — cache entry gone; next GetAsync falls through to instance store
        // (which we've stubbed to return null)
        var result = await _sut.GetAsync(instanceId);
        result.Should().BeNull();
        _instanceStore.Verify(s => s.GetAsync(instanceId, It.IsAny<CancellationToken>()), Times.Once);
    }
}
