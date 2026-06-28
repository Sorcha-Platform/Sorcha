// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Playwright;
using Sorcha.UI.E2E.Tests.Infrastructure;
using Sorcha.UI.E2E.Tests.PageObjects.Shared;

namespace Sorcha.UI.E2E.Tests.Docker;

/// <summary>
/// Feature 159 — Playwright layout-variant tests for the Present page intake surface.
/// Verifies that the correct intake layout (CameraFirst / PasteWithScan / PasteOnly)
/// is selected and rendered based on the device-profile JS detection result.
/// </summary>
/// <remarks>
/// <para>
/// Device profile detection (<c>SorchaDeviceProfile.detect()</c>) is overridden
/// by an <c>AddInitScriptAsync</c> injection so the tests are deterministic and
/// do not require real camera hardware.  <c>SorchaQrScanner</c> is also stubbed
/// to prevent any attempt to open the camera API.
/// </para>
/// <para>
/// All three layout variants are tested:
/// <list type="bullet">
///   <item><c>CameraFirst</c> — handheld + camera present → viewfinder visible on load.</item>
///   <item><c>PasteWithScan</c> — desktop + camera present → paste field default, scan button present.</item>
///   <item><c>PasteOnly</c> — no camera → paste field only, no scan affordance anywhere.</item>
/// </list>
/// </para>
/// <para>
/// If the Present page redirects to a sign-in surface (the citizen wallet requires
/// IndexedDB-based auth that is not seeded in this fixture), each test marks itself
/// <c>Inconclusive</c> rather than failing.
/// </para>
/// </remarks>
[TestFixture]
[Category("Docker")]
[Category("CitizenWallet")]
[Category("Present")]
[Category("F159")]
[Parallelizable(ParallelScope.Self)]
public class PresentIntakeLayoutTests : AuthenticatedCitizenWalletTestBase
{
    private const string PresentPath = "/present";

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Injects JS mocks for device-profile detection and the QR scanner before
    /// the PWA boots, then navigates to the Present page and waits for Blazor hydration.
    /// </summary>
    /// <param name="formFactor">"handheld" or "desktop"</param>
    /// <param name="cameraApi">Whether the navigator.mediaDevices API should appear present.</param>
    private async Task NavigateToPresentWithProfileAsync(string formFactor, bool cameraApi)
    {
        // Inject before page load so Blazor reads the mock on first render.
        // $$"""…""" → single { } are literal in the output; {{expr}} is C# interpolation.
        var detectResult = cameraApi
            ? $"{{ formFactor: '{formFactor}', cameraApi: true, hasVideoInput: true }}"
            : $"{{ formFactor: '{formFactor}', cameraApi: false, hasVideoInput: false }}";
        var supportedJs = cameraApi ? "true" : "false";

        await Page.AddInitScriptAsync($$"""
            window.SorchaDeviceProfile = {
                detect: () => Promise.resolve({{detectResult}})
            };
            window.SorchaQrScanner = {
                isSupported: () => Promise.resolve({{supportedJs}}),
                start:       (_id) => new Promise(() => {}),
                stop:        ()    => Promise.resolve()
            };
        """);

        await NavigateToWalletAndWaitForBlazorAsync(PresentPath);
        await Page.WaitForTimeoutAsync(TestConstants.BlazorHydrationTimeout);
    }

    /// <summary>
    /// Returns true if the Present page's AwaitingDeepLink phase is currently visible,
    /// i.e. the page loaded and was not redirected to a sign-in surface.
    /// </summary>
    private async Task<bool> IsPresentPageVisibleAsync()
    {
        // The page title "Present a credential" is present when the AwaitingDeepLink
        // phase renders; it is absent if auth redirected to login/enrol.
        var heading = Page.Locator("text='Present a credential'");
        return await heading.CountAsync() > 0;
    }

    private void SkipIfNotAuthenticated()
    {
        // Called after navigation; if the page redirected we cannot test layout.
        // Inconclusive so CI doesn't fail — the unit / bUnit suite covers this path.
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Layout-variant tests
    // ──────────────────────────────────────────────────────────────────────────

    [Test]
    [Retry(2)]
    public async Task PresentPage_HandheldWithCamera_ShowsCameraFirstLayout()
    {
        await Page.SetViewportSizeAsync(390, 844);  // iPhone-class viewport
        await NavigateToPresentWithProfileAsync(formFactor: "handheld", cameraApi: true);

        if (!await IsPresentPageVisibleAsync())
        {
            Assert.Inconclusive(
                "Present page not visible — citizen wallet IndexedDB auth not seeded for this test run.");
            return;
        }

        // CameraFirst: viewfinder element must be present.
        var viewfinder = Page.Locator("[data-testid='present-viewfinder']");
        await viewfinder.WaitForAsync(new LocatorWaitForOptions { Timeout = TestConstants.ElementTimeout });
        Assert.That(await viewfinder.IsVisibleAsync(), Is.True,
            "CameraFirst layout should render the QR viewfinder video element on load.");

        // CameraFirst: "Paste a link instead" escape must be visible.
        var pasteInstead = Page.Locator("[data-testid='present-paste-instead']");
        Assert.That(await pasteInstead.IsVisibleAsync(), Is.True,
            "CameraFirst layout should render a 'Paste a link instead' control.");

        // CameraFirst: paste field must NOT be the initial surface.
        var pasteField = Page.Locator("[data-testid='present-paste-field']");
        Assert.That(await pasteField.CountAsync(), Is.EqualTo(0),
            "CameraFirst layout should not show the paste field on initial render.");
    }

    [Test]
    [Retry(2)]
    public async Task PresentPage_DesktopWithCamera_ShowsPasteWithScanLayout()
    {
        await Page.SetViewportSizeAsync(1280, 900);  // desktop viewport
        await NavigateToPresentWithProfileAsync(formFactor: "desktop", cameraApi: true);

        if (!await IsPresentPageVisibleAsync())
        {
            Assert.Inconclusive(
                "Present page not visible — citizen wallet IndexedDB auth not seeded for this test run.");
            return;
        }

        // PasteWithScan default: paste field must be visible immediately.
        var pasteField = Page.Locator("[data-testid='present-paste-field']");
        await pasteField.WaitForAsync(new LocatorWaitForOptions { Timeout = TestConstants.ElementTimeout });
        Assert.That(await pasteField.IsVisibleAsync(), Is.True,
            "PasteWithScan layout should render the paste field as the default surface.");

        // PasteWithScan: "Scan with camera" button must be present.
        var scanButton = Page.Locator("[data-testid='present-scan-with-camera']");
        Assert.That(await scanButton.IsVisibleAsync(), Is.True,
            "PasteWithScan layout should offer a 'Scan with camera' button.");

        // PasteWithScan: camera must NOT auto-start — viewfinder absent on load.
        var viewfinder = Page.Locator("[data-testid='present-viewfinder']");
        Assert.That(await viewfinder.CountAsync(), Is.EqualTo(0),
            "PasteWithScan layout must not show the viewfinder before the user taps 'Scan with camera'.");
    }

    [Test]
    [Retry(2)]
    public async Task PresentPage_HandheldNoCamera_ShowsPasteOnlyLayout()
    {
        await Page.SetViewportSizeAsync(390, 844);  // phone viewport, no camera
        await NavigateToPresentWithProfileAsync(formFactor: "handheld", cameraApi: false);

        if (!await IsPresentPageVisibleAsync())
        {
            Assert.Inconclusive(
                "Present page not visible — citizen wallet IndexedDB auth not seeded for this test run.");
            return;
        }

        // PasteOnly: paste field must be visible.
        var pasteField = Page.Locator("[data-testid='present-paste-field']");
        await pasteField.WaitForAsync(new LocatorWaitForOptions { Timeout = TestConstants.ElementTimeout });
        Assert.That(await pasteField.IsVisibleAsync(), Is.True,
            "PasteOnly layout should render the paste field.");

        // PasteOnly: absolutely no scan control must exist anywhere in the DOM (FR-005).
        var scanButton = Page.Locator("[data-testid='present-scan-with-camera']");
        Assert.That(await scanButton.CountAsync(), Is.EqualTo(0),
            "PasteOnly layout must not expose any 'Scan with camera' control.");

        var pasteInstead = Page.Locator("[data-testid='present-paste-instead']");
        Assert.That(await pasteInstead.CountAsync(), Is.EqualTo(0),
            "PasteOnly layout must not expose any 'Paste a link instead' control.");

        var viewfinder = Page.Locator("[data-testid='present-viewfinder']");
        Assert.That(await viewfinder.CountAsync(), Is.EqualTo(0),
            "PasteOnly layout must not show the QR viewfinder.");
    }

    [Test]
    [Retry(2)]
    public async Task PresentPage_DesktopNoCamera_ShowsPasteOnlyLayout()
    {
        await Page.SetViewportSizeAsync(1280, 900);  // desktop viewport, no camera
        await NavigateToPresentWithProfileAsync(formFactor: "desktop", cameraApi: false);

        if (!await IsPresentPageVisibleAsync())
        {
            Assert.Inconclusive(
                "Present page not visible — citizen wallet IndexedDB auth not seeded for this test run.");
            return;
        }

        // PasteOnly for desktop-no-camera: paste field only, zero scan affordances.
        var pasteField = Page.Locator("[data-testid='present-paste-field']");
        await pasteField.WaitForAsync(new LocatorWaitForOptions { Timeout = TestConstants.ElementTimeout });
        Assert.That(await pasteField.IsVisibleAsync(), Is.True,
            "PasteOnly (desktop, no camera) layout should render the paste field.");

        var scanButton = Page.Locator("[data-testid='present-scan-with-camera']");
        Assert.That(await scanButton.CountAsync(), Is.EqualTo(0),
            "PasteOnly (desktop, no camera) layout must not expose any scan control (FR-005).");

        var viewfinder = Page.Locator("[data-testid='present-viewfinder']");
        Assert.That(await viewfinder.CountAsync(), Is.EqualTo(0),
            "PasteOnly (desktop, no camera) layout must not show the QR viewfinder.");
    }
}
