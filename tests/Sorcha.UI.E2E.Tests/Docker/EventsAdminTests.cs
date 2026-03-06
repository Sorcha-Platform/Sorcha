// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.UI.E2E.Tests.Infrastructure;

namespace Sorcha.UI.E2E.Tests.Docker;

/// <summary>
/// E2E tests for events admin page (Feature 051 US4).
/// </summary>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
[Category("Docker")]
[Category("Admin")]
[Category("Authenticated")]
public class EventsAdminTests : AuthenticatedDockerTestBase
{
    [Test]
    [Retry(2)]
    public async Task EventsAdmin_LoadsSuccessfully()
    {
        await NavigateAuthenticatedAsync(TestConstants.AuthenticatedRoutes.AdminEvents);

        await Expect(Page).ToHaveTitleAsync(
            new System.Text.RegularExpressions.Regex("Events|Sorcha"));
    }

    [Test]
    public async Task EventsAdmin_HasBreadcrumbs()
    {
        await NavigateAuthenticatedAsync(TestConstants.AuthenticatedRoutes.AdminEvents);

        var breadcrumbs = Page.Locator(".mud-breadcrumbs");
        await Expect(breadcrumbs).ToBeVisibleAsync();
    }

    [Test]
    public async Task EventsAdmin_ShowsTableOrEmptyState()
    {
        await NavigateAuthenticatedAsync(TestConstants.AuthenticatedRoutes.AdminEvents);

        var table = Page.Locator(".mud-table");
        var emptyText = Page.Locator("text=No events");
        var hasTable = await table.CountAsync() > 0;
        var hasEmpty = await emptyText.CountAsync() > 0;

        Assert.That(hasTable || hasEmpty, Is.True,
            "Events page should show either a data table or empty state");
    }
}
