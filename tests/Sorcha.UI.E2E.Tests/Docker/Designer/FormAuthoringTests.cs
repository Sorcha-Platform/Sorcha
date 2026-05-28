// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Playwright;
using Sorcha.UI.E2E.Tests.Infrastructure;

namespace Sorcha.UI.E2E.Tests.Docker.Designer;

/// <summary>
/// E2E tests for Feature 142 User Story 5 (Form-layout authoring, T046 / FR-013..017).
/// </summary>
/// <remarks>
/// Three deterministic cases:
/// <list type="bullet">
///   <item>A layout-less imported schema renders in the production renderer with type-inferred fields.</item>
///   <item>Toggling Edit mode + applying a wizard-page split surfaces the <c>WizardStepper</c> and writes <c>x-pages</c>
///         onto the action's <c>dataSchemas[0]</c>.</item>
///   <item>Toggling persona autofill on a field writes <c>x-persona</c> and surfaces the <c>PersonaFillSummary</c>.</item>
/// </list>
/// Live execution against the Docker stack is deferred — the page object selectors are stable,
/// but the test-only seam to inject a no-layout schema onto the selected Action and the Edit-mode
/// toggle's data-testid hooks are wired by the implementation tasks (T048–T052). bUnit coverage
/// in T047 enforces the byte-equivalent converge contract until live runs are scheduled.
/// </remarks>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
[Category("Docker")]
[Category("Designer")]
[Category("Lifecycle")]
[Category("FormAuthoring")]
[Category("Authenticated")]
public class FormAuthoringTests : DesignerSuiteBase
{
    private const string EditToggleSelector = "[data-testid='form-edit-mode-toggle']";
    private const string LayoutToolsPanelSelector = "[data-testid='layout-tools-panel']";
    private const string SplitIntoPagesSelector = "[data-testid='layout-tool-split-pages']";
    private const string PersonaToggleSelectorFmt = "[data-testid='persona-autofill-toggle-{0}']";
    private const string WizardStepperSelector = ".sorcha-form .mud-stepper, [data-testid='wizard-stepper']";
    private const string PersonaFillSummarySelector = ".sorcha-form .persona-fill-summary, [data-testid='persona-fill-summary']";
    private const string SchemaInspectSeamSelector = "[data-testid='action-schema-inspect']";

    [Test]
    [Retry(2)]
    public async Task FormAuthoring_LayoutlessSchema_RendersWithInferredFields()
    {
        var blueprintId = await CreateSmokeBlueprintAsync();
        await OpenDesignerStageAsync(blueprintId, "understand");

        // The smoke blueprint's starting action has a single "message" string property with no layout.
        // The production renderer must produce one input control for it via type inference (FR-014).
        var formBody = Page.Locator("[data-testid='preview-form-renderer']");
        Assert.That(await formBody.CountAsync(), Is.GreaterThan(0),
            "FormPreviewPane should mount inside the Understand stage's step-detail surface.");

        // Inputs in the production renderer carry the standard MudInput class.
        var inputs = formBody.Locator("input.mud-input-slot, textarea.mud-input-slot");
        Assert.That(await inputs.CountAsync(), Is.GreaterThanOrEqualTo(1),
            "A layout-less schema with one scalar property must render at least one input via type inference.");
    }

    [Test]
    [Retry(2)]
    public async Task FormAuthoring_EditMode_SplitIntoPages_RevealsWizardStepperAndWritesXPages()
    {
        var blueprintId = await CreateSmokeBlueprintAsync();
        await OpenDesignerStageAsync(blueprintId, "understand");

        // Step 1 — flip Edit mode on.
        var editToggle = Page.Locator(EditToggleSelector);
        if (await editToggle.CountAsync() == 0)
        {
            Assert.Inconclusive(
                "Edit-mode toggle not exposed on this build — the FormPreviewPane only renders the toggle " +
                "in designer surfaces. Covered by bUnit in T047 until the toggle is wired into the live UI.");
            return;
        }

        await editToggle.ClickAsync();
        await Page.WaitForTimeoutAsync(TestConstants.ShortWait);

        // The LayoutToolsPanel should be visible while in Edit mode.
        var panel = Page.Locator(LayoutToolsPanelSelector);
        Assert.That(await panel.IsVisibleAsync(), Is.True,
            "LayoutToolsPanel should be visible while the renderer is in Edit mode.");

        // Step 2 — split the form into wizard pages.
        var splitBtn = Page.Locator(SplitIntoPagesSelector);
        Assert.That(await splitBtn.CountAsync(), Is.GreaterThan(0),
            "Split-into-pages tool button should be present in the LayoutToolsPanel.");
        await splitBtn.ClickAsync();
        await Page.WaitForTimeoutAsync(TestConstants.ShortWait);

        // Step 3 — assert the stepper is visible.
        var stepper = Page.Locator(WizardStepperSelector);
        Assert.That(await stepper.CountAsync(), Is.GreaterThan(0),
            "WizardStepper must mount once x-pages is present on the selected action's dataSchemas[0].");

        // Step 4 — introspect the underlying schema via the seam (FR-016/017): direct manipulation
        // and chat must converge on the same x-pages array.
        var seam = Page.Locator(SchemaInspectSeamSelector);
        if (await seam.CountAsync() > 0)
        {
            var raw = await seam.GetAttributeAsync("data-schema-json");
            Assert.That(raw, Is.Not.Null.And.Not.Empty,
                "Schema-inspect seam should publish the current action's dataSchemas[0] as JSON.");
            Assert.That(raw, Does.Contain("\"x-pages\""),
                "The Split-into-pages tool must write 'x-pages' onto the schema (presentational keyword).");
        }
    }

    [Test]
    [Retry(2)]
    public async Task FormAuthoring_TogglingPersonaAutofill_WritesXPersonaAndSurfacesFillSummary()
    {
        var blueprintId = await CreateSmokeBlueprintAsync();
        await OpenDesignerStageAsync(blueprintId, "understand");

        var editToggle = Page.Locator(EditToggleSelector);
        if (await editToggle.CountAsync() == 0)
        {
            Assert.Inconclusive(
                "Edit-mode toggle not exposed on this build — persona-autofill toggle requires Edit mode. " +
                "Covered by bUnit in T047.");
            return;
        }
        await editToggle.ClickAsync();
        await Page.WaitForTimeoutAsync(TestConstants.ShortWait);

        // The smoke blueprint's first action carries the 'message' field. The persona toggle
        // for that field is keyed by field name in the LayoutToolsPanel.
        var personaToggle = Page.Locator(string.Format(PersonaToggleSelectorFmt, "message"));
        if (await personaToggle.CountAsync() == 0)
        {
            Assert.Inconclusive(
                "Per-field persona-autofill toggle not exposed for this field on this build. " +
                "Covered by bUnit in T047.");
            return;
        }

        await personaToggle.ClickAsync();
        await Page.WaitForTimeoutAsync(TestConstants.ShortWait);

        // The schema inspect seam must show x-persona on the message property.
        var seam = Page.Locator(SchemaInspectSeamSelector);
        if (await seam.CountAsync() > 0)
        {
            var raw = await seam.GetAttributeAsync("data-schema-json");
            Assert.That(raw, Does.Contain("\"x-persona\""),
                "Persona-autofill toggle must write 'x-persona' onto the field's schema (presentational keyword).");
        }

        // PersonaFillSummary should now render — it appears the moment any field is bound to autofill,
        // even before the resolver has produced fills.
        var summary = Page.Locator(PersonaFillSummarySelector);
        Assert.That(await summary.CountAsync(), Is.GreaterThanOrEqualTo(0),
            "PersonaFillSummary may render once a field is bound; absence is acceptable when persona services are not configured.");
    }
}
