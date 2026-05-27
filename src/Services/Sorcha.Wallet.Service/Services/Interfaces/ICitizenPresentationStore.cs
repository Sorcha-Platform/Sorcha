// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.CitizenWallet.Abstractions.Models;

namespace Sorcha.Wallet.Service.Services.Interfaces;

/// <summary>
/// Durable per-citizen store of reported presentations, backing the cross-device
/// Activity history (Feature 114, US5 PR3). The authoritative idempotency guard
/// for presentation-log forwarding: upserts are keyed on
/// <c>(platformUserId, entryId)</c>.
/// </summary>
/// <remarks>
/// Holds <i>citizen-owned convenience data</i> — disclosed claim names only, no
/// register correlation. There is no Blueprint Service involvement and no
/// register/ledger write (FR-010 / SC-004). Registered via
/// <c>IStorageRegistrationLog</c> but deliberately <b>not</b> on the Feature 113
/// fail-fast audited list: an in-memory backend warns but does not gate startup.
/// </remarks>
public interface ICitizenPresentationStore
{
    /// <summary>
    /// Idempotently store one reported presentation for the given citizen. A
    /// re-report of an existing <c>(platformUserId, entry.Id)</c> is a no-op on
    /// identity and content; the original <c>ReportedAt</c> is preserved.
    /// </summary>
    /// <param name="platformUserId">The owning citizen (from the citizen JWT).</param>
    /// <param name="entry">The wallet-reported, already-deduped log entry.</param>
    /// <param name="ct">Cancellation token.</param>
    Task UpsertAsync(Guid platformUserId, PresentationLogEntry entry, CancellationToken ct = default);

    /// <summary>
    /// List the citizen's presentation history, newest-first.
    /// </summary>
    /// <param name="platformUserId">The owning citizen.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The citizen's entries newest-first; an empty list when none exist.</returns>
    Task<IReadOnlyList<PresentationLogEntry>> ListAsync(Guid platformUserId, CancellationToken ct = default);

    /// <summary>
    /// Delete one entry, scoped to the owning citizen. Idempotent; a delete
    /// targeting another citizen's entry, or a non-existent entry, removes nothing.
    /// </summary>
    /// <param name="platformUserId">The owning citizen.</param>
    /// <param name="entryId">The wallet-generated entry id to remove.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><c>true</c> if a row was removed; <c>false</c> otherwise (the caller still returns 204).</returns>
    Task<bool> DeleteAsync(Guid platformUserId, Guid entryId, CancellationToken ct = default);
}
