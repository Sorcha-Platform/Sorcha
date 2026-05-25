// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.ServiceDefaults;

/// <summary>
/// Options for the shared wall-clock skew tolerance used by Feature 138 freshness checks
/// (KB-JWT <c>exp</c>, delegation <c>exp</c>, status-list freshness, peer heartbeat timestamp).
/// Bound from the <c>Verifier</c> configuration section so a single value governs every
/// boundary's tolerance for honest clock drift.
/// </summary>
public sealed class ClockSkewOptions
{
    /// <summary>Configuration section these options bind from.</summary>
    public const string SectionName = "Verifier";

    /// <summary>
    /// Wall-clock tolerance, in seconds, applied to expiry and freshness checks. Secure
    /// default 60s — wide enough to absorb honest NTP drift, narrow enough to keep replay
    /// windows tight. (<c>Verifier:ClockSkewSeconds</c>.)
    /// </summary>
    public int ClockSkewSeconds { get; set; } = 60;

    /// <summary>The configured skew as a <see cref="TimeSpan"/>.</summary>
    public TimeSpan Skew => TimeSpan.FromSeconds(ClockSkewSeconds);
}

/// <summary>
/// Pure, dependency-free wall-clock comparisons that apply a bounded <c>skew</c>
/// tolerance. Fail-closed by construction: a value at or past its boundary (after adding skew)
/// is treated as expired/stale. Callers supply <c>now</c> from an injected
/// <see cref="TimeProvider"/> so the checks are deterministic under test.
/// </summary>
public static class ClockSkew
{
    /// <summary>
    /// True when <paramref name="expiresAt"/> has passed relative to <paramref name="now"/>,
    /// allowing <paramref name="skew"/> of tolerance. Use to reject expired tokens/lists.
    /// </summary>
    public static bool IsExpired(DateTimeOffset expiresAt, DateTimeOffset now, TimeSpan skew) =>
        now > expiresAt + skew;

    /// <summary>
    /// True when <paramref name="timestamp"/> is within <paramref name="skew"/> of
    /// <paramref name="now"/> in either direction — i.e. fresh enough to accept. A timestamp
    /// too far in the past (stale/replayed) or implausibly far in the future is rejected.
    /// </summary>
    public static bool IsFresh(DateTimeOffset timestamp, DateTimeOffset now, TimeSpan skew)
    {
        var delta = now - timestamp;
        return delta <= skew && delta >= -skew;
    }
}
