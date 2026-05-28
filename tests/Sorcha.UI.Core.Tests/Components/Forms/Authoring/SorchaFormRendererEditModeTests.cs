// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using Sorcha.Blueprint.Models.Forms;
using Sorcha.UI.Core.Components.Forms;
using Sorcha.UI.Core.Services.Forms;
using Xunit;
using BlueprintAction = Sorcha.Blueprint.Models.Action;

namespace Sorcha.UI.Core.Tests.Components.Forms.Authoring;

/// <summary>
/// T048 [US5] — verify the production <see cref="SorchaFormRenderer"/> exposes
/// the Edit / Preview toggle in its header ONLY when the parent opts in via
/// <c>EditAllowed</c>. Citizens never see it.
/// </summary>
public class SorchaFormRendererEditModeTests : BunitContext
{
    public SorchaFormRendererEditModeTests()
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
    public void Renderer_EditAllowedFalse_DoesNotRenderToggle()
    {
        var cut = Render<SorchaFormRenderer>(p => p
            .Add(x => x.Action, MakeAction())
            .Add(x => x.EditAllowed, false));

        cut.FindAll("[data-testid='form-edit-mode-toggle']").Count.Should().Be(0);
    }

    [Fact]
    public void Renderer_EditAllowedTrue_RendersToggle()
    {
        var cut = RenderWithPopover(MakeAction(), editAllowed: true, mode: FormAuthoringMode.Preview, onChange: null);
        cut.FindAll("[data-testid='form-edit-mode-toggle']").Count.Should().Be(1);
    }

    [Fact]
    public void Renderer_ToggleClick_InvokesOnEditModeChangedWithFlippedMode()
    {
        FormAuthoringMode? captured = null;
        var cut = RenderWithPopover(MakeAction(), editAllowed: true, mode: FormAuthoringMode.Preview,
            onChange: m => { captured = m; });

        cut.Find("[data-testid='form-edit-mode-toggle']").Click();

        captured.Should().Be(FormAuthoringMode.Edit);
    }

    /// <summary>
    /// MudTooltip on the edit toggle renders through a popover that requires the
    /// <see cref="MudPopoverProvider"/> in the render tree. Mount it as a sibling.
    /// </summary>
    private IRenderedComponent<Bunit.Rendering.ContainerFragment> RenderWithPopover(
        BlueprintAction action, bool editAllowed, FormAuthoringMode mode,
        System.Action<FormAuthoringMode>? onChange)
        => Render(builder =>
        {
            builder.OpenComponent<MudPopoverProvider>(0);
            builder.CloseComponent();
            builder.OpenComponent<SorchaFormRenderer>(1);
            builder.AddAttribute(2, nameof(SorchaFormRenderer.Action), action);
            builder.AddAttribute(3, nameof(SorchaFormRenderer.EditAllowed), editAllowed);
            builder.AddAttribute(4, nameof(SorchaFormRenderer.Mode), mode);
            if (onChange is not null)
            {
                builder.AddAttribute(5, nameof(SorchaFormRenderer.OnEditModeChanged),
                    Microsoft.AspNetCore.Components.EventCallback.Factory.Create<FormAuthoringMode>(this, onChange));
            }
            builder.CloseComponent();
        });
}
