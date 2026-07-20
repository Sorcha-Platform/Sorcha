// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Sorcha.Haip.Service.Services;
using Xunit;

namespace Sorcha.Haip.Service.Tests.Services;

/// <summary>
/// Tests for <see cref="AccessTokenStore"/>, with the emphasis on what the cache KEYSPACE holds.
/// The store previously keyed on the raw bearer token, so anyone who could read the Redis keyspace
/// — a <c>SCAN</c>, a managed-Redis console, a memory dump — could lift live tokens verbatim and
/// replay them until expiry. Asserting the round-trip alone would not have caught that: the
/// round-trip worked fine. These tests assert on the keys themselves.
/// </summary>
public class AccessTokenStoreTests
{
    private const string Token = "s3cret-bearer-token-value-do-not-leak";

    private static IConfiguration Config() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Haip:TokenLifetimeSeconds"] = "300" })
            .Build();

    private static AccessTokenStore StoreWith(IDistributedCache cache) =>
        new(NullLogger<AccessTokenStore>.Instance, Config(), cache);

    [Fact]
    public async Task StoreAsync_NeverPutsTheRawTokenInTheCacheKey()
    {
        var cache = new RecordingDistributedCache();
        using var store = StoreWith(cache);

        await store.StoreAsync(Token, Guid.NewGuid());

        cache.Keys.Should().ContainSingle();
        cache.Keys[0].Should().NotContain(Token, "the keyspace must not hold a replayable bearer token");
    }

    [Fact]
    public async Task StoreAsync_KeysOnTheSha256HexOfTheToken()
    {
        var cache = new RecordingDistributedCache();
        using var store = StoreWith(cache);

        await store.StoreAsync(Token, Guid.NewGuid());

        var expected = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Token)));
        cache.Keys[0].Should().Be($"haip:token:{expected}");
    }

    [Fact]
    public async Task LookupAsync_RoundTripsTheOfferId()
    {
        // Hashing is only useful if the caller can still resolve their own token.
        var cache = new RecordingDistributedCache();
        using var store = StoreWith(cache);
        var offerId = Guid.NewGuid();

        await store.StoreAsync(Token, offerId);
        var resolved = await store.LookupAsync(Token);

        resolved.Should().Be(offerId);
    }

    [Fact]
    public async Task LookupAsync_UnknownToken_ReturnsNull()
    {
        var cache = new RecordingDistributedCache();
        using var store = StoreWith(cache);
        await store.StoreAsync(Token, Guid.NewGuid());

        var resolved = await store.LookupAsync("some-other-token");

        resolved.Should().BeNull();
    }

    [Fact]
    public async Task LookupAsync_HashesOnTheReadPathToo()
    {
        // A hash applied only on write would break every lookup; a hash applied only on read would
        // leave the write path leaking. Pin that both sides agree.
        var cache = new RecordingDistributedCache();
        using var store = StoreWith(cache);

        await store.LookupAsync(Token);

        cache.Keys.Should().ContainSingle();
        cache.Keys[0].Should().NotContain(Token);
    }

    [Fact]
    public async Task InMemoryFallback_StillRoundTripsWhenNoDistributedCache()
    {
        // cache: null is the no-Redis path — it must behave identically.
        using var store = new AccessTokenStore(NullLogger<AccessTokenStore>.Instance, Config(), cache: null);
        var offerId = Guid.NewGuid();

        await store.StoreAsync(Token, offerId);

        (await store.LookupAsync(Token)).Should().Be(offerId);
    }

    /// <summary>
    /// Records every key the store touches. The point of the whole exercise is what lands here.
    /// </summary>
    private sealed class RecordingDistributedCache : IDistributedCache
    {
        private readonly Dictionary<string, byte[]> _entries = new(StringComparer.Ordinal);

        public List<string> Keys { get; } = [];

        public byte[]? Get(string key)
        {
            Keys.Add(key);
            return _entries.TryGetValue(key, out var value) ? value : null;
        }

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) =>
            Task.FromResult(Get(key));

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
        {
            Keys.Add(key);
            _entries[key] = value;
        }

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            Set(key, value, options);
            return Task.CompletedTask;
        }

        public void Refresh(string key) => Keys.Add(key);

        public Task RefreshAsync(string key, CancellationToken token = default)
        {
            Refresh(key);
            return Task.CompletedTask;
        }

        public void Remove(string key)
        {
            Keys.Add(key);
            _entries.Remove(key);
        }

        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            Remove(key);
            return Task.CompletedTask;
        }
    }
}
