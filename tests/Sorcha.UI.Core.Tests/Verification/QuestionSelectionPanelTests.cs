// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Bunit;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using MudBlazor.Services;
using Sorcha.UI.Components.User.Extensions;
using Sorcha.UI.Components.User.Models.Verification;
using Sorcha.UI.Components.User.Services.Verification;
using Microsoft.AspNetCore.Components;
using Sorcha.UI.Core.Components.Verify;
using Xunit;

namespace Sorcha.UI.Core.Tests.Verification;

/// <summary>
/// bUnit tests for <see cref="QuestionSelectionPanel"/> (Feature 163, US1). Proves presets render
/// as selectable options, the custom affordance renders, and selecting a preset raises
/// <c>OnQuestionSelected</c> with the correct <see cref="VerificationPreset"/> (FR-014, R-006).
/// </summary>
public class QuestionSelectionPanelTests : BunitContext
{
    private static readonly VerificationPreset Preset1 = new("p1", "Age over 18?", "Confirm age", "https://vct/1", ["age_over_18"], [], []);
    private static readonly VerificationPreset Preset2 = new("p2", "Confirm identity", "Confirm identity", "https://vct/2", ["name"], [], []);
    private static readonly VerificationPreset Preset3 = new("p3", "Driving licence", "Check licence", "https://vct/3", ["licence"], [], []);

    private readonly Mock<IVerificationPresetCatalogue> _mockCatalogue = new();

    public QuestionSelectionPanelTests()
    {
        _mockCatalogue.Setup(c => c.GetAll()).Returns([Preset1, Preset2, Preset3]);
        _mockCatalogue.Setup(c => c.BuildCustom(
            It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<IReadOnlyList<string>>(), It.IsAny<IReadOnlyList<string>>()))
            .Returns(new VerificationPreset("custom", "Custom", "Custom", "", [], [], []));

        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;

        // Host override wins — register before AddSorchaUserComponents (R-006)
        Services.AddSingleton<IVerificationPresetCatalogue>(_mockCatalogue.Object);
        var config = new ConfigurationBuilder().Build();
        Services.AddSingleton<IConfiguration>(config);
        Services.AddSorchaUserComponents(config);
    }

    [Fact]
    public void QuestionSelectionPanel_ThreePresets_RenderThreeOptions()
    {
        // US1 scenario 1 — three presets render as three selectable options.
        var cut = Render<QuestionSelectionPanel>();

        cut.FindAll("[data-testid^='preset-']").Should().HaveCount(3);
    }

    [Fact]
    public void QuestionSelectionPanel_CustomAffordance_Renders()
    {
        // US1 scenario 2 — the custom-question affordance renders.
        var cut = Render<QuestionSelectionPanel>();

        cut.Find("[data-testid='custom-question']").Should().NotBeNull();
    }

    [Fact]
    public async Task QuestionSelectionPanel_SelectPreset_RaisesOnQuestionSelectedWithCorrectPreset()
    {
        // US1 scenario 3 — selecting a preset raises OnQuestionSelected with the chosen VerificationPreset.
        VerificationPreset? raised = null;
        var cut = Render<QuestionSelectionPanel>(p =>
            p.Add(x => x.OnQuestionSelected, EventCallback.Factory.Create<VerificationPreset>(this, v => raised = v)));

        // Click the first preset button
        var firstPreset = cut.Find("[data-testid='preset-p1']");
        await firstPreset.ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        raised.Should().NotBeNull();
        raised!.Key.Should().Be("p1");
        raised.Label.Should().Be("Age over 18?");
    }
}
