// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using Sorcha.Blueprint.Models;
using Sorcha.UI.Core.Components.Forms.Controls;
using Sorcha.UI.Core.Models.Forms;
using Sorcha.UI.Core.Services.Forms;
using Xunit;

namespace Sorcha.UI.Core.Tests.Components.Forms.Controls;

/// <summary>
/// GAP-030 — field control renderers must refresh their inline error UI when an EXTERNAL
/// validation pass raises <see cref="FormContext.OnValidationChanged"/> (e.g. a wizard's
/// HandleNext / final HandleSubmit calls FormContext.SetErrors on fields the user never
/// touched). Before the fix the renderers only refreshed via OnParametersSet (parent
/// re-render); a direct SetErrors with no parent re-render left the error UI stale.
/// Each test renders the control standalone (no parent), fires SetErrors, and asserts the
/// message surfaces — which can only happen if the renderer subscribed to OnValidationChanged.
/// </summary>
public class ControlValidationRefreshTests : BunitContext
{
    private const string ErrMsg = "Name is required";

    public ControlValidationRefreshTests()
    {
        Services.AddMudServices();
        Services.AddScoped<IFormSchemaService, FormSchemaService>();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private static FormContext MakeContext() => new()
    {
        AllFieldsDisclosed = true,
        DataSchema = JsonDocument.Parse(
            """{"type":"object","properties":{"name":{"type":"string","enum":["Yes","No"]}},"required":["name"]}"""),
    };

    private static Control MakeControl() => new() { Scope = "/name", Title = "Name" };

    // Renders a control standalone under a MudPopoverProvider (date/select use popovers) and the
    // cascading FormContext the renderer reads its value + errors from.
    private IRenderedComponent<Bunit.Rendering.ContainerFragment> RenderControl<TRenderer>(FormContext ctx)
        where TRenderer : IComponent
        => Render(builder =>
        {
            builder.OpenComponent<MudPopoverProvider>(0);
            builder.CloseComponent();
            builder.OpenComponent<CascadingValue<FormContext>>(1);
            builder.AddAttribute(2, "Value", ctx);
            builder.AddAttribute(3, "ChildContent", (RenderFragment)(inner =>
            {
                inner.OpenComponent<TRenderer>(0);
                inner.AddAttribute(1, "Control", MakeControl());
                inner.CloseComponent();
            }));
            builder.CloseComponent();
        });

    private void AssertRefreshesOnExternalValidation(IRenderedComponent<Bunit.Rendering.ContainerFragment> cut, FormContext ctx)
    {
        // No error yet — the renderer starts clean.
        cut.Markup.Should().NotContain(ErrMsg);

        // Simulate an external validation pass (wizard HandleNext / HandleSubmit) raising
        // OnValidationChanged WITHOUT re-rendering the parent that owns this control.
        ctx.SetErrors("/name", new List<string> { ErrMsg });

        // The error must surface — only possible if the renderer subscribed to OnValidationChanged.
        cut.WaitForAssertion(() => cut.Markup.Should().Contain(ErrMsg));
    }

    [Fact]
    public void TextLineRenderer_RefreshesErrorOnExternalValidation()
    {
        var ctx = MakeContext();
        AssertRefreshesOnExternalValidation(RenderControl<TextLineRenderer>(ctx), ctx);
    }

    [Fact]
    public void TextAreaRenderer_RefreshesErrorOnExternalValidation()
    {
        var ctx = MakeContext();
        AssertRefreshesOnExternalValidation(RenderControl<TextAreaRenderer>(ctx), ctx);
    }

    [Fact]
    public void NumericRenderer_RefreshesErrorOnExternalValidation()
    {
        var ctx = MakeContext();
        AssertRefreshesOnExternalValidation(RenderControl<NumericRenderer>(ctx), ctx);
    }

    [Fact]
    public void DateTimeRenderer_RefreshesErrorOnExternalValidation()
    {
        var ctx = MakeContext();
        AssertRefreshesOnExternalValidation(RenderControl<DateTimeRenderer>(ctx), ctx);
    }

    [Fact]
    public void CheckboxRenderer_RefreshesErrorOnExternalValidation()
    {
        var ctx = MakeContext();
        AssertRefreshesOnExternalValidation(RenderControl<CheckboxRenderer>(ctx), ctx);
    }

    [Fact]
    public void SelectionRenderer_RefreshesErrorOnExternalValidation()
    {
        var ctx = MakeContext();
        AssertRefreshesOnExternalValidation(RenderControl<SelectionRenderer>(ctx), ctx);
    }

    [Fact]
    public void ChoiceRenderer_RefreshesErrorOnExternalValidation()
    {
        var ctx = MakeContext();
        AssertRefreshesOnExternalValidation(RenderControl<ChoiceRenderer>(ctx), ctx);
    }
}
