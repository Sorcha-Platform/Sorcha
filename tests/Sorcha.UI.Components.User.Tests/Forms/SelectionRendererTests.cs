// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using Sorcha.Blueprint.Models;
using Sorcha.UI.Core.Components.Forms.Controls;
using Sorcha.UI.Core.Models.Forms;
using Sorcha.UI.Core.Services.Forms;
using Xunit;

namespace Sorcha.UI.Components.User.Tests.Forms;

/// <summary>
/// Render-level tests for the selection dropdown.
/// </summary>
/// <remarks>
/// These exist because a unit test on the option list cannot see the defect they guard.
/// <c>SelectionOption.FromSchema</c> can be perfectly correct — values and labels paired exactly as
/// intended — while the rendered field still shows the citizen a raw code, because MudSelect derives
/// the CLOSED field's text from the bound value through its converter and never consults the
/// MudSelectItem child content outside the open popover. The two halves are individually right and
/// nothing but a render verifies the join.
/// </remarks>
public sealed class SelectionRendererTests : BunitContext
{
    public SelectionRendererTests()
    {
        // MudSelect builds a popover, and MudBlazor throws unless a MudPopoverProvider is somewhere in
        // the tree — which is a layout concern the real app satisfies in MainLayout. These tests render
        // the control alone, so the check is switched off rather than mounting a fake layout.
        Services.AddMudServices(o => o.PopoverOptions.CheckForPopoverProvider = false);
        Services.AddSingleton<IFormSchemaService, FormSchemaService>();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private const string CountrySchema = """
        {
          "type": "object",
          "properties": {
            "country": {
              "type": "string",
              "enum": ["GB", "FR"],
              "x-enumNames": ["United Kingdom", "France"]
            }
          }
        }
        """;

    private IRenderedComponent<SelectionRenderer> RenderWith(string schemaJson, string? storedValue)
    {
        var context = new FormContext { DataSchema = JsonDocument.Parse(schemaJson) };
        if (storedValue is not null)
        {
            context.SetValue("/country", storedValue);
        }

        return Render<SelectionRenderer>(p => p
            .AddCascadingValue(context)
            .Add(c => c.Control, new Control { Scope = "/country", Title = "Country" }));
    }

    [Fact]
    public void ClosedField_ShowsTheLabel_NotTheStoredCode()
    {
        // The defect this guards: the citizen opens the list, sees "United Kingdom", picks it, and
        // the field collapses back to "GB" — labels visible only while the dropdown is open.
        var cut = RenderWith(CountrySchema, "GB");

        var input = cut.Find("input");
        input.GetAttribute("value").Should().Be("United Kingdom");
    }

    [Fact]
    public void StoredValueIsUnchangedByLabelling()
    {
        // The label is presentation. The signed payload must still carry the code.
        var context = new FormContext { DataSchema = JsonDocument.Parse(CountrySchema) };
        context.SetValue("/country", "FR");

        Render<SelectionRenderer>(p => p
            .AddCascadingValue(context)
            .Add(c => c.Control, new Control { Scope = "/country", Title = "Country" }));

        context.GetValue<string>("/country").Should().Be("FR");
    }

    [Fact]
    public void UnrecognisedStoredValue_StaysVisible()
    {
        // A payload can carry a code the current schema no longer lists. Showing it is honest;
        // blanking the field would hide from the citizen that their record holds something odd.
        var cut = RenderWith(CountrySchema, "XK");

        cut.Find("input").GetAttribute("value").Should().Be("XK");
    }

    [Fact]
    public void WithoutLabels_ClosedFieldShowsTheValue()
    {
        var cut = RenderWith("""
            {"type":"object","properties":{"country":{"type":"string","enum":["GB","FR"]}}}
            """, "GB");

        cut.Find("input").GetAttribute("value").Should().Be("GB");
    }
}
