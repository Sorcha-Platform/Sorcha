// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Validator.Service.Configuration;

/// <summary>
/// Configuration for the blueprint cache
/// </summary>
public class BlueprintCacheConfiguration
{
    /// <summary>
    /// Configuration section name in appsettings
    /// </summary>
    public const string SectionName = "BlueprintCache";

    /// <summary>
    /// Redis key prefix for cached blueprints
    /// </summary>
    public string KeyPrefix { get; set; } = "sorcha:validator:blueprint:";

    /// <summary>
    /// Default TTL for cached blueprints
    /// </summary>
    public TimeSpan DefaultTtl { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// TTL for blueprint schema cache entries
    /// </summary>
    public TimeSpan SchemaTtl { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Maximum retries for cache operations
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Delay between retry attempts
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Whether to enable local in-memory cache as L1
    /// </summary>
    public bool EnableLocalCache { get; set; } = true;

    /// <summary>
    /// TTL for local in-memory cache (L1)
    /// </summary>
    public TimeSpan LocalCacheTtl { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Maximum entries in local cache
    /// </summary>
    public int LocalCacheMaxEntries { get; set; } = 100;

    /// <summary>
    /// Whether the cache is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Redis pub/sub channel name for cache invalidation events.
    /// When a blueprint is updated, a message is published on this channel
    /// so all validator instances can flush their local caches.
    /// </summary>
    public string InvalidationChannel { get; set; } = "sorcha:validator:blueprint:invalidation";
}
