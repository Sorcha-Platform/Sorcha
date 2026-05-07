// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Hubs;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Services;
using Sorcha.Tenant.Service.Storage;
using Sorcha.Tenant.Service.Tests.Helpers;

namespace Sorcha.Tenant.Service.Tests.Services;

/// <summary>
/// Tests for <see cref="InboxService"/> covering Phase 5 (US3) acceptance scenarios:
/// idempotent writes, read transitions, dismiss transitions, mark-all-read,
/// per-user scoping, and unread-count accuracy.
/// </summary>
public sealed class InboxServiceTests : IDisposable
{
    private readonly TenantDbContext _db;
    private readonly InboxService _sut;
    private readonly Mock<IHubContext<TenantHub>> _hub = new();
    private readonly Mock<IHubClients> _hubClients = new();
    private readonly Mock<IClientProxy> _clientProxy = new();
    private readonly Guid _userA = Guid.NewGuid();
    private readonly Guid _userB = Guid.NewGuid();

    public InboxServiceTests()
    {
        _db = InMemoryDbContextFactory.Create();

        _hub.SetupGet(h => h.Clients).Returns(_hubClients.Object);
        _hubClients.Setup(c => c.Group(It.IsAny<string>())).Returns(_clientProxy.Object);

        var store = new EfCoreInboxStore(_db);
        _sut = new InboxService(store, _hub.Object, NullLogger<InboxService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    private InboxWriteRequest BuildRequest(
        Guid? userId = null,
        Guid? sourceEventId = null,
        InboxCategory category = InboxCategory.Action,
        string correlationKey = "tx:wallet-1:0")
        => new(
            PlatformUserId: userId ?? _userA,
            Category: category,
            Severity: InboxSeverity.ActionRequired,
            CorrelationKey: correlationKey,
            DetailHref: "/api/instances/00000000-0000-0000-0000-000000000001/actions/00000000-0000-0000-0000-000000000002",
            SourceEventId: sourceEventId ?? Guid.NewGuid(),
            OccurredAt: DateTimeOffset.UtcNow,
            Title: "Action required");

    [Fact]
    public async Task WriteAsync_FirstCall_PersistsAndReturnsNonIdempotent()
    {
        var result = await _sut.WriteAsync(BuildRequest());

        result.IsIdempotent.Should().BeFalse();
        result.Entry.Id.Should().NotBeEmpty();
        result.Entry.PlatformUserId.Should().Be(_userA);
        result.Entry.ChannelHints.Should().Be(ChannelHints.Inbox | ChannelHints.Push | ChannelHints.Email);

        (await _sut.GetUnreadCountAsync(_userA)).Should().Be(1);
    }

    [Fact]
    public async Task WriteAsync_DuplicateSourceEventId_IsIdempotent()
    {
        var sourceId = Guid.NewGuid();
        var first = await _sut.WriteAsync(BuildRequest(sourceEventId: sourceId));
        var second = await _sut.WriteAsync(BuildRequest(sourceEventId: sourceId));

        second.IsIdempotent.Should().BeTrue();
        second.Entry.Id.Should().Be(first.Entry.Id);
        (await _sut.GetUnreadCountAsync(_userA)).Should().Be(1);
    }

    [Fact]
    public async Task WriteAsync_DefaultChannelHints_VariesByCategory()
    {
        var actionResult = await _sut.WriteAsync(BuildRequest(category: InboxCategory.Action));
        var systemResult = await _sut.WriteAsync(BuildRequest(category: InboxCategory.System, correlationKey: "system:1"));
        var membershipResult = await _sut.WriteAsync(BuildRequest(category: InboxCategory.Membership, correlationKey: "membership:1"));

        actionResult.Entry.ChannelHints.Should().Be(ChannelHints.Inbox | ChannelHints.Push | ChannelHints.Email);
        systemResult.Entry.ChannelHints.Should().Be(ChannelHints.Inbox);
        membershipResult.Entry.ChannelHints.Should().Be(ChannelHints.Inbox | ChannelHints.Email);
    }

    [Fact]
    public async Task GetPageAsync_ReturnsNewestFirst_ExcludesDismissed_ByDefault()
    {
        var first = await _sut.WriteAsync(BuildRequest(correlationKey: "tx:1"));
        await Task.Delay(5);
        var second = await _sut.WriteAsync(BuildRequest(correlationKey: "tx:2"));
        await Task.Delay(5);
        var third = await _sut.WriteAsync(BuildRequest(correlationKey: "tx:3"));

        await _sut.DismissAsync(_userA, first.Entry.Id);

        var page = await _sut.GetPageAsync(_userA);

        page.TotalCount.Should().Be(2);
        page.Entries.Should().HaveCount(2);
        page.Entries[0].Id.Should().Be(third.Entry.Id);
        page.Entries[1].Id.Should().Be(second.Entry.Id);
    }

    [Fact]
    public async Task GetPageAsync_IncludeDismissed_ReturnsAll()
    {
        var first = await _sut.WriteAsync(BuildRequest(correlationKey: "tx:1"));
        await _sut.WriteAsync(BuildRequest(correlationKey: "tx:2"));
        await _sut.DismissAsync(_userA, first.Entry.Id);

        var page = await _sut.GetPageAsync(_userA, includeDismissed: true);

        page.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task GetPageAsync_PerUserScoping_DoesNotLeak()
    {
        await _sut.WriteAsync(BuildRequest(userId: _userA, correlationKey: "tx:a"));
        await _sut.WriteAsync(BuildRequest(userId: _userB, correlationKey: "tx:b"));

        var pageA = await _sut.GetPageAsync(_userA);
        var pageB = await _sut.GetPageAsync(_userB);

        pageA.TotalCount.Should().Be(1);
        pageB.TotalCount.Should().Be(1);
        pageA.Entries[0].PlatformUserId.Should().Be(_userA);
        pageB.Entries[0].PlatformUserId.Should().Be(_userB);
    }

    [Fact]
    public async Task GetByIdAsync_CrossUser_ReturnsNullIndistinguishably()
    {
        var written = await _sut.WriteAsync(BuildRequest(userId: _userA));

        var bView = await _sut.GetByIdAsync(_userB, written.Entry.Id);

        bView.Should().BeNull();
    }

    [Fact]
    public async Task MarkReadAsync_SetsReadAt_AndDecrementsUnread()
    {
        var written = await _sut.WriteAsync(BuildRequest());
        (await _sut.GetUnreadCountAsync(_userA)).Should().Be(1);

        var ok = await _sut.MarkReadAsync(_userA, written.Entry.Id);

        ok.Should().BeTrue();
        var refreshed = await _sut.GetByIdAsync(_userA, written.Entry.Id);
        refreshed!.ReadAt.Should().NotBeNull();
        (await _sut.GetUnreadCountAsync(_userA)).Should().Be(0);
    }

    [Fact]
    public async Task MarkReadAsync_AlreadyRead_IsIdempotent()
    {
        var written = await _sut.WriteAsync(BuildRequest());
        await _sut.MarkReadAsync(_userA, written.Entry.Id);

        var second = await _sut.MarkReadAsync(_userA, written.Entry.Id);

        second.Should().BeTrue();
        (await _sut.GetUnreadCountAsync(_userA)).Should().Be(0);
    }

    [Fact]
    public async Task MarkReadAsync_UnknownEntry_ReturnsFalse()
    {
        var ok = await _sut.MarkReadAsync(_userA, Guid.NewGuid());
        ok.Should().BeFalse();
    }

    [Fact]
    public async Task DismissAsync_SetsDismissedAt_AndDecrementsUnreadOnlyIfWasUnread()
    {
        var unread = await _sut.WriteAsync(BuildRequest(correlationKey: "tx:1"));
        var read = await _sut.WriteAsync(BuildRequest(correlationKey: "tx:2"));
        await _sut.MarkReadAsync(_userA, read.Entry.Id);
        (await _sut.GetUnreadCountAsync(_userA)).Should().Be(1);

        await _sut.DismissAsync(_userA, read.Entry.Id);  // already-read entry: count unchanged
        (await _sut.GetUnreadCountAsync(_userA)).Should().Be(1);

        await _sut.DismissAsync(_userA, unread.Entry.Id);  // unread entry: count goes to 0
        (await _sut.GetUnreadCountAsync(_userA)).Should().Be(0);
    }

    [Fact]
    public async Task MarkAllReadAsync_OnlyTouchesUnreadEntries()
    {
        var a = await _sut.WriteAsync(BuildRequest(correlationKey: "tx:1"));
        var b = await _sut.WriteAsync(BuildRequest(correlationKey: "tx:2"));
        var c = await _sut.WriteAsync(BuildRequest(correlationKey: "tx:3"));
        await _sut.MarkReadAsync(_userA, b.Entry.Id);

        var marked = await _sut.MarkAllReadAsync(_userA);

        marked.Should().Be(2, "two entries were unread before mark-all-read");
        (await _sut.GetUnreadCountAsync(_userA)).Should().Be(0);
    }
}
