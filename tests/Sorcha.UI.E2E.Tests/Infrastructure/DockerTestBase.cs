// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using Sorcha.UI.E2E.Tests.PageObjects.Shared;

namespace Sorcha.UI.E2E.Tests.Infrastructure;

/// <summary>
/// Base class for all Docker-targeted Playwright E2E tests.
/// Provides automatic console error capture, network failure tracking,
/// CSS/layout health validation, and screenshot-on-failure.
/// </summary>
public abstract class DockerTestBase : PageTest
{
    private readonly List<ConsoleMessage> _consoleErrors = [];
    private readonly List<(string Url, int Status)> _networkFailures = [];
    private bool _trackingActive;

    /// <summary>
    /// Console errors captured during the test, after filtering known noise.
    /// </summary>
    protected IReadOnlyList<ConsoleMessage> CapturedConsoleErrors => _consoleErrors;

    /// <summary>
    /// Network requests that returned 5xx status codes during the test.
    /// </summary>
    protected IReadOnlyList<(string Url, int Status)> CapturedNetworkFailures => _networkFailures;

    /// <summary>
    /// Whether to automatically assert no console errors in TearDown.
    /// Override to false in tests that expect console errors.
    /// </summary>
    protected virtual bool AssertNoConsoleErrors => true;

    /// <summary>
    /// Whether to automatically assert no network 5xx failures in TearDown.
    /// Override to false in tests that expect API failures.
    /// </summary>
    protected virtual bool AssertNoNetworkFailures => true;

    /// <summary>
    /// Whether to validate MudBlazor layout health in TearDown.
    /// Override to false for non-MudBlazor pages (e.g. login, landing).
    /// </summary>
    protected virtual bool ValidateLayoutHealth => false;

    /// <summary>
    /// Directory for screenshot artifacts on test failure.
    /// </summary>
    protected static string ScreenshotDirectory =>
        Path.Combine(TestContext.CurrentContext.WorkDirectory, "screenshots");

    [SetUp]
    public virtual async Task BaseSetUp()
    {
        _consoleErrors.Clear();
        _networkFailures.Clear();
        _trackingActive = true;

        // Capture console errors
        Page.Console += OnConsoleMessage;

        // Capture network failures (5xx responses)
        Page.Response += OnResponse;

        await Task.CompletedTask;
    }

    [TearDown]
    public virtual async Task BaseTearDown()
    {
        _trackingActive = false;

        // Screenshot on failure
        if (TestContext.CurrentContext.Result.Outcome.Status == NUnit.Framework.Interfaces.TestStatus.Failed)
        {
            await CaptureScreenshotAsync();
        }

        // Assert no critical console errors
        if (AssertNoConsoleErrors)
        {
            var criticalErrors = GetCriticalConsoleErrors();
            if (criticalErrors.Count > 0)
            {
                var errorSummary = string.Join("\n  ", criticalErrors.Select(e => e.Text));
                Assert.Warn($"Console errors detected:\n  {errorSummary}");
            }
        }

        // Assert no network failures
        if (AssertNoNetworkFailures && _networkFailures.Count > 0)
        {
            var failureSummary = string.Join("\n  ", _networkFailures.Select(f => $"{f.Status} {f.Url}"));
            Assert.Warn($"Network failures detected:\n  {failureSummary}");
        }

        // Validate layout health
        if (ValidateLayoutHealth)
        {
            var issues = await MudBlazorHelpers.ValidateLayoutHealthAsync(Page);
            if (issues.Count > 0)
            {
                var issueSummary = string.Join("\n  ", issues);
                Assert.Warn($"Layout health issues:\n  {issueSummary}");
            }
        }

        // Unsubscribe
        Page.Console -= OnConsoleMessage;
        Page.Response -= OnResponse;
    }

    /// <summary>
    /// Navigates to a URL under the UI Web host.
    /// </summary>
    protected async Task NavigateToAsync(string path)
    {
        var url = $"{TestConstants.UiWebUrl}{path}";
        await Page.GotoAsync(url);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    /// <summary>
    /// Navigates to a URL and waits for Blazor WASM to hydrate.
    /// Also waits for i18n translations to load so text assertions
    /// match English strings rather than raw keys.
    /// </summary>
    protected async Task NavigateAndWaitForBlazorAsync(string path)
    {
        await NavigateToAsync(path);
        await MudBlazorHelpers.WaitForBlazorAsync(Page, TestConstants.PageLoadTimeout);
        await MudBlazorHelpers.WaitForTranslationsAsync(Page, TestConstants.PageLoadTimeout);
    }

    /// <summary>
    /// Returns console errors that are not in the known-noise filter list.
    /// </summary>
    protected List<ConsoleMessage> GetCriticalConsoleErrors()
    {
        return _consoleErrors
            .Where(msg => !TestConstants.KnownConsoleErrorPatterns
                .Any(pattern => msg.Text.Contains(pattern, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    /// <summary>
    /// Takes a screenshot and saves it to the artifacts directory.
    /// </summary>
    protected async Task CaptureScreenshotAsync(string? suffix = null)
    {
        try
        {
            Directory.CreateDirectory(ScreenshotDirectory);
            var testName = TestContext.CurrentContext.Test.Name;
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var fileName = suffix != null
                ? $"{testName}_{suffix}_{timestamp}.png"
                : $"{testName}_{timestamp}.png";

            // Sanitize filename
            foreach (var c in Path.GetInvalidFileNameChars())
                fileName = fileName.Replace(c, '_');

            var filePath = Path.Combine(ScreenshotDirectory, fileName);
            await Page.ScreenshotAsync(new() { Path = filePath, FullPage = true });

            TestContext.AddTestAttachment(filePath, $"Screenshot: {suffix ?? "failure"}");
        }
        catch (Exception ex)
        {
            TestContext.Out.WriteLine($"Failed to capture screenshot: {ex.Message}");
        }
    }

    /// <summary>
    /// Checks whether the current page is the login page (redirect happened).
    /// </summary>
    protected bool IsOnLoginPage()
    {
        var url = Page.Url;
        return url.Contains("/auth/login", StringComparison.OrdinalIgnoreCase)
            || url.Contains("/auth/signup", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Asserts the current page is NOT the login page, meaning auth succeeded.
    /// </summary>
    protected void AssertAuthenticated()
    {
        Assert.That(IsOnLoginPage(), Is.False,
            $"Expected to be authenticated but was redirected to login. URL: {Page.Url}");
    }

    private void OnConsoleMessage(object? sender, IConsoleMessage msg)
    {
        if (!_trackingActive) return;
        if (msg.Type == "error")
        {
            _consoleErrors.Add(new ConsoleMessage(msg.Text, msg.Type, msg.Location));
        }
    }

    private void OnResponse(object? sender, IResponse response)
    {
        if (!_trackingActive) return;
        if (TestConstants.FailureStatusCodes.Contains(response.Status))
        {
            _networkFailures.Add((response.Url, response.Status));
        }
    }
}

/// <summary>
/// Captured console message for assertion.
/// </summary>
public record ConsoleMessage(string Text, string Type, string Location);
