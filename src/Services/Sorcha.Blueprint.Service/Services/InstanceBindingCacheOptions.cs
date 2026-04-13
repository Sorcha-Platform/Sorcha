// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Blueprint.Service.Services;

/// <summary>
/// Options for the per-instance participant binding cache.
/// </summary>
/// <remarks>
/// The cache sits in front of <see cref="Storage.IInstanceStore"/> and provides a
/// sub-millisecond hot path for resolving <c>instance.ParticipantWallets</c> during
/// action submission. The instance store remains the authoritative persistent record;
/// the cache is an eviction-tolerant read-through layer. See
/// <c>specs/103-verified-citizen-v2/contracts/instance-binding-cache.md</c> for the
/// full contract.
/// </remarks>
public sealed class InstanceBindingCacheOptions
{
    /// <summary>
    /// Configuration section name bound to this options type.
    /// </summary>
    public const string SectionName = "InstanceBindingCache";

    /// <summary>
    /// Sliding TTL applied to cache entries. Each read extends the expiry by this
    /// interval. Default: 1 hour.
    /// </summary>
    public TimeSpan SlidingExpiration { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Redis key prefix. Keys are formed as
    /// <c>{KeyPrefix}:instance:{instanceId}:bindings</c>. Default: empty (no prefix).
    /// Set a per-environment prefix via configuration if multiple environments share
    /// a Redis instance.
    /// </summary>
    public string KeyPrefix { get; set; } = string.Empty;
}
