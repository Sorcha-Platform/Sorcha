---
name: playwright
description: |
  Develops end-to-end UI tests with Playwright for Blazor applications.
  Use when: Writing E2E tests, testing Blazor WASM pages, validating UI flows, checking responsive design, detecting JavaScript errors, or testing MudBlazor components.
allowed-tools: Read, Edit, Write, Glob, Grep, Bash, mcp__context7__resolve-library-id, mcp__context7__query-docs
---

# Playwright Skill

E2E testing for Blazor WebAssembly using Playwright .NET with NUnit and .NET Aspire integration. Tests run against the full Aspire application stack with all services.

## Quick Start

### Basic Page Test

```csharp
[Parallelizable(ParallelScope.Self)]
[TestFixture]
public class BlazorUITests : PageTest
{
    private DistributedApplication? _app;
    private string? _blazorUrl;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.Sorcha_AppHost>();
        _app = await appHost.BuildAsync();
        await _app.StartAsync();
        _blazorUrl = _app.GetEndpoint("blazor-client").ToString();
    }

    [Test]
    public async Task HomePage_LoadsSuccessfully()
    {
        await Page.GotoAsync(_blazorUrl!);
        await Page.WaitForLoadStateAsync();
        await Expect(Page).ToHaveTitleAsync(new Regex("Sorcha|Blueprint"));
    }
}
```

### Locator Patterns

```csharp
// Role-based (preferred)
await Page.GetByRole(AriaRole.Button, new() { Name = "Submit" }).ClickAsync();

// Text-based
Page.Locator("a:has-text('Designer')")

// MudBlazor components
Page.Locator(".mud-button")
Page.Locator(".mud-table")

// Test IDs (most stable)
Page.Locator("[data-testid='my-element']")
```

## Key Concepts

| Concept | Usage | Example |
|---------|-------|---------|
| PageTest | Base class providing Page object | `class MyTests : PageTest` |
| Locator | Lazy element reference | `Page.Locator("button")` |
| Expect | Assertion API | `await Expect(Page).ToHaveURLAsync(...)` |
| Aspire Testing | Full stack integration | `DistributedApplicationTestingBuilder` |
| Auto-wait | Built-in waiting | Actions wait for actionability |

## Common Patterns

### JavaScript Error Detection

```csharp
[Test]
public async Task NoJavaScriptErrors()
{
    var errors = new List<string>();
    Page.Console += (_, msg) =>
    {
        if (msg.Type == "error") errors.Add(msg.Text);
    };

    await Page.GotoAsync(_blazorUrl!);
    await Page.WaitForLoadStateAsync();
    
    var criticalErrors = errors.Where(e =>
        !e.Contains("WASM") && !e.Contains("Blazor")).ToList();
    Assert.That(criticalErrors, Is.Empty);
}
```

### Responsive Design Testing

```csharp
[Test]
public async Task ResponsiveDesign_Works()
{
    await Page.SetViewportSizeAsync(375, 667); // Mobile
    await Page.GotoAsync(_blazorUrl!);
    Assert.That(await Page.TextContentAsync("body"), Is.Not.Empty);

    await Page.SetViewportSizeAsync(1920, 1080); // Desktop
    await Page.ReloadAsync();
    Assert.That(await Page.TextContentAsync("body"), Is.Not.Empty);
}
```

### Anti-pattern: locators declared but never clicked

When a page object declares a locator (`EnrolDeviceButton`, `FooterNavSettings`) but **no test in the suite ever calls `.ClickAsync()` on it**, you are paying the cost of maintaining the locator without getting any coverage from it. The smell is easy to miss in review: the locator exists, the test compiles, CI is green — but the underlying user gesture has never been exercised end-to-end.

This is exactly how the Citizen Wallet PWA shipped twelve broken navigation buttons (PR #698) past every CI gate. `CitizenWalletPage` declared `EnrolDeviceButton` + `PresentButton`, the test fixture imported them, but no test clicked them. The first time a real human clicked Enrol was on production — every nav button 404'd.

**Discipline for new page objects**:
1. Every nav-triggering locator gets a corresponding `Click_RoutesTo<Page>` test that asserts the URL after the click.
2. Every state-mutating button gets a click + assert-on-resulting-state test.
3. If a locator is for an assertion target only (e.g. `EmptyState`), document that — name it `*Indicator` / `*Banner` so future contributors don't expect it to be clicked.

**Stable selector convention** — use `data-testid` attributes on every nav element rather than CSS path or text content. Pattern: kebab-case, scope-prefixed (`footer-nav-settings`, `home-present-button`, `credential-detail-back-button`). Survives MudBlazor markup churn; survives localisation.

**Example test for a nav-button sweep** — one TestCaseSource covering N buttons:

```csharp
private static IEnumerable<TestCaseData> NavCases
{
    get
    {
        yield return new TestCaseData("footer-nav-devices", "devices").SetName("Footer Devices → /wallet/devices");
        yield return new TestCaseData("footer-nav-settings", "settings").SetName("Footer Settings → /wallet/settings");
        // ... one row per nav element
    }
}

[Test, TestCaseSource(nameof(NavCases))]
public async Task FooterNav_ClickRoutesToExpectedPage(string testId, string expectedSuffix)
{
    await NavigateToWalletAndWaitForBlazorAsync();
    await Page.Locator($"[data-testid='{testId}']").ClickAsync();
    await Page.WaitForURLAsync($"**/wallet/{expectedSuffix}*", new() { Timeout = 5000 });
    Assert.That(new Uri(Page.Url).AbsolutePath,
        Does.StartWith($"/wallet/{expectedSuffix}"));
}
```

### Post-redeploy cache testing for PWAs

A PWA with year-cached entry-point JS (`dotnet.js`, `blazor.webassembly.js` — see the **blazor** skill on this) only breaks on **return visits after a redeploy**. CI's normal "fresh browser context, fresh container" Playwright runs miss it entirely.

Two cheap regression guards:

1. **HTTP cache-header probes** — straight `HttpClient` against the live PWA, assert `dotnet.js` and `blazor.webassembly.js` carry `no-cache`/`must-revalidate` and *not* `immutable`. No browser needed, sub-second runtime. See `tests/Sorcha.UI.E2E.Tests/Docker/CitizenWallet/CitizenWalletNginxCacheHeadersTests.cs`.

2. **Browser context reuse across redeploy** — non-trivial Playwright dance: visit `/wallet/`, force fingerprint rotation (rebuild + recreate citizen-wallet container with a tagged content change), navigate again **without clearing browser state**, assert no wasm fetches 404. Phase 2 of issue #700.

## See Also

- [patterns](references/patterns.md) - Locator strategies and assertions
- [workflows](references/workflows.md) - Test setup and CI integration

## Related Skills

- See the **xunit** skill for unit testing patterns
- See the **fluent-assertions** skill for SignalR integration tests
- See the **blazor** skill for component architecture
- See the **signalr** skill for real-time notification testing
- See the **docker** skill for container-based test environments

## Documentation Resources

> Fetch latest Playwright .NET documentation with Context7.

**How to use Context7:**
1. Use `mcp__context7__resolve-library-id` to search for "playwright"
2. **Prefer website documentation** (`/websites/playwright_dev_dotnet`) over source code
3. Query with `mcp__context7__query-docs` using the resolved library ID

**Library ID:** `/websites/playwright_dev_dotnet`

**Recommended Queries:**
- "Locators selectors best practices"
- "NUnit test fixtures setup teardown"
- "Wait for element network idle auto-waiting"
- "Assertions expect API"
- "Trace viewer debugging"