// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Storage.Abstractions.Caching;

namespace Sorcha.Register.Storage;

/// <summary>
/// Configuration for Register Service storage layer.
/// </summary>
public class RegisterStorageConfiguration
{
    /// <summary>
    /// Configuration section name in appsettings.json.
    /// </summary>
    public const string SectionName = "RegisterStorage";

    /// <summary>
    /// Redis connection string for hot-tier cache.
    /// Default: <c>localhost:6379</c>. Override via configuration for production deployments.
    /// </summary>
    public string RedisConnectionString { get; set; } = "localhost:6379";

    /// <summary>
    /// MongoDB connection string for WORM (Write-Once Read-Many) store.
    /// Default: <c>mongodb://localhost:27017</c>. Override via configuration for production deployments.
    /// </summary>
    public string MongoConnectionString { get; set; } = "mongodb://localhost:27017";

    /// <summary>
    /// MongoDB database name.
    /// Default: <c>sorcha_register</c>.
    /// </summary>
    public string MongoDatabaseName { get; set; } = "sorcha_register";

    /// <summary>
    /// Collection name for dockets in MongoDB.
    /// Default: <c>dockets</c>.
    /// </summary>
    public string DocketCollectionName { get; set; } = "dockets";

    /// <summary>
    /// Collection name for transactions in MongoDB.
    /// Default: <c>transactions</c>.
    /// </summary>
    public string TransactionCollectionName { get; set; } = "transactions";

    /// <summary>
    /// Collection name for registers in MongoDB.
    /// Default: <c>registers</c>.
    /// </summary>
    public string RegisterCollectionName { get; set; } = "registers";

    /// <summary>
    /// Verified cache configuration for dockets.
    /// Default TTL: 24 hours (86400 seconds) since dockets are immutable after creation.
    /// Hash verification is enabled by default for integrity checks.
    /// Uses progressive startup strategy with a warming batch size of 1000
    /// and a blocking threshold of 100.
    /// </summary>
    public VerifiedCacheConfiguration DocketCacheConfiguration { get; set; } = new()
    {
        KeyPrefix = "register:docket:",
        CacheTtlSeconds = 86400, // 24 hours - dockets are immutable
        EnableHashVerification = true,
        WarmingBatchSize = 1000,
        StartupStrategy = CacheStartupStrategy.Progressive,
        BlockingThreshold = 100
    };

    /// <summary>
    /// Verified cache configuration for transactions.
    /// Default TTL: 1 hour (3600 seconds) since transactions may need fresher reads.
    /// Hash verification is enabled by default for integrity checks.
    /// Uses progressive startup strategy with a warming batch size of 500
    /// and a blocking threshold of 50.
    /// </summary>
    public VerifiedCacheConfiguration TransactionCacheConfiguration { get; set; } = new()
    {
        KeyPrefix = "register:tx:",
        CacheTtlSeconds = 3600, // 1 hour
        EnableHashVerification = true,
        WarmingBatchSize = 500,
        StartupStrategy = CacheStartupStrategy.Progressive,
        BlockingThreshold = 50
    };

    /// <summary>
    /// Whether to use in-memory storage (for testing/development).
    /// Default: <c>false</c>. When <c>true</c>, MongoDB and Redis are not used;
    /// all data is stored in-memory and lost on restart.
    /// </summary>
    public bool UseInMemoryStorage { get; set; } = false;

    /// <summary>
    /// Whether to enable cache warming on startup.
    /// Default: <c>true</c>. When enabled, the cache is progressively populated
    /// from the WORM store on service start to reduce cold-start latency.
    /// </summary>
    public bool EnableCacheWarming { get; set; } = true;
}
