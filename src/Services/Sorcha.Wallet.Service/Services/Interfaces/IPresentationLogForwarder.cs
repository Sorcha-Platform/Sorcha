// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.CitizenWallet.Abstractions.Models;

namespace Sorcha.Wallet.Service.Services.Interfaces;

/// <summary>
/// Forwards a single, already-deduped presentation-log entry on to its durable
/// destination so the citizen's cross-device presentation history is filled
/// (Feature 114 US5).
/// </summary>
/// <remarks>
/// This is the seam between the Wallet Service edge (which receives the citizen's
/// report, authenticates it, and absorbs hot retries via Redis SET-NX dedupe) and
/// the durable store that backs the read endpoint.
/// <para>
/// US5 PR3's implementation is <see cref="Implementation.CitizenPresentationStoreForwarder"/>,
/// which writes to the Wallet Service's own <see cref="ICitizenPresentationStore"/>.
/// Per the PR3 reconciliation there is deliberately <b>no</b> Blueprint Service
/// involvement and <b>no</b> register/ledger write — a free-standing offline
/// presentation has no originating register. Delivery is convenience-grade
/// (at-most-once-ish): PR2 marks the local entry synced on the 202 before this
/// off-request-path forward runs, so a store-write failure is logged and not
/// retried by the wallet; the store's <c>(platformUserId, entryId)</c> upsert
/// idempotency heals re-reports while the 24h SET-NX claim is live. Stronger
/// (outbox) delivery is explicitly deferred.
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
