// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Playwright;
using Sorcha.UI.E2E.Tests.PageObjects.Shared;

namespace Sorcha.UI.E2E.Tests.PageObjects.WorkflowPages;

/// <summary>
/// Page object for the Work Queue page (/my-actions) — renamed from "My Pending Actions"
/// by Feature 186 (#1163); route, scope and behaviour are unchanged.
/// </summary>
public class MyActionsPage
{
    private readonly IPage _page;

    public MyActionsPage(IPage page) => _page = page;

    // Page header
    public ILocator PageTitle => _page.Locator("h4:has-text('Work Queue')");
    public ILocator Subtitle => _page.Locator("text=Actions requiring your attention");

    // SignalR connection status chip
    public ILocator ConnectionChip => _page.Locator(".mud-chip").First;

    // Action cards
    public ILocator ActionCards => MudBlazorHelpers.Cards(_page);
    public ILocator EmptyState => _page.Locator("text=All Caught Up");
    public ILocator ServiceError => _page.Locator("text=unavailable");

    // Real-time info banner
    public ILocator RealTimeInfoBanner => _page.Locator("text=Real-time updates enabled");

    // Refresh button
    public ILocator RefreshButton => MudBlazorHelpers.Button(_page, "Refresh");

    // Notification banner
    public ILocator NotificationBanner => _page.Locator(".mud-alert:has-text('notification')");

    // Methods
    public async Task NavigateAsync()
    {
        await _page.GotoAsync($"{Infrastructure.TestConstants.UiWebUrl}{Infrastructure.TestConstants.AuthenticatedRoutes.MyActions}");
        await MudBlazorHelpers.WaitForBlazorAsync(_page);
    }

    public async Task<string?> GetConnectionStatusTextAsync()
    {
        if (await ConnectionChip.CountAsync() > 0)
        {
            return await ConnectionChip.TextContentAsync();
        }
        return null;
    }

    public async Task<bool> IsPageLoadedAsync()
    {
        try
        {
            await PageTitle.WaitForAsync(new() { Timeout = Infrastructure.TestConstants.PageLoadTimeout });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }
}
