// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Playwright;
using Sorcha.UI.E2E.Tests.Infrastructure;

namespace Sorcha.UI.E2E.Tests.Docker.Designer;

/// <summary>
/// E2E tests for Feature 142 User Story 4 (Guided AI on-ramp, T041 / FR-010 / FR-011).
/// </summary>
/// <remarks>
/// Asserts the deterministic parts of the guided opening:
/// <list type="bullet">
///   <item>On a fresh designer load with no blueprint, the AI pane shows a row of directed-build
///         chips (<c>[data-testid='directed-build-chips']</c>) with at least the three starter
///         options (grant / permit / certify-then-apply).</item>
///   <item>Clicking a chip sends a recognisable user message into the chat (the chip row hides
///         once the conversation has progressed).</item>
///   <item>The Describe-stage journey canvas re-renders when a <c>BlueprintUpdated</c> event
///         lands (exercised via the existing
///         <c>TestInject_SimulateBlueprintUpdated</c> seam on DEBUG / E2E_TEST_HOOKS builds).</item>
/// </list>
/// Steps that genuinely require a live AI round-trip are gated by <c>SORCHA_E2E_REQUIRES_AI=1</c>
/// (<see cref="Trait"/> <c>RequiresAi=true</c>) so CI stays deterministic.
/// </remarks>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
[Category("Docker")]
[Category("Designer")]
[Category("Lifecycle")]
[Category("GuidedOnRamp")]
[Category("Authenticated")]
public class GuidedOnRampTests : DesignerSuiteBase
{
    private const string ChipRowSelector = "[data-testid='directed-build-chips']";
    private const string ChipButtonSelector = "[data-testid='directed-build-chips'] [data-starter-id]";

    [Test]
    [Retry(2)]
    public async Task GuidedOnRamp_FreshDesigner_RendersDirectedBuildChips()
    {
        await OpenDesignerAsync();

        // The chip row should be visible when no blueprint is loaded and the chat is on its first turn.
        var chipRow = Page.Locator(ChipRowSelector);
        Assert.That(await chipRow.CountAsync(), Is.GreaterThan(0),
            "Directed-build chip row should render on a fresh designer load.");

        var chips = Page.Locator(ChipButtonSelector);
        var chipCount = await chips.CountAsync();
        Assert.That(chipCount, Is.GreaterThanOrEqualTo(3),
            "At least three directed-build starters should be offered (grant / permit / certify-then-apply).");

        // Stable starter ids — the orchestration recognises these.
        var ids = new List<string>();
        for (var i = 0; i < chipCount; i++)
        {
            var id = await chips.Nth(i).GetAttributeAsync("data-starter-id");
            if (!string.IsNullOrEmpty(id)) { ids.Add(id); }
        }

        Assert.Multiple(() =>
        {
            Assert.That(ids, Does.Contain("grant"), "grant starter chip");
            Assert.That(ids, Does.Contain("permit"), "permit starter chip");
            Assert.That(ids, Does.Contain("certify-then-apply"), "certify-then-apply starter chip");
        });
    }

    [Test]
    [Retry(2)]
    [Property("RequiresAi", "false")]
    public async Task GuidedOnRamp_ClickingChip_EnqueuesUserMessageAndHidesChipRow()
    {
        await OpenDesignerAsync();

        var chipRow = Page.Locator(ChipRowSelector);
        Assert.That(await chipRow.CountAsync(), Is.GreaterThan(0),
            "Precondition: directed-build chips visible on fresh load.");

        // Click the grant starter chip.
        var grantChip = Page.Locator("[data-testid='directed-build-chips'] [data-starter-id='grant']");
        Assert.That(await grantChip.CountAsync(), Is.GreaterThan(0), "grant chip exists");
        await grantChip.ClickAsync();

        await Page.WaitForTimeoutAsync(TestConstants.ShortWait);

        // The chip row should disappear once the user has progressed past the opening turn.
        Assert.That(await chipRow.IsVisibleAsync(), Is.False,
            "Chip row should hide once a directed-build choice has been made.");

        // A user message corresponding to the chip choice should now be in the message list.
        var messages = Page.Locator("[data-testid='chat-message-list']");
        Assert.That(await messages.CountAsync(), Is.GreaterThan(0), "chat message list rendered");
        var messageText = (await messages.InnerTextAsync()).ToLowerInvariant();
        Assert.That(messageText, Does.Contain("grant"),
            "The grant chip should enqueue a recognisable user message containing 'grant'.");
    }

    [Test]
    [Retry(2)]
    public async Task GuidedOnRamp_BlueprintUpdate_RendersJourneyLiveInDescribeStage()
    {
        await OpenDesignerAsync();

        // Verify the Describe stage is visible (it is the default landing canvas on a fresh load).
        Assert.That(await Designer.StageDescribe.IsVisibleAsync(), Is.True,
            "Describe stage should be visible on fresh designer load.");

        // The journey starts empty (no blueprint).
        Assert.That(await Designer.JourneySteps.CountAsync(), Is.EqualTo(0),
            "No journey steps before a blueprint is applied.");

        // Use the AiDesignerPane test seam to simulate a BlueprintUpdated hub event.
        var seamReady = await Page.EvaluateAsync<bool>(
            "() => !!(window.sorcha && window.sorcha.designer && window.sorcha.designer.aiPaneRef)");

        if (!seamReady)
        {
            Assert.Inconclusive(
                "AiDesignerPane test seam unavailable — Release build without E2E_TEST_HOOKS; " +
                "live-journey render covered by bUnit (T045).");
            return;
        }

        // Minimal two-action blueprint matching the JourneyView model expectations.
        const string blueprintJson = """
            {
                "id":"gor-bp-1",
                "title":"Guided On-Ramp Smoke",
                "description":"Generated by the directed-build test.",
                "version":1,
                "participants":[
                    {"id":"applicant","name":"Applicant"},
                    {"id":"officer","name":"Officer","walletAddress":"ws11qofficer"}
                ],
                "actions":[
                    {"id":1,"title":"Apply","sender":"applicant","isStartingAction":true},
                    {"id":2,"title":"Review","sender":"officer"}
                ]
            }
            """;

        await Page.EvaluateAsync(
            "async (json) => { await window.sorcha.designer.aiPaneRef.invokeMethodAsync(" +
            "'TestInject_SimulateBlueprintUpdated', json, null); }",
            blueprintJson);

        await Page.WaitForTimeoutAsync(TestConstants.ShortWait);

        var steps = await Designer.JourneySteps.CountAsync();
        Assert.That(steps, Is.EqualTo(2),
            "Describe stage journey should re-render live when the AI emits a BlueprintUpdated event.");
    }
}
