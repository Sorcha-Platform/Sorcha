// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.ServiceClients.Did;

/// <summary>
/// Configuration knobs for <see cref="DidResolverCache"/>. Bound from the
/// <c>DidResolver:Cache</c> configuration section.
/// </summary>
public sealed class DidResolverCacheOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "DidResolver:Cache";

    /// <summary>Positive-result TTL for <c>did:web</c> entries, in minutes. Default 60.</summary>
    public int WebTtlMinutes { get; set; } = 60;

    /// <summary>Negative-result TTL for all methods, in seconds. Default 60.</summary>
    public int NegativeTtlSeconds { get; set; } = 60;
}
