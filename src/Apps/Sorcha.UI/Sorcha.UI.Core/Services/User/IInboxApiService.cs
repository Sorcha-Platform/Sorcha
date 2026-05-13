// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Services;

/// <summary>
/// UI-side wrapper around the Tenant Service inbox endpoints
/// (<c>/api/me/inbox/*</c>). Phase 5 of Feature 118.
/// </summary>
/// <remarks>
/// Future UI rewrites — <c>PendingActionInbox.razor</c>,
/// <c>ActivityLogPanel.razor</c>, <c>PendingActionToast.razor</c> — call
/// this surface to render the durable inbox. The complement to
/// <see cref="TenantHubConnection"/> for realtime nudges.
/// </remarks>
public interface IInboxApiService
{
    /// <summary>List the authenticated user's inbox entries, newest first.</summary>
    /// <param name="page">1-based page number.</param>
    /// <param name="pageSize">Page size (1..100, server clamps).</param>
    /// <param name="category">Optional filter — one of Action, Credential, Membership, Security, System, Workflow, Custom.</param>
    /// <param name="unreadOnly">When true, returns only entries with no <c>ReadAt</c>.</param>
    /// <param name="includeDismissed">When true, includes dismissed entries (default excludes).</param>
    Task<InboxPageDto?> ListAsync(
        int page = 1,
        int pageSize = 20,
        string? category = null,
        bool unreadOnly = false,
        bool includeDismissed = false,
        CancellationToken ct = default);

    /// <summary>Fetch a single entry. Returns <c>null</c> if not found or not owned.</summary>
    Task<InboxEntryDto?> GetByIdAsync(Guid entryId, CancellationToken ct = default);

    /// <summary>Server-authoritative unread count.</summary>
    Task<int> GetUnreadCountAsync(CancellationToken ct = default);

    /// <summary>Mark an entry as read. Idempotent.</summary>
    Task<bool> MarkReadAsync(Guid entryId, CancellationToken ct = default);

    /// <summary>Dismiss an entry. Idempotent.</summary>
    Task<bool> DismissAsync(Guid entryId, CancellationToken ct = default);

    /// <summary>Mark every unread entry read for the authenticated user. Returns count affected.</summary>
    Task<int> MarkAllReadAsync(CancellationToken ct = default);
}

/// <summary>Wire shape for an inbox entry returned by the Tenant Service inbox endpoints.</summary>
public sealed record InboxEntryDto
{
    public required Guid Id { get; init; }
    public required Guid PlatformUserId { get; init; }
    public required string Category { get; init; }
    public required string Severity { get; init; }
    public required string CorrelationKey { get; init; }
    public required string DetailHref { get; init; }
    public required Guid SourceEventId { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
    public DateTimeOffset? ReadAt { get; init; }
    public DateTimeOffset? DismissedAt { get; init; }
    public required string Title { get; init; }
    public string? Summary { get; init; }
    public string? IconKey { get; init; }
    public string? ChannelHints { get; init; }
}

/// <summary>Wire shape for the paginated inbox listing.</summary>
public sealed record InboxPageDto(IReadOnlyList<InboxEntryDto> Entries, int Page, int PageSize, int TotalCount);
