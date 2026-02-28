// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Storage.Abstractions.Caching;

/// <summary>
/// Interface for verified cache operations.
/// Combines hot-tier cache with cold-tier verification for data integrity.
/// </summary>
/// <typeparam name="TDocument">Document type to cache.</typeparam>
/// <typeparam name="TId">Document identifier type.</typeparam>
/// <remarks>
/// The verified cache ensures that:
/// 1. Data is first retrieved from fast cache (Redis)
/// 2. On cache miss, data is fetched from WORM store (MongoDB)
/// 3. Data integrity is verified before returning
/// 4. Cache is warmed on startup using configurable strategies
/// </remarks>
public interface IVerifiedCache<TDocument, TId>
    where TDocument : class
    where TId : notnull
{
    /// <summary>
    /// Gets a document by ID with cache-aside pattern.
    /// </summary>
    /// <param name="id">Document identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Document if found and verified, null otherwise.</returns>
    Task<TDocument?> GetAsync(TId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets multiple documents by IDs with cache-aside pattern.
    /// </summary>
    /// <param name="ids">Document identifiers.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Found and verified documents.</returns>
    Task<IEnumerable<TDocument>> GetManyAsync(
        IEnumerable<TId> ids,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a range of documents (typically by sequence/height).
    /// </summary>
    /// <param name="startId">Start identifier (inclusive).</param>
    /// <param name="endId">End identifier (inclusive).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Documents in range.</returns>
    Task<IEnumerable<TDocument>> GetRangeAsync(
        TId startId,
        TId endId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends a new document to both cache and WORM store.
    /// </summary>
    /// <param name="document">Document to append.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Appended document.</returns>
    Task<TDocument> AppendAsync(TDocument document, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current sequence/height from the WORM store.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Current sequence number.</returns>
    Task<ulong> GetCurrentSequenceAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates a cached document (forces re-fetch from WORM).
    /// </summary>
    /// <param name="id">Document identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task InvalidateAsync(TId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates all cached documents matching a pattern.
    /// </summary>
    /// <param name="pattern">Key pattern (e.g., "register:*").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of entries invalidated.</returns>
    Task<long> InvalidateByPatternAsync(string pattern, CancellationToken cancellationToken = default);

    /// <summary>
    /// Warms the cache with documents up to the specified sequence.
    /// </summary>
    /// <param name="upToSequence">Maximum sequence to warm.</param>
    /// <param name="progress">Optional progress reporter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task WarmCacheAsync(
        ulong upToSequence,
        IProgress<CacheWarmingProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets cache statistics.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Cache statistics.</returns>
    Task<VerifiedCacheStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies integrity of cached documents against WORM store.
    /// </summary>
    /// <param name="startId">Optional start ID for range check.</param>
    /// <param name="endId">Optional end ID for range check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Integrity check result.</returns>
    Task<CacheIntegrityResult> VerifyIntegrityAsync(
        TId? startId = default,
        TId? endId = default,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Progress information for cache warming operations.
/// </summary>
public record CacheWarmingProgress(
    ulong DocumentsLoaded,
    ulong TotalDocuments,
    TimeSpan Elapsed)
{
    /// <summary>
    /// Percentage complete (0-100).
    /// </summary>
    public double PercentComplete => TotalDocuments > 0
        ? (double)DocumentsLoaded / TotalDocuments * 100
        : 0;

    /// <summary>
    /// Estimated time remaining based on current rate.
    /// </summary>
    public TimeSpan? EstimatedTimeRemaining
    {
        get
        {
            if (DocumentsLoaded == 0 || Elapsed.TotalSeconds == 0)
                return null;

            var rate = DocumentsLoaded / Elapsed.TotalSeconds;
            var remaining = TotalDocuments - DocumentsLoaded;
            return TimeSpan.FromSeconds(remaining / rate);
        }
    }
}

/// <summary>
/// Statistics for verified cache operations.
/// </summary>
public record VerifiedCacheStatistics(
    long CacheHits,
    long CacheMisses,
    long WormFetches,
    long VerificationFailures,
    long DocumentsInCache,
    double AverageLatencyMs)
{
    /// <summary>
    /// Cache hit rate (0.0 to 1.0).
    /// </summary>
    public double HitRate => CacheHits + CacheMisses > 0
        ? (double)CacheHits / (CacheHits + CacheMisses)
        : 0;
}

/// <summary>
/// Result of cache integrity verification.
/// </summary>
public record CacheIntegrityResult(
    bool IsValid,
    long DocumentsVerified,
    long MismatchCount,
    IReadOnlyList<CacheIntegrityViolation> Violations);

/// <summary>
/// Details of a cache integrity violation.
/// </summary>
public record CacheIntegrityViolation(
    string DocumentId,
    string ViolationType,
    string Details);

/// <summary>
/// Startup strategy for cache warming.
/// </summary>
public enum CacheStartupStrategy
{
    /// <summary>
    /// Block service startup until cache is fully warmed.
    /// Use for smaller datasets or when consistency is critical.
    /// </summary>
    Blocking,

    /// <summary>
    /// Start service immediately and warm cache progressively.
    /// Use for larger datasets where availability is preferred.
    /// </summary>
    Progressive
}

/// <summary>
/// Configuration for verified cache behavior.
/// </summary>
/// <seealso cref="Sorcha.Storage.Abstractions.RegisterCacheConfiguration"/>
public class VerifiedCacheConfiguration
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "Storage:Register";

    /// <summary>
    /// Startup strategy for cache warming.
    /// </summary>
    public CacheStartupStrategy StartupStrategy { get; set; } = CacheStartupStrategy.Progressive;

    /// <summary>
    /// For blocking strategy: maximum documents to load before timing out.
    /// </summary>
    public int BlockingThreshold { get; set; } = 1000;

    /// <summary>
    /// Cache key prefix.
    /// </summary>
    public string KeyPrefix { get; set; } = "register:";

    /// <summary>
    /// TTL for cached documents in seconds.
    /// </summary>
    public int CacheTtlSeconds { get; set; } = 3600;

    /// <summary>
    /// Batch size for cache warming operations.
    /// </summary>
    public int WarmingBatchSize { get; set; } = 100;

    /// <summary>
    /// Enable hash verification on cache hits.
    /// </summary>
    public bool EnableHashVerification { get; set; } = true;

    /// <summary>
    /// Gets the cache TTL as a TimeSpan.
    /// </summary>
    public TimeSpan CacheTtl => TimeSpan.FromSeconds(CacheTtlSeconds);
}
