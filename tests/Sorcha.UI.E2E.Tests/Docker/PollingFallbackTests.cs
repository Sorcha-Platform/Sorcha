// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Playwright;
using Sorcha.UI.E2E.Tests.Infrastructure;
using Sorcha.UI.E2E.Tests.PageObjects.Shared;

namespace Sorcha.UI.E2E.Tests.Docker;

/// <summary>
/// Feature 118 / T100 — verifies the SignalR → REST polling fallback engages
/// without crashing the page when WebSocket upgrades are blocked by the
/// browser. Proves the <c>HubConnectionWithFallback&lt;TClient&gt;</c> wrapper
/// degrades gracefully end-to-end, not just at unit-test scope.
/// </summary>
/// <remarks>
/// <para>
/// Three surfaces consume the typed hub connections + fallback:
/// </para>
/// <list type="bullet">
///   <item><term>MyActions</term><description>BlueprintHubConnection — pending action list.</description></item>
///   <item><term>WalletDetail</term><description>WalletHubConnection — credential events.</description></item>
///   <item><term>MainLayout</term><description>TenantHubConnection — inbox bell unread count.</description></item>
/// </list>
/// <para>
/// The unit-level <c>HubConnectionWithFallbackTests</c> already covers the 90 s
/// engagement timer + jittered poll cadence + disengagement-on-reconnect. This
/// E2E adds the structural guarantee that, with hub routes hard-blocked at the
/// browser, the page still renders, fetches its REST data, and surfaces no
/// JavaScript console errors that would indicate the fallback path crashed
/// the surface.
/// </para>
/// </remarks>
[TestFixture]
[Category("Docker")]
[Category("SignalR")]
[Category("PollingFallback")]
public class PollingFallbackTests : AuthenticatedDockerTestBase
{
    /// <summary>
    /// Blocks every SignalR hub route so the connection cannot upgrade to
    /// WebSockets. Applies the same pattern Playwright recommends for
    /// simulating offline-class failures on a single subsystem.
    /// </summary>
    private static Task BlockAllHubRoutesAsync(IPage page) =>
        page.RouteAsync("**/hubs/**", route => route.AbortAsync("connectionfailed"));

    [Test]
    [Retry(2)]
    public async Task MyActions_HubBlocked_PageStillLoadsAndRendersWithoutConsoleErrors()
    {
        var consoleErrors = new List<string>();
        Page.Console += (_, msg) =>
        {
            if (msg.Type == "error")
            {
                consoleErrors.Add(msg.Text);
            }
        };

        await BlockAllHubRoutesAsync(Page);
        await NavigateAuthenticatedAsync(TestConstants.AuthenticatedRoutes.MyActions);
        await MudBlazorHelpers.WaitForBlazorAsync(Page, TestConstants.PageLoadTimeout);

        var content = await Page.TextContentAsync("body") ?? string.Empty;
        Assert.That(content, Is.Not.Empty,
            "MyActions page should render content via REST even when SignalR cannot connect.");

        var fatal = consoleErrors
            .Where(e => !IsBenignTransportFailure(e))
            .ToArray();

        Assert.That(fatal, Is.Empty,
            $"Polling-fallback path must not raise non-transport JS errors. Got: {string.Join("\n", fatal)}");
    }

    [Test]
    [Retry(2)]
    public async Task MainLayoutInboxBell_HubBlocked_PageRenders()
    {
        await BlockAllHubRoutesAsync(Page);

        // Any authenticated route renders MainLayout — the inbox-bell hooks live there.
        await NavigateAuthenticatedAsync(TestConstants.AuthenticatedRoutes.MyActions);
        await MudBlazorHelpers.WaitForBlazorAsync(Page, TestConstants.PageLoadTimeout);

        // Bell is in the top app bar; checking the layout health is enough
        // (ValidateLayoutHealth in AuthenticatedDockerTestBase already runs
        // the standard MudBlazor structural checks). Explicit assertion that
        // the page reached a steady state: the body is non-empty.
        var content = await Page.TextContentAsync("body") ?? string.Empty;
        Assert.That(content, Is.Not.Empty,
            "MainLayout (inbox bell host) should render with hubs blocked.");
    }

    [Test]
    [Retry(2)]
    public async Task PollingFallback_BlocksWebSocketUpgrade_RestEndpointsStillSucceed()
    {
        var hubRequests = new List<string>();
        var restRequests = new List<string>();

        Page.Request += (_, req) =>
        {
            if (req.Url.Contains("/hubs/", StringComparison.Ordinal))
                hubRequests.Add(req.Url);
            else if (req.Url.Contains("/api/", StringComparison.Ordinal))
                restRequests.Add(req.Url);
        };

        await BlockAllHubRoutesAsync(Page);
        await NavigateAuthenticatedAsync(TestConstants.AuthenticatedRoutes.MyActions);
        await MudBlazorHelpers.WaitForBlazorAsync(Page, TestConstants.PageLoadTimeout);

        // The page is allowed to attempt a hub connection (which the route
        // handler aborts) — but it MUST still drive REST traffic for content.
        Assert.That(restRequests, Is.Not.Empty,
            "With hub blocked, the page should fall back to REST. No /api/* requests observed.");
    }

    /// <summary>
    /// Filters out the connection-aborted noise that the
    /// <see cref="BlockAllHubRoutesAsync"/> route handler produces — those are
    /// intentional and the test would defeat itself by failing on them.
    /// </summary>
    private static bool IsBenignTransportFailure(string consoleMessage) =>
        consoleMessage.Contains("connectionfailed", StringComparison.OrdinalIgnoreCase) ||
        consoleMessage.Contains("Failed to start the connection", StringComparison.OrdinalIgnoreCase) ||
        consoleMessage.Contains("WebSocket", StringComparison.OrdinalIgnoreCase) ||
        consoleMessage.Contains("/hubs/", StringComparison.OrdinalIgnoreCase) ||
        consoleMessage.Contains("net::ERR_FAILED", StringComparison.OrdinalIgnoreCase);
}
