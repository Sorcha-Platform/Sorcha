// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Wallet.Core.Domain.Entities;

/// <summary>
/// Append-only log of credential-lifecycle events scoped to a single citizen
/// (Feature 114, US4). Backs <c>ICitizenCredentialEventStream</c> so
/// <c>CitizenSyncService</c> can return monotonic deltas to the wallet PWA.
/// </summary>
/// <remarks>
/// One row per credential lifecycle change relevant to the citizen:
/// <c>Kind=Added</c> when a new credential lands in <c>CredentialStore</c>
/// against the citizen's holder address; <c>Kind=Revoked</c> on status
/// transitions to <see cref="Sorcha.Wallet.Core.Domain.Entities.CredentialStatus.Revoked"/>
/// or <see cref="Sorcha.Wallet.Core.Domain.Entities.CredentialStatus.Declined"/>.
/// <c>Seq</c> is monotonic per <see cref="PlatformUserId"/>; the
/// <c>(PlatformUserId, Seq)</c> composite index supports
/// <c>WHERE PlatformUserId = ? AND Seq &gt; ? ORDER BY Seq</c> reads.
/// </remarks>
public class CitizenCredentialEventLog
{
    /// <summary>Surrogate primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Owning citizen account (Tenant Service's PlatformUser).</summary>
    public Guid PlatformUserId { get; set; }

    /// <summary>
    /// Monotonic event sequence within this citizen. Allocated as
    /// <c>MAX(Seq)+1</c> under a serializable transaction by the projector
    /// to guarantee strictly-increasing ordering across concurrent writes.
    /// </summary>
    public long Seq { get; set; }

    /// <summary>
    /// Event kind. Persisted as <c>integer</c>; values map to
    /// <c>Sorcha.Wallet.Service.Services.Interfaces.CitizenCredentialEventKind</c>
    /// (<c>Added=0</c>, <c>Revoked=1</c>, <c>Replaced=2</c>).
    /// </summary>
    public int Kind { get; set; }

    /// <summary>
    /// Identifier of the credential this event refers to. References
    /// <see cref="CredentialEntity.Id"/>; not a foreign key because the same
    /// credential id can live in multiple wallet rows (issuer + recipient).
    /// </summary>
    public required string CredentialId { get; set; }

    /// <summary>UTC time the event was appended.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
