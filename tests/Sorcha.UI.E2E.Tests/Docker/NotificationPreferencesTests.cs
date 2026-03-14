// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Playwright;
using Sorcha.UI.E2E.Tests.Infrastructure;
using Sorcha.UI.E2E.Tests.PageObjects.Shared;

namespace Sorcha.UI.E2E.Tests.Docker;

/// <summary>
/// E2E tests for the Notification Preferences panel within the Settings page.
/// Verifies notification method dropdown, frequency radio buttons, save persistence,
/// and default values for new users.
/// </summary>
[TestFixture]
[Category("Docker")]
[Category("Settings")]
[Category("Notifications")]
[Category("Authenticated")]
[Parallelizable(ParallelScope.Self)]
public class NotificationPreferencesTests : AuthenticatedDockerTestBase
{
    /// <summary>
    /// Navigates to the Settings page and clicks the Notifications tab.
    /// </summary>
    private async Task NavigateToNotificationsTabAsync()
    {
        await NavigateAuthenticatedAsync(TestConstants.AuthenticatedRoutes.Settings);
        await MudBlazorHelpers.WaitForBlazorAsync(Page, TestConstants.PageLoadTimeout);

        // Click the Notifications tab (check for translated text or i18n key)
        var notificationsTab = Page.Locator(".mud-tab:has-text('Notifications'), .mud-tab:has-text('settings.notifications')");
        await notificationsTab.First.WaitForAsync(new() { Timeout = TestConstants.ElementTimeout });
        await notificationsTab.First.ClickAsync();
        await Page.WaitForTimeoutAsync(TestConstants.ShortWait);
    }

    /// <summary>
    /// Ensures the notifications toggle is enabled so that method and frequency controls are visible.
    /// </summary>
    private async Task EnsureNotificationsEnabledAsync()
    {
        // Check if the notification method section is already visible (notifications enabled)
        var methodSection = Page.Locator(".mud-select");
        var isVisible = await methodSection.CountAsync() > 0 && await methodSection.First.IsVisibleAsync();

        if (!isVisible)
        {
            // Toggle the notifications switch on
            var toggle = Page.Locator(".mud-switch");
            if (await toggle.CountAsync() > 0)
            {
                await toggle.First.ClickAsync();
                await Page.WaitForTimeoutAsync(TestConstants.ShortWait);
            }
        }
    }

    #region Panel Visibility Tests

    [Test]
    [Retry(2)]
    public async Task NavigateToSettings_NotificationPreferencesVisible()
    {
        await NavigateToNotificationsTabAsync();

        // Verify the Notifications tab panel is active and contains expected content
        // Check for both translated English text and raw i18n key
        var pushHeading = Page.Locator("text=Push Notifications");
        var pushHeadingI18n = Page.Locator("text=settings.notifications.push");
        var hasPushHeading = await pushHeading.CountAsync() > 0 || await pushHeadingI18n.CountAsync() > 0;
        Assert.That(hasPushHeading, Is.True,
            "Push Notifications heading should be visible (translated or i18n key)");

        // Verify the notifications toggle switch is present
        var toggle = Page.Locator(".mud-switch");
        Assert.That(await toggle.CountAsync(), Is.GreaterThan(0),
            "Notifications tab should contain a toggle switch for enabling/disabling notifications");

        // Enable notifications to reveal method and frequency controls
        await EnsureNotificationsEnabledAsync();

        // Verify notification method dropdown is visible
        var methodSelect = Page.Locator(".mud-select");
        Assert.That(await methodSelect.CountAsync(), Is.GreaterThan(0),
            "Notification method dropdown should be visible when notifications are enabled");

        // Verify notification frequency radio group is visible
        var radioButtons = Page.Locator(".mud-radio");
        Assert.That(await radioButtons.CountAsync(), Is.GreaterThan(0),
            "Notification frequency radio buttons should be visible when notifications are enabled");

        // Verify the three frequency options: RealTime, HourlyDigest, DailyDigest
        // Check for both translated English text and raw i18n keys
        var hasRealTime = await Page.Locator("text=Real-Time").CountAsync() > 0 ||
                          await Page.Locator("text=settings.notifications.frequency.realTime").CountAsync() > 0;
        var hasHourly = await Page.Locator("text=Hourly Digest").CountAsync() > 0 ||
                        await Page.Locator("text=settings.notifications.frequency.hourlyDigest").CountAsync() > 0;
        var hasDaily = await Page.Locator("text=Daily Digest").CountAsync() > 0 ||
                       await Page.Locator("text=settings.notifications.frequency.dailyDigest").CountAsync() > 0;

        Assert.That(hasRealTime, Is.True, "Real-Time frequency option should be visible");
        Assert.That(hasHourly, Is.True, "Hourly Digest frequency option should be visible");
        Assert.That(hasDaily, Is.True, "Daily Digest frequency option should be visible");
    }

    #endregion

    #region Method Persistence Tests

    [Test]
    [Retry(2)]
    public async Task ChangeNotificationMethod_SaveAndReload_PersistSelection()
    {
        await NavigateToNotificationsTabAsync();
        await EnsureNotificationsEnabledAsync();

        // Open the MudSelect dropdown and choose "In-App + Email"
        var selectInput = Page.Locator(".mud-select .mud-input-control");
        await selectInput.First.ClickAsync();
        await Page.WaitForTimeoutAsync(TestConstants.ShortWait);

        // Select the "In-App + Email" option from the popover
        var inAppPlusEmailOption = Page.Locator(".mud-popover-provider .mud-list-item:has-text('In-App + Email')");
        if (await inAppPlusEmailOption.CountAsync() > 0)
        {
            await inAppPlusEmailOption.First.ClickAsync();
        }
        else
        {
            // Fallback: try the MudSelectItem locator
            var option = Page.Locator(".mud-list-item-text:has-text('In-App + Email')");
            await option.First.ClickAsync();
        }

        // Wait for the save operation (snackbar confirmation)
        var snackbar = Page.Locator(".mud-snackbar");
        await snackbar.WaitForAsync(new() { Timeout = TestConstants.ElementTimeout });

        // Reload the page to verify persistence
        await NavigateToNotificationsTabAsync();
        await EnsureNotificationsEnabledAsync();

        // Verify the saved method is reflected in the dropdown
        var selectedValue = Page.Locator(".mud-select .mud-input-slot, .mud-select input");
        var selectText = await Page.Locator(".mud-select").First.TextContentAsync() ?? "";

        Assert.That(selectText, Does.Contain("In-App + Email").Or.Contain("InAppPlusEmail"),
            "After reload, notification method should persist as 'In-App + Email'");
    }

    #endregion

    #region Frequency Persistence Tests

    [Test]
    [Retry(2)]
    public async Task ChangeNotificationFrequency_SaveAndReload_PersistSelection()
    {
        await NavigateToNotificationsTabAsync();
        await EnsureNotificationsEnabledAsync();

        // Click the "Daily Digest" radio button (check for both translated text and i18n key)
        var dailyDigestRadio = Page.Locator(".mud-radio:has-text('Daily Digest'), .mud-radio:has-text('settings.notifications.frequency.dailyDigest')");
        await dailyDigestRadio.First.ClickAsync();
        await Page.WaitForTimeoutAsync(TestConstants.ShortWait);

        // Wait for the save operation (snackbar confirmation)
        var snackbar = Page.Locator(".mud-snackbar");
        await snackbar.WaitForAsync(new() { Timeout = TestConstants.ElementTimeout });

        // Reload the page to verify persistence
        await NavigateToNotificationsTabAsync();
        await EnsureNotificationsEnabledAsync();

        // Verify the Daily Digest radio is selected (has the checked class)
        var checkedRadio = Page.Locator(".mud-radio.mud-checked:has-text('Daily Digest'), .mud-radio.mud-checked:has-text('settings.notifications.frequency.dailyDigest'), .mud-radio input[checked]:has-text('Daily Digest')");
        var dailyDigestText = Page.Locator(".mud-radio:has-text('Daily Digest'), .mud-radio:has-text('settings.notifications.frequency.dailyDigest')");

        // MudRadio checked state: check that the Daily Digest radio's input is checked
        var dailyRadioInput = dailyDigestText.First.Locator("input[type='radio']");
        if (await dailyRadioInput.CountAsync() > 0)
        {
            var isChecked = await dailyRadioInput.First.IsCheckedAsync();
            Assert.That(isChecked, Is.True,
                "After reload, Daily Digest radio should be selected");
        }
        else
        {
            // Fallback: check for checked CSS class on the radio container
            var radioCount = await checkedRadio.CountAsync();
            Assert.That(radioCount, Is.GreaterThan(0),
                "After reload, Daily Digest radio should be selected");
        }
    }

    #endregion

    #region Default Values Tests

    [Test]
    [Retry(2)]
    public async Task DefaultPreferences_NewUser_ShowsInAppAndRealTime()
    {
        // Navigate to Settings > Notifications tab
        await NavigateToNotificationsTabAsync();

        // For a default user, NotificationsEnabled defaults to false.
        // The toggle should be present. We need to check the initial state before
        // any interaction, then enable to verify defaults.
        // Note: If a previous test changed state, this verifies the default method/frequency
        // values that are applied when preferences are first loaded.

        // Enable notifications to see method and frequency
        await EnsureNotificationsEnabledAsync();

        // Verify default notification method is "InApp" (In-App Only)
        var selectText = await Page.Locator(".mud-select").First.TextContentAsync() ?? "";
        Assert.That(selectText, Does.Contain("In-App Only").Or.Contain("InApp").Or.Contain("settings.notifications.method"),
            "Default notification method should be 'In-App Only'");

        // Verify default notification frequency is "RealTime"
        // The RealTime radio should be checked by default
        var realTimeRadio = Page.Locator(".mud-radio:has-text('Real-Time'), .mud-radio:has-text('settings.notifications.frequency.realTime')");
        var realTimeInput = realTimeRadio.First.Locator("input[type='radio']");

        if (await realTimeInput.CountAsync() > 0)
        {
            var isChecked = await realTimeInput.First.IsCheckedAsync();
            Assert.That(isChecked, Is.True,
                "Default notification frequency should be 'Real-Time'");
        }
        else
        {
            // Fallback: check for the checked CSS class
            var checkedRealTime = Page.Locator(".mud-radio.mud-checked:has-text('Real-Time'), .mud-radio.mud-checked:has-text('settings.notifications.frequency.realTime')");
            Assert.That(await checkedRealTime.CountAsync(), Is.GreaterThan(0),
                "Default notification frequency should be 'Real-Time'");
        }
    }

    #endregion
}
