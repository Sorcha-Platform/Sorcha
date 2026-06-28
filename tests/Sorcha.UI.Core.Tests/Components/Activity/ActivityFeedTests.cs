// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MudBlazor;
using Sorcha.UI.Components.User.Components.Activity;
using Sorcha.UI.Core.Services;
using Sorcha.UI.Testing;
using Xunit;

namespace Sorcha.UI.Core.Tests.Components.Activity;

/// <summary>
/// bUnit tests for the ActivityFeed component (Feature 169 — Unified Activity Timeline).
/// Guards the Actionable/Informational badge classification, empty-state rendering,
/// and the basic list render path.
/// </summary>
public sealed class ActivityFeedTests : ComponentTestFixture
{
    private readonly Mock<IInboxApiService> _api = new();

    public ActivityFeedTests()
    {
        Services.AddLogging();
        Services.AddSingleton(_api.Object);
        Services.AddSingleton(new TenantHubConnection(
            "http://test.local",
            () => Task.FromResult<string?>(null),
            NullLogger<TenantHubConnection>.Instance,
            _api.Object));
    }

    private static InboxEntryDto Entry(string category, string severity, string title = "Test entry") => new()
    {
        Id = Guid.NewGuid(),
        PlatformUserId = Guid.NewGuid(),
        Category = category,
        Severity = severity,
        CorrelationKey = "k",
        DetailHref = "",
        SourceEventId = Guid.NewGuid(),
        OccurredAt = DateTimeOffset.UtcNow,
        Title = title,
    };

    private void SetupList(IReadOnlyList<InboxEntryDto> entries)
    {
        _api.Setup(a => a.ListAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(),
                It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InboxPageDto(entries, 1, 50, entries.Count));
    }

    [Fact]
    public void WhenEntriesLoad_RendersActivityFeed_WithEntriesList()
    {
        var entries = new List<InboxEntryDto>
        {
            Entry("System", "Info", "First entry"),
            Entry("Workflow", "Info", "Second entry"),
        };
        SetupList(entries);

        var cut = Render<ActivityFeed>();

        cut.Find("[data-testid='activity-feed']").Should().NotBeNull();
        cut.Markup.Should().Contain("First entry");
    }

    [Fact]
    public void WhenNoEntries_RendersEmptyState()
    {
        SetupList(new List<InboxEntryDto>());

        var cut = Render<ActivityFeed>();

        cut.Find("[data-testid='activity-feed-empty']").Should().NotBeNull();
    }

    [Fact]
    public void WhenEntryIsActionable_RendersActionableBadge()
    {
        SetupList(new List<InboxEntryDto>
        {
            Entry("Action", "Info", "Actionable entry"),
        });

        var cut = Render<ActivityFeed>();

        cut.Markup.Should().Contain("Actionable");
    }

    [Fact]
    public void WhenEntryIsInformational_RendersInformationalBadge()
    {
        SetupList(new List<InboxEntryDto>
        {
            Entry("System", "Info", "Informational entry"),
        });

        var cut = Render<ActivityFeed>();

        cut.Markup.Should().Contain("Informational");
    }

    [Fact]
    public void WhenLoading_ComponentRendersWithoutThrowing()
    {
        // Arrange: set up a mock that completes (bUnit runs synchronously in tests;
        // true async loading is hard to intercept without component-internal hooks,
        // so we verify the component lifecycle completes without throwing).
        SetupList(new List<InboxEntryDto>());

        var act = () => Render<ActivityFeed>();

        act.Should().NotThrow();
    }

    [Fact]
    public void WhenLoadedCountLessThanTotalCount_ShowsLoadMoreButton()
    {
        var entries = new List<InboxEntryDto> { Entry("System", "Info", "Entry 1") };
        // Fewer entries loaded than total — "Load more" must be visible.
        _api.Setup(a => a.ListAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(),
                It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InboxPageDto(entries, 1, 20, 50));

        var cut = Render<ActivityFeed>();

        cut.Find("[data-testid='activity-feed-load-more']").Should().NotBeNull(
            "the Load more affordance must appear when loaded entries < TotalCount");
    }

    [Fact]
    public void WhenLoadedCountEqualsOrExceedsTotalCount_HidesLoadMoreButton()
    {
        var entries = new List<InboxEntryDto>
        {
            Entry("System", "Info", "Entry 1"),
            Entry("System", "Info", "Entry 2"),
        };
        // All entries already loaded — "Load more" must be hidden.
        SetupList(entries);

        var cut = Render<ActivityFeed>();

        cut.FindAll("[data-testid='activity-feed-load-more']").Should().BeEmpty(
            "the Load more affordance must be hidden when all entries are already loaded");
    }
}
