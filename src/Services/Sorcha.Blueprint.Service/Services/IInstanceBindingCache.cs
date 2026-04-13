// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Blueprint.Service.Services;

/// <summary>
/// Read-through cache for per-instance participant bindings. Provides a fast hot-path
/// lookup for <c>Instance.ParticipantWallets</c> while the persistent
/// <see cref="Storage.IInstanceStore"/> remains the authoritative source of truth.
/// </summary>
/// <remarks>
/// Contract: <c>specs/103-verified-citizen-v2/contracts/instance-binding-cache.md</c>.
/// Bindings are populated by the late-binding block in
/// <c>ActionExecutionService.ExecuteAsync</c> (around line 327) when an open starting
/// action receives its first sender. They are immutable once written: attempting to
/// rebind throws at the caller site, not in this cache. The cache itself is
/// write-through on set and sliding-TTL on read.
/// </remarks>
public interface IInstanceBindingCache
{
    /// <summary>
    /// Get the participant binding map for an instance. Returns <c>null</c> if the
    /// instance has no bindings recorded anywhere reachable by the implementation
    /// (cache miss, instance store miss, ledger replay miss).
    /// </summary>
    /// <remarks>
    /// Implementations MUST follow the tiered fallback described in the contract:
    /// cache → instance store → (optional) ledger replay. On a cache miss that
    /// succeeds against a lower tier, the result is written through to the cache.
    /// </remarks>
    /// <param name="instanceId">The workflow instance identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A dictionary mapping participant id to wallet address, or <c>null</c> if the
    /// instance is unknown. Never returns a partial or stale result — if the lookup
    /// can find a binding map at any tier, that map is considered canonical for
    /// the instance at the time of the call.
    /// </returns>
    Task<IReadOnlyDictionary<string, string>?> GetAsync(
        string instanceId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Write the participant binding map for an instance into the cache. Called by
    /// the late-binding code path after a successful <c>IInstanceStore.UpdateAsync</c>
    /// write. Sliding TTL is reset on every successful write.
    /// </summary>
    /// <remarks>
    /// The instance store write is authoritative; a cache write failure does NOT
    /// fail the caller. Implementations SHOULD log warnings on cache write failures
    /// and allow the caller to continue.
    /// </remarks>
    /// <param name="instanceId">The workflow instance identifier.</param>
    /// <param name="bindings">Participant id → wallet address map.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SetAsync(
        string instanceId,
        IReadOnlyDictionary<string, string> bindings,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidate the cached binding map for an instance. Intended for operational
    /// use only; the binding contract is immutable so this is never called by normal
    /// execution paths.
    /// </summary>
    /// <param name="instanceId">The workflow instance identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task InvalidateAsync(
        string instanceId,
        CancellationToken cancellationToken = default);
}
