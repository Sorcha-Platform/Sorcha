// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Storage;

/// <summary>
/// Storage abstraction over <see cref="InboxEntry"/> rows. Owns every read and
/// write of inbox state; <c>InboxService</c> orchestrates idempotency, SignalR
/// fan-out, and logging on top of this surface.
/// </summary>
/// <remarks>
/// Audited under Feature 113 — Production / Staging refuse to start when this
/// interface lands on an in-memory implementation. Tenant Service always has
/// Postgres in those environments, so only <see cref="EfCoreInboxStore"/>
/// ships today; tests use the InMemory EF provider via the same impl.
/// </remarks>
public interface IInboxStore
{
    /// <summary>
    /// Adds the candidate entry, or returns the row that already matches the
    /// (PlatformUserId, SourceEventId) idempotency key. Handles the
    /// concurrent-write unique-index race transparently.
    /// </summary>
    Task<InboxAddResult> AddOrFindAsync(InboxEntry candidate, CancellationToken ct = default);

    /// <summary>
    /// True when <paramref name="platformUserId"/> names a real platform user.
    /// </summary>
    /// <remarks>
    /// Issue #1506. <c>InboxEntries.PlatformUserId</c> is a foreign key, so writing an id that is
    /// not a platform user reaches Postgres and throws. That is a 500 caused entirely by the
    /// caller's argument — and on n1 a run of those 500s tripped a circuit breaker that then
    /// blocked credential issuance, so a best-effort notification write took out the operation it
    /// was meant to describe. Checking first turns it into the 4xx it always was.
    /// </remarks>
    Task<bool> PlatformUserExistsAsync(Guid platformUserId, CancellationToken ct = default);

    /// <summary>Returns a page of entries, newest first, with optional filters.</summary>
    /// <param name="platformUserId">Owner of the inbox entries.</param>
    /// <param name="page">One-based page number.</param>
    /// <param name="pageSize">Maximum entries per page.</param>
    /// <param name="category">When non-null, restricts results to the given category.</param>
    /// <param name="unreadOnly">When true, excludes entries where <c>ReadAt</c> is set.</param>
    /// <param name="includeDismissed">When true, includes entries where <c>DismissedAt</c> is set.</param>
    /// <param name="actionableOnly">When true, returns only entries where <c>Category == Action</c> or <c>Severity >= ActionRequired</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<InboxPageResult> GetPageAsync(
        Guid platformUserId,
        int page,
        int pageSize,
        InboxCategory? category,
        bool unreadOnly,
        bool includeDismissed,
        bool actionableOnly = false,
        CancellationToken ct = default);

    /// <summary>Returns one entry, scoped to the owning user.</summary>
    Task<InboxEntry?> GetByIdAsync(Guid platformUserId, Guid entryId, CancellationToken ct = default);

    /// <summary>Returns the user's unread, non-dismissed count.</summary>
    /// <param name="platformUserId">Owner of the inbox entries.</param>
    /// <param name="actionableOnly">When true, returns only entries where <c>Category == Action</c> or <c>Severity >= ActionRequired</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<int> GetUnreadCountAsync(Guid platformUserId, bool actionableOnly = false, CancellationToken ct = default);

    /// <summary>Marks an entry read. Idempotent.</summary>
    Task<InboxMarkReadResult> MarkReadAsync(Guid platformUserId, Guid entryId, CancellationToken ct = default);

    /// <summary>Marks an entry dismissed. Idempotent.</summary>
    Task<InboxDismissResult> DismissAsync(Guid platformUserId, Guid entryId, CancellationToken ct = default);

    /// <summary>Marks every unread, non-dismissed entry for the user read. Returns the count affected.</summary>
    Task<int> MarkAllReadAsync(Guid platformUserId, CancellationToken ct = default);
}

/// <summary>Outcome of <see cref="IInboxStore.AddOrFindAsync"/>.</summary>
/// <param name="Entry">The persisted entry (newly added or pre-existing).</param>
/// <param name="IsIdempotent"><c>true</c> when the candidate matched an existing row.</param>
public sealed record InboxAddResult(InboxEntry Entry, bool IsIdempotent);

/// <summary>Outcome of <see cref="IInboxStore.GetPageAsync"/>.</summary>
public sealed record InboxPageResult(IReadOnlyList<InboxEntry> Entries, int TotalCount);

/// <summary>Outcome of <see cref="IInboxStore.MarkReadAsync"/>.</summary>
/// <param name="Found"><c>true</c> if the entry exists for the user.</param>
/// <param name="StateChanged"><c>true</c> if this call transitioned ReadAt from null to a value.</param>
public sealed record InboxMarkReadResult(bool Found, bool StateChanged);

/// <summary>Outcome of <see cref="IInboxStore.DismissAsync"/>.</summary>
/// <param name="Found"><c>true</c> if the entry exists for the user.</param>
/// <param name="StateChanged"><c>true</c> if this call transitioned DismissedAt from null to a value.</param>
/// <param name="WasUnread"><c>true</c> if the entry was unread (and not already dismissed) at the moment of dismissal.</param>
public sealed record InboxDismissResult(bool Found, bool StateChanged, bool WasUnread);
