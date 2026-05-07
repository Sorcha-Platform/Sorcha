// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Storage;
using Sorcha.Tenant.Service.Tests.Helpers;

namespace Sorcha.Tenant.Service.Tests.Storage;

/// <summary>
/// Direct coverage for <see cref="EfCoreInboxStore"/> — the storage abstraction
/// introduced in Feature 118 / T065 to bring inbox writes under the Feature 113
/// audited-storage pattern.
/// </summary>
/// <remarks>
/// <see cref="InboxServiceTests"/> already exercises the orchestration / SignalR
/// path against the same in-memory DbContext, so these tests intentionally focus
/// on store-level invariants: idempotency keying, scoping, paging boundaries,
/// state transitions, and the read/dismiss outcome contract.
/// </remarks>
public sealed class EfCoreInboxStoreTests : IDisposable
{
    private readonly TenantDbContext _db;
    private readonly EfCoreInboxStore _sut;
    private readonly Guid _userA = Guid.NewGuid();
    private readonly Guid _userB = Guid.NewGuid();

    public EfCoreInboxStoreTests()
    {
        _db = InMemoryDbContextFactory.Create();
        _sut = new EfCoreInboxStore(_db);
    }

    public void Dispose() => _db.Dispose();

    private InboxEntry NewEntry(
        Guid? userId = null,
        Guid? sourceEventId = null,
        InboxCategory category = InboxCategory.Action,
        DateTimeOffset? occurredAt = null)
        => new()
        {
            Id = Guid.NewGuid(),
            PlatformUserId = userId ?? _userA,
            Category = category,
            Severity = InboxSeverity.ActionRequired,
            CorrelationKey = "tx:wallet-1:0",
            DetailHref = "/api/me/inbox",
            SourceEventId = sourceEventId ?? Guid.NewGuid(),
            OccurredAt = occurredAt ?? DateTimeOffset.UtcNow,
            Title = "Test entry",
            ChannelHints = ChannelHints.Inbox,
        };

    [Fact]
    public async Task AddOrFindAsync_FirstCall_PersistsEntryAndReportsNotIdempotent()
    {
        var entry = NewEntry();

        var result = await _sut.AddOrFindAsync(entry);

        result.IsIdempotent.Should().BeFalse();
        result.Entry.Id.Should().Be(entry.Id);

        var fetched = await _sut.GetByIdAsync(_userA, entry.Id);
        fetched.Should().NotBeNull();
    }

    [Fact]
    public async Task AddOrFindAsync_DuplicateSourceEventId_ReturnsExistingEntry()
    {
        var sourceId = Guid.NewGuid();
        var first = await _sut.AddOrFindAsync(NewEntry(sourceEventId: sourceId));
        var second = await _sut.AddOrFindAsync(NewEntry(sourceEventId: sourceId));

        second.IsIdempotent.Should().BeTrue();
        second.Entry.Id.Should().Be(first.Entry.Id);
    }

    [Fact]
    public async Task AddOrFindAsync_SameSourceEventId_DifferentUsers_BothPersisted()
    {
        var sourceId = Guid.NewGuid();

        var a = await _sut.AddOrFindAsync(NewEntry(userId: _userA, sourceEventId: sourceId));
        var b = await _sut.AddOrFindAsync(NewEntry(userId: _userB, sourceEventId: sourceId));

        a.IsIdempotent.Should().BeFalse();
        b.IsIdempotent.Should().BeFalse();
        a.Entry.Id.Should().NotBe(b.Entry.Id);
    }

    [Fact]
    public async Task GetByIdAsync_EntryOwnedByOtherUser_ReturnsNull()
    {
        var entry = await _sut.AddOrFindAsync(NewEntry(userId: _userA));

        var crossUser = await _sut.GetByIdAsync(_userB, entry.Entry.Id);

        crossUser.Should().BeNull();
    }

    [Fact]
    public async Task GetUnreadCountAsync_OnlyCountsUnreadAndNonDismissed()
    {
        var unread = await _sut.AddOrFindAsync(NewEntry());
        var read = await _sut.AddOrFindAsync(NewEntry());
        var dismissed = await _sut.AddOrFindAsync(NewEntry());

        await _sut.MarkReadAsync(_userA, read.Entry.Id);
        await _sut.DismissAsync(_userA, dismissed.Entry.Id);

        var count = await _sut.GetUnreadCountAsync(_userA);
        count.Should().Be(1);
        unread.Entry.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task GetPageAsync_OrdersByOccurredAtDescending()
    {
        var older = await _sut.AddOrFindAsync(NewEntry(occurredAt: DateTimeOffset.UtcNow.AddMinutes(-10)));
        var newer = await _sut.AddOrFindAsync(NewEntry(occurredAt: DateTimeOffset.UtcNow));

        var page = await _sut.GetPageAsync(_userA, page: 1, pageSize: 20,
            category: null, unreadOnly: false, includeDismissed: false);

        page.Entries.Should().HaveCount(2);
        page.Entries[0].Id.Should().Be(newer.Entry.Id);
        page.Entries[1].Id.Should().Be(older.Entry.Id);
        page.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task GetPageAsync_ClampsPagingArguments()
    {
        for (var i = 0; i < 3; i++)
        {
            await _sut.AddOrFindAsync(NewEntry());
        }

        var page = await _sut.GetPageAsync(_userA, page: -5, pageSize: 0,
            category: null, unreadOnly: false, includeDismissed: false);

        page.Entries.Should().HaveCount(3);
        page.TotalCount.Should().Be(3);
    }

    [Fact]
    public async Task GetPageAsync_FilterByCategory_OnlyReturnsMatching()
    {
        await _sut.AddOrFindAsync(NewEntry(category: InboxCategory.Action));
        await _sut.AddOrFindAsync(NewEntry(category: InboxCategory.Security));

        var page = await _sut.GetPageAsync(_userA, page: 1, pageSize: 20,
            category: InboxCategory.Security, unreadOnly: false, includeDismissed: false);

        page.Entries.Should().ContainSingle();
        page.Entries[0].Category.Should().Be(InboxCategory.Security);
    }

    [Fact]
    public async Task GetPageAsync_ExcludesDismissedByDefault()
    {
        var keep = await _sut.AddOrFindAsync(NewEntry());
        var drop = await _sut.AddOrFindAsync(NewEntry());
        await _sut.DismissAsync(_userA, drop.Entry.Id);

        var page = await _sut.GetPageAsync(_userA, page: 1, pageSize: 20,
            category: null, unreadOnly: false, includeDismissed: false);

        page.Entries.Should().ContainSingle().Which.Id.Should().Be(keep.Entry.Id);
    }

    [Fact]
    public async Task MarkReadAsync_UnknownEntry_ReturnsNotFound()
    {
        var result = await _sut.MarkReadAsync(_userA, Guid.NewGuid());

        result.Found.Should().BeFalse();
        result.StateChanged.Should().BeFalse();
    }

    [Fact]
    public async Task MarkReadAsync_FirstCall_TransitionsState()
    {
        var entry = await _sut.AddOrFindAsync(NewEntry());

        var first = await _sut.MarkReadAsync(_userA, entry.Entry.Id);
        var second = await _sut.MarkReadAsync(_userA, entry.Entry.Id);

        first.Should().Be(new InboxMarkReadResult(Found: true, StateChanged: true));
        second.Should().Be(new InboxMarkReadResult(Found: true, StateChanged: false));
    }

    [Fact]
    public async Task DismissAsync_RecordsWasUnreadFromPriorReadState()
    {
        var unread = await _sut.AddOrFindAsync(NewEntry());
        var alreadyRead = await _sut.AddOrFindAsync(NewEntry());
        await _sut.MarkReadAsync(_userA, alreadyRead.Entry.Id);

        var dismissUnread = await _sut.DismissAsync(_userA, unread.Entry.Id);
        var dismissRead = await _sut.DismissAsync(_userA, alreadyRead.Entry.Id);

        dismissUnread.Should().Be(new InboxDismissResult(Found: true, StateChanged: true, WasUnread: true));
        dismissRead.Should().Be(new InboxDismissResult(Found: true, StateChanged: true, WasUnread: false));
    }

    [Fact]
    public async Task DismissAsync_AlreadyDismissed_NoStateChange()
    {
        var entry = await _sut.AddOrFindAsync(NewEntry());
        await _sut.DismissAsync(_userA, entry.Entry.Id);

        var second = await _sut.DismissAsync(_userA, entry.Entry.Id);

        second.Should().Be(new InboxDismissResult(Found: true, StateChanged: false, WasUnread: false));
    }

    [Fact]
    public async Task MarkAllReadAsync_OnlyTouchesUnreadAndScopedToUser()
    {
        await _sut.AddOrFindAsync(NewEntry(userId: _userA));
        await _sut.AddOrFindAsync(NewEntry(userId: _userA));
        var alreadyRead = await _sut.AddOrFindAsync(NewEntry(userId: _userA));
        await _sut.MarkReadAsync(_userA, alreadyRead.Entry.Id);

        // Other user's unread entry must not be affected.
        await _sut.AddOrFindAsync(NewEntry(userId: _userB));

        var affected = await _sut.MarkAllReadAsync(_userA);

        affected.Should().Be(2);
        (await _sut.GetUnreadCountAsync(_userA)).Should().Be(0);
        (await _sut.GetUnreadCountAsync(_userB)).Should().Be(1);
    }

    [Fact]
    public async Task MarkAllReadAsync_NoUnread_ReturnsZeroAndNoOp()
    {
        var affected = await _sut.MarkAllReadAsync(_userA);

        affected.Should().Be(0);
    }
}
