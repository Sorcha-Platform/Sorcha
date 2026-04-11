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

        // Wait for either org selection or dashboard
        await Page.WaitForURLAsync(url =>
            url.Contains("/app/") || url.Contains("/auth/select-org") || url.Contains("/wallets/create"),
            new() { Timeout = TestConstants.PageLoadTimeout });

        // If org selection is required, select the org
        if (Page.Url.Contains("/auth/select-org") && orgName != null)
        {
            // Look for the org card and click it
            await Page.WaitForTimeoutAsync(TestConstants.ShortWait);
            var orgCard = Page.GetByText(orgName);
            if (await orgCard.CountAsync() > 0)
            {
                await orgCard.First.ClickAsync();
                await Page.WaitForURLAsync(url => url.Contains("/app/"),
                    new() { Timeout = TestConstants.PageLoadTimeout });
            }
        }

        // Wait for Blazor hydration
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
        TestContext.WriteLine($"Screenshot: {path}");
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
}
