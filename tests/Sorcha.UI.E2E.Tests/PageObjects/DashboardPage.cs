// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Playwright;
using Sorcha.UI.E2E.Tests.Infrastructure;
using Sorcha.UI.E2E.Tests.PageObjects.Shared;

namespace Sorcha.UI.E2E.Tests.PageObjects;

/// <summary>
/// Page object for the Dashboard page (/app/dashboard).
/// Selectors handle both translated text and raw i18n keys.
/// </summary>
public class DashboardPage
{
    private readonly IPage _page;

    public DashboardPage(IPage page)
    {
        _page = page;
    }

    // Welcome heading — matches translated "Welcome back" or raw key "dashboard.welcomeBack"
    public ILocator WelcomeHeading => _page.Locator(
        ".mud-typography-h4:has-text('Welcome'), .mud-typography-h4:has-text('dashboard.welcomeBack')");

    // Stat cards — MudCards in a MudGrid
    public ILocator StatCards => _page.Locator(".mud-grid .mud-card");

    // Individual stat cards — match translated or raw key
    public ILocator BlueprintsStat => _page.Locator(
        ".mud-card:has-text('Blueprints'), .mud-card:has-text('dashboard.stats.blueprints')");
    public ILocator WalletsStat => _page.Locator(
        ".mud-card:has-text('Wallets'), .mud-card:has-text('dashboard.stats.wallets')");
    public ILocator TransactionsStat => _page.Locator(
        ".mud-card:has-text('Transactions'), .mud-card:has-text('dashboard.stats.transactions')");
    public ILocator PeersStat => _page.Locator(
        ".mud-card:has-text('Peers'), .mud-card:has-text('dashboard.stats.peers')");
    public ILocator RegistersStat => _page.Locator(
        ".mud-card:has-text('Registers'), .mud-card:has-text('dashboard.stats.registers')");
    public ILocator OrganizationsStat => _page.Locator(
        ".mud-card:has-text('Organizations'), .mud-card:has-text('dashboard.stats.organizations')");

    // Quick actions — MudButtons (match translated or uppercase key)
    public ILocator CreateBlueprintButton => _page.Locator(
        ".mud-button-root:has-text('Create Blueprint'), .mud-button-root:has-text('blueprint.create')");
    public ILocator ManageWalletsButton => _page.Locator(
        ".mud-button-root:has-text('Manage Wallets'), .mud-button-root:has-text('dashboard.manageWallets')");
    public ILocator ViewRegistersButton => _page.Locator(
        ".mud-button-root:has-text('View Registers'), .mud-button-root:has-text('dashboard.viewRegisters')");

    // Recent activity section
    public ILocator RecentActivity => _page.Locator(
        ".mud-typography:has-text('Recent Activity'), .mud-typography:has-text('dashboard.recentActivity')");
    public ILocator EmptyState => _page.Locator(
        ":has-text('No recent activity'), :has-text('dashboard.noRecentActivity')");

    // Alerts section
    public ILocator AlertsSection => _page.Locator(
        ".mud-typography:has-text('Active Alerts'), .mud-alert");

    /// <summary>
    /// Gets the welcome message text.
    /// </summary>
    public async Task<string?> GetWelcomeMessageAsync()
    {
        if (await WelcomeHeading.CountAsync() > 0)
        {
            return await WelcomeHeading.First.TextContentAsync();
        }
        return null;
    }

    /// <summary>
    /// Gets the stat card value for a given label.
    /// </summary>
    public async Task<string?> GetStatValueAsync(string label)
    {
        var card = _page.Locator($".mud-card:has-text('{label}') .mud-typography-h5");
        if (await card.CountAsync() > 0)
        {
            return await card.TextContentAsync();
        }
        return null;
    }

    /// <summary>
    /// Gets the count of visible stat cards.
    /// </summary>
    public async Task<int> GetStatCardCountAsync()
    {
        return await StatCards.CountAsync();
    }

    /// <summary>
    /// Checks whether the quick action buttons are visible.
    /// </summary>
    public async Task<bool> AreQuickActionsVisibleAsync()
    {
        return await CreateBlueprintButton.CountAsync() > 0
            || await ManageWalletsButton.CountAsync() > 0
            || await ViewRegistersButton.CountAsync() > 0;
    }

    /// <summary>
    /// Checks whether the dashboard shows the empty state for recent activity.
    /// </summary>
    public async Task<bool> IsRecentActivityEmptyAsync()
    {
        return await EmptyState.CountAsync() > 0;
    }

    /// <summary>
    /// Checks whether the dashboard has loaded with authenticated content.
    /// Looks for welcome heading OR stat cards (either indicates dashboard rendered).
    /// </summary>
    public async Task<bool> IsLoadedAsync()
    {
        return await WelcomeHeading.CountAsync() > 0
            || await StatCards.CountAsync() > 0;
    }
}
