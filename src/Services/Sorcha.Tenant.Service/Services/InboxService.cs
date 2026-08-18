// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.SignalR;
using Sorcha.Tenant.Service.Hubs;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Storage;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Default <see cref="IInboxService"/> implementation. Delegates persistence to
/// <see cref="IInboxStore"/> (audited under Feature 113); orchestrates idempotent
/// writes, default channel-hint resolution, and SignalR fan-out to TenantHub on
/// every state transition.
/// </summary>
/// <remarks>
/// <para>
/// Phase 5 v1: unread-count is computed via <c>COUNT(*)</c>. The Redis sorted-set
/// index from research R-002 is a phase-5 follow-up — at this user volume the
/// COUNT query is sub-millisecond, and adding the Redis index without first
/// observing real load would be premature.
/// </para>
/// <para>
/// SignalR emits use the untyped <c>SendAsync</c> path because
/// <see cref="ITenantHubClient"/> is empty until Phase 5 also lands the hub
/// methods themselves. The methods below send under the same names that
/// the typed-client interface declares, so the contract stays consistent.
/// </para>
/// </remarks>
public sealed class InboxService : IInboxService
{
    private readonly IInboxStore _store;
    private readonly IHubContext<TenantHub> _hub;
    private readonly ILogger<InboxService> _logger;

    /// <summary>Initialises a new <see cref="InboxService"/>.</summary>
    public InboxService(IInboxStore store, IHubContext<TenantHub> hub, ILogger<InboxService> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _hub = hub ?? throw new ArgumentNullException(nameof(hub));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    /// <inheritdoc />
    public Task<bool> PlatformUserExistsAsync(Guid platformUserId, CancellationToken ct = default) =>
        _store.PlatformUserExistsAsync(platformUserId, ct);

    public async Task<InboxWriteResult> WriteAsync(InboxWriteRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var candidate = new InboxEntry
        {
            Id = Guid.NewGuid(),
            PlatformUserId = request.PlatformUserId,
            Category = request.Category,
            Severity = request.Severity,
            CorrelationKey = request.CorrelationKey,
            DetailHref = request.DetailHref,
            SourceEventId = request.SourceEventId,
            OccurredAt = request.OccurredAt,
            Title = request.Title,
            Summary = request.Summary,
            IconKey = request.IconKey,
            ChannelHints = request.ChannelHints ?? DefaultChannelHintsFor(request.Category),
            WriterServiceId = request.WriterServiceId,
        };

        var addResult = await _store.AddOrFindAsync(candidate, ct).ConfigureAwait(false);

        if (addResult.IsIdempotent)
        {
            _logger.LogDebug(
                "Inbox idempotent write — entry already exists. PlatformUserId={PlatformUserId} SourceEventId={SourceEventId} EntryId={EntryId}",
                request.PlatformUserId, request.SourceEventId, addResult.Entry.Id);
            return new InboxWriteResult(addResult.Entry, IsIdempotent: true);
        }

        _logger.LogInformation(
            "Inbox entry written. PlatformUserId={PlatformUserId} EntryId={EntryId} Category={Category} CorrelationKey={CorrelationKey}",
            addResult.Entry.PlatformUserId, addResult.Entry.Id, addResult.Entry.Category, addResult.Entry.CorrelationKey);

        await EmitInboxEntryAddedAsync(addResult.Entry, ct).ConfigureAwait(false);
        await EmitUnreadCountAsync(addResult.Entry.PlatformUserId, ct).ConfigureAwait(false);

        return new InboxWriteResult(addResult.Entry, IsIdempotent: false);
    }

    /// <inheritdoc />
    public async Task<InboxPage> GetPageAsync(
        Guid platformUserId,
        int page = 1,
        int pageSize = 20,
        InboxCategory? category = null,
        bool unreadOnly = false,
        bool includeDismissed = false,
        bool actionableOnly = false,
        CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 100) pageSize = 100;

        var result = await _store.GetPageAsync(
            platformUserId, page, pageSize, category, unreadOnly, includeDismissed, actionableOnly, ct)
            .ConfigureAwait(false);

        return new InboxPage(result.Entries, page, pageSize, result.TotalCount);
    }

    /// <inheritdoc />
    public Task<InboxEntry?> GetByIdAsync(Guid platformUserId, Guid entryId, CancellationToken ct = default)
        => _store.GetByIdAsync(platformUserId, entryId, ct);

    /// <inheritdoc />
    public Task<int> GetUnreadCountAsync(Guid platformUserId, CancellationToken ct = default)
        => _store.GetUnreadCountAsync(platformUserId, actionableOnly: true, ct);

    /// <inheritdoc />
    public async Task<bool> MarkReadAsync(Guid platformUserId, Guid entryId, CancellationToken ct = default)
    {
        var result = await _store.MarkReadAsync(platformUserId, entryId, ct).ConfigureAwait(false);
        if (!result.Found)
        {
            return false;
        }

        if (result.StateChanged)
        {
            await EmitUnreadCountAsync(platformUserId, ct).ConfigureAwait(false);
        }
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> DismissAsync(Guid platformUserId, Guid entryId, CancellationToken ct = default)
    {
        var result = await _store.DismissAsync(platformUserId, entryId, ct).ConfigureAwait(false);
        if (!result.Found)
        {
            return false;
        }

        if (result.StateChanged && result.WasUnread)
        {
            await EmitUnreadCountAsync(platformUserId, ct).ConfigureAwait(false);
        }
        return true;
    }

    /// <inheritdoc />
    public async Task<int> MarkAllReadAsync(Guid platformUserId, CancellationToken ct = default)
    {
        var affected = await _store.MarkAllReadAsync(platformUserId, ct).ConfigureAwait(false);
        if (affected > 0)
        {
            await EmitUnreadCountAsync(platformUserId, ct).ConfigureAwait(false);
        }
        return affected;
    }

    private static ChannelHints DefaultChannelHintsFor(InboxCategory category) => category switch
    {
        InboxCategory.Action       => ChannelHints.Inbox | ChannelHints.Push | ChannelHints.Email,
        InboxCategory.Credential   => ChannelHints.Inbox | ChannelHints.Push,
        InboxCategory.Membership   => ChannelHints.Inbox | ChannelHints.Email,
        InboxCategory.Security     => ChannelHints.Inbox | ChannelHints.Push | ChannelHints.Email,
        InboxCategory.System       => ChannelHints.Inbox,
        InboxCategory.Workflow     => ChannelHints.Inbox,
        _                          => ChannelHints.Inbox,
    };

    private async Task EmitInboxEntryAddedAsync(InboxEntry entry, CancellationToken ct)
    {
        try
        {
            // Thin signal: entry id, occurredAt, traceId. No content on the wire.
            await _hub.Clients.Group(TenantHubGroups.User(entry.PlatformUserId))
                .SendAsync(
                    "InboxEntryAdded",
                    entry.Id.ToString("N"),
                    entry.OccurredAt,
                    System.Diagnostics.Activity.Current?.TraceId.ToString() ?? "",
                    ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to emit InboxEntryAdded for {EntryId} to {PlatformUserId}",
                entry.Id, entry.PlatformUserId);
        }
    }

    private async Task EmitUnreadCountAsync(Guid platformUserId, CancellationToken ct)
    {
        try
        {
            var count = await _store.GetUnreadCountAsync(platformUserId, actionableOnly: true, ct).ConfigureAwait(false);
            await _hub.Clients.Group(TenantHubGroups.User(platformUserId))
                .SendAsync(
                    "InboxUnreadCountUpdated",
                    count,
                    DateTimeOffset.UtcNow,
                    System.Diagnostics.Activity.Current?.TraceId.ToString() ?? "",
                    ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to emit InboxUnreadCountUpdated for {PlatformUserId}",
                platformUserId);
        }
    }
}
