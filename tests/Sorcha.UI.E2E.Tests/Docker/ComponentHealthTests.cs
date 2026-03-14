// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.UI.E2E.Tests.Infrastructure;
using Sorcha.UI.E2E.Tests.PageObjects.Shared;

namespace Sorcha.UI.E2E.Tests.Docker;

/// <summary>
/// E2E tests for MudBlazor component rendering, CSS integrity,
/// and responsive design across the application.
/// </summary>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
[Category("Docker")]
[Category("Components")]
[Category("Smoke")]
public class ComponentHealthTests : DockerTestBase
{
    #region MudBlazor CSS Tests

    [Test]
    public async Task Landing_StylesheetsLoaded()
    {
        await NavigateToAsync(TestConstants.PublicRoutes.Landing);

        var cssLinks = await Page.Locator("link[rel='stylesheet']").CountAsync();
        Assert.That(cssLinks, Is.GreaterThan(0),
            "Page should have CSS stylesheets loaded");
    }

    [Test]
    public async Task Landing_NoJavaScriptErrors()
    {
        await NavigateToAsync(TestConstants.PublicRoutes.Landing);
        await Page.WaitForTimeoutAsync(TestConstants.NetworkIdleWait);

        var criticalErrors = GetCriticalConsoleErrors();
        Assert.That(criticalErrors, Is.Empty,
            $"Landing page should have no critical JS errors. Found: " +
            string.Join(", ", criticalErrors.Select(e => e.Text)));
    }

    [Test]
    public async Task LoginPage_NoJavaScriptErrors()
    {
        await NavigateAndWaitForBlazorAsync(TestConstants.PublicRoutes.Login);

        var criticalErrors = GetCriticalConsoleErrors();
        Assert.That(criticalErrors, Is.Empty,
            $"Login page should have no critical JS errors. Found: " +
            string.Join(", ", criticalErrors.Select(e => e.Text)));
    }

    #endregion

    #region Responsive Design Tests

    [Test]
    [TestCase(375, 667, "Mobile")]
    [TestCase(768, 1024, "Tablet")]
    [TestCase(1920, 1080, "Desktop")]
    [TestCase(2560, 1440, "Wide Desktop")]
    public async Task Landing_RendersAtViewport(int width, int height, string label)
    {
        await Page.SetViewportSizeAsync(width, height);
        await NavigateToAsync(TestConstants.PublicRoutes.Landing);

        var content = await Page.TextContentAsync("body");
        Assert.That(content, Is.Not.Null.And.Not.Empty,
            $"Landing page should render content at {label} ({width}x{height})");
    }

    [Test]
    [TestCase(375, 667, "Mobile")]
    [TestCase(768, 1024, "Tablet")]
    [TestCase(1920, 1080, "Desktop")]
    public async Task LoginPage_RendersAtViewport(int width, int height, string label)
    {
        await Page.SetViewportSizeAsync(width, height);
        await NavigateAndWaitForBlazorAsync(TestConstants.PublicRoutes.Login);

        var loginCard = Page.Locator(".login-card");
        if (await loginCard.CountAsync() > 0)
        {
            await Expect(loginCard).ToBeVisibleAsync();

            // Login card should not overflow viewport
            var box = await loginCard.BoundingBoxAsync();
            Assert.That(box, Is.Not.Null, "Login card should have dimensions");
            Assert.That(box!.Width, Is.LessThanOrEqualTo(width),
                $"Login card should not overflow {label} viewport width");
        }
    }

    #endregion

    #region API Gateway Tests

    [Test]
    [Retry(3)]
    public async Task ApiGateway_IsAccessible()
    {
        var response = await Page.GotoAsync(TestConstants.ApiGatewayUrl);

        Assert.That(response?.Status, Is.LessThan(500),
            $"API Gateway should not return server error. Got: {response?.Status}");
    }

    [Test]
    public async Task ApiGateway_ScalarDocs_Accessible()
    {
        // Scalar UI requires auth; the OpenAPI JSON spec is public
        var response = await Page.GotoAsync($"{TestConstants.ApiGatewayUrl}/openapi/v1.json");

        Assert.That(response?.Status, Is.EqualTo(200),
            "OpenAPI spec should be accessible at /openapi/v1.json");
    }

    [Test]
    public async Task ApiGateway_HealthEndpoint_Accessible()
    {
        var response = await Page.GotoAsync($"{TestConstants.ApiGatewayUrl}/api/health");

        Assert.That(response?.Status, Is.LessThan(500),
            $"Health endpoint should not return server error. Got: {response?.Status}");
    }

    #endregion
}
