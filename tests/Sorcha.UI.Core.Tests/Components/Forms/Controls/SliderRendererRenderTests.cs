// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using Sorcha.Blueprint.Models;
using Sorcha.UI.Core.Components.Forms.Controls;
using Sorcha.UI.Core.Models.Forms;
using Sorcha.UI.Core.Services.Forms;
using Xunit;

namespace Sorcha.UI.Core.Tests.Components.Forms.Controls;

/// <summary>
/// The slider must seed an absent field and write an INTEGER. A stringly-typed value would pass
/// schema validation but silently score 0 in every band comparison. Feature AIAS M2.
/// </summary>
public class SliderRendererRenderTests : BunitContext
{
    private const string SchemaJson = """
        {"type":"object","properties":{
          "sharedPasswordCount":{"type":"integer","minimum":2,"maximum":10,
            "x-slider":{"step":1,"minLabel":"None","maxLabel":"10 or more"}}}}
        """;

    private const string SchemaJsonNoBounds = """
        {"type":"object","properties":{
          "sharedPasswordCount":{"type":"integer",
            "x-slider":{"step":1,"minLabel":"None","maxLabel":"10 or more"}}}}
        """;

    public SliderRendererRenderTests()
    {
        Services.AddMudServices();
        Services.AddSingleton<IFormSchemaService>(new FormSchemaService());
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private static FormContext MakeContext(string schemaJson = SchemaJson) => new()
    {
        AllFieldsDisclosed = true,
        DataSchema = JsonDocument.Parse(schemaJson)
    };

    private static Control MakeControl() => new()
    {
        ControlType = ControlTypes.Slider,
        Scope = "/sharedPasswordCount",
        Title = "How many of your accounts share a password?"
    };

    private IRenderedComponent<Bunit.Rendering.ContainerFragment> RenderSlider(FormContext ctx)
        => Render(builder =>
        {
            builder.OpenComponent<CascadingValue<FormContext>>(0);
            builder.AddAttribute(1, "Value", ctx);
            builder.AddAttribute(2, "ChildContent", (RenderFragment)(inner =>
            {
                inner.OpenComponent<SliderRenderer>(0);
                inner.AddAttribute(1, "Control", MakeControl());
                inner.CloseComponent();
            }));
            builder.CloseComponent();
        });

    [Fact]
    public void OnInit_AbsentValue_SeedsFromMinimumAndWritesAnInteger()
    {
        var ctx = MakeContext();

        RenderSlider(ctx);

        ctx.GetValue<int?>("/sharedPasswordCount").Should().Be(2);
    }

    [Fact]
    public void OnInit_AbsentValue_DoesNotWriteAString()
    {
        var ctx = MakeContext();

        RenderSlider(ctx);

        // Inspect the raw stored value directly — bypassing GetValue<T>'s coercion, which would
        // itself fail (and thus mask the bug) if a string had been written. FormData is the
        // renderer's actual write target, so this is the only way to genuinely tell 2 from "2".
        ctx.FormData["/sharedPasswordCount"].Should().BeOfType<int>();
    }

    [Fact]
    public void Rerender_WithUnchangedValue_RaisesNoFurtherDataChange()
    {
        // THE RENDER-LOOP GUARD. The seed write lives in OnParametersSet, which runs on every
        // re-render. FormContext.SetValue raises OnDataChanged; ControlDispatcher subscribes to
        // OnDataChanged and calls StateHasChanged; that re-renders this component, running
        // OnParametersSet again. Writing unconditionally therefore closes a feedback loop that
        // never settles — it froze the browser tab on the AIAS Cyber form (two sliders, mutually
        // reinforcing) with no error and no further network activity.
        //
        // ControlDispatcher is not in this render tree, so the loop cannot be reproduced here
        // directly. What IS reproducible — and is precisely the condition that closes it — is a
        // redundant write when nothing changed. Seeding must be idempotent.
        var ctx = MakeContext();
        var host = RenderSlider(ctx);
        var slider = host.FindComponent<SliderRenderer>();

        var writesAfterSeed = 0;
        ctx.OnDataChanged += () => writesAfterSeed++;

        // Drive OnParametersSet the way a parent re-render does. Re-rendering the CONTAINER is
        // not enough — Blazor only re-runs a child's OnParametersSet when its parameters are
        // actually set again, which is exactly what ControlDispatcher's StateHasChanged causes.
        slider.Render(p => p.Add(c => c.Control, MakeControl()));

        writesAfterSeed.Should().Be(0,
            "an already-seeded slider must not write again on re-render; each redundant write "
            + "re-raises OnDataChanged and drives another render");
    }

    [Fact]
    public void Rerender_StillPreservesTheSeededValue()
    {
        // Guard the guard: suppressing the redundant write must not lose the seed.
        var ctx = MakeContext();
        var host = RenderSlider(ctx);

        host.FindComponent<SliderRenderer>()
            .Render(p => p.Add(c => c.Control, MakeControl()));

        ctx.GetValue<int?>("/sharedPasswordCount").Should().Be(2);
        ctx.FormData["/sharedPasswordCount"].Should().BeOfType<int>();
    }

    [Fact]
    public void OnInit_ExistingValue_IsPreserved()
    {
        var ctx = MakeContext();
        ctx.SetValue("/sharedPasswordCount", 7);

        RenderSlider(ctx);

        ctx.GetValue<int?>("/sharedPasswordCount").Should().Be(7);
    }

    [Fact]
    public void OnInit_MissingBounds_DoesNotWriteAValueAndRendersWarning()
    {
        // Task 1's inference branch does not require minimum/maximum to be present, so a
        // blueprint author can produce an x-slider field with no declared range. Silently
        // inventing a 0-10 range would seed and submit a fabricated answer the citizen never
        // gave. The renderer must instead refuse to guess: render a visible warning and leave
        // the field unset.
        var ctx = MakeContext(SchemaJsonNoBounds);

        var component = RenderSlider(ctx);

        ctx.FormData.Should().NotContainKey("/sharedPasswordCount");
        component.Markup.Should().Contain("mud-alert");
    }
}
