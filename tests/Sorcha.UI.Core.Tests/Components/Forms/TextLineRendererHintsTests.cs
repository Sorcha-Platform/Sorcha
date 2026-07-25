// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Sorcha.Blueprint.Models;
using Sorcha.UI.Core.Components.Forms.Controls;
using Sorcha.UI.Core.Models.Forms;
using Sorcha.UI.Core.Services.Forms;
using Sorcha.UI.Testing;
using Xunit;

namespace Sorcha.UI.Core.Tests.Components.Forms;

/// <summary>
/// Issue #1278 (UT-015) — proves the keyboard hints actually reach the rendered <c>&lt;input&gt;</c>.
/// <see cref="TextInputHints"/> is unit-tested for the RULE; this pins the wiring, because the whole
/// fix rests on an assumption about MudBlazor that nothing else asserts: that unmatched attributes
/// splatted onto <c>MudTextField</c> land on the underlying input element rather than being dropped
/// or stuck on a wrapper. If that ever changes, the citizen silently gets the corrupting keyboard
/// back and every unit test still passes.
/// </summary>
public sealed class TextLineRendererHintsTests : ComponentTestFixture
{
    private static FormContext ContextFor(string propertyJson)
    {
        var schema = JsonDocument.Parse(
            "{\"type\":\"object\",\"properties\":{\"field\":" + propertyJson + "}}");
        return new FormContext { DataSchema = schema };
    }

    private IRenderedComponent<TextLineRenderer> RenderFor(string propertyJson)
    {
        Services.AddSingleton<IFormSchemaService, FormSchemaService>();
        var ctx = ContextFor(propertyJson);

        return Render<TextLineRenderer>(ps => ps
            .AddCascadingValue(ctx)
            .Add(p => p.Control, new Control { Scope = "/field", Title = "Field" }));
    }

    [Fact]
    public void AMachineCheckedField_SuppressesCapitalisationOnTheInputItself()
    {
        var cut = RenderFor("""{"type":"string","pattern":"^[A-Z0-9 ]+$"}""");

        var input = cut.Find("input");
        input.GetAttribute("autocapitalize").Should().Be("none");
        input.GetAttribute("autocorrect").Should().Be("off");
        input.GetAttribute("spellcheck").Should().Be("false");
    }

    [Fact]
    public void AProseField_LeavesTheBrowserAlone()
    {
        var cut = RenderFor("""{"type":"string","title":"Your name"}""");

        var input = cut.Find("input");
        input.GetAttribute("autocapitalize").Should().BeNull(
            "capitalising the first letter of a name is what the citizen wants — prose must not opt out");
    }
}
