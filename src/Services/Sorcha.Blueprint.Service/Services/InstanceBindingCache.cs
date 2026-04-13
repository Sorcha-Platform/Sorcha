// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using Sorcha.Blueprint.Service.Storage;

namespace Sorcha.Blueprint.Service.Services;

/// <summary>
/// Default implementation of <see cref="IInstanceBindingCache"/> over
/// <see cref="IDistributedCache"/> (Redis in production, in-memory in tests).
/// </summary>
/// <remarks>
/// <para>
/// Uses the platform's shared <see cref="IDistributedCache"/> abstraction rather than
/// raw <c>IConnectionMultiplexer</c> — this keeps the cache testable against
/// <c>MemoryDistributedCache</c> and sits inside the same serialisation story every
/// other Sorcha cache uses.
/// </para>
/// <para>
/// Two-tier fallback is implemented here: <b>cache → instance store</b>. The
/// third tier (ledger replay) described in
/// <c>specs/103-verified-citizen-v2/contracts/instance-binding-cache.md</c> is
/// intentionally NOT implemented in this commit — it only fires if an instance
/// document is lost from the instance store, which cannot happen under the current
/// persistence model (the late-bind block writes to the instance store before
/// writing to the cache; deletion is admin-only). If a follow-up feature introduces
/// instance document eviction or cross-peer recovery, a third tier can be layered
/// in by implementing <c>ReplayFromLedgerAsync</c> below.
/// </para>
/// <para>
/// Cache write failures are fire-and-forget — the method logs a warning and returns
/// successfully so that the caller's action submission proceeds against the
/// authoritative instance store write. Cache reads that throw on deserialisation
/// are treated as misses (with a warning log) rather than fatal errors.
/// </para>
/// </remarks>
public sealed class InstanceBindingCache : IInstanceBindingCache
{
    private const string CacheKeySuffix = "instance:{0}:bindings";

    private static readonly ActivitySource ActivitySource =
        new("Sorcha.Blueprint.Service.InstanceBindingCache");

    private readonly IDistributedCache _cache;
    private readonly IInstanceStore _instanceStore;
    private readonly InstanceBindingCacheOptions _options;
    private readonly ILogger<InstanceBindingCache> _logger;

    /// <summary>Initialises a new instance of the <see cref="InstanceBindingCache"/> class.</summary>
    public InstanceBindingCache(
        IDistributedCache cache,
        IInstanceStore instanceStore,
        IOptions<InstanceBindingCacheOptions> options,
        ILogger<InstanceBindingCache> logger)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _instanceStore = instanceStore ?? throw new ArgumentNullException(nameof(instanceStore));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, string>?> GetAsync(
        string instanceId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
            throw new ArgumentException("Instance id must not be empty", nameof(instanceId));

        using var activity = ActivitySource.StartActivity("InstanceBindingCache.Get");
        activity?.SetTag("instance.id", instanceId);

        var key = BuildKey(instanceId);

        // Tier 1 — cache hit
        try
        {
            var cached = await _cache.GetAsync(key, cancellationToken);
            if (cached is not null)
            {
                var bindings = DeserialiseOrNull(cached, instanceId);
                if (bindings is not null)
                {
                    activity?.SetTag("binding.cache_result", "hit");
                    _logger.LogDebug(
                        "InstanceBindingCache hit for instance {InstanceId} ({Count} bindings)",
                        instanceId, bindings.Count);
                    return bindings;
                }
            }
        }
        catch (Exception ex)
        {
            // Cache read failed — log and fall through to the instance store. We do
            // NOT propagate because a broken cache must not fail action submission.
            _logger.LogWarning(ex,
                "InstanceBindingCache read failed for instance {InstanceId}; falling through to instance store",
                instanceId);
        }

        // Tier 2 — instance store hit, write through
        var instance = await _instanceStore.GetAsync(instanceId, cancellationToken);
        if (instance is null)
        {
            activity?.SetTag("binding.cache_result", "miss-instance-store");
            _logger.LogDebug(
                "InstanceBindingCache instance-store miss for {InstanceId}; returning null",
                instanceId);
            return null;
        }

        // The instance exists — materialise a read-only copy of its ParticipantWallets
        // (which may be empty if no open participant has bound yet).
        var storeBindings = new Dictionary<string, string>(
            instance.ParticipantWallets ?? new Dictionary<string, string>(),
            StringComparer.OrdinalIgnoreCase);

        activity?.SetTag("binding.cache_result", "miss-cache-hit-store");
        _logger.LogDebug(
            "InstanceBindingCache miss for instance {InstanceId}; hydrated {Count} bindings from instance store",
            instanceId, storeBindings.Count);

        // Write through to cache (best-effort). Failure is non-fatal.
        await TrySetCacheAsync(key, storeBindings, cancellationToken);

        return storeBindings;
    }

    /// <inheritdoc />
    public async Task SetAsync(
        string instanceId,
        IReadOnlyDictionary<string, string> bindings,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
            throw new ArgumentException("Instance id must not be empty", nameof(instanceId));
        if (bindings is null) throw new ArgumentNullException(nameof(bindings));

        using var activity = ActivitySource.StartActivity("InstanceBindingCache.Set");
        activity?.SetTag("instance.id", instanceId);
        activity?.SetTag("binding.count", bindings.Count);

        var key = BuildKey(instanceId);
        await TrySetCacheAsync(key, bindings, cancellationToken);
    }

    /// <inheritdoc />
    public async Task InvalidateAsync(string instanceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
            throw new ArgumentException("Instance id must not be empty", nameof(instanceId));

        using var activity = ActivitySource.StartActivity("InstanceBindingCache.Invalidate");
        activity?.SetTag("instance.id", instanceId);

        try
        {
            await _cache.RemoveAsync(BuildKey(instanceId), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "InstanceBindingCache invalidate failed for instance {InstanceId}",
                instanceId);
        }
    }

    // ------- private helpers -------

    private string BuildKey(string instanceId)
    {
        var suffix = string.Format(CacheKeySuffix, instanceId);
        return string.IsNullOrEmpty(_options.KeyPrefix)
            ? suffix
            : $"{_options.KeyPrefix}:{suffix}";
    }

    private async Task TrySetCacheAsync(
        string key,
        IReadOnlyDictionary<string, string> bindings,
        CancellationToken cancellationToken)
    {
        try
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(
                bindings.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase));

            var cacheOptions = new DistributedCacheEntryOptions
            {
                SlidingExpiration = _options.SlidingExpiration
            };

            await _cache.SetAsync(key, payload, cacheOptions, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "InstanceBindingCache write failed for key {Key}; caller proceeds against instance store only",
                key);
        }
    }

    private Dictionary<string, string>? DeserialiseOrNull(byte[] payload, string instanceId)
    {
        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(payload);
            return dict is null
                ? null
                : new Dictionary<string, string>(dict, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "InstanceBindingCache deserialise failed for instance {InstanceId}; treating as cache miss",
                instanceId);
            return null;
        }
    }
}
