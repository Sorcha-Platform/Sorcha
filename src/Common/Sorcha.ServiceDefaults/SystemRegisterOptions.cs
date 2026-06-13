// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.ServiceDefaults;

/// <summary>
/// Controls how the system register is obtained on first startup.
/// </summary>
public enum BootstrapMode
{
    /// <summary>
    /// Default. Brief peer sync window (3 retries, 14s), then fall back to genesis file.
    /// Suitable for local development.
    /// </summary>
    Auto = 0,

    /// <summary>
    /// Wait for peers indefinitely. Never ingest a genesis file.
    /// Suitable for production nodes joining an existing network.
    /// </summary>
    SyncOnly = 1,

    /// <summary>
    /// Ingest the configured or embedded genesis file immediately. No peer sync.
    /// Suitable for the first node creating a new network.
    /// </summary>
    GenesisFile = 2
}

/// <summary>
/// Configuration for system register genesis trust anchor.
/// Bound from the "SystemRegister" configuration section.
/// </summary>
public class SystemRegisterOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "SystemRegister";

    /// <summary>
    /// Absolute path to the genesis JSON file.
    /// When null, the embedded assembly resource is used as fallback.
    /// </summary>
    public string? GenesisFile { get; set; }

    /// <summary>
    /// Controls how the system register is obtained on first startup.
    /// </summary>
    public BootstrapMode BootstrapMode { get; set; } = BootstrapMode.Auto;

    /// <summary>
    /// Interval in seconds between retries during the fast-retry phase (SyncOnly mode).
    /// </summary>
    public int FastRetryIntervalSeconds { get; set; } = 5;

    /// <summary>
    /// Total duration in seconds of the fast-retry phase before switching to backoff polling (SyncOnly mode).
    /// </summary>
    public int FastRetryDurationSeconds { get; set; } = 120;

    /// <summary>
    /// Interval in seconds between retries during the backoff-polling phase (SyncOnly mode).
    /// </summary>
    public int BackoffIntervalSeconds { get; set; } = 300;

    /// <summary>
    /// Timeout in seconds to wait for a seed blueprint publish to seal into the system
    /// register before the publish is considered failed (Auto / GenesisFile modes).
    /// Each seed must seal before the next is published so the chain head stays fresh
    /// (issue #917).
    /// </summary>
    public int BlueprintSealTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Number of times the bootstrapper re-publishes a seed blueprint that fails to seal
    /// within <see cref="BlueprintSealTimeoutSeconds"/> before failing the bootstrap loudly.
    /// A half-seeded system register is a correctness bug, not a degraded mode — bootstrap
    /// must not report success when a seed never sealed (issue #917 resilience follow-up).
    /// </summary>
    public int BlueprintSeedMaxPublishAttempts { get; set; } = 3;
}
