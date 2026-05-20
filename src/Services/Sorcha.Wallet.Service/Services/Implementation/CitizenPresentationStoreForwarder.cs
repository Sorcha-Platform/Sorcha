// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.CitizenWallet.Abstractions.Models;
using Sorcha.Wallet.Service.Services.Interfaces;

namespace Sorcha.Wallet.Service.Services.Implementation;

/// <summary>
/// US5 PR3 <see cref="IPresentationLogForwarder"/>. Persists each newly-deduped
/// presentation-log entry into the citizen's durable
/// <see cref="ICitizenPresentationStore"/>, giving the citizen cross-device
/// presentation history (Feature 114, US5).
/// </summary>
/// <remarks>
/// Replaces PR2's logging no-op. The forward target is the Wallet Service's own
/// store — there is deliberately <b>no</b> Blueprint Service involvement, no
/// <c>IPresentationConsumer</c>, and no register/ledger write: a free-standing
/// offline presentation has no originating register (see
/// <c>docs/superpowers/specs/2026-05-20-f114-us5-offline-presentation-reconciliation-design.md</c>).
/// <para>
/// <see cref="ICitizenPresentationStore.UpsertAsync"/> is idempotent on
/// <c>(platformUserId, entryId)</c> — the authoritative dedupe — so a re-report
/// (after PR2's 24h Redis SET-NX claim expires) heals safely.
/// </para>
/// </remarks>
public sealed class CitizenPresentationStoreForwarder : IPresentationLogForwarder
{
    private readonly ICitizenPresentationStore _store;

    /// <summary>Initialise a new instance.</summary>
    public CitizenPresentationStoreForwarder(ICitizenPresentationStore store)
        => _store = store ?? throw new ArgumentNullException(nameof(store));

    /// <inheritdoc />
    public Task ForwardAsync(Guid platformUserId, PresentationLogEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return _store.UpsertAsync(platformUserId, entry, ct);
    }
}
