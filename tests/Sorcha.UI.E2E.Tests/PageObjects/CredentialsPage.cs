// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Playwright;
using Sorcha.UI.E2E.Tests.Infrastructure;
using Sorcha.UI.E2E.Tests.PageObjects.Shared;

namespace Sorcha.UI.E2E.Tests.PageObjects;

/// <summary>
/// Page object for the Credentials page (/app/credentials).
/// Displays verifiable credentials issued to the logged-in user.
/// </summary>
public class CredentialsPage
{
    private readonly IPage _page;

    public CredentialsPage(IPage page)
    {
        _page = page;
    }

    // Page structure
    public ILocator PageTitle => _page.Locator(
        "h1:has-text('Credentials'), h2:has-text('Credentials'), " +
        ".mud-typography:has-text('Credentials'), " +
        ":has-text('credentials.title'), :has-text('nav.credentials')").First;

    // Credential cards/list
    public ILocator CredentialCards => _page.Locator(
        ".mud-card, [data-testid='credential-card'], [data-testid*='credential']");
    public ILocator CredentialTable => MudBlazorHelpers.Table(_page);
    public ILocator CredentialRows => MudBlazorHelpers.TableRows(_page);

    // Empty state
    public ILocator EmptyState => _page.Locator(
        ".empty-state, [data-testid='empty-state'], " +
        ":has-text('No credentials'), :has-text('no credentials')").First;

    // Loading
    public ILocator LoadingIndicator => MudBlazorHelpers.CircularProgress(_page);

    // Error
    public ILocator ServiceError => _page.Locator(
        ".mud-alert-error, [data-testid='service-error']");

    /// <summary>
    /// Navigates to the credentials page.
    /// </summary>
    public async Task NavigateAsync()
    {
        await _page.GotoAsync(
            $"{TestConstants.UiWebUrl}{TestConstants.AuthenticatedRoutes.Credentials}");
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await MudBlazorHelpers.WaitForBlazorAsync(_page, TestConstants.PageLoadTimeout);
    }

    /// <summary>
    /// Waits for the page to load with content.
    /// </summary>
    public async Task<bool> WaitForPageAsync(int timeout = 0)
    {
        timeout = timeout > 0 ? timeout : TestConstants.PageLoadTimeout;
        try
        {
            // Wait for either credentials content or empty state
            await _page.Locator(
                ".mud-card, .mud-table, .empty-state, [data-testid='credential-card'], " +
                "[data-testid='empty-state']")
                .First.WaitForAsync(new() { Timeout = timeout });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    /// <summary>
    /// Gets the count of credential cards/rows displayed.
    /// </summary>
    public async Task<int> GetCredentialCountAsync()
    {
        var cardCount = await CredentialCards.CountAsync();
        if (cardCount > 0) return cardCount;

        return await CredentialRows.CountAsync();
    }

    /// <summary>
    /// Checks if the page shows the empty state (no credentials).
    /// </summary>
    public async Task<bool> IsEmptyStateVisibleAsync()
    {
        return await EmptyState.CountAsync() > 0 && await EmptyState.IsVisibleAsync();
    }

    /// <summary>
    /// Checks if there is a service error displayed.
    /// </summary>
    public async Task<bool> HasServiceErrorAsync()
    {
        return await ServiceError.CountAsync() > 0 && await ServiceError.IsVisibleAsync();
    }

    /// <summary>
    /// Gets the text content of the first credential card.
    /// </summary>
    public async Task<string?> GetFirstCredentialTextAsync()
    {
        if (await CredentialCards.CountAsync() > 0)
        {
            return await CredentialCards.First.TextContentAsync();
        }
        return null;
    }
}
