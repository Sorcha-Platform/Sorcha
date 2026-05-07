// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.ObjectModel;

namespace Sorcha.CitizenWallet.Abstractions.Models;

/// <summary>
/// Reason a credential was revoked.
/// </summary>
public enum CredentialRevocationReason
{
    Superseded = 0,
    Erroneous = 1,
    Compromised = 2,
    Expired = 3,
    Withdrawn = 4,
    Regulatory = 5
}

/// <summary>
/// Sync delta entry: a credential the wallet was holding has been revoked upstream.
/// </summary>
public sealed record RevokedCredentialEntry
{
    /// <summary>
    /// Credential identifier the wallet should mark as revoked. Matches
    /// <c>CredentialEntity.Id</c> (typically a URN like
    /// <c>urn:credential:AssuredIdentityCredential:...</c>).
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Revocation reason.</summary>
    public CredentialRevocationReason Reason { get; init; }

    /// <summary>UTC time of revocation.</summary>
    public DateTimeOffset RevokedAt { get; init; }
}

/// <summary>
/// Sync delta entry: an old credential has been replaced by a new one (e.g. re-issuance
/// after attribute change). The wallet should remove <see cref="OldId"/> and add the new payload.
/// </summary>
public sealed record ReplacedCredentialEntry
{
    /// <summary>
    /// Identifier of the old (now-superseded) credential. Matches
    /// <c>CredentialEntity.Id</c>.
    /// </summary>
    public string OldId { get; init; } = string.Empty;

    /// <summary>
    /// Identifier of the new credential. Matches <c>CredentialEntity.Id</c>.
    /// </summary>
    public string NewId { get; init; } = string.Empty;

    /// <summary>Compact-serialised SD-JWT VC of the new credential.</summary>
    public string Jwt { get; init; } = string.Empty;

    /// <summary>UTC issuance time of the new credential.</summary>
    public DateTimeOffset IssuedAt { get; init; }
}

/// <summary>
/// Credential changes since the last sync.
/// </summary>
public sealed record SyncCredentialChanges
{
    /// <summary>New credentials to add to the cache.</summary>
    public IReadOnlyList<CachedCredentialPayload> Added { get; init; } =
        new ReadOnlyCollection<CachedCredentialPayload>([]);

    /// <summary>Credentials to mark as revoked.</summary>
    public IReadOnlyList<RevokedCredentialEntry> Revoked { get; init; } =
        new ReadOnlyCollection<RevokedCredentialEntry>([]);

    /// <summary>Credentials replaced by re-issuance.</summary>
    public IReadOnlyList<ReplacedCredentialEntry> Replaced { get; init; } =
        new ReadOnlyCollection<ReplacedCredentialEntry>([]);
}

/// <summary>
/// Delegation update bundled with the sync response. <see cref="Renewed"/> indicates
/// whether the server issued a fresh delegation in this sync (silent renewal path).
/// </summary>
public sealed record SyncDelegationUpdate
{
    /// <summary><c>true</c> if a new delegation credential is included.</summary>
    public bool Renewed { get; init; }

    /// <summary>The renewed delegation, if <see cref="Renewed"/> is true.</summary>
    public string? Jwt { get; init; }

    /// <summary>UTC expiry of the renewed delegation, if applicable.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }
}

/// <summary>
/// Response body for <c>GET /api/v1/wallet/sync</c>. Carries the delta since the
/// caller's previous sync token, plus any silent delegation renewal and a list
/// of status-list URIs the wallet should refresh.
/// </summary>
public sealed record SyncResponse
{
    /// <summary>
    /// Opaque cursor — must be passed back as <c>since</c> on the next sync to
    /// receive the next delta. Server-signed; do not parse client-side.
    /// </summary>
    public string SyncToken { get; init; } = string.Empty;

    /// <summary>Credential adds / revokes / replacements since the last sync.</summary>
    public SyncCredentialChanges Credentials { get; init; } = new();

    /// <summary>Optional delegation renewal piggybacking on this sync response.</summary>
    public SyncDelegationUpdate Delegation { get; init; } = new();

    /// <summary>Status list URIs the wallet should refresh now (cache may be stale).</summary>
    public IReadOnlyList<string> StatusListsToRefresh { get; init; } =
        new ReadOnlyCollection<string>([]);
}
