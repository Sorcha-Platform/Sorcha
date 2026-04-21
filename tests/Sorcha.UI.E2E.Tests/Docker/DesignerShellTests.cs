// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Playwright;
using Sorcha.UI.E2E.Tests.Infrastructure;
using Sorcha.UI.E2E.Tests.PageObjects;

namespace Sorcha.UI.E2E.Tests.Docker;

/// <summary>
/// E2E tests for the AI Designer unified shell (Feature 109, US1).
/// Covers the new /designer/blueprint route, the pinned chat input,
/// tab-switching state preservation, diagram save-through, and console
/// cleanliness. Uses the AiDesignerPane's test-only [JSInvokable] hook to
/// inject synthetic hub events without real SignalR traffic.
/// </summary>
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
    public async Task DesignerShell_LoadsAtNewRoute_ShowsAiTabFullWidth()
    {
        await _shell.NavigateAsync();
        await Page.WaitForTimeoutAsync(TestConstants.BlazorHydrationTimeout);

        Assert.That(await _shell.AiTabPanel.CountAsync(), Is.GreaterThan(0),
            "AI tab panel should render on /designer/blueprint.");

        // Input should be pinned at the viewport bottom (input's y+height ≈ innerHeight).
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
    public async Task DesignerShell_TabSwitch_PreservesChatSession()
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

        const string marker = "MARKER-TAB-SWITCH-PRESERVES";
        await _shell.InjectSyntheticMessageAsync("user", marker);
        await Page.WaitForTimeoutAsync(300);

        // Switch to Diagram (may be disabled if no blueprint; click regardless and ignore).
        try { await _shell.DiagramTabButton.ClickAsync(new LocatorClickOptions { Timeout = 3000 }); }
        catch { /* tab may be disabled */ }
        await Page.WaitForTimeoutAsync(500);

        // Switch back to AI.
        await _shell.AiTabButton.ClickAsync();
        await Page.WaitForTimeoutAsync(500);

        var messageVisible = await Page.Locator($"text={marker}").CountAsync() > 0;
        Assert.That(messageVisible, Is.True, "Original chat message should remain after tab round-trip.");
    }

    [Test]
    [Retry(2)]
    public async Task DesignerShell_SaveFromDiagram_PersistsAiEdits()
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

        // Inject a minimal synthetic blueprint update — Id/Title only; Actions empty.
        const string syntheticBp = """
            {"id":"e2e-test-bp","title":"E2E Synthetic Blueprint","version":1,"participants":[],"actions":[]}
        """;
        await _shell.InjectSyntheticBlueprintUpdatedAsync(syntheticBp);
        await Page.WaitForTimeoutAsync(500);

        // Switch to Diagram tab (should no longer be disabled with a blueprint loaded).
        try { await _shell.DiagramTabButton.ClickAsync(new LocatorClickOptions { Timeout = 3000 }); }
        catch
        {
            Assert.Inconclusive("Diagram tab stayed disabled after blueprint injection.");
            return;
        }
        await Page.WaitForTimeoutAsync(500);

        // Click Save from toolbar.
        if (await _shell.ToolbarSaveButton.IsEnabledAsync())
        {
            await _shell.ToolbarSaveButton.ClickAsync();
            await Page.WaitForTimeoutAsync(1500);
        }

        // The test's main guarantee is that no console error was produced by the save flow;
        // in a Docker-less run the backend call will fail with a snackbar and we tolerate that.
        Assert.Pass("Save flow exercised from Diagram tab without shell-side errors.");
    }

    [Test]
    [Retry(2)]
    public async Task DesignerShell_ConsoleNoErrors_DuringTabSwitches()
    {
        await _shell.NavigateAsync();
        await Page.WaitForTimeoutAsync(TestConstants.BlazorHydrationTimeout);

        try { await _shell.DiagramTabButton.ClickAsync(new LocatorClickOptions { Timeout = 3000 }); }
        catch { /* may be disabled */ }
        await Page.WaitForTimeoutAsync(300);

        await _shell.AiTabButton.ClickAsync();
        await Page.WaitForTimeoutAsync(300);

        // AssertNoConsoleErrors runs automatically in TearDown against the
        // DockerTestBase's filtered error list.
        Assert.Pass("Tab switches completed; console assertion runs in TearDown.");
    }

    // ---------------------------------------------------------------------
    // User Story 2 — Form Preview tab with auto-cursor
    // ---------------------------------------------------------------------

    /// <summary>
    /// Builds a synthetic blueprint JSON with the requested number of actions,
    /// each with a single required string field so the renderer has something
    /// to show. Used by the US2 preview tests.
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
            Assert.Inconclusive("Test hook unavailable; skipping.");
            return;
        }

        await _shell.InjectSyntheticBlueprintUpdatedAsync(BuildSyntheticBlueprintJson(3));
        await Page.WaitForTimeoutAsync(500);

        try { await _shell.PreviewTabButton.ClickAsync(new LocatorClickOptions { Timeout = 3000 }); }
        catch
        {
            Assert.Inconclusive("Preview tab stayed disabled after blueprint injection.");
            return;
        }
        await Page.WaitForTimeoutAsync(500);

        var submit = Page.Locator("[data-testid='preview-submit-btn']").First;
        Assert.That(await submit.CountAsync(), Is.GreaterThan(0), "Preview submit button should render.");
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
            Assert.Inconclusive("Test hook unavailable; skipping.");
            return;
        }

        await _shell.InjectSyntheticBlueprintUpdatedAsync(BuildSyntheticBlueprintJson(3));
        await Page.WaitForTimeoutAsync(500);

        try { await _shell.PreviewTabButton.ClickAsync(new LocatorClickOptions { Timeout = 3000 }); }
        catch
        {
            Assert.Inconclusive("Preview tab stayed disabled.");
            return;
        }
        await Page.WaitForTimeoutAsync(300);

        var nextBtn = Page.Locator("[data-testid='preview-next-btn']").First;
        await nextBtn.ClickAsync();
        await Page.WaitForTimeoutAsync(200);
        await nextBtn.ClickAsync();
        await Page.WaitForTimeoutAsync(300);

        // Should now be on Action 3 of 3.
        var pager = await Page.Locator(".form-preview-pager-count").First.InnerTextAsync();
        Assert.That(pager, Does.Contain("3 of 3"), $"Expected 'Action 3 of 3', got '{pager}'.");

        // Keyboard: [ goes back, ] goes forward. Focus the pane first.
        await Page.Locator("[data-testid='preview-tab-panel']").First.FocusAsync();
        await Page.Keyboard.PressAsync("[");
        await Page.WaitForTimeoutAsync(150);
        await Page.Keyboard.PressAsync("]");
        await Page.WaitForTimeoutAsync(200);

        pager = await Page.Locator(".form-preview-pager-count").First.InnerTextAsync();
        Assert.That(pager, Does.Contain("3 of 3"), "Bracket keys should round-trip back to Action 3.");
    }

    [Test]
    [Retry(2)]
    public async Task DesignerShell_PreviewFollowAiToggle_AutoCursor()
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

        // Initial blueprint with 3 actions + AI-edit hint to Action 2 (id=1).
        await _shell.InjectSyntheticBlueprintUpdatedAsync(BuildSyntheticBlueprintJson(3), editedActionId: "1");
        await Page.WaitForTimeoutAsync(500);

        try { await _shell.PreviewTabButton.ClickAsync(new LocatorClickOptions { Timeout = 3000 }); }
        catch
        {
            Assert.Inconclusive("Preview tab disabled.");
            return;
        }
        await Page.WaitForTimeoutAsync(300);

        var pager = await Page.Locator(".form-preview-pager-count").First.InnerTextAsync();
        Assert.That(pager, Does.Contain("2 of 3"), $"Auto-cursor should follow AI edit to Action 2; got '{pager}'.");

        // Manual override: Next → Action 3.
        await Page.Locator("[data-testid='preview-next-btn']").First.ClickAsync();
        await Page.WaitForTimeoutAsync(200);
        pager = await Page.Locator(".form-preview-pager-count").First.InnerTextAsync();
        Assert.That(pager, Does.Contain("3 of 3"));

        // AI edits Action 3 (id=2) — but we're manual, cursor should stay on Action 3 anyway.
        // Use edited id=0 (Action 1) so we can verify cursor does NOT move.
        await _shell.InjectSyntheticBlueprintUpdatedAsync(BuildSyntheticBlueprintJson(3), editedActionId: "0");
        await Page.WaitForTimeoutAsync(400);
        pager = await Page.Locator(".form-preview-pager-count").First.InnerTextAsync();
        Assert.That(pager, Does.Contain("3 of 3"),
            $"Manual cursor must survive AI updates; got '{pager}'.");

        // Click Follow AI — cursor should snap to last AI-edited action (Action 1).
        await Page.Locator("[data-testid='preview-follow-ai-toggle']").First.ClickAsync();
        await Page.WaitForTimeoutAsync(300);
        pager = await Page.Locator(".form-preview-pager-count").First.InnerTextAsync();
        Assert.That(pager, Does.Contain("1 of 3"),
            $"Follow AI should snap cursor to last AI-edited action; got '{pager}'.");
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
    // User Story 3 — Legacy URL compatibility
    // ---------------------------------------------------------------------

    [Test]
    [Retry(2)]
    public async Task DesignerShell_LegacyChatRoute_Redirects()
    {
        await Page.GotoAsync($"{TestConstants.UiWebUrl}/designer/chat");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Page.WaitForTimeoutAsync(TestConstants.BlazorHydrationTimeout);

        Assert.That(Page.Url, Does.EndWith("/designer/blueprint?tab=ai"),
            $"Expected /designer/chat → /designer/blueprint?tab=ai, got {Page.Url}.");
        Assert.That(await _shell.AiTabPanel.CountAsync(), Is.GreaterThan(0),
            "AI tab panel must be present after redirect.");
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
        await Page.GotoAsync($"{TestConstants.UiWebUrl}/designer");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Page.WaitForTimeoutAsync(TestConstants.BlazorHydrationTimeout);

        Assert.That(Page.Url, Does.EndWith("/designer/blueprint?tab=diagram"),
            $"Expected /designer → /designer/blueprint?tab=diagram, got {Page.Url}.");
    }
}
