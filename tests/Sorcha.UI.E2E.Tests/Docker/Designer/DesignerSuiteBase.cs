// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.UI.E2E.Tests.Infrastructure;
using Sorcha.UI.E2E.Tests.PageObjects;

namespace Sorcha.UI.E2E.Tests.Docker.Designer;

/// <summary>
/// Shared base for the Feature 142 Blueprint Design Lifecycle Playwright suites
/// (rail gating, rehearsal walk, governed Go live, guided on-ramp, form authoring,
/// amend loop). Provides the <see cref="DesignerLifecyclePage"/> and an
/// authenticated navigation helper to the designer workspace.
/// </summary>
/// <remarks>
/// Abstract — NUnit does not run it directly. Concrete fixtures in this folder
/// MUST declare the suite categories so CI can filter them:
/// <code>
/// [Parallelizable(ParallelScope.Self)]
/// [TestFixture]
/// [Category("Docker")]
/// [Category("Designer")]
/// [Category("Lifecycle")]
/// [Category("Authenticated")]
/// public class LifecycleRailTests : DesignerSuiteBase { ... }
/// </code>
/// Per the <c>sorcha-ui</c> discipline: page object → Playwright test → component,
/// against Docker, with the base auto-checking console errors, network 5xx, and
/// MudBlazor CSS health on every authenticated navigation.
/// </remarks>
public abstract class DesignerSuiteBase : AuthenticatedDockerTestBase
{
    /// <summary>Shared page object for the rail-driven designer workspace.</summary>
    protected DesignerLifecyclePage Designer { get; private set; } = null!;

    [SetUp]
    public override async Task BaseSetUp()
    {
        await base.BaseSetUp();
        Designer = new DesignerLifecyclePage(Page);
    }

    /// <summary>
    /// Navigate to the designer workspace (optionally for a specific blueprint),
    /// re-authenticating inline if the token has not yet loaded, then wait for
    /// Blazor WASM hydration.
    /// </summary>
    protected async Task OpenDesignerAsync(string? blueprintId = null)
    {
        var path = TestConstants.AuthenticatedRoutes.DesignerBlueprint
                   + (string.IsNullOrEmpty(blueprintId) ? string.Empty : "/" + blueprintId);
        await NavigateAuthenticatedAsync(path);
        await Page.WaitForTimeoutAsync(TestConstants.BlazorHydrationTimeout);
    }
}
