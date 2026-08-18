// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Durable user-inbox surface. Phase 5 (US3) of Feature 118.
/// </summary>
/// <remarks>
/// Service implementations orchestrate the Postgres write, the unread-count
/// state, and the SignalR <c>InboxEntryAdded</c> / <c>InboxUnreadCountUpdated</c>
/// events on TenantHub. Call sites:
/// <list type="bullet">
/// <item><see cref="WriteAsync"/>: invoked from the internal endpoint <c>POST /api/internal/inbox</c>.</item>
/// <item><see cref="GetPageAsync"/> / <see cref="GetByIdAsync"/> / <see cref="GetUnreadCountAsync"/>: invoked from the public <c>GET /api/me/inbox*</c> endpoints.</item>
/// <item><see cref="MarkReadAsync"/> / <see cref="DismissAsync"/> / <see cref="MarkAllReadAsync"/>: invoked from the public <c>POST /api/me/inbox/{id}/*</c> endpoints.</item>
/// </list>
/// </remarks>
public interface IInboxService
{
    /// <summary>Write a new inbox entry. Idempotent on <c>(PlatformUserId, SourceEventId)</c>.</summary>
    /// <returns>
    /// The persisted entry. <see cref="InboxWriteResult.IsIdempotent"/> is <c>true</c>
    /// when this was a duplicate write.
    /// </returns>
    Task<InboxWriteResult> WriteAsync(InboxWriteRequest request, CancellationToken ct = default);

    /// <summary>
    /// True when <paramref name="platformUserId"/> names a real platform user (issue #1506).
    /// Callers writing on someone's behalf should check before writing, so an unknown id is a 4xx
    /// rather than a foreign-key 500.
    /// </summary>
    Task<bool> PlatformUserExistsAsync(Guid platformUserId, CancellationToken ct = default);

    /// <summary>Returns a page of the user's inbox entries, newest first. Excludes dismissed entries unless <paramref name="includeDismissed"/> is true.</summary>
    /// <param name="platformUserId">Owner of the inbox entries.</param>
    /// <param name="page">One-based page number.</param>
    /// <param name="pageSize">Maximum entries per page (capped at 100).</param>
    /// <param name="category">When non-null, restricts results to the given category.</param>
    /// <param name="unreadOnly">When true, excludes entries where <c>ReadAt</c> is set.</param>
    /// <param name="includeDismissed">When true, includes entries where <c>DismissedAt</c> is set.</param>
    /// <param name="actionableOnly">When true, returns only Actionable entries.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<InboxPage> GetPageAsync(
        Guid platformUserId,
        int page = 1,
        int pageSize = 20,
        InboxCategory? category = null,
        bool unreadOnly = false,
        bool includeDismissed = false,
        bool actionableOnly = false,
        CancellationToken ct = default);

    /// <summary>Returns a single entry, scoped to the calling user. <c>null</c> if not found or not owned.</summary>
    Task<InboxEntry?> GetByIdAsync(Guid platformUserId, Guid entryId, CancellationToken ct = default);

    /// <summary>
    /// Returns the user's unread needs-attention count — <c>Category == Action</c> or severity at
    /// <c>Warning</c> or above (issue #1267). Deliberately NOT a plain unread count: <c>Info</c>
    /// entries do not badge, so the bell keeps meaning something.
    /// </summary>
    Task<int> GetUnreadCountAsync(Guid platformUserId, CancellationToken ct = default);

    /// <summary>Marks an entry read. Idempotent. Fires <c>InboxUnreadCountUpdated</c> if state changed.</summary>
    Task<bool> MarkReadAsync(Guid platformUserId, Guid entryId, CancellationToken ct = default);

    /// <summary>Marks an entry dismissed. Idempotent. Fires <c>InboxUnreadCountUpdated</c> if the entry was unread before.</summary>
    Task<bool> DismissAsync(Guid platformUserId, Guid entryId, CancellationToken ct = default);

    /// <summary>Marks every unread entry for the user read. Returns the number of entries affected.</summary>
    Task<int> MarkAllReadAsync(Guid platformUserId, CancellationToken ct = default);
}

/// <summary>Write request shape for <see cref="IInboxService.WriteAsync"/>.</summary>
public sealed record InboxWriteRequest(
    Guid PlatformUserId,
    InboxCategory Category,
    InboxSeverity Severity,
    string CorrelationKey,
    string DetailHref,
    Guid SourceEventId,
    DateTimeOffset OccurredAt,
    string Title,
    string? Summary = null,
    string? IconKey = null,
    ChannelHints? ChannelHints = null,
    Guid? WriterServiceId = null);

/// <summary>Result of <see cref="IInboxService.WriteAsync"/>.</summary>
public sealed record InboxWriteResult(InboxEntry Entry, bool IsIdempotent);

/// <summary>Paginated inbox listing response.</summary>
public sealed record InboxPage(IReadOnlyList<InboxEntry> Entries, int Page, int PageSize, int TotalCount);
