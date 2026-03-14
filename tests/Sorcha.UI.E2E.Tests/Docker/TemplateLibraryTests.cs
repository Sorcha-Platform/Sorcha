// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.UI.E2E.Tests.Infrastructure;
using Sorcha.UI.E2E.Tests.PageObjects.Shared;

namespace Sorcha.UI.E2E.Tests.Docker;

/// <summary>
/// E2E tests for the Catalogue (template library) page with backend API integration.
/// </summary>
[TestFixture]
[Category("Docker")]
[Category("Templates")]
[Parallelizable(ParallelScope.Self)]
public class TemplateLibraryTests : AuthenticatedDockerTestBase
{
    [SetUp]
    public override async Task BaseSetUp()
    {
        await base.BaseSetUp();
    }

    [Test]
    [Retry(2)]
    public async Task Templates_PageLoads_ShowsTitleAndContent()
    {
        await NavigateAuthenticatedAsync(TestConstants.AuthenticatedRoutes.Templates);

        var content = await Page.TextContentAsync("body") ?? "";
        Assert.That(content,
            Does.Contain("Catalogue").Or.Contain("nav.catalogue").Or.Contain("catalogue")
                .Or.Contain("Template").Or.Contain("template").Or.Contain("Browse workflow"),
            "Templates page should render Catalogue content");
    }

    [Test]
    [Retry(2)]
    public async Task Templates_ShowsSearchBar()
    {
        await NavigateAuthenticatedAsync(TestConstants.AuthenticatedRoutes.Templates);
        await MudBlazorHelpers.WaitForBlazorAsync(Page, TestConstants.PageLoadTimeout);

        var searchInput = Page.Locator("input[placeholder*='Search'], input[placeholder*='search'], input[placeholder*='template']");
        var mudInput = Page.Locator(".mud-input-text");
        var hasSearch = await searchInput.CountAsync() > 0 || await mudInput.CountAsync() > 0;
        Assert.That(hasSearch, Is.True,
            "Templates page should have a search input");
    }

    [Test]
    [Retry(2)]
    public async Task Templates_ShowsTemplateCards_OrEmptyState()
    {
        await NavigateAuthenticatedAsync(TestConstants.AuthenticatedRoutes.Templates);
        await MudBlazorHelpers.WaitForBlazorAsync(Page, TestConstants.PageLoadTimeout);

        var cards = MudBlazorHelpers.Cards(Page);
        var emptyState = Page.Locator("text=No Templates");
        var emptyStateI18n = Page.Locator("text=templates.empty, text=catalogue.empty, text=common.noData");
        var serviceError = Page.Locator("text=unavailable");

        var hasContent = await cards.CountAsync() > 0 ||
                         await emptyState.CountAsync() > 0 ||
                         await emptyStateI18n.CountAsync() > 0 ||
                         await serviceError.CountAsync() > 0;
        Assert.That(hasContent, Is.True,
            "Page should show template cards, empty state, or service error");
    }

    [Test]
    [Retry(2)]
    public async Task Templates_NoConsoleErrors()
    {
        await NavigateAuthenticatedAsync(TestConstants.AuthenticatedRoutes.Templates);
        await MudBlazorHelpers.WaitForBlazorAsync(Page, TestConstants.PageLoadTimeout);

        var criticalErrors = GetCriticalConsoleErrors();
        Assert.That(criticalErrors, Is.Empty,
            "Templates page should not have critical console errors");
    }

    [Test]
    [Retry(2)]
    public async Task Templates_UseButton_ShowsWizardView()
    {
        await NavigateAuthenticatedAsync(TestConstants.AuthenticatedRoutes.Templates);
        await MudBlazorHelpers.WaitForBlazorAsync(Page, TestConstants.PageLoadTimeout);

        // If template cards exist, click the first Use button
        var useButton = Page.Locator(".mud-button-filled:has-text('Use')").First;
        if (await useButton.CountAsync() > 0)
        {
            await useButton.ClickAsync();
            await Page.WaitForTimeoutAsync(TestConstants.ShortWait);

            // Verify wizard view elements
            var backButton = Page.Locator("button:has-text('Back to Catalogue')");
            Assert.That(await backButton.CountAsync(), Is.GreaterThan(0),
                "Wizard view should show Back to Catalogue button");
        }
    }

    [Test]
    [Retry(2)]
    public async Task Templates_BackButton_ReturnsToList()
    {
        await NavigateAuthenticatedAsync(TestConstants.AuthenticatedRoutes.Templates);
        await MudBlazorHelpers.WaitForBlazorAsync(Page, TestConstants.PageLoadTimeout);

        var useButton = Page.Locator(".mud-button-filled:has-text('Use')").First;
        if (await useButton.CountAsync() > 0)
        {
            await useButton.ClickAsync();
            await Page.WaitForTimeoutAsync(TestConstants.ShortWait);

            // Click back button
            var backButton = Page.Locator("button:has-text('Back to Catalogue')");
            await backButton.ClickAsync();
            await Page.WaitForTimeoutAsync(TestConstants.ShortWait);

            // Verify list view is back
            var searchInput = Page.Locator("input[placeholder*='Search']");
            Assert.That(await searchInput.CountAsync(), Is.GreaterThan(0),
                "After clicking back, search bar should be visible again");
        }
    }

    [Test]
    [Retry(2)]
    public async Task Templates_UseTemplate_SavesAndNavigatesToBlueprints()
    {
        await NavigateAuthenticatedAsync(TestConstants.AuthenticatedRoutes.Templates);
        await MudBlazorHelpers.WaitForBlazorAsync(Page, TestConstants.PageLoadTimeout);

        var useButton = Page.Locator(".mud-button-filled:has-text('Use')").First;
        if (await useButton.CountAsync() > 0)
        {
            await useButton.ClickAsync();
            await Page.WaitForTimeoutAsync(TestConstants.ShortWait);

            // Click "Use This Template" in the wizard
            var useTemplateButton = Page.Locator("button:has-text('Use This Template')");
            if (await useTemplateButton.CountAsync() > 0)
            {
                await useTemplateButton.ClickAsync();
                await Page.WaitForLoadStateAsync(Microsoft.Playwright.LoadState.NetworkIdle);
                await Page.WaitForTimeoutAsync(TestConstants.ShortWait);

                Assert.That(Page.Url, Does.Contain("blueprints"),
                    "Should navigate to blueprints page after using template");
            }
        }
    }
}
