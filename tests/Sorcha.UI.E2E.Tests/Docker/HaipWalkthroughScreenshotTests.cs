// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Playwright;
using Sorcha.UI.E2E.Tests.Infrastructure;

namespace Sorcha.UI.E2E.Tests.Docker;

/// <summary>
/// Screenshots the UI state after the HAIP walkthroughs have run.
/// Captures the admin, issuer, and citizen views of credentials,
/// wallets, organizations, and presentation requests.
///
/// Prerequisites: run the HAIP walkthrough setup scripts first:
///   pwsh walkthroughs/HaipIdentityAttestation/setup.ps1
///   pwsh walkthroughs/HaipIdentityAttestation/run.ps1
///   pwsh walkthroughs/HaipDrivingLicence/setup.ps1
///   pwsh walkthroughs/HaipDrivingLicence/run.ps1
/// </summary>
[TestFixture]
[NonParallelizable]
[Category("HaipScreenshots")]
public class HaipWalkthroughScreenshotTests : DockerTestBase
{
    private const string GovAdminEmail = "gov-admin@haip-walkthrough.local";
    private const string CouncilAdminEmail = "council-admin@haip-walkthrough.local";
    private const string CitizenEmail = "alice.obrien@haip-walkthrough.local";
    private const string DefaultPassword = "Dev_Pass_2025!";

    private string _screenshotDir = null!;

    [SetUp]
    public override async Task BaseSetUp()
    {
        await base.BaseSetUp();
        _screenshotDir = Path.Combine(
            TestContext.CurrentContext.WorkDirectory, "haip-screenshots");
        Directory.CreateDirectory(_screenshotDir);
    }

    private async Task LoginAsAsync(string email, string password, string? orgName = null)
    {
        var baseUrl = TestConstants.UiWebUrl;

        // Navigate to login
        await Page.GotoAsync($"{baseUrl}/auth/login",
            new() { WaitUntil = WaitUntilState.NetworkIdle, Timeout = TestConstants.PageLoadTimeout });

        // Fill login form
        await Page.Locator("input[type='email'], input[autocomplete='email']").First.FillAsync(email);
        await Page.Locator("input[type='password']").First.FillAsync(password);
        await Page.Locator("button[type='submit']").First.ClickAsync();

        // Multi-org users stay on /auth/login with org selection cards.
        // Single-org users redirect to /app/.
        // Wait for either: org selection cards appear OR URL changes to /app/
        if (orgName != null)
        {
            // Wait for the org card to appear (SPA renders org selection within login page)
            var orgCard = Page.GetByText(orgName, new() { Exact = false });
            try
            {
                await orgCard.First.WaitForAsync(new() { Timeout = TestConstants.PageLoadTimeout });
                await orgCard.First.ClickAsync();
            }
            catch (TimeoutException)
            {
                // Maybe the user only has one org — check if we're already on /app/
                if (!Page.Url.Contains("/app/"))
                {
                    await Page.ScreenshotAsync(new()
                    {
                        Path = Path.Combine(_screenshotDir, $"debug-login-{email.Split('@')[0]}.png")
                    });
                    throw new InvalidOperationException($"Could not find org '{orgName}' for {email}");
                }
            }
        }

        // Wait for dashboard/app to load
        try
        {
            await Page.WaitForURLAsync(
                url => url.Contains("/app/") || url.Contains("/wallets/create"),
                new() { Timeout = TestConstants.PageLoadTimeout });
        }
        catch (TimeoutException)
        {
            // Best effort — capture where we are
        }

        await Page.WaitForTimeoutAsync(TestConstants.BlazorHydrationTimeout);
    }

    private async Task ScreenshotAsync(string name)
    {
        var path = Path.Combine(_screenshotDir, $"{name}.png");
        await Page.ScreenshotAsync(new()
        {
            Path = path,
            FullPage = true
        });
        TestContext.Out.WriteLine($"Screenshot: {path}");
    }

    // ========================================================================
    // Path 1: System Admin View
    // ========================================================================

    [Test]
    [Order(1)]
    public async Task Admin_Dashboard()
    {
        await LoginAsAsync(TestConstants.TestEmail, TestConstants.TestPassword);
        await Page.GotoAsync($"{TestConstants.UiWebUrl}{TestConstants.AuthenticatedRoutes.Dashboard}",
            new() { WaitUntil = WaitUntilState.NetworkIdle });
        await Page.WaitForTimeoutAsync(TestConstants.ShortWait);
        await ScreenshotAsync("01-admin-dashboard");
    }

    [Test]
    [Order(2)]
    public async Task Admin_Organizations()
    {
        await LoginAsAsync(TestConstants.TestEmail, TestConstants.TestPassword);
        await Page.GotoAsync($"{TestConstants.UiWebUrl}{TestConstants.AuthenticatedRoutes.AdminOrganizations}",
            new() { WaitUntil = WaitUntilState.NetworkIdle });
        await Page.WaitForTimeoutAsync(TestConstants.ShortWait);
        await ScreenshotAsync("02-admin-organizations");
    }

    // ========================================================================
    // Path 2: Government Admin (Identity Issuer)
    // ========================================================================

    [Test]
    [Order(10)]
    public async Task GovAdmin_Dashboard()
    {
        await LoginAsAsync(GovAdminEmail, DefaultPassword, "Government Identity Authority");
        await ScreenshotAsync("10-gov-dashboard");
    }

    [Test]
    [Order(11)]
    public async Task GovAdmin_Wallets()
    {
        await LoginAsAsync(GovAdminEmail, DefaultPassword, "Government Identity Authority");
        await Page.GotoAsync($"{TestConstants.UiWebUrl}{TestConstants.AuthenticatedRoutes.Wallets}",
            new() { WaitUntil = WaitUntilState.NetworkIdle });
        await Page.WaitForTimeoutAsync(TestConstants.ShortWait);
        await ScreenshotAsync("11-gov-wallets");
    }

    [Test]
    [Order(12)]
    public async Task GovAdmin_Credentials()
    {
        await LoginAsAsync(GovAdminEmail, DefaultPassword, "Government Identity Authority");
        await Page.GotoAsync($"{TestConstants.UiWebUrl}{TestConstants.AuthenticatedRoutes.Credentials}",
            new() { WaitUntil = WaitUntilState.NetworkIdle });
        await Page.WaitForTimeoutAsync(TestConstants.ShortWait);
        await ScreenshotAsync("12-gov-credentials");
    }

    // ========================================================================
    // Path 3: Council Admin (Licence Issuer + Verifier)
    // ========================================================================

    [Test]
    [Order(20)]
    public async Task CouncilAdmin_Dashboard()
    {
        await LoginAsAsync(CouncilAdminEmail, DefaultPassword, "Council Licensing Authority");
        await ScreenshotAsync("20-council-dashboard");
    }

    [Test]
    [Order(21)]
    public async Task CouncilAdmin_Wallets()
    {
        await LoginAsAsync(CouncilAdminEmail, DefaultPassword, "Council Licensing Authority");
        await Page.GotoAsync($"{TestConstants.UiWebUrl}{TestConstants.AuthenticatedRoutes.Wallets}",
            new() { WaitUntil = WaitUntilState.NetworkIdle });
        await Page.WaitForTimeoutAsync(TestConstants.ShortWait);
        await ScreenshotAsync("21-council-wallets");
    }

    [Test]
    [Order(22)]
    public async Task CouncilAdmin_Presentations()
    {
        await LoginAsAsync(CouncilAdminEmail, DefaultPassword, "Council Licensing Authority");
        await Page.GotoAsync($"{TestConstants.UiWebUrl}{TestConstants.AuthenticatedRoutes.AdminPresentations}",
            new() { WaitUntil = WaitUntilState.NetworkIdle });
        await Page.WaitForTimeoutAsync(TestConstants.ShortWait);
        await ScreenshotAsync("22-council-presentations");
    }

    // ========================================================================
    // Path 4: Citizen (Credential Holder)
    // ========================================================================

    [Test]
    [Order(30)]
    public async Task Citizen_Dashboard()
    {
        await LoginAsAsync(CitizenEmail, DefaultPassword);
        await ScreenshotAsync("30-citizen-dashboard");
    }

    [Test]
    [Order(31)]
    public async Task Citizen_Credentials()
    {
        await LoginAsAsync(CitizenEmail, DefaultPassword);
        await Page.GotoAsync($"{TestConstants.UiWebUrl}{TestConstants.AuthenticatedRoutes.Credentials}",
            new() { WaitUntil = WaitUntilState.NetworkIdle });
        await Page.WaitForTimeoutAsync(TestConstants.ShortWait);
        await ScreenshotAsync("31-citizen-credentials");
    }

    // ========================================================================
    // Path 2b: Government Admin - Extra Views
    // ========================================================================

    [Test]
    [Order(13)]
    public async Task GovAdmin_MyActions()
    {
        await LoginAsAsync(GovAdminEmail, DefaultPassword, "Government Identity Authority");
        await Page.GotoAsync($"{TestConstants.UiWebUrl}{TestConstants.AuthenticatedRoutes.MyActions}",
            new() { WaitUntil = WaitUntilState.NetworkIdle });
        await Page.WaitForTimeoutAsync(TestConstants.ShortWait);
        await ScreenshotAsync("13-gov-my-actions");
    }

    [Test]
    [Order(14)]
    public async Task GovAdmin_MyWorkflows()
    {
        await LoginAsAsync(GovAdminEmail, DefaultPassword, "Government Identity Authority");
        await Page.GotoAsync($"{TestConstants.UiWebUrl}{TestConstants.AuthenticatedRoutes.MyWorkflows}",
            new() { WaitUntil = WaitUntilState.NetworkIdle });
        await Page.WaitForTimeoutAsync(TestConstants.ShortWait);
        await ScreenshotAsync("14-gov-my-workflows");
    }

    // ========================================================================
    // Path 3b: Council Admin - Extra Views
    // ========================================================================

    [Test]
    [Order(23)]
    public async Task CouncilAdmin_Credentials()
    {
        await LoginAsAsync(CouncilAdminEmail, DefaultPassword, "Council Licensing Authority");
        await Page.GotoAsync($"{TestConstants.UiWebUrl}{TestConstants.AuthenticatedRoutes.Credentials}",
            new() { WaitUntil = WaitUntilState.NetworkIdle });
        await Page.WaitForTimeoutAsync(TestConstants.ShortWait);
        await ScreenshotAsync("23-council-credentials");
    }

    [Test]
    [Order(24)]
    public async Task CouncilAdmin_MyActions()
    {
        await LoginAsAsync(CouncilAdminEmail, DefaultPassword, "Council Licensing Authority");
        await Page.GotoAsync($"{TestConstants.UiWebUrl}{TestConstants.AuthenticatedRoutes.MyActions}",
            new() { WaitUntil = WaitUntilState.NetworkIdle });
        await Page.WaitForTimeoutAsync(TestConstants.ShortWait);
        await ScreenshotAsync("24-council-my-actions");
    }

    // ========================================================================
    // Path 4b: Citizen - Extra Views
    // ========================================================================

    [Test]
    [Order(32)]
    public async Task Citizen_MyActions()
    {
        await LoginAsAsync(CitizenEmail, DefaultPassword);
        await Page.GotoAsync($"{TestConstants.UiWebUrl}{TestConstants.AuthenticatedRoutes.MyActions}",
            new() { WaitUntil = WaitUntilState.NetworkIdle });
        await Page.WaitForTimeoutAsync(TestConstants.ShortWait);
        await ScreenshotAsync("32-citizen-my-actions");
    }

    [Test]
    [Order(33)]
    public async Task Citizen_Wallets()
    {
        await LoginAsAsync(CitizenEmail, DefaultPassword);
        await Page.GotoAsync($"{TestConstants.UiWebUrl}{TestConstants.AuthenticatedRoutes.Wallets}",
            new() { WaitUntil = WaitUntilState.NetworkIdle });
        await Page.WaitForTimeoutAsync(TestConstants.ShortWait);
        await ScreenshotAsync("33-citizen-wallets");
    }

    // ========================================================================
    // HAIP Service Endpoints (API responses, not UI)
    // ========================================================================

    [Test]
    [Order(40)]
    public async Task Haip_IssuerMetadata()
    {
        await Page.GotoAsync($"{TestConstants.UiWebUrl}/.well-known/openid-credential-issuer",
            new() { WaitUntil = WaitUntilState.NetworkIdle });
        await Page.WaitForTimeoutAsync(TestConstants.ShortWait);
        await ScreenshotAsync("40-haip-issuer-metadata");
    }

    [Test]
    [Order(41)]
    public async Task Haip_OAuthMetadata()
    {
        await Page.GotoAsync($"{TestConstants.UiWebUrl}/.well-known/oauth-authorization-server",
            new() { WaitUntil = WaitUntilState.NetworkIdle });
        await Page.WaitForTimeoutAsync(TestConstants.ShortWait);
        await ScreenshotAsync("41-haip-oauth-metadata");
    }

    [Test]
    [Order(42)]
    public async Task Haip_NonceEndpoint()
    {
        await Page.GotoAsync($"{TestConstants.UiWebUrl}/api/v1/nonce",
            new() { WaitUntil = WaitUntilState.NetworkIdle });
        await Page.WaitForTimeoutAsync(TestConstants.ShortWait);
        await ScreenshotAsync("42-haip-nonce");
    }

    // ========================================================================
    // Path 5: Admin Organisation Detail Views
    // ========================================================================

    [Test]
    [Order(50)]
    public async Task Admin_GovOrgDetail()
    {
        await LoginAsAsync(TestConstants.TestEmail, TestConstants.TestPassword);
        // Navigate to organizations and click on the gov org
        await Page.GotoAsync($"{TestConstants.UiWebUrl}{TestConstants.AuthenticatedRoutes.AdminOrganizations}",
            new() { WaitUntil = WaitUntilState.NetworkIdle });
        await Page.WaitForTimeoutAsync(TestConstants.ShortWait);

        // Try to click on the Government Identity Authority org
        var govOrgLink = Page.GetByText("Government Identity Authority", new() { Exact = false });
        try
        {
            await govOrgLink.First.WaitForAsync(new() { Timeout = TestConstants.ElementTimeout });
            await govOrgLink.First.ClickAsync();
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            await Page.WaitForTimeoutAsync(TestConstants.ShortWait);
        }
        catch (TimeoutException)
        {
            // Org may not be visible — screenshot whatever is shown
        }
        await ScreenshotAsync("50-admin-gov-org-detail");
    }

    [Test]
    [Order(51)]
    public async Task Admin_CouncilOrgDetail()
    {
        await LoginAsAsync(TestConstants.TestEmail, TestConstants.TestPassword);
        await Page.GotoAsync($"{TestConstants.UiWebUrl}{TestConstants.AuthenticatedRoutes.AdminOrganizations}",
            new() { WaitUntil = WaitUntilState.NetworkIdle });
        await Page.WaitForTimeoutAsync(TestConstants.ShortWait);

        var councilOrgLink = Page.GetByText("Council Licensing Authority", new() { Exact = false });
        try
        {
            await councilOrgLink.First.WaitForAsync(new() { Timeout = TestConstants.ElementTimeout });
            await councilOrgLink.First.ClickAsync();
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            await Page.WaitForTimeoutAsync(TestConstants.ShortWait);
        }
        catch (TimeoutException)
        {
            // Org may not be visible — screenshot whatever is shown
        }
        await ScreenshotAsync("51-admin-council-org-detail");
    }
}
