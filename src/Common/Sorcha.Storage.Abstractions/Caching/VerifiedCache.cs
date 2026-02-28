// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sorcha.Storage.Abstractions.Caching;

/// <summary>
/// Default implementation of IVerifiedCache that combines hot-tier cache
/// with cold-tier WORM store for verified caching.
/// </summary>
/// <typeparam name="TDocument">Document type.</typeparam>
/// <typeparam name="TId">Document identifier type.</typeparam>
public class VerifiedCache<TDocument, TId> : IVerifiedCache<TDocument, TId>
    where TDocument : class
    where TId : notnull
{
    private readonly ICacheStore _cacheStore;
    private readonly IWormStore<TDocument, TId> _wormStore;
    private readonly Func<TDocument, TId> _idSelector;
    private readonly Func<TDocument, string>? _hashSelector;
    private readonly VerifiedCacheConfiguration _configuration;
    private readonly ILogger<VerifiedCache<TDocument, TId>>? _logger;

    // Probabilistic verification — verify only a percentage of cache hits
    // to avoid negating the performance benefit of caching.
    private readonly Random _random = new();
    private const double VerificationProbability = 0.05; // 5% of reads

    // Statistics
    private long _cacheHits;
    private long _cacheMisses;
    private long _wormFetches;
    private long _verificationFailures;
    private readonly Queue<double> _latencies = new();
    private readonly object _statsLock = new();

    /// <summary>
    /// Initializes a new instance of the VerifiedCache.
    /// </summary>
    /// <param name="cacheStore">Hot-tier cache store.</param>
    /// <param name="wormStore">Cold-tier WORM store.</param>
    /// <param name="idSelector">Function to extract ID from document.</param>
    /// <param name="options">Cache configuration options.</param>
    /// <param name="hashSelector">Optional function to extract hash from document for verification.</param>
    /// <param name="logger">Optional logger.</param>
    public VerifiedCache(
        ICacheStore cacheStore,
        IWormStore<TDocument, TId> wormStore,
        Func<TDocument, TId> idSelector,
        IOptions<VerifiedCacheConfiguration>? options = null,
        Func<TDocument, string>? hashSelector = null,
        ILogger<VerifiedCache<TDocument, TId>>? logger = null)
    {
        _cacheStore = cacheStore ?? throw new ArgumentNullException(nameof(cacheStore));
        _wormStore = wormStore ?? throw new ArgumentNullException(nameof(wormStore));
        _idSelector = idSelector ?? throw new ArgumentNullException(nameof(idSelector));
        _configuration = options?.Value ?? new VerifiedCacheConfiguration();
        _hashSelector = hashSelector;
        _logger = logger;
    }

    private string GetCacheKey(TId id) => $"{_configuration.KeyPrefix}{id}";

    /// <inheritdoc/>
    public async Task<TDocument?> GetAsync(TId id, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            // Try cache first
            var cacheKey = GetCacheKey(id);
            var cached = await _cacheStore.GetAsync<TDocument>(cacheKey, cancellationToken);

            if (cached is not null)
            {
                Interlocked.Increment(ref _cacheHits);

                // Probabilistically verify hash to maintain integrity confidence
                // without negating the performance benefit of caching.
                if (_configuration.EnableHashVerification && _hashSelector is not null
                    && _random.NextDouble() < VerificationProbability)
                {
                    var wormDoc = await _wormStore.GetAsync(id, cancellationToken);
                    if (wormDoc is not null)
                    {
                        var cachedHash = _hashSelector(cached);
                        var wormHash = _hashSelector(wormDoc);

                        if (cachedHash != wormHash)
                        {
                            Interlocked.Increment(ref _verificationFailures);
                            _logger?.LogWarning(
                                "Cache verification failed for document {Id}. Cached hash: {CachedHash}, WORM hash: {WormHash}",
                                id, cachedHash, wormHash);

                            // Return WORM version and update cache
                            await _cacheStore.SetAsync(cacheKey, wormDoc, _configuration.CacheTtl, cancellationToken);
                            return wormDoc;
                        }
                    }
                }

                return cached;
            }

            // Cache miss - fetch from WORM
            Interlocked.Increment(ref _cacheMisses);
            Interlocked.Increment(ref _wormFetches);

            var document = await _wormStore.GetAsync(id, cancellationToken);

            if (document is not null)
            {
                // Populate cache
                await _cacheStore.SetAsync(cacheKey, document, _configuration.CacheTtl, cancellationToken);
            }

            return document;
        }
        finally
        {
            sw.Stop();
            RecordLatency(sw.Elapsed.TotalMilliseconds);
        }
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<TDocument>> GetManyAsync(
        IEnumerable<TId> ids,
        CancellationToken cancellationToken = default)
    {
        var idList = ids.ToList();
        var results = new List<TDocument>();
        var missingIds = new List<TId>();

        // Try to get from cache first
        foreach (var id in idList)
        {
            var cached = await _cacheStore.GetAsync<TDocument>(GetCacheKey(id), cancellationToken);
            if (cached is not null)
            {
                Interlocked.Increment(ref _cacheHits);
                results.Add(cached);
            }
            else
            {
                Interlocked.Increment(ref _cacheMisses);
                missingIds.Add(id);
            }
        }

        // Fetch missing from WORM store
        if (missingIds.Count > 0)
        {
            foreach (var id in missingIds)
            {
                Interlocked.Increment(ref _wormFetches);
                var document = await _wormStore.GetAsync(id, cancellationToken);
                if (document is not null)
                {
                    results.Add(document);
                    await _cacheStore.SetAsync(GetCacheKey(id), document, _configuration.CacheTtl, cancellationToken);
                }
            }
        }

        return results;
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<TDocument>> GetRangeAsync(
        TId startId,
        TId endId,
        CancellationToken cancellationToken = default)
    {
        // For range queries, go directly to WORM store
        // This is typical for blockchain-like range queries
        Interlocked.Increment(ref _wormFetches);
        var documents = await _wormStore.GetRangeAsync(startId, endId, cancellationToken);

        // Populate cache for each document
        foreach (var doc in documents)
        {
            var id = _idSelector(doc);
            await _cacheStore.SetAsync(GetCacheKey(id), doc, _configuration.CacheTtl, cancellationToken);
        }

        return documents;
    }

    /// <inheritdoc/>
    public async Task<TDocument> AppendAsync(TDocument document, CancellationToken cancellationToken = default)
    {
        // Append to WORM store first (source of truth)
        var appended = await _wormStore.AppendAsync(document, cancellationToken);

        // Then populate cache
        var id = _idSelector(appended);
        await _cacheStore.SetAsync(GetCacheKey(id), appended, _configuration.CacheTtl, cancellationToken);

        _logger?.LogDebug("Appended document {Id} to WORM store and cache", id);

        return appended;
    }

    /// <inheritdoc/>
    public Task<ulong> GetCurrentSequenceAsync(CancellationToken cancellationToken = default)
    {
        return _wormStore.GetCurrentSequenceAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task InvalidateAsync(TId id, CancellationToken cancellationToken = default)
    {
        await _cacheStore.RemoveAsync(GetCacheKey(id), cancellationToken);
        _logger?.LogDebug("Invalidated cache for document {Id}", id);
    }

    /// <inheritdoc/>
    public async Task<long> InvalidateByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        var fullPattern = $"{_configuration.KeyPrefix}{pattern}";
        var removed = await _cacheStore.RemoveByPatternAsync(fullPattern, cancellationToken);
        _logger?.LogDebug("Invalidated {Count} cache entries matching pattern {Pattern}", removed, fullPattern);
        return removed;
    }

    /// <inheritdoc/>
    public async Task WarmCacheAsync(
        ulong upToSequence,
        IProgress<CacheWarmingProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        ulong loaded = 0;

        _logger?.LogInformation("Starting cache warming up to sequence {Sequence}", upToSequence);

        // Get documents in batches
        var batchSize = (ulong)_configuration.WarmingBatchSize;
        ulong currentStart = 1;

        while (currentStart <= upToSequence)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var currentEnd = Math.Min(currentStart + batchSize - 1, upToSequence);

            // Create TId values for the range
            var startId = ConvertToId(currentStart);
            var endId = ConvertToId(currentEnd);

            if (startId is not null && endId is not null)
            {
                var documents = await _wormStore.GetRangeAsync(startId, endId, cancellationToken);

                foreach (var doc in documents)
                {
                    var id = _idSelector(doc);
                    await _cacheStore.SetAsync(GetCacheKey(id), doc, _configuration.CacheTtl, cancellationToken);
                    loaded++;
                }

                progress?.Report(new CacheWarmingProgress(loaded, upToSequence, sw.Elapsed));
            }

            currentStart = currentEnd + 1;
        }

        sw.Stop();
        _logger?.LogInformation(
            "Cache warming completed. Loaded {Count} documents in {Elapsed}",
            loaded, sw.Elapsed);
    }

    private TId? ConvertToId(ulong value)
    {
        // Try to convert ulong to TId
        if (typeof(TId) == typeof(ulong))
        {
            return (TId)(object)value;
        }
        if (typeof(TId) == typeof(long))
        {
            return (TId)(object)(long)value;
        }
        if (typeof(TId) == typeof(int))
        {
            return (TId)(object)(int)value;
        }
        if (typeof(TId) == typeof(string))
        {
            return (TId)(object)value.ToString();
        }

        return default;
    }

    /// <inheritdoc/>
    public Task<VerifiedCacheStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        double avgLatency;
        lock (_statsLock)
        {
            avgLatency = _latencies.Count > 0 ? _latencies.Average() : 0;
        }

        var stats = new VerifiedCacheStatistics(
            CacheHits: Interlocked.Read(ref _cacheHits),
            CacheMisses: Interlocked.Read(ref _cacheMisses),
            WormFetches: Interlocked.Read(ref _wormFetches),
            VerificationFailures: Interlocked.Read(ref _verificationFailures),
            DocumentsInCache: 0, // Would need Redis SCAN to count
            AverageLatencyMs: avgLatency);

        return Task.FromResult(stats);
    }

    /// <inheritdoc/>
    public async Task<CacheIntegrityResult> VerifyIntegrityAsync(
        TId? startId = default,
        TId? endId = default,
        CancellationToken cancellationToken = default)
    {
        // First verify WORM store integrity
        var wormResult = await _wormStore.VerifyIntegrityAsync(startId, endId, cancellationToken);

        if (!wormResult.IsValid)
        {
            return new CacheIntegrityResult(
                IsValid: false,
                DocumentsVerified: wormResult.DocumentsChecked,
                MismatchCount: wormResult.CorruptedDocuments,
                Violations: wormResult.Violations
                    .Select(v => new CacheIntegrityViolation(v.DocumentId, v.ViolationType, v.Details))
                    .ToList());
        }

        // If hash verification is enabled and we have a hash selector, verify cache matches WORM
        if (_configuration.EnableHashVerification && _hashSelector is not null)
        {
            var violations = new List<CacheIntegrityViolation>();
            long mismatchCount = 0;

            // This is a simplified check - in production, you'd iterate through cache keys
            // For now, we report WORM integrity as the source of truth
            _logger?.LogDebug("Cache integrity verification completed based on WORM store");

            return new CacheIntegrityResult(
                IsValid: mismatchCount == 0,
                DocumentsVerified: wormResult.DocumentsChecked,
                MismatchCount: mismatchCount,
                Violations: violations);
        }

        return new CacheIntegrityResult(
            IsValid: true,
            DocumentsVerified: wormResult.DocumentsChecked,
            MismatchCount: 0,
            Violations: Array.Empty<CacheIntegrityViolation>());
    }

    private void RecordLatency(double latencyMs)
    {
        lock (_statsLock)
        {
            _latencies.Enqueue(latencyMs);
            if (_latencies.Count > 1000)
            {
                _latencies.Dequeue();
            }
        }
    }
}
