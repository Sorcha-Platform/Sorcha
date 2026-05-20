// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.CitizenWallet.Abstractions.Models;

namespace Sorcha.Wallet.Service.Services.Interfaces;

/// <summary>
/// Forwards a single, already-deduped presentation-log entry on to the platform
/// so the Feature 111 lifecycle record can be written (Feature 114 US5).
/// </summary>
/// <remarks>
/// This is the seam between the Wallet Service edge (which receives the citizen's
/// report, authenticates it, and absorbs retries via Redis SET-NX dedupe) and the
/// Blueprint Service consumer that turns the report into an on-register lifecycle
/// transaction.
/// <para>
/// In US5 PR2 the only implementation is <see cref="Implementation.LoggingPresentationLogForwarder"/>
/// — a no-op that records the intent. The real Blueprint forward lands in PR3,
/// once the offline <c>IPresentationConsumer</c> shape is reconciled against the
/// F127 contract (consumers no longer write the register directly, and the offline
/// path has no server-side <c>InitiateAsync</c>). PR3 should also revisit
/// durability: today the entry is claimed in Redis BEFORE the forward, so a forward
/// failure orphans the dedupe claim for its 24h TTL. That is acceptable while the
/// forward is a no-op; a real forward should either release the claim on failure or
/// move to an outbox so a dropped forward stays retryable.
/// </para>
/// </remarks>
public interface IPresentationLogForwarder
{
    /// <summary>
    /// Forward one presentation-log entry for the given citizen.
    /// </summary>
    /// <param name="platformUserId">The citizen who made the presentation.</param>
    /// <param name="entry">The wallet-reported log entry (already deduped).</param>
    /// <param name="ct">Cancellation token.</param>
    Task ForwardAsync(Guid platformUserId, PresentationLogEntry entry, CancellationToken ct = default);
}
