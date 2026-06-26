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
using Sorcha.Verifier.Engine;
using Sorcha.Verifier.Engine.Models;
using Sorcha.UI.Core.Components.Verify;
using Xunit;

namespace Sorcha.UI.Core.Tests.Verification;

/// <summary>
/// bUnit tests for <see cref="VerdictTrailPanel"/> (Feature 163, US3). Proves headline, disclosed/
/// withheld split, and the first three layers render without any network call; and that the layer-4
/// affordance invokes <see cref="IRegisterAnchorClient"/> and re-renders with the anchor result
/// (FR-014, R-006).
/// </summary>
public class VerdictTrailPanelTests : BunitContext
{
    private static readonly VerificationPreset AgePreset = new(
        "age-over-18", "Age over 18?", "Confirm age over 18",
        "https://sorcha.example/vc/citizen/v1",
        ["age_over_18"], [], ["age_over_18", "portrait"]);

    private readonly Mock<IRegisterAnchorClient> _mockAnchorClient = new();

    private static VerificationOutcome BuildOutcomeWithThreeLayers(bool accepted = true)
    {
        return new VerificationOutcome
        {
            Accepted = accepted,
            DisclosedClaims = new Dictionary<string, object?>
            {
                ["age_over_18"] = true,
                ["registerAnchor"] = "reg-001",
            },
            Errors = [],
            CompletedAt = DateTimeOffset.UtcNow,
            Layers =
            [
                new ValidationLayerResult { Layer = ValidationLayer.LivePresentation, Status = LayerStatus.Pass, Headline = "Valid KB-JWT" },
                new ValidationLayerResult { Layer = ValidationLayer.IssuerSignature, Status = LayerStatus.Pass, Headline = "Verified" },
                new ValidationLayerResult { Layer = ValidationLayer.Revocation, Status = LayerStatus.Pass, Headline = "Not revoked" },
            ],
        };
    }

    public VerdictTrailPanelTests()
    {
        _mockAnchorClient
            .Setup(c => c.CheckAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RegisterAnchorResult
            {
                Anchored = true,
                Status = LayerStatus.Pass,
                Note = "Anchored in docket #42.",
            });

        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;

        Services.AddSingleton<IRegisterAnchorClient>(_mockAnchorClient.Object);

        var config = new ConfigurationBuilder().Build();
        Services.AddSingleton<IConfiguration>(config);
        Services.AddSorchaUserComponents(config);
    }

    [Fact]
    public void VerdictTrailPanel_ThreeOfflineLayers_RendersHeadlineAndLayers()
    {
        // US3 scenario 1 — headline, disclosed/withheld split, and all three offline layers render
        // with no network call on first display.
        var verdict = VerdictViewModel.From(AgePreset, BuildOutcomeWithThreeLayers());
        var cut = Render<VerdictTrailPanel>(p => p.Add(x => x.Verdict, verdict));

        cut.Find("[data-testid='verdict-headline']").TextContent
            .Should().Contain("Over 18");

        cut.Find("[data-testid='trail-LivePresentation']").Should().NotBeNull();
        cut.Find("[data-testid='trail-IssuerSignature']").Should().NotBeNull();
        cut.Find("[data-testid='trail-Revocation']").Should().NotBeNull();

        // No anchor client calls on initial render
        _mockAnchorClient.Verify(
            c => c.CheckAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public void VerdictTrailPanel_DisclosedAndWithheld_Renders()
    {
        // US3 scenario 2 (partial) — disclosed claim and withheld indicator render.
        var verdict = VerdictViewModel.From(AgePreset, BuildOutcomeWithThreeLayers());
        var cut = Render<VerdictTrailPanel>(p => p.Add(x => x.Verdict, verdict));

        cut.Find("[data-testid='trail-disclosure']").Should().NotBeNull();
        var disclosedClaims = cut.FindAll("[data-testid='disclosed-claim']");
        disclosedClaims.Should().NotBeEmpty();
    }

    [Fact]
    public async Task VerdictTrailPanel_AnchorButton_CallsAnchorClientAndReRendersWithLayer4()
    {
        // US3 scenario 3 — trigger the layer-4 affordance; IRegisterAnchorClient.CheckAsync is called
        // and the trail re-renders with the RegisterAnchor layer.
        var verdict = VerdictViewModel.From(AgePreset, BuildOutcomeWithThreeLayers());
        var cut = Render<VerdictTrailPanel>(p => p.Add(x => x.Verdict, verdict));

        // Verify-anchor button must be present (RegisterAnchorId = "reg-001" from disclosed claims)
        var anchorButton = cut.Find("[data-testid='verify-anchor']");
        anchorButton.Should().NotBeNull();

        await anchorButton.ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
        await Task.Delay(100); // let async click handler complete

        _mockAnchorClient.Verify(
            c => c.CheckAsync("reg-001", It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);

        // RegisterAnchor layer must now be in the Verdict.Layers list
        verdict.Layers.Should().Contain(l => l.Layer == ValidationLayer.RegisterAnchor);
    }
}
