// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Tests.Services;

public class EventServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TenantDbContext _db;
    private readonly EventService _sut;
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _orgId = Guid.NewGuid();

    public EventServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new TenantDbContext(options);
        _db.Database.EnsureCreated();
        var logger = Mock.Of<ILogger<EventService>>();
        _sut = new EventService(_db, logger);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task CreateEventAsync_SetsCreatedAtAndExpiresAt()
    {
        var evt = MakeEvent();

        var created = await _sut.CreateEventAsync(evt);

        created.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        created.ExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddDays(90), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CreateEventAsync_PersistsToDatabase()
    {
        var evt = MakeEvent();

        var created = await _sut.CreateEventAsync(evt);

        var stored = await _db.ActivityEvents.FindAsync(created.Id);
        stored.Should().NotBeNull();
        stored!.Title.Should().Be("Test Event");
    }

    [Fact]
    public async Task GetEventsAsync_ReturnsPaginatedResults()
    {
        await SeedEvents(15);

        var (items, totalCount) = await _sut.GetEventsAsync(_userId, page: 1, pageSize: 10);

        totalCount.Should().Be(15);
        items.Should().HaveCount(10);
    }

    [Fact]
    public async Task GetEventsAsync_OrdersByCreatedAtDescending()
    {
        await SeedEvents(5);

        var (items, _) = await _sut.GetEventsAsync(_userId, page: 1, pageSize: 10);

        items.Should().BeInDescendingOrder(e => e.CreatedAt);
    }

    [Fact]
    public async Task GetEventsAsync_FiltersUnreadOnly()
    {
        await SeedEvents(5);
        var allEvents = await _db.ActivityEvents.ToListAsync();
        allEvents[0].IsRead = true;
        allEvents[1].IsRead = true;
        await _db.SaveChangesAsync();

        var (items, totalCount) = await _sut.GetEventsAsync(_userId, 1, 50, unreadOnly: true);

        totalCount.Should().Be(3);
        items.Should().AllSatisfy(e => e.IsRead.Should().BeFalse());
    }

    [Fact]
    public async Task GetEventsAsync_FiltersBySeverity()
    {
        await SeedEvents(3);
        var all = await _db.ActivityEvents.ToListAsync();
        all[0].Severity = EventSeverity.Error;
        await _db.SaveChangesAsync();

        var (items, totalCount) = await _sut.GetEventsAsync(
            _userId, 1, 50, severity: EventSeverity.Error);

        totalCount.Should().Be(1);
        items.Should().OnlyContain(e => e.Severity == EventSeverity.Error);
    }

    [Fact]
    public async Task GetUnreadCountAsync_ReturnsCorrectCount()
    {
        await SeedEvents(5);
        var all = await _db.ActivityEvents.ToListAsync();
        all[0].IsRead = true;
        all[1].IsRead = true;
        await _db.SaveChangesAsync();

        var count = await _sut.GetUnreadCountAsync(_userId);

        count.Should().Be(3);
    }

    [Fact]
    public async Task MarkReadAsync_MarkSpecificEvents()
    {
        await SeedEvents(3);
        var all = await _db.ActivityEvents.ToListAsync();
        var idsToMark = new[] { all[0].Id, all[1].Id };

        var marked = await _sut.MarkReadAsync(_userId, idsToMark);

        marked.Should().Be(2);
        _db.ChangeTracker.Clear();
        var refreshed = await _db.ActivityEvents.Where(e => idsToMark.Contains(e.Id)).ToListAsync();
        refreshed.Should().AllSatisfy(e => e.IsRead.Should().BeTrue());
    }

    [Fact]
    public async Task MarkReadAsync_MarkAllWhenNoIdsProvided()
    {
        await SeedEvents(3);

        var marked = await _sut.MarkReadAsync(_userId);

        marked.Should().Be(3);
        _db.ChangeTracker.Clear();
        var allRead = await _db.ActivityEvents.AllAsync(e => e.IsRead);
        allRead.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteEventAsync_DeletesOwnEvent()
    {
        var evt = MakeEvent();
        await _sut.CreateEventAsync(evt);

        var deleted = await _sut.DeleteEventAsync(evt.Id, _userId);

        deleted.Should().BeTrue();
        _db.ChangeTracker.Clear();
        (await _db.ActivityEvents.FindAsync(evt.Id)).Should().BeNull();
    }

    [Fact]
    public async Task DeleteEventAsync_ReturnsFalseForOtherUsersEvent()
    {
        var evt = MakeEvent();
        await _sut.CreateEventAsync(evt);

        var deleted = await _sut.DeleteEventAsync(evt.Id, Guid.NewGuid());

        deleted.Should().BeFalse();
    }

    [Fact]
    public async Task GetAdminEventsAsync_ReturnsOrgWideEvents()
    {
        await SeedEvents(3);
        // Add event from different user in same org
        var otherUserEvt = MakeEvent();
        otherUserEvt.UserId = Guid.NewGuid();
        await _sut.CreateEventAsync(otherUserEvt);

        var (items, totalCount) = await _sut.GetAdminEventsAsync(_orgId, 1, 50);

        totalCount.Should().Be(4);
    }

    [Fact]
    public async Task GetAdminEventsAsync_FiltersByUserId()
    {
        await SeedEvents(3);
        var otherUser = Guid.NewGuid();
        var otherEvt = MakeEvent();
        otherEvt.UserId = otherUser;
        await _sut.CreateEventAsync(otherEvt);

        var (items, totalCount) = await _sut.GetAdminEventsAsync(_orgId, 1, 50, userId: _userId);

        totalCount.Should().Be(3);
        items.Should().AllSatisfy(e => e.UserId.Should().Be(_userId));
    }

    [Fact]
    public async Task GetEventsAsync_ClampsPageSize()
    {
        await SeedEvents(5);

        var (items, _) = await _sut.GetEventsAsync(_userId, page: 1, pageSize: 200);

        items.Should().HaveCount(5); // clamped to 100, only 5 exist
    }

    private ActivityEvent MakeEvent() => new()
    {
        OrganizationId = _orgId,
        UserId = _userId,
        EventType = "test.created",
        Severity = EventSeverity.Info,
        Title = "Test Event",
        Message = "A test event occurred",
        SourceService = "test"
    };

    private async Task SeedEvents(int count)
    {
        for (var i = 0; i < count; i++)
        {
            var evt = MakeEvent();
            evt.CreatedAt = DateTime.UtcNow.AddMinutes(-i);
            evt.ExpiresAt = evt.CreatedAt.AddDays(90);
            _db.ActivityEvents.Add(evt);
        }
        await _db.SaveChangesAsync();
    }
}
