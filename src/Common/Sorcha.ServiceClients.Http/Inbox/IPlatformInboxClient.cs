// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.ServiceClients.Inbox;

/// <summary>
/// Service-to-service HTTP client for the Tenant Service internal inbox-write
/// endpoint. Phase 5 (US3) of Feature 118.
/// </summary>
/// <remarks>
/// Used by emitter services (Blueprint, Wallet, future inbox writers) to drop
/// a user-facing notification into the durable inbox owned by Tenant Service.
/// Idempotent on <c>(PlatformUserId, SourceEventId)</c> — duplicate POSTs
/// resolve to the original entry.
/// </remarks>
public interface IPlatformInboxClient
{
    /// <summary>
    /// POST <c>/api/internal/inbox</c>. Returns the persisted entry id and
    /// whether the write was idempotent (duplicate of an earlier write).
    /// </summary>
    Task<InboxWriteOutcome> WriteAsync(InboxWritePayload payload, CancellationToken ct = default);

    /// <summary>
    /// GET <c>/api/internal/users/by-identity/{userIdentityId}</c>. Resolves the
    /// org-scoped <c>UserIdentity.Id</c> (which the Participant Service surface
    /// hands back via <c>ParticipantInfo.UserId</c>) to the cross-org
    /// <c>PlatformUser.Id</c> the inbox is addressed by.
    /// </summary>
    /// <returns>The platform user id, or <c>null</c> if no UserIdentity matches.</returns>
    Task<Guid?> ResolvePlatformUserIdAsync(Guid userIdentityId, CancellationToken ct = default);

    /// <summary>
    /// Whether <paramref name="platformUserId"/> names a real platform user.
    /// </summary>
    /// <remarks>
    /// Issue #1506. A caller that has a GUID and believes it is a PlatformUserId has no way to
    /// check that by shape — every id in the system is a GUID — so the belief used to be verified
    /// only by the foreign key, as a 500 on a best-effort notification write. Returns false when
    /// the id is unknown, and false when the check itself cannot be made: an unverified id must
    /// never be written, and skipping a notice is the cheaper failure.
    /// </remarks>
    Task<bool> PlatformUserExistsAsync(Guid platformUserId, CancellationToken ct = default);
}

/// <summary>Wire shape sent to the internal inbox endpoint.</summary>
/// <param name="ChannelHints">
/// Optional bit-flag mask (matches the Tenant <c>ChannelHints</c> enum:
/// <c>Inbox=1, Push=2, Email=4, Digest=8</c>). When omitted, Tenant applies
/// the per-category default. T076 sets <c>Inbox|Digest = 9</c> for digest
/// notifications.
/// </param>
public sealed record InboxWritePayload(
    Guid PlatformUserId,
    string Category,
    string Severity,
    string CorrelationKey,
    string DetailHref,
    Guid SourceEventId,
    DateTimeOffset OccurredAt,
    string Title,
    string? Summary = null,
    string? IconKey = null,
    int? ChannelHints = null);

/// <summary>Outcome returned by the internal inbox endpoint.</summary>
public sealed record InboxWriteOutcome(Guid EntryId, bool Idempotent);
