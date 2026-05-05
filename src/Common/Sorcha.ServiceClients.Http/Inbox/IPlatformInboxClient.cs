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
}

/// <summary>Wire shape sent to the internal inbox endpoint.</summary>
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
    string? IconKey = null);

/// <summary>Outcome returned by the internal inbox endpoint.</summary>
public sealed record InboxWriteOutcome(Guid EntryId, bool Idempotent);
