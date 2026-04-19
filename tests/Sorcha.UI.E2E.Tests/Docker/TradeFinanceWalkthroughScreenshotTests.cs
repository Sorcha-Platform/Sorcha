// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.Playwright;
using Sorcha.UI.E2E.Tests.Infrastructure;

namespace Sorcha.UI.E2E.Tests.Docker;

/// <summary>
/// Screenshots the UI state after the TradeFinance walkthrough has run.
/// Captures all 6 actor views across 4 organisations and 2 registers:
///   - Cairngorm Construction: Procurement Manager, Site Manager
///   - Highland Timber Supplies: Sales Manager, Finance Director
///   - ScotTrade Finance: Credit Analyst
///   - UK Trade Credit Bureau: Assessment Service
///
/// Prerequisites: run the TradeFinance walkthrough first:
///   pwsh walkthroughs/TradeFinance/setup.ps1
///   pwsh walkthroughs/TradeFinance/run-agents.ps1
/// </summary>
[TestFixture]
[NonParallelizable]
[Category("TradeFinanceScreenshots")]
public class TradeFinanceWalkthroughScreenshotTests : DockerTestBase
{
    private string _screenshotDir = null!;
    private TradeFinanceState _state = null!;

    [OneTimeSetUp]
    public void LoadState()
    {
        var statePath = Environment.GetEnvironmentVariable("TRADEFINANCE_STATE_PATH")
            ?? Path.Combine(FindWalkthroughDir(), "state.json");

        if (!File.Exists(statePath))
            Assert.Ignore($"state.json not found at {statePath}. Run setup.ps1 first.");

        var json = File.ReadAllText(statePath);
        _state = TradeFinanceState.FromJson(json);
    }

    [SetUp]
    public override async Task BaseSetUp()
    {
        await base.BaseSetUp();
        _screenshotDir = Path.Combine(
            TestContext.CurrentContext.WorkDirectory, "trade-finance-screenshots");
        Directory.CreateDirectory(_screenshotDir);
    }

    // ========================================================================
    // Helpers
    // ========================================================================

    private static string FindWalkthroughDir()
    {
        // Walk up from test output dir to find walkthroughs/TradeFinance
        var dir = TestContext.CurrentContext.WorkDirectory;
        for (var i = 0; i < 10; i++)
        {
            var candidate = Path.Combine(dir, "walkthroughs", "TradeFinance");
            if (Directory.Exists(candidate)) return candidate;
            var parent = Directory.GetParent(dir);
            if (parent == null) break;
            dir = parent.FullName;
        }
        return Path.Combine(Directory.GetCurrentDirectory(), "walkthroughs", "TradeFinance");
    }

    private async Task LoginAsActorAsync(string roleId)
    {
        var role = _state.GetRole(roleId);
        var baseUrl = TestConstants.UiWebUrl;

        await Page.GotoAsync($"{baseUrl}/auth/login",
            new() { WaitUntil = WaitUntilState.NetworkIdle, Timeout = TestConstants.PageLoadTimeout });

        await Page.Locator("input[type='email'], input[autocomplete='email']").First.FillAsync(role.Email);
        await Page.Locator("input[type='password']").First.FillAsync(role.Password);
        await Page.Locator("button[type='submit']").First.ClickAsync();

        // Single-org users redirect to /app/ directly; multi-org users see org selection
        if (role.OrganizationName != null)
        {
            var orgCard = Page.GetByText(role.OrganizationName, new() { Exact = false });
            try
            {
                await orgCard.First.WaitForAsync(new() { Timeout = TestConstants.PageLoadTimeout });
                await orgCard.First.ClickAsync();
            }
            catch (TimeoutException)
            {
                if (!Page.Url.Contains("/app/"))
                {
                    await ScreenshotAsync($"debug-login-{roleId}");
                    throw new InvalidOperationException(
                        $"Could not find org '{role.OrganizationName}' for {roleId}");
                }
            }
        }

        try
        {
            await Page.WaitForURLAsync(
                url => url.Contains("/app/") || url.Contains("/wallets/create"),
                new() { Timeout = TestConstants.PageLoadTimeout });
        }
        catch (TimeoutException) { /* best effort */ }

        await Page.WaitForTimeoutAsync(TestConstants.BlazorHydrationTimeout);
    }

    private async Task ScreenshotAsync(string name)
    {
        var path = Path.Combine(_screenshotDir, $"{name}.png");
        await Page.ScreenshotAsync(new() { Path = path, FullPage = true });
        TestContext.Out.WriteLine($"Screenshot: {path}");
    }

    private async Task NavigateAndScreenshotAsync(string route, string screenshotName)
    {
        await Page.GotoAsync($"{TestConstants.UiWebUrl}{route}",
            new() { WaitUntil = WaitUntilState.NetworkIdle });
        await Page.WaitForTimeoutAsync(TestConstants.ShortWait);
        await ScreenshotAsync(screenshotName);
    }

    // ========================================================================
    // Path 1: Cairngorm Construction — Procurement Manager (Buyer)
    // ========================================================================

    [Test, Order(10)]
    public async Task ProcurementMgr_Dashboard()
    {
        await LoginAsActorAsync("procurement-mgr");
        await ScreenshotAsync("10-procurement-mgr-dashboard");
    }

    [Test, Order(11)]
    public async Task ProcurementMgr_Wallet()
    {
        await LoginAsActorAsync("procurement-mgr");
        await NavigateAndScreenshotAsync(TestConstants.AuthenticatedRoutes.Wallets,
            "11-procurement-mgr-wallet");
    }

    [Test, Order(12)]
    public async Task ProcurementMgr_MyActions()
    {
        await LoginAsActorAsync("procurement-mgr");
        await NavigateAndScreenshotAsync(TestConstants.AuthenticatedRoutes.MyActions,
            "12-procurement-mgr-actions");
    }

    [Test, Order(13)]
    public async Task ProcurementMgr_MyWorkflows()
    {
        await LoginAsActorAsync("procurement-mgr");
        await NavigateAndScreenshotAsync(TestConstants.AuthenticatedRoutes.MyWorkflows,
            "13-procurement-mgr-workflows");
    }

    [Test, Order(14)]
    public async Task ProcurementMgr_Registers()
    {
        await LoginAsActorAsync("procurement-mgr");
        await NavigateAndScreenshotAsync(TestConstants.AuthenticatedRoutes.Registers,
            "14-procurement-mgr-registers");
    }

    [Test, Order(15)]
    public async Task ProcurementMgr_Transactions()
    {
        await LoginAsActorAsync("procurement-mgr");
        await NavigateAndScreenshotAsync(TestConstants.AuthenticatedRoutes.MyTransactions,
            "15-procurement-mgr-transactions");
    }

    // ========================================================================
    // Path 2: Cairngorm Construction — Site Manager (Buyer)
    // ========================================================================

    [Test, Order(20)]
    public async Task SiteMgr_Dashboard()
    {
        await LoginAsActorAsync("site-mgr");
        await ScreenshotAsync("20-site-mgr-dashboard");
    }

    [Test, Order(21)]
    public async Task SiteMgr_MyActions()
    {
        await LoginAsActorAsync("site-mgr");
        await NavigateAndScreenshotAsync(TestConstants.AuthenticatedRoutes.MyActions,
            "21-site-mgr-actions");
    }

    [Test, Order(22)]
    public async Task SiteMgr_MyWorkflows()
    {
        await LoginAsActorAsync("site-mgr");
        await NavigateAndScreenshotAsync(TestConstants.AuthenticatedRoutes.MyWorkflows,
            "22-site-mgr-workflows");
    }

    // ========================================================================
    // Path 3: Highland Timber Supplies — Sales Manager (Supplier)
    // ========================================================================

    [Test, Order(30)]
    public async Task SalesMgr_Dashboard()
    {
        await LoginAsActorAsync("sales-mgr");
        await ScreenshotAsync("30-sales-mgr-dashboard");
    }

    [Test, Order(31)]
    public async Task SalesMgr_Wallet()
    {
        await LoginAsActorAsync("sales-mgr");
        await NavigateAndScreenshotAsync(TestConstants.AuthenticatedRoutes.Wallets,
            "31-sales-mgr-wallet");
    }

    [Test, Order(32)]
    public async Task SalesMgr_MyActions()
    {
        await LoginAsActorAsync("sales-mgr");
        await NavigateAndScreenshotAsync(TestConstants.AuthenticatedRoutes.MyActions,
            "32-sales-mgr-actions");
    }

    [Test, Order(33)]
    public async Task SalesMgr_MyWorkflows()
    {
        await LoginAsActorAsync("sales-mgr");
        await NavigateAndScreenshotAsync(TestConstants.AuthenticatedRoutes.MyWorkflows,
            "33-sales-mgr-workflows");
    }

    [Test, Order(34)]
    public async Task SalesMgr_Credentials()
    {
        await LoginAsActorAsync("sales-mgr");
        await NavigateAndScreenshotAsync(TestConstants.AuthenticatedRoutes.Credentials,
            "34-sales-mgr-credentials");
    }

    // ========================================================================
    // Path 4: Highland Timber Supplies — Finance Director (Supplier)
    // ========================================================================

    [Test, Order(40)]
    public async Task FinanceDirector_Dashboard()
    {
        await LoginAsActorAsync("finance-director");
        await ScreenshotAsync("40-finance-director-dashboard");
    }

    [Test, Order(41)]
    public async Task FinanceDirector_MyActions()
    {
        await LoginAsActorAsync("finance-director");
        await NavigateAndScreenshotAsync(TestConstants.AuthenticatedRoutes.MyActions,
            "41-finance-director-actions");
    }

    [Test, Order(42)]
    public async Task FinanceDirector_MyWorkflows()
    {
        await LoginAsActorAsync("finance-director");
        await NavigateAndScreenshotAsync(TestConstants.AuthenticatedRoutes.MyWorkflows,
            "42-finance-director-workflows");
    }

    [Test, Order(43)]
    public async Task FinanceDirector_Credentials()
    {
        await LoginAsActorAsync("finance-director");
        await NavigateAndScreenshotAsync(TestConstants.AuthenticatedRoutes.Credentials,
            "43-finance-director-credentials");
    }

    // ========================================================================
    // Path 5: ScotTrade Finance — Credit Analyst (Funder)
    // ========================================================================

    [Test, Order(50)]
    public async Task CreditAnalyst_Dashboard()
    {
        await LoginAsActorAsync("credit-analyst");
        await ScreenshotAsync("50-credit-analyst-dashboard");
    }

    [Test, Order(51)]
    public async Task CreditAnalyst_Wallet()
    {
        await LoginAsActorAsync("credit-analyst");
        await NavigateAndScreenshotAsync(TestConstants.AuthenticatedRoutes.Wallets,
            "51-credit-analyst-wallet");
    }

    [Test, Order(52)]
    public async Task CreditAnalyst_MyActions()
    {
        await LoginAsActorAsync("credit-analyst");
        await NavigateAndScreenshotAsync(TestConstants.AuthenticatedRoutes.MyActions,
            "52-credit-analyst-actions");
    }

    [Test, Order(53)]
    public async Task CreditAnalyst_MyWorkflows()
    {
        await LoginAsActorAsync("credit-analyst");
        await NavigateAndScreenshotAsync(TestConstants.AuthenticatedRoutes.MyWorkflows,
            "53-credit-analyst-workflows");
    }

    [Test, Order(54)]
    public async Task CreditAnalyst_Registers()
    {
        await LoginAsActorAsync("credit-analyst");
        await NavigateAndScreenshotAsync(TestConstants.AuthenticatedRoutes.Registers,
            "54-credit-analyst-registers");
    }

    // ========================================================================
    // Path 6: UK Trade Credit Bureau — Assessment Service
    // ========================================================================

    [Test, Order(60)]
    public async Task AssessmentSvc_Dashboard()
    {
        await LoginAsActorAsync("assessment-svc");
        await ScreenshotAsync("60-assessment-svc-dashboard");
    }

    [Test, Order(61)]
    public async Task AssessmentSvc_MyActions()
    {
        await LoginAsActorAsync("assessment-svc");
        await NavigateAndScreenshotAsync(TestConstants.AuthenticatedRoutes.MyActions,
            "61-assessment-svc-actions");
    }

    // ========================================================================
    // Path 7: Cross-Org Views (Blueprints, Participants)
    // ========================================================================

    [Test, Order(70)]
    public async Task ProcurementMgr_Blueprints()
    {
        await LoginAsActorAsync("procurement-mgr");
        await NavigateAndScreenshotAsync(TestConstants.AuthenticatedRoutes.Blueprints,
            "70-blueprints-procurement-view");
    }

    [Test, Order(71)]
    public async Task ProcurementMgr_Participants()
    {
        await LoginAsActorAsync("procurement-mgr");
        await NavigateAndScreenshotAsync(TestConstants.AuthenticatedRoutes.Participants,
            "71-participants-buyer-view");
    }

    [Test, Order(72)]
    public async Task CreditAnalyst_Blueprints()
    {
        await LoginAsActorAsync("credit-analyst");
        await NavigateAndScreenshotAsync(TestConstants.AuthenticatedRoutes.Blueprints,
            "72-blueprints-funder-view");
    }

    [Test, Order(73)]
    public async Task CreditAnalyst_Participants()
    {
        await LoginAsActorAsync("credit-analyst");
        await NavigateAndScreenshotAsync(TestConstants.AuthenticatedRoutes.Participants,
            "73-participants-funder-view");
    }

    // ========================================================================
    // State file loader
    // ========================================================================

    private sealed class TradeFinanceState
    {
        private readonly JsonElement _root;
        private readonly Dictionary<string, string> _orgNames = new();

        private TradeFinanceState(JsonElement root)
        {
            _root = root;

            // Build org name lookup from organizations section
            if (root.TryGetProperty("organizations", out var orgs))
            {
                foreach (var org in orgs.EnumerateObject())
                {
                    if (org.Value.TryGetProperty("name", out var name))
                        _orgNames[org.Name] = name.GetString()!;
                }
            }
        }

        public static TradeFinanceState FromJson(string json)
        {
            var doc = JsonDocument.Parse(json);
            return new TradeFinanceState(doc.RootElement);
        }

        public ActorRole GetRole(string roleId)
        {
            var roles = _root.GetProperty("roles").GetProperty(roleId);
            var email = roles.GetProperty("email").GetString()!;
            var password = roles.GetProperty("password").GetString()!;
            var orgId = roles.GetProperty("organizationId").GetString()!;

            // Resolve org name from organizationId
            string? orgName = null;
            foreach (var kv in _orgNames)
            {
                if (_root.TryGetProperty("organizations", out var orgs)
                    && orgs.TryGetProperty(kv.Key, out var org)
                    && org.TryGetProperty("id", out var id)
                    && id.GetString() == orgId)
                {
                    orgName = kv.Value;
                    break;
                }
            }

            return new ActorRole(email, password, orgId, orgName);
        }
    }

    private sealed record ActorRole(string Email, string Password, string OrganizationId, string? OrganizationName);
}
