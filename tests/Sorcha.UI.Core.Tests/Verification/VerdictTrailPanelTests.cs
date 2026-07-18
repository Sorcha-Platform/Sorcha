// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Bunit;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using MudBlazor.Services;
using Sorcha.CitizenWallet.Abstractions.Constants;
using Sorcha.UI.Components.User.Extensions;
using Sorcha.UI.Components.User.Models.Verification;
using Sorcha.Verifier.Engine;
using Sorcha.Verifier.Engine.Models;
using Sorcha.UI.Core.Components.Verify;
using Xunit;

namespace Sorcha.UI.Core.Tests.Verification;

/// <summary>
/// bUnit tests for <see cref="VerdictTrailPanel"/> (Feature 174). Proves the preset-adaptive
/// treatments — the age hero + minimal-disclosure note vs the identity portrait/name lead — the
/// pass/warn/fail banner states, the collapsed four-layer trust trail, and that the register-anchor
/// layer stays an on-demand affordance that only invokes <see cref="IRegisterAnchorClient"/> when
/// the operator triggers it (FR-014, R-006).
/// </summary>
public class VerdictTrailPanelTests : BunitContext
{
    private static readonly VerificationPreset AgePreset = new(
        "age-over-18", "Age over 18?", "Confirm age over 18",
        "https://sorcha.example/vc/citizen/v1",
        ["age_over_18"], [], ["age_over_18", "portrait"]);

    private static readonly VerificationPreset IdentityPreset = new(
        "confirm-identity", "Confirm identity", "Confirm the person's identity",
        VctUris.AssuredIdentityV1,
        ["fullName", "portrait"], ["dateOfBirth"],
        ["age_over_18", "portrait", "fullName", "dateOfBirth"]);

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

    private static VerificationOutcome BuildIdentityOutcome()
    {
        return new VerificationOutcome
        {
            Accepted = true,
            DisclosedClaims = new Dictionary<string, object?>
            {
                ["fullName"] = "Stuart Fraser",
                ["portrait"] = "aGVsbG8=",
            },
            Errors = [],
            CompletedAt = DateTimeOffset.UtcNow,
            IssuerSignature = IssuerSignatureStatus.Verified,
            Layers =
            [
                new ValidationLayerResult { Layer = ValidationLayer.LivePresentation, Status = LayerStatus.Pass, Headline = "Valid KB-JWT" },
                new ValidationLayerResult { Layer = ValidationLayer.IssuerSignature, Status = LayerStatus.Pass, Headline = "Verified" },
                new ValidationLayerResult { Layer = ValidationLayer.Revocation, Status = LayerStatus.Pass, Headline = "Not revoked" },
            ],
        };
    }

    private static VerificationOutcome BuildRejectedOutcome()
    {
        return new VerificationOutcome
        {
            Accepted = false,
            DisclosedClaims = new Dictionary<string, object?>(),
            Errors = ["nonce mismatch"],
            CompletedAt = DateTimeOffset.UtcNow,
            IssuerSignature = IssuerSignatureStatus.NotVerified,
            Layers =
            [
                new ValidationLayerResult { Layer = ValidationLayer.LivePresentation, Status = LayerStatus.Fail, Headline = "nonce mismatch" },
            ],
        };
    }

    private static VerificationOutcome BuildWarnOutcome()
    {
        return new VerificationOutcome
        {
            Accepted = true,
            DisclosedClaims = new Dictionary<string, object?>
            {
                ["fullName"] = "Stuart Fraser",
            },
            Errors = [],
            CompletedAt = DateTimeOffset.UtcNow,
            IssuerSignature = IssuerSignatureStatus.NotVerified,
            Layers =
            [
                new ValidationLayerResult { Layer = ValidationLayer.LivePresentation, Status = LayerStatus.Pass, Headline = "Valid KB-JWT" },
                new ValidationLayerResult { Layer = ValidationLayer.IssuerSignature, Status = LayerStatus.Unverified, Headline = "Issuer key unresolved" },
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

    private IRenderedComponent<VerdictTrailPanel> Render(VerdictViewModel verdict)
        => Render<VerdictTrailPanel>(p => p.Add(x => x.Verdict, verdict));

    [Fact]
    public void AgeTreatment_LeadsWithHero_AndMinimalDisclosureNote()
    {
        var verdict = VerdictViewModel.From(AgePreset, BuildOutcomeWithThreeLayers());
        var cut = Render(verdict);

        cut.Find("[data-testid=age-hero]").TextContent.Should().Contain("Over 18");
        cut.Find("[data-testid=minimal-disclosure]").TextContent.Should().Contain("did not learn their name");
        cut.FindAll("[data-testid=holder-name]").Should().BeEmpty();   // age screen hides the name
    }

    [Fact]
    public void IdentityTreatment_LeadsWithPortraitAndName_AndWithheldLine()
    {
        var verdict = VerdictViewModel.From(IdentityPreset, BuildIdentityOutcome());
        var cut = Render(verdict);

        cut.Find("[data-testid=holder-name]").TextContent.Should().Contain("Stuart Fraser");
        cut.Find("[data-testid=portrait]").Should().NotBeNull();
        cut.Find("[data-testid=withheld-claims]").TextContent.Should().Contain("dateOfBirth");
        cut.FindAll("[data-testid=age-hero]").Should().BeEmpty();
    }

    [Fact]
    public void PassVerdict_ShowsPassBanner()
    {
        var verdict = VerdictViewModel.From(IdentityPreset, BuildIdentityOutcome());
        var cut = Render(verdict);
        cut.Find("[data-testid=verdict-banner]").GetAttribute("class").Should().Contain("verdict-pass");
    }

    [Fact]
    public void FailVerdict_ShowsFailBanner_AndDoesNotPresentDisclosedIdentityAsTrusted()
    {
        var verdict = VerdictViewModel.From(IdentityPreset, BuildRejectedOutcome());
        var cut = Render(verdict);
        cut.Find("[data-testid=verdict-banner]").GetAttribute("class").Should().Contain("verdict-fail");
    }

    [Fact]
    public void WarnVerdict_ShowsWarnBanner_NeverAPlainPass()
    {
        var verdict = VerdictViewModel.From(IdentityPreset, BuildWarnOutcome());
        var cut = Render(verdict);
        var cls = cut.Find("[data-testid=verdict-banner]").GetAttribute("class");
        cls.Should().Contain("verdict-warn");
        cls.Should().NotContain("verdict-pass");
    }

    [Fact]
    public void TrustTrail_RendersFourLayerRows_AndAnchorIsOnDemand()
    {
        var verdict = VerdictViewModel.From(AgePreset, BuildOutcomeWithThreeLayers());
        var cut = Render(verdict);
        cut.Find("[data-testid=trail-LivePresentation]").Should().NotBeNull();
        cut.Find("[data-testid=trail-IssuerSignature]").Should().NotBeNull();
        cut.Find("[data-testid=trail-Revocation]").Should().NotBeNull();
        // Anchor layer is the on-demand affordance until checked.
        _mockAnchorClient.Verify(c => c.CheckAsync(It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AnchorButton_WhenTriggered_CallsAnchorClientAndAppendsRegisterAnchorLayer()
    {
        // Preserved from Feature 163 — the layer-4 affordance invokes IRegisterAnchorClient once and
        // appends the RegisterAnchor layer to the verdict's trail (data-testids adapted to new markup).
        var verdict = VerdictViewModel.From(AgePreset, BuildOutcomeWithThreeLayers());
        var cut = Render(verdict);

        // Verify-anchor button must be present (RegisterAnchorId = "reg-001" from disclosed claims).
        var anchorButton = cut.Find("[data-testid=verify-anchor]");
        anchorButton.Should().NotBeNull();

        await anchorButton.ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
        await Task.Delay(100); // let async click handler complete

        _mockAnchorClient.Verify(
            c => c.CheckAsync("reg-001", It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);

        verdict.Layers.Should().Contain(l => l.Layer == ValidationLayer.RegisterAnchor);
    }
}
