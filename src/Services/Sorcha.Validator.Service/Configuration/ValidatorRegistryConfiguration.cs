// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Validator.Service.Configuration;

/// <summary>
/// Configuration for the validator registry service
/// </summary>
public class ValidatorRegistryConfiguration
{
    /// <summary>
    /// Configuration section name
    /// </summary>
    public const string SectionName = "ValidatorRegistry";

    /// <summary>
    /// Redis key prefix for validator data
    /// </summary>
    public string KeyPrefix { get; set; } = "sorcha:validators:";

    /// <summary>
    /// Cache TTL for validator list
    /// </summary>
    public TimeSpan CacheTtl { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Enable local L1 cache
    /// </summary>
    public bool EnableLocalCache { get; set; } = true;

    /// <summary>
    /// Local cache TTL (shorter than Redis)
    /// </summary>
    public TimeSpan LocalCacheTtl { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Maximum entries in local cache
    /// </summary>
    public int LocalCacheMaxEntries { get; set; } = 100;

    /// <summary>
    /// Max retries for Redis operations
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Retry delay for Redis operations
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// How often to refresh validator list from source
    /// </summary>
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Default operational TTL in seconds for validator heartbeat presence.
    /// Used when the register policy is unavailable. Policy value takes precedence.
    /// Must be >= 10.
    /// </summary>
    public int DefaultOperationalTtlSeconds { get; set; } = 60;

    /// <summary>
    /// MongoDB database name for durable validator registry storage.
    /// Validators are persisted to MongoDB (write-through) with Redis as cache.
    /// </summary>
    public string MongoDatabase { get; set; } = "sorcha-validators";

    /// <summary>
    /// MongoDB collection name for validator registrations.
    /// </summary>
    public string ValidatorsCollection { get; set; } = "validators";

    /// <summary>
    /// MongoDB collection name for validator audit trail.
    /// </summary>
    public string AuditCollection { get; set; } = "validator_audit";

    /// <summary>
    /// MongoDB collection name for wallet sequence numbers (replay protection).
    /// </summary>
    public string WalletSequencesCollection { get; set; } = "wallet_sequences";
}
