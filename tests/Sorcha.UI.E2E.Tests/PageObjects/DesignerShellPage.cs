// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Playwright;
using Sorcha.UI.E2E.Tests.Infrastructure;

namespace Sorcha.UI.E2E.Tests.PageObjects;

/// <summary>
/// Page object for the unified designer shell at <c>/designer/blueprint</c>.
/// Uses stable <c>data-testid</c> selectors added by the Feature 109 pane
/// components.
/// </summary>
public class DesignerShellPage
{
    private readonly IPage _page;

    public DesignerShellPage(IPage page) => _page = page;

    // The persistent AI pane (Feature 142 — no longer a tab; always present on the left).
    // Root testid retained as `ai-tab-panel` for selector stability across the 109→142 shell move.
    public ILocator AiTabPanel => _page.Locator("[data-testid='ai-tab-panel']");

    // Lifecycle rail stage buttons (Feature 142 — replace the old AI/Diagram/Preview tabs).
    public ILocator RailDescribe => _page.Locator("[data-testid='rail-stage-describe']");
    public ILocator RailUnderstand => _page.Locator("[data-testid='rail-stage-understand']");
    public ILocator RailRehearse => _page.Locator("[data-testid='rail-stage-rehearse']");
    public ILocator RailGoLive => _page.Locator("[data-testid='rail-stage-golive']");

    // Chat input (pinned at the bottom of the AI pane).
    public ILocator ChatInput => _page.Locator("[data-testid='chat-input'] textarea").First;
    public ILocator ChatSendButton => _page.Locator("[data-testid='chat-send-button']");
    public ILocator ChatMessageList => _page.Locator("[data-testid='chat-message-list']");

    // Toolbar.
    public ILocator ToolbarSaveButton => _page.Locator(".designer-toolbar button:has-text('Save')").First;

    public string BaseUrl => $"{TestConstants.UiWebUrl}{TestConstants.AuthenticatedRoutes.DesignerBlueprint}";

    public Task NavigateAsync(string? blueprintId = null, string? tab = null)
    {
        var url = BaseUrl;
        if (!string.IsNullOrEmpty(blueprintId))
        {
            url += "/" + blueprintId;
        }
        if (!string.IsNullOrEmpty(tab))
        {
            url += "?tab=" + tab;
        }
        return _page.GotoAsync(url);
    }

    /// <summary>
    /// Invokes the test-only <see cref="T:Sorcha.UI.Web.Client.Pages.DesignerShell.Panes.AiDesignerPane"/>
    /// JS-invokable hook to simulate a blueprint-update hub event without a SignalR round-trip.
    /// </summary>
    public Task InjectSyntheticBlueprintUpdatedAsync(string blueprintJson, string? editedActionId = null)
    {
        return _page.EvaluateAsync(
            "async ([bp, eid]) => { await window.sorcha.designer.aiPaneRef.invokeMethodAsync('TestInject_SimulateBlueprintUpdated', bp, eid); }",
            new object?[] { blueprintJson, editedActionId });
    }

    /// <summary>
    /// Appends a synthetic chat message via the test-only hook. Used to load the
    /// message list without hitting the real AI backend.
    /// </summary>
    public Task InjectSyntheticMessageAsync(string role, string content)
    {
        return _page.EvaluateAsync(
            "async ([r, c]) => { await window.sorcha.designer.aiPaneRef.invokeMethodAsync('TestInject_AppendMessage', r, c); }",
            new object[] { role, content });
    }
}
