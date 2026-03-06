// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.UI.E2E.Tests.Infrastructure;

namespace Sorcha.UI.E2E.Tests.Docker;

/// <summary>
/// E2E tests for push notification settings page (Feature 051 US5).
/// </summary>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
[Category("Docker")]
[Category("Settings")]
[Category("Authenticated")]
public class PushNotificationTests : AuthenticatedDockerTestBase
{
    [Test]
    [Retry(2)]
    public async Task NotificationSettings_LoadsSuccessfully()
    {
        await NavigateAuthenticatedAsync(TestConstants.AuthenticatedRoutes.NotificationSettings);

        await Expect(Page).ToHaveTitleAsync(
            new System.Text.RegularExpressions.Regex("Notification|Sorcha"));
    }

    [Test]
    public async Task NotificationSettings_ShowsToggle()
    {
        await NavigateAuthenticatedAsync(TestConstants.AuthenticatedRoutes.NotificationSettings);

        var toggle = Page.Locator(".mud-switch");
        await Expect(toggle).ToBeVisibleAsync();
    }

    [Test]
    public async Task NotificationSettings_HasBreadcrumbs()
    {
        await NavigateAuthenticatedAsync(TestConstants.AuthenticatedRoutes.NotificationSettings);

        var breadcrumbs = Page.Locator(".mud-breadcrumbs");
        await Expect(breadcrumbs).ToBeVisibleAsync();
    }

    [Test]
    public async Task NotificationSettings_ShowsStatusIndicator()
    {
        await NavigateAuthenticatedAsync(TestConstants.AuthenticatedRoutes.NotificationSettings);

        var chip = Page.Locator(".mud-chip");
        var spinner = Page.Locator(".mud-progress-circular");
        var hasChip = await chip.CountAsync() > 0;
        var hasSpinner = await spinner.CountAsync() > 0;

        Assert.That(hasChip || hasSpinner, Is.True,
            "Should show status chip or loading indicator");
    }
}
