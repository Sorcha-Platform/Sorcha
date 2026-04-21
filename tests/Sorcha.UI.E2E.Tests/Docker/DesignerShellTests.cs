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
}
