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
using Sorcha.Tenant.Service.Tests.Helpers;
using Xunit;

namespace Sorcha.Tenant.Service.Tests.Hubs;

/// <summary>
/// Feature 118 T059 — verifies <see cref="InboxService"/> emits
/// <c>InboxEntryAdded(entryId, occurredAt, traceId)</c> and
/// <c>InboxUnreadCountUpdated(count, occurredAt, traceId)</c> on the
/// per-user TenantHub group with the thin-signal shape (opaque IDs +
/// timestamps + W3C trace token only).
/// </summary>
public class TenantHubInboxEventsTests
{
    private readonly Mock<IHubContext<TenantHub>> _hub = new();
    private readonly Mock<IHubClients> _clients = new();
    private readonly Mock<IClientProxy> _userGroup = new();
    private readonly List<(string Method, object?[] Args)> _emits = new();

    public TenantHubInboxEventsTests()
    {
        _hub.Setup(h => h.Clients).Returns(_clients.Object);
        _clients.Setup(c => c.Group(It.IsAny<string>())).Returns(_userGroup.Object);
        _userGroup
            .Setup(c => c.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Callback<string, object?[], CancellationToken>((method, args, _) => _emits.Add((method, args)))
            .Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task WriteAsync_NewEntry_Emits_InboxEntryAdded_AndUnreadCount_OnUserGroup()
    {
        var platformUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var (service, _) = CreateService();

        await service.WriteAsync(NewRequest(platformUserId));

        _clients.Verify(c => c.Group(TenantHubGroups.User(platformUserId)), Times.AtLeast(2));

        var added = _emits.SingleOrDefault(e => e.Method == "InboxEntryAdded");
        added.Method.Should().Be("InboxEntryAdded");
        added.Args.Should().HaveCount(3, "thin-signal: entryId + occurredAt + traceId");
        added.Args[0].Should().BeOfType<string>("entryId is the GUID-N string form");
        added.Args[1].Should().BeOfType<DateTimeOffset>();
        added.Args[2].Should().BeOfType<string>("traceId is a W3C trace token string");

        var unread = _emits.SingleOrDefault(e => e.Method == "InboxUnreadCountUpdated");
        unread.Method.Should().Be("InboxUnreadCountUpdated");
        unread.Args.Should().HaveCount(3, "thin-signal: count + occurredAt + traceId");
        unread.Args[0].Should().BeOfType<int>().Which.Should().Be(1);
        unread.Args[1].Should().BeOfType<DateTimeOffset>();
        unread.Args[2].Should().BeOfType<string>();
    }

    [Fact]
    public async Task WriteAsync_DuplicateOnSourceEventId_DoesNotEmit()
    {
        var platformUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var (service, _) = CreateService();
        var request = NewRequest(platformUserId);

        await service.WriteAsync(request);
        _emits.Clear();

        var second = await service.WriteAsync(request);

        second.IsIdempotent.Should().BeTrue();
        _emits.Should().BeEmpty("idempotent re-writes must NOT re-emit on TenantHub");
    }

    [Fact]
    public async Task MarkReadAsync_FirstTime_EmitsUnreadCountOnly()
    {
        var platformUserId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var (service, _) = CreateService();
        var write = await service.WriteAsync(NewRequest(platformUserId));
        _emits.Clear();

        var ok = await service.MarkReadAsync(platformUserId, write.Entry.Id);

        ok.Should().BeTrue();
        _emits.Should().ContainSingle(e => e.Method == "InboxUnreadCountUpdated");
        _emits.Should().NotContain(e => e.Method == "InboxEntryAdded");
        _emits.Single().Args[0].Should().BeOfType<int>().Which.Should().Be(0);
    }

    [Fact]
    public async Task MarkReadAsync_AlreadyRead_DoesNotEmit()
    {
        var platformUserId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var (service, _) = CreateService();
        var write = await service.WriteAsync(NewRequest(platformUserId));
        await service.MarkReadAsync(platformUserId, write.Entry.Id);
        _emits.Clear();

        await service.MarkReadAsync(platformUserId, write.Entry.Id);

        _emits.Should().BeEmpty("idempotent re-read must NOT re-emit");
    }

    [Fact]
    public async Task DismissAsync_UnreadEntry_EmitsUnreadCount()
    {
        var platformUserId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var (service, _) = CreateService();
        var write = await service.WriteAsync(NewRequest(platformUserId));
        _emits.Clear();

        await service.DismissAsync(platformUserId, write.Entry.Id);

        _emits.Should().ContainSingle(e => e.Method == "InboxUnreadCountUpdated");
    }

    [Fact]
    public async Task DismissAsync_AlreadyReadEntry_DoesNotEmit()
    {
        var platformUserId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        var (service, _) = CreateService();
        var write = await service.WriteAsync(NewRequest(platformUserId));
        await service.MarkReadAsync(platformUserId, write.Entry.Id);
        _emits.Clear();

        await service.DismissAsync(platformUserId, write.Entry.Id);

        _emits.Should().BeEmpty("dismissing an already-read entry doesn't change unread count");
    }

    [Fact]
    public async Task MarkAllReadAsync_WithUnread_EmitsUnreadCountOnce_OnCorrectGroup()
    {
        var platformUserId = Guid.Parse("77777777-7777-7777-7777-777777777777");
        var (service, _) = CreateService();
        await service.WriteAsync(NewRequest(platformUserId));
        await service.WriteAsync(NewRequest(platformUserId));
        _emits.Clear();

        var marked = await service.MarkAllReadAsync(platformUserId);

        marked.Should().Be(2);
        _clients.Verify(c => c.Group(TenantHubGroups.User(platformUserId)), Times.AtLeastOnce);
        _emits.Should().ContainSingle(e => e.Method == "InboxUnreadCountUpdated");
        _emits.Single().Args[0].Should().BeOfType<int>().Which.Should().Be(0);
    }

    [Fact]
    public async Task MarkAllReadAsync_NoUnread_DoesNotEmit()
    {
        var platformUserId = Guid.Parse("88888888-8888-8888-8888-888888888888");
        var (service, _) = CreateService();

        var marked = await service.MarkAllReadAsync(platformUserId);

        marked.Should().Be(0);
        _emits.Should().BeEmpty();
    }

    private (InboxService Service, TenantDbContext Db) CreateService()
    {
        var db = InMemoryDbContextFactory.Create();
        var service = new InboxService(db, _hub.Object, NullLogger<InboxService>.Instance);
        return (service, db);
    }

    private static InboxWriteRequest NewRequest(Guid platformUserId) => new(
        PlatformUserId: platformUserId,
        Category: InboxCategory.Action,
        Severity: InboxSeverity.Info,
        CorrelationKey: $"test:{Guid.NewGuid():N}",
        DetailHref: "/api/test/detail",
        SourceEventId: Guid.NewGuid(),
        OccurredAt: DateTimeOffset.UtcNow,
        Title: "Test entry");
}
