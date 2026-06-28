// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;

using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Storage;
using Sorcha.Tenant.Service.Tests.Helpers;

namespace Sorcha.Tenant.Service.Tests.Services;

/// <summary>
/// Tests that <see cref="EfCoreInboxStore.GetPageAsync"/> and
/// <see cref="EfCoreInboxStore.GetUnreadCountAsync"/> correctly apply the
/// Actionable predicate when <c>actionableOnly: true</c> is requested.
/// </summary>
public sealed class EfCoreInboxStoreActionableTests : IDisposable
{
    private readonly TenantDbContext _db;
    private readonly EfCoreInboxStore _sut;
    private readonly Guid _userId = Guid.NewGuid();

    public EfCoreInboxStoreActionableTests()
    {
        _db = InMemoryDbContextFactory.Create();
        _sut = new EfCoreInboxStore(_db);
    }

    public void Dispose() => _db.Dispose();

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private InboxEntry BuildEntry(
        InboxCategory category,
        InboxSeverity severity,
        bool unread = true)
        => new()
        {
            Id = Guid.NewGuid(),
            PlatformUserId = _userId,
            Category = category,
            Severity = severity,
            CorrelationKey = $"test:{Guid.NewGuid()}",
            DetailHref = "/api/test/entry",
            SourceEventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
            ReadAt = unread ? null : DateTimeOffset.UtcNow,
            Title = $"{category}/{severity}",
        };

    private async Task SeedAsync(params InboxEntry[] entries)
    {
        _db.InboxEntries.AddRange(entries);
        await _db.SaveChangesAsync();
    }

    // -------------------------------------------------------------------------
    // GetPageAsync — actionableOnly: true
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetPageAsync_WithActionableOnly_ReturnsOnlyActionableEntries()
    {
        // Action/Info is actionable (category == Action)
        var actionable = BuildEntry(InboxCategory.Action, InboxSeverity.Info);
        // System/Info is NOT actionable (not Action category, severity < ActionRequired)
        var nonActionable = BuildEntry(InboxCategory.System, InboxSeverity.Info);

        await SeedAsync(actionable, nonActionable);

        var result = await _sut.GetPageAsync(
            platformUserId: _userId,
            page: 1,
            pageSize: 20,
            category: null,
            unreadOnly: false,
            includeDismissed: false,
            actionableOnly: true);

        result.Entries.Should().HaveCount(1);
        result.Entries[0].Id.Should().Be(actionable.Id);
        result.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task GetPageAsync_WithoutActionableOnly_ReturnsAllEntries()
    {
        var actionable = BuildEntry(InboxCategory.Action, InboxSeverity.Info);
        var nonActionable = BuildEntry(InboxCategory.System, InboxSeverity.Info);

        await SeedAsync(actionable, nonActionable);

        var result = await _sut.GetPageAsync(
            platformUserId: _userId,
            page: 1,
            pageSize: 20,
            category: null,
            unreadOnly: false,
            includeDismissed: false,
            actionableOnly: false);

        result.Entries.Should().HaveCount(2);
        result.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task GetPageAsync_WithActionableOnly_ExcludesWarningNonAction()
    {
        // Workflow/Warning: not Action category, Warning < ActionRequired — NOT actionable
        var nonActionable = BuildEntry(InboxCategory.Workflow, InboxSeverity.Warning);

        await SeedAsync(nonActionable);

        var result = await _sut.GetPageAsync(
            platformUserId: _userId,
            page: 1,
            pageSize: 20,
            category: null,
            unreadOnly: false,
            includeDismissed: false,
            actionableOnly: true);

        result.Entries.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }

    // -------------------------------------------------------------------------
    // GetUnreadCountAsync — actionableOnly: true
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetUnreadCountAsync_WithActionableOnly_CountsOnlyActionable()
    {
        // Action/Info (unread) — actionable
        var actionable = BuildEntry(InboxCategory.Action, InboxSeverity.Info, unread: true);
        // System/Info (unread) — NOT actionable
        var nonActionable = BuildEntry(InboxCategory.System, InboxSeverity.Info, unread: true);

        await SeedAsync(actionable, nonActionable);

        var count = await _sut.GetUnreadCountAsync(_userId, actionableOnly: true);

        count.Should().Be(1);
    }

    [Fact]
    public async Task GetUnreadCountAsync_WithActionableOnly_IncludesHighSeverity()
    {
        // Credential/Critical: severity >= ActionRequired — actionable even though not Action category
        var highSeverity = BuildEntry(InboxCategory.Credential, InboxSeverity.Critical, unread: true);

        await SeedAsync(highSeverity);

        var count = await _sut.GetUnreadCountAsync(_userId, actionableOnly: true);

        count.Should().Be(1);
    }
}
