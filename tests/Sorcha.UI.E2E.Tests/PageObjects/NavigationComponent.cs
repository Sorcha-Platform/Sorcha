// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Playwright;
using Sorcha.UI.E2E.Tests.Infrastructure;
using Sorcha.UI.E2E.Tests.PageObjects.Shared;

namespace Sorcha.UI.E2E.Tests.PageObjects;

/// <summary>
/// Page object for the MudBlazor navigation drawer and app bar.
/// Selectors handle both translated text and raw i18n keys.
/// </summary>
public class NavigationComponent
{
    private readonly IPage _page;

    public NavigationComponent(IPage page)
    {
        _page = page;
    }

    // App Bar
    public ILocator AppBar => MudBlazorHelpers.AppBar(_page);
    public ILocator AppTitle => _page.Locator(".mud-appbar .mud-typography-h5, .mud-appbar .mud-typography:has-text('Sorcha')");
    public ILocator MenuToggle => _page.Locator(".mud-appbar .mud-icon-button").First;
    public ILocator UserMenu => _page.Locator(".mud-appbar .mud-menu, .mud-appbar .mud-avatar");
    public ILocator SignInButton => _page.Locator(
        ".mud-appbar a:has-text('Sign In'), .mud-appbar button:has-text('Sign In'), .mud-appbar :has-text('nav.signIn')");

    // Drawer
    public ILocator Drawer => _page.Locator(".mud-drawer");
    public ILocator DrawerHeader => _page.Locator(".mud-drawer-header");
    public ILocator NavMenu => MudBlazorHelpers.NavMenu(_page);

    // Navigation links — match translated text OR raw i18n key
    public ILocator DashboardLink => NavLink("Dashboard", "nav.dashboard");
    public ILocator PendingActionsLink => NavLink("Pending Actions", "nav.pendingActions");
    public ILocator NewSubmissionLink => NavLink("New Submission", "nav.newSubmission");
    public ILocator MyTransactionsLink => NavLink("My Transactions", "nav.myTransactions");
    public ILocator MyWalletLink => NavLink("My Wallet", "nav.myWallet");
    public ILocator MyCredentialsLink => NavLink("My Credentials", "nav.myCredentials");
    public ILocator MyBlueprintsLink => NavLink("My Blueprints", "nav.myBlueprints");
    public ILocator VisualDesignerLink => NavLink("Visual Designer", "nav.visualDesigner");
    public ILocator AiChatDesignerLink => NavLink("AI Chat Designer", "nav.aiChatDesigner");
    public ILocator CatalogueLink => NavLink("Catalogue", "nav.catalogue");
    public ILocator DataSchemasLink => NavLink("Data Schemas", "nav.dataSchemas");
    public ILocator AllWalletsLink => NavLink("All Wallets", "nav.allWallets");
    public ILocator CreateWalletLink => NavLink("Create Wallet", "nav.createWallet");
    public ILocator RecoverWalletLink => NavLink("Recover Wallet", "nav.recoverWallet");
    public ILocator RegistersLink => NavLink("Registers", "nav.registers");
    public ILocator ParticipantsLink => NavLink("Participants", "nav.participants");
    public ILocator SystemHealthLink => NavLink("System Health", "nav.systemHealth");
    public ILocator PeerNetworkLink => NavLink("Peer Network", "nav.peerNetwork");
    public ILocator SettingsLink => NavLink("Settings", "nav.settings");
    public ILocator HelpLink => NavLink("Help", "nav.help");

    // Nav groups (expandable) — match translated or raw key
    public ILocator WalletsGroup => _page.Locator(
        ".mud-nav-group:has-text('Wallets'), .mud-nav-group:has-text('nav.wallets')");
    public ILocator AdministrationGroup => _page.Locator(
        ".mud-nav-group:has-text('Administration'), .mud-nav-group:has-text('System'), .mud-nav-group:has-text('Identity')");

    /// <summary>
    /// Creates a nav link locator matching either translated text or raw i18n key.
    /// </summary>
    private ILocator NavLink(string translatedText, string i18nKey)
    {
        return _page.Locator($".mud-nav-link:has-text('{translatedText}'), .mud-nav-link:has-text('{i18nKey}')");
    }

    /// <summary>
    /// Toggles the drawer open/closed.
    /// </summary>
    public async Task ToggleDrawerAsync()
    {
        await MenuToggle.ClickAsync();
        await _page.WaitForTimeoutAsync(300);
    }

    /// <summary>
    /// Checks whether the drawer is currently open.
    /// MudBlazor uses mud-drawer--open or similar class.
    /// </summary>
    public async Task<bool> IsDrawerOpenAsync()
    {
        var drawerClass = await Drawer.First.GetAttributeAsync("class") ?? "";
        return drawerClass.Contains("open", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Expands a nav group if it's collapsed.
    /// </summary>
    public async Task ExpandNavGroupAsync(ILocator navGroup)
    {
        var toggle = navGroup.Locator("button, .mud-nav-link").First;
        if (await toggle.CountAsync() > 0)
        {
            await toggle.ClickAsync();
            await _page.WaitForTimeoutAsync(500);
        }
    }

    /// <summary>
    /// Navigates to a page by clicking a nav link.
    /// </summary>
    public async Task NavigateToAsync(ILocator navLink)
    {
        await navLink.First.ClickAsync();
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await _page.WaitForTimeoutAsync(TestConstants.ShortWait);
    }

    /// <summary>
    /// Opens the user menu in the app bar.
    /// </summary>
    public async Task OpenUserMenuAsync()
    {
        var menuButton = UserMenu.Locator(".mud-icon-button, .mud-avatar").First;
        await menuButton.ClickAsync();
        await _page.WaitForTimeoutAsync(300);
    }

    /// <summary>
    /// Gets the displayed username from the user menu.
    /// </summary>
    public async Task<string?> GetDisplayedUsernameAsync()
    {
        await OpenUserMenuAsync();
        var menuContent = _page.Locator(".mud-popover-open .mud-typography-body2, .mud-popover-open .mud-list-item");
        if (await menuContent.CountAsync() > 0)
        {
            return await menuContent.First.TextContentAsync();
        }
        return null;
    }

    /// <summary>
    /// Checks whether the authenticated nav menu is visible.
    /// </summary>
    public async Task<bool> IsAuthenticatedNavVisibleAsync()
    {
        return await DashboardLink.CountAsync() > 0;
    }
}
