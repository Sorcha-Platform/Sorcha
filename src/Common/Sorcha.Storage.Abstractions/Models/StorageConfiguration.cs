// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Storage.Abstractions;

/// <summary>
/// Root configuration for all storage tiers.
/// </summary>
public class StorageConfiguration
{
    /// <summary>
    /// Configuration section name in appsettings.json.
    /// </summary>
    public const string SectionName = "Storage";

    /// <summary>
    /// Hot tier (cache) configuration.
    /// </summary>
    public HotTierConfiguration? Hot { get; set; }

    /// <summary>
    /// Warm tier (operational) configuration.
    /// </summary>
    public WarmTierConfiguration? Warm { get; set; }

    /// <summary>
    /// Cold tier (WORM) configuration.
    /// </summary>
    public ColdTierConfiguration? Cold { get; set; }

    /// <summary>
    /// Register Service verified cache configuration.
    /// </summary>
    public RegisterCacheConfiguration? Register { get; set; }

    /// <summary>
    /// Validates that at least one tier is configured.
    /// </summary>
    public bool IsValid => Hot is not null || Warm is not null || Cold is not null;
}

/// <summary>
/// Configuration for Register Service verified cache startup verification.
/// </summary>
/// <seealso cref="Sorcha.Storage.Abstractions.Caching.VerifiedCacheConfiguration"/>
public class RegisterCacheConfiguration
{
    /// <summary>
    /// Startup verification strategy.
    /// </summary>
    public StartupStrategy StartupStrategy { get; set; } = StartupStrategy.Blocking;

    /// <summary>
    /// Docket count threshold for auto-progressive loading.
    /// Default: 1000 dockets.
    /// </summary>
    public int BlockingThreshold { get; set; } = 1000;

    /// <summary>
    /// Batch size for progressive verification.
    /// </summary>
    public int VerificationBatchSize { get; set; } = 100;

    /// <summary>
    /// Number of parallel verification workers.
    /// </summary>
    public int VerificationParallelism { get; set; } = 4;
}

/// <summary>
/// Startup verification strategy for Register Service.
/// </summary>
public enum StartupStrategy
{
    /// <summary>
    /// Block until all dockets are verified.
    /// </summary>
    Blocking,

    /// <summary>
    /// Start serving immediately, verify in background.
    /// </summary>
    Progressive
}
