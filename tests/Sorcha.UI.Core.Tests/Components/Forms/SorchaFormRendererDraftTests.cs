// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Generic;
using System.Text.Json;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using Sorcha.UI.Core.Components.Forms;
using Sorcha.UI.Core.Services.Forms;
using Xunit;
using BlueprintAction = Sorcha.Blueprint.Models.Action;

namespace Sorcha.UI.Core.Tests.Components.Forms;

/// <summary>
/// Feature 152 (US1) — the renderer can seed editable values from a saved draft
/// (<see cref="SorchaFormRenderer.InitialFormData"/>) and surfaces edits to a host autosave
/// (<see cref="SorchaFormRenderer.OnFormDataChanged"/>).
/// </summary>
public class SorchaFormRendererDraftTests : BunitContext
{
    public SorchaFormRendererDraftTests()
    {
        Services.AddMudServices();
        Services.AddScoped<IFormSchemaService, FormSchemaService>();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private static BlueprintAction MakeAction() => new()
    {
        Id = 1,
        Title = "Apply",
        Sender = "applicant",
        DataSchemas = new[]
        {
            JsonDocument.Parse("""{"type":"object","properties":{"name":{"type":"string"}}}"""),
        },
    };

    [Fact]
    public void InitialFormData_SeedsEditableValue_IntoRenderedForm()
    {
        var cut = Render<SorchaFormRenderer>(p => p
            .Add(x => x.Action, MakeAction())
            .Add(x => x.IsSenderAction, true)
            .Add(x => x.InitialFormData, new Dictionary<string, object?> { ["/name"] = "Ada Lovelace" }));

        // Robust to MudBlazor's input binding: the seeded value is present in the rendered form
        // (whether reflected as an input value attribute or bound text).
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Ada Lovelace"));
    }

    [Fact]
    public void OnFormDataChanged_ParameterIsAccepted_ForHostAutosave()
    {
        // The autosave forward fires from FormContext.OnDataChanged — the same event the renderer's
        // existing persona edit-detection relies on, so it is exercised on real user edits. Here we
        // assert the parameter is accepted and the form renders with it bound (the end-to-end
        // autosave is covered by ApplicationInstance integration + the quickstart manual check;
        // simulating MudBlazor's commit-on-blur through the auto-generated control is brittle).
        var rendered = false;
        var cut = Render<SorchaFormRenderer>(p => p
            .Add(x => x.Action, MakeAction())
            .Add(x => x.IsSenderAction, true)
            .Add(x => x.OnFormDataChanged, (IReadOnlyDictionary<string, object?> _) => rendered = true));

        cut.FindAll("input").Count.Should().BeGreaterThan(0);
        rendered.Should().BeFalse("no edit has occurred yet");
    }
}
