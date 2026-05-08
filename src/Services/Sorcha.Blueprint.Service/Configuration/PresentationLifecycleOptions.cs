// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Blueprint.Service.Configuration;

/// <summary>
/// Deployment knobs for the Timebound Presentation Lifecycle (Feature 111).
/// Bound from the <c>PresentationLifecycle</c> configuration section.
/// </summary>
public sealed class PresentationLifecycleOptions
{
    /// <summary>Default validity window when a blueprint does not override (seconds).</summary>
    public int DefaultValidityWindowSeconds { get; set; } = 600;

    /// <summary>Abandonment sweeper tick interval (seconds).</summary>
    public int SweeperIntervalSeconds { get; set; } = 30;

    /// <summary>Redis leader-lock TTL for the sweeper in HA deployments (seconds).</summary>
    public int SweeperLeaderLockTtlSeconds { get; set; } = 60;

    /// <summary>
    /// Tick cadence for the seal-aware ordering recovery sweeper (Feature 119).
    /// The sweeper drains queue entries whose predecessor sealed without a
    /// <c>transaction:confirmed</c> event being observed, and fails entries past
    /// their TTL with a structured timeout. Default 5 seconds (research R3).
    /// </summary>
    public int SealRecoverySweepIntervalSeconds { get; set; } = 5;

    /// <summary>Rate limit settings (per-wallet-per-register).</summary>
    public RateLimitOptions RateLimit { get; set; } = new();

    public sealed class RateLimitOptions
    {
        public int Threshold { get; set; } = 10;
        public int WindowSeconds { get; set; } = 600;
    }
}
