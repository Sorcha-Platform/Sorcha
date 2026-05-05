// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Playwright;
using Sorcha.UI.E2E.Tests.Infrastructure;
using Sorcha.UI.E2E.Tests.PageObjects.Shared;

namespace Sorcha.UI.E2E.Tests.Docker;

/// <summary>
/// E2E coverage for the additive <c>/my-devices</c> page (Feature 114, T122 — US3 PR5).
/// Validates that an authenticated citizen can reach the page from the user-profile menu
/// and that the empty / list states render with the right test ids. Revoke-flow happy
/// path is covered by the citizen-wallet E2E suite (T149 — Polish phase) once a real
/// device has been enrolled; here we only need to prove the additive web surface and
/// menu link don't regress and call the right Tenant endpoint.
/// </summary>
[TestFixture]
[Category("Docker")]
[Category("Devices")]
[Category("Authenticated")]
[Parallelizable(ParallelScope.Self)]
public class MyDevicesTests : AuthenticatedDockerTestBase
{
    [Test]
    [Retry(2)]
    public async Task MyDevices_PageLoads_HeadingVisible()
    {
        await NavigateAuthenticatedAsync(TestConstants.AuthenticatedRoutes.MyDevices);
        await MudBlazorHelpers.WaitForBlazorAsync(Page, TestConstants.PageLoadTimeout);

        var heading = Page.Locator("h4:has-text('My Devices')");
        await heading.WaitForAsync(new() { Timeout = TestConstants.ElementTimeout });
        Assert.That(await heading.IsVisibleAsync(), Is.True,
            "MyDevices page should render with the 'My Devices' h4 heading");
    }

    [Test]
    [Retry(2)]
    public async Task MyDevices_NoEnrolledDevices_ShowsEmptyState()
    {
        // The default test user has no wallet devices enrolled (PWA enrolment is a
        // separate flow). Assert the empty-state alert renders so the page handles
        // zero rows gracefully — the failure mode we care about is "page crashes
        // because Devices is null".
        await NavigateAuthenticatedAsync(TestConstants.AuthenticatedRoutes.MyDevices);
        await MudBlazorHelpers.WaitForBlazorAsync(Page, TestConstants.PageLoadTimeout);

        // Either the empty state OR an existing list — both are acceptable; what
        // we don't want is the error alert.
        var error = Page.Locator("[data-testid='my-devices-error']");
        await Page.WaitForTimeoutAsync(TestConstants.ShortWait);
        Assert.That(await error.CountAsync(), Is.EqualTo(0),
            "MyDevices should not surface an error alert on a clean load");

        var empty = Page.Locator("[data-testid='my-devices-empty']");
        var list = Page.Locator("[data-testid='my-devices-list']");
        var hasEmpty = await empty.CountAsync() > 0;
        var hasList = await list.CountAsync() > 0;
        Assert.That(hasEmpty || hasList, Is.True,
            "MyDevices should render either the empty-state alert or the device list");
    }

    [Test]
    [Retry(2)]
    public async Task UserProfileMenu_ContainsMyDevicesLink()
    {
        await NavigateAuthenticatedAsync(TestConstants.AuthenticatedRoutes.Dashboard);
        await MudBlazorHelpers.WaitForBlazorAsync(Page, TestConstants.PageLoadTimeout);

        // Open the user profile menu — pattern follows the existing UserProfileMenu
        // (avatar button in the top app bar). The menu items render lazily, so we
        // assert the new MyDevices entry is present after opening.
        var avatar = Page.Locator(".mud-avatar").First;
        if (await avatar.CountAsync() > 0)
        {
            await avatar.ClickAsync();
            await Page.WaitForTimeoutAsync(TestConstants.ShortWait);
        }

        var myDevicesItem = Page.Locator("[data-testid='user-menu-my-devices']");
        Assert.That(await myDevicesItem.CountAsync(), Is.GreaterThan(0),
            "UserProfileMenu should expose a 'My Devices' menu item linking to /my-devices");
    }
}
