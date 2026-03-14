// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Playwright;
using Sorcha.UI.E2E.Tests.PageObjects.Shared;

namespace Sorcha.UI.E2E.Tests.PageObjects.WalletPages;

/// <summary>
/// Page object for the Create Wallet page (/wallets/create).
/// Supports the two-step wizard: form → mnemonic display.
/// </summary>
public class CreateWalletPage
{
    private readonly IPage _page;

    public CreateWalletPage(IPage page) => _page = page;

    // Page header (translated or raw key)
    public ILocator PageTitle => _page.Locator(
        "h4:has-text('Create New Wallet'), h4:has-text('wallet.create'), .mud-typography-h4:has-text('Create')");

    // First-login welcome banner
    public ILocator WelcomeBanner => _page.Locator(
        ".mud-alert:has-text('Welcome to Sorcha'), .mud-alert:has-text('wallet.firstLogin')");
    public ILocator WelcomeBannerTitle => _page.Locator("text=Welcome to Sorcha!");

    // Form fields
    public ILocator WalletNameInput => _page.Locator("input").First;
    public ILocator AlgorithmSelect => _page.Locator(".mud-select").First;
    public ILocator WordCountSelect => _page.Locator(".mud-select").Nth(1);
    public ILocator PassphraseInput => _page.Locator("input[type='password']").First;

    // Buttons
    public ILocator CancelButton => MudBlazorHelpers.Button(_page, "Cancel");
    public ILocator CreateButton => _page.Locator(
        ".mud-button-root:has-text('Create Wallet'), .mud-button-root:has-text('wallet.create')");

    // Mnemonic display step
    public ILocator MnemonicSection => _page.Locator(
        "text=Your Recovery Phrase, text=wallet.mnemonic");
    public ILocator MnemonicWords => _page.Locator(".mud-grid .mud-paper");
    public ILocator CopyAllButton => _page.Locator(
        ".mud-button-root:has-text('Copy All'), .mud-button-root:has-text('wallet.copyAll')");

    // Confirmation checkboxes
    public ILocator WrittenDownCheckbox => _page.Locator(
        "text=I have written down my recovery phrase").Locator("..").Locator("input");
    public ILocator OneTimeCheckbox => _page.Locator(
        "text=I understand this phrase will NEVER").Locator("..").Locator("input");
    public ILocator ContinueButton => _page.Locator(
        ".mud-button-root:has-text('Continue to Wallet'), .mud-button-root:has-text('wallet.continue')");

    // Algorithm info alert
    public ILocator AlgorithmInfoAlert => _page.Locator(".mud-alert-text");

    /// <summary>
    /// Navigates to the create wallet page.
    /// </summary>
    public async Task NavigateAsync()
    {
        await _page.GotoAsync($"{Infrastructure.TestConstants.UiWebUrl}{Infrastructure.TestConstants.AuthenticatedRoutes.WalletCreate}");
        await MudBlazorHelpers.WaitForBlazorAsync(_page);
        await MudBlazorHelpers.WaitForTranslationsAsync(_page);
    }

    /// <summary>
    /// Navigates to the first-login create wallet page.
    /// </summary>
    public async Task NavigateFirstLoginAsync()
    {
        await _page.GotoAsync($"{Infrastructure.TestConstants.UiWebUrl}{Infrastructure.TestConstants.AuthenticatedRoutes.WalletCreateFirstLogin}");
        await MudBlazorHelpers.WaitForBlazorAsync(_page);
        await MudBlazorHelpers.WaitForTranslationsAsync(_page);
    }

    /// <summary>
    /// Opens the algorithm dropdown and returns all available algorithm names.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetAvailableAlgorithmsAsync()
    {
        await AlgorithmSelect.ClickAsync();
        await _page.WaitForTimeoutAsync(300);

        var options = await _page.Locator(".mud-popover-open .mud-list-item").AllTextContentsAsync();

        // Close the dropdown by pressing Escape
        await _page.Keyboard.PressAsync("Escape");
        await _page.WaitForTimeoutAsync(200);

        return options;
    }

    /// <summary>
    /// Selects an algorithm from the dropdown by text match.
    /// </summary>
    public async Task SelectAlgorithmAsync(string algorithmText)
    {
        await AlgorithmSelect.ClickAsync();
        await _page.WaitForTimeoutAsync(300);

        var option = _page.Locator($".mud-popover-open .mud-list-item:has-text('{algorithmText}')");
        await option.ClickAsync();
        await _page.WaitForTimeoutAsync(200);
    }

    /// <summary>
    /// Opens the word count dropdown and returns available options.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetAvailableWordCountsAsync()
    {
        await WordCountSelect.ClickAsync();
        await _page.WaitForTimeoutAsync(300);

        var options = await _page.Locator(".mud-popover-open .mud-list-item").AllTextContentsAsync();

        await _page.Keyboard.PressAsync("Escape");
        await _page.WaitForTimeoutAsync(200);

        return options;
    }

    public async Task<bool> IsWelcomeBannerVisibleAsync()
    {
        return await WelcomeBanner.CountAsync() > 0;
    }

    public async Task<bool> IsCancelButtonVisibleAsync()
    {
        return await CancelButton.CountAsync() > 0;
    }

    public async Task<bool> IsPageLoadedAsync()
    {
        try
        {
            await PageTitle.First.WaitForAsync(new() { Timeout = Infrastructure.TestConstants.PageLoadTimeout });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }
}
