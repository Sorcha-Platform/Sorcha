// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.CitizenWallet.Abstractions.Models;

namespace Sorcha.Wallet.Service.Services.Interfaces;

/// <summary>
/// Receives a batch of presentation-log entries reported by a citizen wallet,
/// dedupes each against earlier reports, and forwards the new ones to the platform
/// (Feature 114 US5).
/// </summary>
/// <remarks>
/// Backs <c>POST /api/v1/wallet/presentations/log</c>. The wallet may report the
/// same entry more than once (retry after a network blip, background-sync replay),
/// so dedupe is keyed on the wallet-generated entry id via Redis SET-NX with a 24h
/// TTL on <c>sorcha:wallet:presentation-log-dedupe:{logEntryId}</c>. Forwarding goes
/// through <see cref="IPresentationLogForwarder"/>.
/// </remarks>
public interface ICitizenPresentationLogReporter
{
    /// <summary>
    /// Dedupe and forward a batch of reported entries for one citizen.
    /// </summary>
    /// <param name="platformUserId">The citizen who made the presentations.</param>
    /// <param name="entries">The reported entries; may contain duplicates of earlier reports.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of entries that were newly accepted (not deduped).</returns>
    Task<int> ReportAsync(
        Guid platformUserId,
        IReadOnlyList<PresentationLogEntry> entries,
        CancellationToken ct = default);
}
