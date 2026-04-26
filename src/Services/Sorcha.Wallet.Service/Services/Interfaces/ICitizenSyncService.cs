// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.CitizenWallet.Abstractions.Models;

namespace Sorcha.Wallet.Service.Services.Interfaces;

/// <summary>
/// Composes citizen wallet sync responses (Feature 114, T103). Reads the
/// citizen's credential events stream, the current device delegation status,
/// and the set of status lists referenced by their credentials. Mints and
/// validates the opaque sync cursor (research §R-006: signed compact JWT
/// with claims <c>{ sub, lastEventSeq, iat }</c>).
/// </summary>
public interface ICitizenSyncService
{
    /// <summary>
    /// Compose the delta sync response since the supplied cursor.
    /// Pass <paramref name="sinceToken"/> = null on first sync.
    /// </summary>
    /// <returns>The sync response, or null if the token is too old to service (caller returns 410).</returns>
    Task<SyncResponse?> ComposeDeltaAsync(
        Guid platformUserId,
        string holderKeyId,
        string? sinceToken,
        CancellationToken ct = default);

    /// <summary>
    /// Compose a full credential snapshot (used by <c>GET /wallet/credentials</c>
    /// — fresh wallets seed their cache via this path).
    /// </summary>
    Task<CredentialListResponse> ListAllCredentialsAsync(
        Guid platformUserId,
        string holderKeyId,
        CancellationToken ct = default);
}

/// <summary>
/// Source of credential events for a citizen. v1 returns an empty enumeration
/// because the citizen-credential issuance pipeline lands with US4 / Feature
/// 114 Phase 6. The interface exists now so the sync surface, sync tokens,
/// and tests are stable when real events flow in.
/// </summary>
public interface ICitizenCredentialEventStream
{
    /// <summary>Events strictly newer than <paramref name="afterSeq"/>, ordered ascending.</summary>
    Task<IReadOnlyList<CitizenCredentialEvent>> ReadAsync(
        Guid platformUserId,
        long afterSeq,
        CancellationToken ct = default);

    /// <summary>Returns the highest event sequence currently observable for this citizen.</summary>
    Task<long> GetHighestSeqAsync(Guid platformUserId, CancellationToken ct = default);
}

/// <summary>One change observed in a citizen's credential set.</summary>
/// <param name="Seq">Monotonic event sequence within this citizen.</param>
/// <param name="Kind">Event kind: <c>Added</c>, <c>Revoked</c>, or <c>Replaced</c>.</param>
/// <param name="Payload">
/// Kind-specific payload — typed under <see cref="Sorcha.CitizenWallet.Abstractions.Models"/>:
/// <see cref="CachedCredentialPayload"/>, <see cref="RevokedCredentialEntry"/>,
/// or <see cref="ReplacedCredentialEntry"/>.
/// </param>
public sealed record CitizenCredentialEvent(long Seq, CitizenCredentialEventKind Kind, object Payload);

/// <summary>Kinds of credential events the wallet observes through sync.</summary>
public enum CitizenCredentialEventKind
{
    /// <summary>A new credential was issued to this citizen.</summary>
    Added = 0,
    /// <summary>An existing credential was revoked.</summary>
    Revoked = 1,
    /// <summary>An existing credential was replaced by a newer-version credential.</summary>
    Replaced = 2
}
