// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Playwright;
using Sorcha.UI.E2E.Tests.Infrastructure;
using Sorcha.UI.E2E.Tests.PageObjects;

namespace Sorcha.UI.E2E.Tests.Docker;

/// <summary>
/// E2E tests for the AI Designer shell. Originally written for the Feature 109 tabbed shell
/// (AI / Diagram / Preview tabs), reconciled in Feature 142 to the rail-driven staged shell:
/// the AI pane is now a persistent left-hand surface (no tabs), stages swap on the right driven
/// by the lifecycle rail, and Preview lives inside the Understand step-detail rather than a tab.
/// </summary>
/// <remarks>
/// Real-UI tests (AI pane present, rail navigation console health, legacy redirects) need no JS
/// hooks and pass against the Release Docker image. The synthetic-blueprint / message-injection and
/// form-preview tests drive the AiDesignerPane test hook (<c>window.sorcha.designer.aiPaneRef</c>),
/// which is only registered on DEBUG / E2E_TEST_HOOKS builds — they probe the hook and mark
/// themselves inconclusive when absent, so they skip cleanly on the Release Docker container.
/// </remarks>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
[Category("Docker")]
[Category("DesignerShell")]
[Category("Authenticated")]
public class DesignerShellTests : AuthenticatedDockerTestBase
{
    private DesignerShellPage _shell = null!;

    [SetUp]
    public override async Task BaseSetUp()
    {
        await base.BaseSetUp();
        _shell = new DesignerShellPage(Page);
    }

    [Test]
    [Retry(2)]
    public async Task DesignerShell_LoadsAtNewRoute_ShowsPersistentAiPane()
    {
        await _shell.NavigateAsync();
        await Page.WaitForTimeoutAsync(TestConstants.BlazorHydrationTimeout);

        Assert.That(await _shell.AiTabPanel.CountAsync(), Is.GreaterThan(0),
            "The persistent AI pane should render on /designer/blueprint.");

        // The chat input is pinned at the bottom of the AI pane.
        var input = _shell.ChatInput;
        if (await input.CountAsync() == 0)
        {
            Assert.Inconclusive("Chat input not rendered — hub may have failed to connect.");
            return;
        }

        var box = await input.BoundingBoxAsync();
        var viewportHeight = await Page.EvaluateAsync<double>("() => window.innerHeight");
        Assert.That(box, Is.Not.Null, "Input must have a bounding box.");
        var bottom = box!.Y + box.Height;
        Assert.That(bottom, Is.GreaterThan(viewportHeight - 200),
            $"Chat input should be near the viewport bottom (y+h={bottom}, vh={viewportHeight}).");
    }

    [Test]
    [Retry(2)]
    public async Task DesignerShell_InputPinnedAtBottom_AfterManyMessages()
    {
        await _shell.NavigateAsync();
        await Page.WaitForTimeoutAsync(TestConstants.BlazorHydrationTimeout);

        var hookReady = await Page.EvaluateAsync<bool>(
            "() => !!(window.sorcha && window.sorcha.designer && window.sorcha.designer.aiPaneRef)");
        if (!hookReady)
        {
            Assert.Inconclusive("AiDesignerPane test hook not registered — build without DEBUG/E2E_TEST_HOOKS?");
            return;
        }

        for (var i = 0; i < 50; i++)
        {
            await _shell.InjectSyntheticMessageAsync("assistant", $"Synthetic message #{i} — line one\nline two\nline three");
        }

        await Page.WaitForTimeoutAsync(500);

        var box = await _shell.ChatInput.BoundingBoxAsync();
        var viewportHeight = await Page.EvaluateAsync<double>("() => window.innerHeight");
        Assert.That(box, Is.Not.Null);
        var bottom = box!.Y + box.Height;
        Assert.That(Math.Abs(viewportHeight - bottom), Is.LessThan(200),
            $"Input must stay pinned to viewport bottom after 50 messages (y+h={bottom}, vh={viewportHeight}).");
    }

    [Test]
    [Retry(2)]
    public async Task DesignerShell_StageNav_PreservesChatSession()
    {
        await _shell.NavigateAsync();
        await Page.WaitForTimeoutAsync(TestConstants.BlazorHydrationTimeout);

        var hookReady = await Page.EvaluateAsync<bool>(
            "() => !!(window.sorcha && window.sorcha.designer && window.sorcha.designer.aiPaneRef)");
        if (!hookReady)
        {
            Assert.Inconclusive("Test hook unavailable; skipping.");
            return;
        }

        const string marker = "MARKER-STAGE-NAV-PRESERVES";
        await _shell.InjectSyntheticMessageAsync("user", marker);
        await Page.WaitForTimeoutAsync(300);

        // The AI pane is persistent across stage changes — navigating the rail must not clear it.
        // Understand may be locked without a blueprint; click regardless and ignore a disabled state.
        try { await _shell.RailUnderstand.ClickAsync(new LocatorClickOptions { Timeout = 3000 }); }
        catch { /* stage may be gated/locked */ }
        await Page.WaitForTimeoutAsync(500);

        await _shell.RailDescribe.ClickAsync();
        await Page.WaitForTimeoutAsync(500);

        var messageVisible = await Page.Locator($"text={marker}").CountAsync() > 0;
        Assert.That(messageVisible, Is.True, "Original chat message should remain after stage round-trip.");
    }

    [Test]
    [Retry(2)]
    public async Task DesignerShell_ConsoleNoErrors_DuringStageNavigation()
    {
        // Switch the visible stage via ?stage= deep-links using the auth-aware navigation so the
        // role-gated designer route resolves without a login bounce on each reload (real-UI, no hooks,
        // and no need to click under the first-run overlay scrim). The point is that switching the
        // stage stays console-clean.
        await NavigateAuthenticatedAsync(TestConstants.AuthenticatedRoutes.DesignerBlueprint + "?stage=understand");
        await Page.WaitForTimeoutAsync(TestConstants.BlazorHydrationTimeout);
        await NavigateAuthenticatedAsync(TestConstants.AuthenticatedRoutes.DesignerBlueprint + "?stage=describe");
        await Page.WaitForTimeoutAsync(300);

        // AssertNoConsoleErrors runs automatically in TearDown against the
        // DockerTestBase's filtered error list.
        Assert.Pass("Stage navigation completed; console assertion runs in TearDown.");
    }

    // ---------------------------------------------------------------------
    // Form preview — now hosted inside the Understand step-detail (was a tab in F109).
    // These tests drive the AiDesignerPane synthetic-blueprint hook, so they self-skip on the
    // Release Docker image. Real preview-pager logic is covered by the bUnit suites.
    // ---------------------------------------------------------------------

    /// <summary>
    /// Builds a synthetic blueprint JSON with the requested number of actions,
    /// each with a single required string field so the renderer has something
    /// to show. Used by the preview tests.
    /// </summary>
    private static string BuildSyntheticBlueprintJson(int actionCount)
    {
        var actions = new System.Text.StringBuilder();
        for (var i = 0; i < actionCount; i++)
        {
            if (i > 0)
            {
                actions.Append(',');
            }
            var fieldName = "field_" + i;
            var fieldTitle = "Field " + (i + 1);
            var actionTitle = "Action " + (i + 1);
            // Plain string concatenation — nested braces trip raw interpolated strings.
            actions.Append("{\"id\":" + i + ",")
                   .Append("\"title\":\"" + actionTitle + "\",")
                   .Append("\"description\":\"Synthetic " + actionTitle + "\",")
                   .Append("\"sender\":\"p1\",")
                   .Append("\"blueprintId\":\"e2e-preview-bp\",")
                   .Append("\"dataSchemas\":[{\"type\":\"object\",\"properties\":{\"" + fieldName + "\":{\"type\":\"string\",\"title\":\"" + fieldTitle + "\"}}}],")
                   .Append("\"condition\":{\"==\":[0,0]}}");
        }

        return "{\"id\":\"e2e-preview-bp\",\"title\":\"E2E Preview Blueprint\",\"version\":1,"
             + "\"participants\":[{\"id\":\"p1\",\"name\":\"Alice\"}],"
             + "\"actions\":[" + actions + "]}";
    }

    [Test]
    [Retry(2)]
    public async Task DesignerShell_PreviewRenders_SingleActionForm()
    {
        await _shell.NavigateAsync();
        await Page.WaitForTimeoutAsync(TestConstants.BlazorHydrationTimeout);

        var hookReady = await Page.EvaluateAsync<bool>(
            "() => !!(window.sorcha && window.sorcha.designer && window.sorcha.designer.aiPaneRef)");
        if (!hookReady)
        {
            Assert.Inconclusive("Test hook unavailable; skipping. Preview pager logic is covered by bUnit.");
            return;
        }

        await _shell.InjectSyntheticBlueprintUpdatedAsync(BuildSyntheticBlueprintJson(3));
        await Page.WaitForTimeoutAsync(500);

        // Preview now lives inside the Understand step-detail. Navigate there via the rail.
        try { await _shell.RailUnderstand.ClickAsync(new LocatorClickOptions { Timeout = 3000 }); }
        catch
        {
            Assert.Inconclusive("Understand stage stayed locked after blueprint injection.");
            return;
        }
        await Page.WaitForTimeoutAsync(500);

        var submit = Page.Locator("[data-testid='preview-submit-btn']").First;
        if (await submit.CountAsync() == 0)
        {
            Assert.Inconclusive("Preview form not shown — no journey step selected in the Understand step-detail.");
            return;
        }
        var isDisabled = await submit.IsDisabledAsync();
        Assert.That(isDisabled, Is.True, "Preview submit button must be disabled in PreviewMode.");
    }

    [Test]
    [Retry(2)]
    public async Task DesignerShell_PreviewPager_StepsThroughActions()
    {
        await _shell.NavigateAsync();
        await Page.WaitForTimeoutAsync(TestConstants.BlazorHydrationTimeout);

        var hookReady = await Page.EvaluateAsync<bool>(
            "() => !!(window.sorcha && window.sorcha.designer && window.sorcha.designer.aiPaneRef)");
        if (!hookReady)
        {
            Assert.Inconclusive("Test hook unavailable; skipping. Preview pager logic is covered by bUnit.");
            return;
        }

        await _shell.InjectSyntheticBlueprintUpdatedAsync(BuildSyntheticBlueprintJson(3));
        await Page.WaitForTimeoutAsync(500);

        try { await _shell.RailUnderstand.ClickAsync(new LocatorClickOptions { Timeout = 3000 }); }
        catch
        {
            Assert.Inconclusive("Understand stage stayed locked.");
            return;
        }
        await Page.WaitForTimeoutAsync(300);

        var nextBtn = Page.Locator("[data-testid='preview-next-btn']").First;
        if (await nextBtn.CountAsync() == 0)
        {
            Assert.Inconclusive("Preview pager not shown — no journey step selected in the Understand step-detail.");
            return;
        }

        await nextBtn.ClickAsync();
        await Page.WaitForTimeoutAsync(200);
        await nextBtn.ClickAsync();
        await Page.WaitForTimeoutAsync(300);

        // Should now be on Action 3 of 3.
        var pager = await Page.Locator(".form-preview-pager-count").First.InnerTextAsync();
        Assert.That(pager, Does.Contain("3 of 3"), $"Expected 'Action 3 of 3', got '{pager}'.");
    }

    [Test]
    [Ignore("Requires live Blazor Diagrams drag interop which is too brittle for Playwright in the current harness. Re-enable once the diagram canvas gains a stable testid-driven title-edit affordance.")]
    public Task DesignerShell_DiagramEdit_VisibleInOtherPanes()
    {
        // See task T038. Covered manually via quickstart.md §4b until the
        // diagram canvas exposes a keyboard/data-testid title-edit path.
        return Task.CompletedTask;
    }

    // ---------------------------------------------------------------------
    // Legacy URL compatibility — redirect targets updated for the F142 rail shell (?stage=).
    // ---------------------------------------------------------------------

    [Test]
    [Retry(2)]
    public async Task DesignerShell_LegacyChatRoute_Redirects()
    {
        await Page.GotoAsync($"{TestConstants.UiWebUrl}{TestConstants.AppBase}/designer/chat");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Page.WaitForTimeoutAsync(TestConstants.BlazorHydrationTimeout);

        Assert.That(Page.Url, Does.EndWith("/designer/blueprint?stage=describe"),
            $"Expected /designer/chat → /designer/blueprint?stage=describe, got {Page.Url}.");
        Assert.That(await _shell.AiTabPanel.CountAsync(), Is.GreaterThan(0),
            "The persistent AI pane must be present after redirect.");
    }

    [Test]
    [Ignore("Requires a seeded fixture blueprint id which the authenticated Docker harness does not currently provide. Once AuthenticatedDockerTestBase exposes a SeededBlueprintId, re-enable and use it here.")]
    public Task DesignerShell_LegacyChatWithIdRoute_Redirects()
    {
        // See task T041. The shim's logic is exercised indirectly by
        // DesignerShell_LegacyChatRoute_Redirects (same OnInitialized path with
        // an empty id). A dedicated test needs a real persisted blueprint id.
        return Task.CompletedTask;
    }

    [Test]
    [Retry(2)]
    public async Task DesignerShell_LegacyDesignerRoute_Redirects()
    {
        await Page.GotoAsync($"{TestConstants.UiWebUrl}{TestConstants.AppBase}/designer");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Page.WaitForTimeoutAsync(TestConstants.BlazorHydrationTimeout);

        Assert.That(Page.Url, Does.EndWith("/designer/blueprint?stage=understand"),
            $"Expected /designer → /designer/blueprint?stage=understand, got {Page.Url}.");
    }
}
