// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using Sorcha.Blueprint.Models.Forms;
using Sorcha.UI.Core.Components.Forms.Authoring;
using Sorcha.UI.Core.Services.Forms;
using Xunit;
using BlueprintAction = Sorcha.Blueprint.Models.Action;

namespace Sorcha.UI.Core.Tests.Components.Forms.Authoring;

/// <summary>
/// T050 [US5] — verify the <see cref="LayoutToolsPanel"/> wires its tool buttons through the
/// shared <see cref="IFormLayoutAuthoringService"/> and emits the mutated Action on
/// <c>OnLayoutChanged</c>. Smoke-test of the presentational write surface (FR-015).
/// </summary>
public class LayoutToolsPanelTests : BunitContext
{
    public LayoutToolsPanelTests()
    {
        Services.AddMudServices();
        Services.AddScoped<IFormLayoutAuthoringService, FormLayoutAuthoringService>();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private static BlueprintAction MakeAction() => new()
    {
        Id = 1,
        Title = "Apply",
        Sender = "applicant",
        DataSchemas = new List<JsonDocument>
        {
            JsonDocument.Parse(
                """
                {
                    "type":"object",
                    "properties":{
                        "name":{"type":"string"},
                        "email":{"type":"string"}
                    }
                }
                """),
        },
    };

    private IRenderedComponent<Bunit.Rendering.ContainerFragment> RenderPanel(
        BlueprintAction action, System.Action<BlueprintAction>? onChange = null)
        => Render(builder =>
        {
            builder.OpenComponent<MudPopoverProvider>(0);
            builder.CloseComponent();
            builder.OpenComponent<LayoutToolsPanel>(1);
            builder.AddAttribute(2, nameof(LayoutToolsPanel.Action), action);
            if (onChange is not null)
            {
                builder.AddAttribute(3, nameof(LayoutToolsPanel.OnLayoutChanged),
                    Microsoft.AspNetCore.Components.EventCallback.Factory.Create<BlueprintAction>(this, onChange));
            }
            builder.CloseComponent();
        });

    [Fact]
    public void Panel_Renders_AllSixToolButtons()
    {
        var cut = RenderPanel(MakeAction());

        cut.Find("[data-testid='layout-tools-panel']").Should().NotBeNull();
        cut.Find("[data-testid='layout-tool-split-pages']").Should().NotBeNull();
        cut.Find("[data-testid='layout-tool-section']").Should().NotBeNull();
        cut.Find("[data-testid='layout-tool-intro']").Should().NotBeNull();
        cut.Find("[data-testid='layout-tool-width']").Should().NotBeNull();
        cut.Find("[data-testid='layout-tool-review-page']").Should().NotBeNull();
        cut.Find("[data-testid='layout-tool-file']").Should().NotBeNull();
    }

    [Fact]
    public void SplitIntoPages_Click_WritesXPages_AndRaisesCallback()
    {
        BlueprintAction? captured = null;
        var action = MakeAction();
        var cut = RenderPanel(action, a => captured = a);

        cut.Find("[data-testid='layout-tool-split-pages']").Click();

        captured.Should().NotBeNull();
        captured!.DataSchemas!.First().RootElement.TryGetProperty("x-pages", out _).Should().BeTrue();
    }

    [Fact]
    public void GroupFields_Click_WritesXSections()
    {
        BlueprintAction? captured = null;
        var cut = RenderPanel(MakeAction(), a => captured = a);

        cut.Find("[data-testid='layout-tool-section']").Click();

        captured!.DataSchemas!.First().RootElement.TryGetProperty("x-sections", out _).Should().BeTrue();
    }

    [Fact]
    public void PersonaToggleRow_RendersOneButtonPerTopLevelField()
    {
        var cut = RenderPanel(MakeAction());

        cut.Find("[data-testid='persona-autofill-toggle-name']").Should().NotBeNull();
        cut.Find("[data-testid='persona-autofill-toggle-email']").Should().NotBeNull();
    }

    [Fact]
    public void PersonaToggle_Click_WritesXPersonaOnTargetedField()
    {
        BlueprintAction? captured = null;
        var cut = RenderPanel(MakeAction(), a => captured = a);

        cut.Find("[data-testid='persona-autofill-toggle-email']").Click();

        var email = captured!.DataSchemas!.First().RootElement.GetProperty("properties").GetProperty("email");
        email.GetProperty("x-persona").GetString().Should().Be("email");
    }

    [Fact]
    public void FileUploadButton_Click_SurfacesBehaviouralWarning_DoesNotMutateSchema()
    {
        BlueprintAction? captured = null;
        var action = MakeAction();
        var cut = RenderPanel(action, a => captured = a);

        cut.Find("[data-testid='layout-tool-file']").Click();

        captured.Should().BeNull("file upload is behavioural — the panel does NOT write x-file");
        cut.Markup.Should().Contain("behavioural change");
    }
}
