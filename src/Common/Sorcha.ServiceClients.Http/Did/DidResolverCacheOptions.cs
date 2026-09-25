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

    /// <summary>
    /// Positive-result TTL for <c>did:sorcha</c> entries, in seconds. Default 60.
    /// </summary>
    /// <remarks>
    /// #1720 — this used to be infinite, relying on a register-event invalidation service that was
    /// never registered and that listened to the wrong events anyway: an org's DID document changes
    /// when its VC-issuance key is derived, rotated or revoked, none of which writes a register
    /// transaction. This TTL is the upper bound on how long a verifier can go on trusting a revoked
    /// issuance key. The Wallet Service additionally invalidates its own entry the moment it changes
    /// a key; other processes rely on this bound.
    /// </remarks>
    public int SorchaTtlSeconds { get; set; } = 60;

    /// <summary>Negative-result TTL for all methods, in seconds. Default 60.</summary>
    public int NegativeTtlSeconds { get; set; } = 60;
}
