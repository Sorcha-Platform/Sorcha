// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Blueprint.Service.Storage.Presentations;

/// <summary>
/// Per-wallet-per-register sliding-window rate limiter for presentation attempts
/// (Feature 111, FR-011). Enforced at the submission endpoint before any
/// register write or pending-state storage.
/// </summary>
public interface IPresentationRateLimiter
{
    /// <summary>
    /// Attempt to reserve capacity for one presentation attempt by this wallet on
    /// this register. Returns <c>Allowed</c> with the remaining count on success,
    /// or <c>Rejected</c> with a retry-after hint when the threshold is reached.
    /// </summary>
    Task<PresentationRateLimitResult> CheckAsync(string walletAddress, string registerId, CancellationToken ct = default);
}

public sealed record PresentationRateLimitResult(
    bool Allowed,
    long CurrentCount,
    long Threshold,
    TimeSpan? RetryAfter);
