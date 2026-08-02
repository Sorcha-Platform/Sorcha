// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Playwright;

using Sorcha.UI.E2E.Tests.Infrastructure;
using Sorcha.UI.E2E.Tests.PageObjects.Shared;

namespace Sorcha.UI.E2E.Tests.PageObjects;

/// <summary>
/// Feature 186 (#1163) — the citizen "My Applications" list at <c>/app/my-applications</c>.
/// </summary>
public class MyApplicationsPage
{
    private readonly IPage _page;

    /// <summary>Initialises the page object.</summary>
    /// <param name="page">The Playwright page.</param>
    public MyApplicationsPage(IPage page) => _page = page;

    /// <summary>One element per listed application.</summary>
    public ILocator Rows => MudBlazorHelpers.TestIdPrefix(_page, "my-application-row-");

    /// <summary>The empty state shown when the citizen has submitted nothing.</summary>
    public ILocator EmptyState => MudBlazorHelpers.TestId(_page, "my-applications-empty");

    /// <summary>The loading indicator.</summary>
    public ILocator Loading => MudBlazorHelpers.TestId(_page, "my-applications-loading");

    /// <summary>Navigates to the list and waits for Blazor to settle.</summary>
    public async Task NavigateAsync()
    {
        await _page.GotoAsync(TestConstants.AuthenticatedRoutes.MyApplications);
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await MudBlazorHelpers.WaitForBlazorAsync(_page);
    }

    /// <summary>Number of applications listed.</summary>
    public async Task<int> GetApplicationCountAsync() => await Rows.CountAsync();

    /// <summary>True when the empty state is shown.</summary>
    public async Task<bool> IsEmptyStateVisibleAsync() =>
        await EmptyState.CountAsync() > 0 && await EmptyState.IsVisibleAsync();
}
