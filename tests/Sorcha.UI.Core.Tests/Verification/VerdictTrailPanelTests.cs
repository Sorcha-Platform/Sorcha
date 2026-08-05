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
using Sorcha.Verification.Abstractions;

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

    /// <summary>
    /// Issue #1180 — an outcome carrying a STRUCTURED disclosed claim (a nested <c>address</c>
    /// object, boxed as a <see cref="System.Text.Json.JsonElement"/> exactly as it arrives off the
    /// wire), plus a raw <c>_sd</c> digest array that must never be shown.
    /// </summary>
    private static VerificationOutcome BuildOutcomeWithStructuredClaim()
    {
        using var doc = System.Text.Json.JsonDocument.Parse(
            """
            {
              "address": { "street": "6/2 Warrender Park Terrace", "city": "Edinburgh", "postcode": "EH9 1JA" },
              "sdDigests": { "_sd": ["zSH_kfTeW2Mlc", "qX2vBn8Lp0Ra"] }
            }
            """);

        return new VerificationOutcome
        {
            Accepted = true,
            DisclosedClaims = new Dictionary<string, object?>
            {
                ["fullName"] = "Stuart Fraser",
                ["address"] = doc.RootElement.GetProperty("address").Clone(),
                ["sdDigests"] = doc.RootElement.GetProperty("sdDigests").Clone(),
            },
            Errors = [],
            CompletedAt = DateTimeOffset.UtcNow,
            IssuerSignature = IssuerSignatureStatus.Verified,
            Layers =
            [
                new ValidationLayerResult { Layer = ValidationLayer.LivePresentation, Status = VerificationStatus.Verified, Headline = "Valid KB-JWT" },
                new ValidationLayerResult { Layer = ValidationLayer.IssuerSignature, Status = VerificationStatus.Verified, Headline = "Verified" },
                new ValidationLayerResult { Layer = ValidationLayer.Revocation, Status = VerificationStatus.Verified, Headline = "Not revoked" },
            ],
        };
    }

    /// <summary>
    /// Issue #1180 — a disclosed structured claim must RENDER (formatted), not be silently dropped.
    ///
    /// <para>The original defect was raw <c>{"_sd":[…]}</c> JSON reaching the operator's verdict
    /// panel. That was mitigated by skipping any value whose text starts with <c>{</c> or <c>[</c> —
    /// which stopped the raw JSON but also means a genuinely disclosed nested claim, such as the
    /// address a citizen chose to share, VANISHES from the verdict with no trace. A verifier reading
    /// the panel cannot tell "not disclosed" from "disclosed but undisplayable".</para>
    /// </summary>
    [Fact]
    public void StructuredDisclosedClaim_IsRenderedFormatted_NotDropped()
    {
        var vm = VerdictViewModel.From(IdentityPreset, BuildOutcomeWithStructuredClaim());

        vm.Disclosed.Should().ContainSingle(p => p.Key == "address",
            "a claim the citizen chose to disclose must appear on the verdict, not silently disappear");

        var address = vm.Disclosed.Single(p => p.Key == "address").Value;
        address.Should().Contain("Edinburgh").And.Contain("EH9 1JA",
            "the value must be rendered for a human, via the shared claim formatter");
        address.Should().NotContain("{").And.NotContain("\"street\"",
            "it must be formatted, never dumped as raw JSON");
    }

    /// <summary>
    /// Issue #1180 — the protocol plumbing must still never reach the panel. This is the half the
    /// skip-filter got right, and it must survive routing through the shared formatter.
    /// </summary>
    [Fact]
    public void RawSdDigestArray_NeverReachesTheVerdictPanel()
    {
        var vm = VerdictViewModel.From(IdentityPreset, BuildOutcomeWithStructuredClaim());

        var rendered = string.Join(" | ", vm.Disclosed.Select(p => $"{p.Key}={p.Value}"));
        rendered.Should().NotContain("zSH_kfTeW2Mlc").And.NotContain("_sd",
            "selective-disclosure digests are credential plumbing and must never be shown");
    }

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
                new ValidationLayerResult { Layer = ValidationLayer.LivePresentation, Status = VerificationStatus.Verified, Headline = "Valid KB-JWT" },
                new ValidationLayerResult { Layer = ValidationLayer.IssuerSignature, Status = VerificationStatus.Verified, Headline = "Verified" },
                new ValidationLayerResult { Layer = ValidationLayer.Revocation, Status = VerificationStatus.Verified, Headline = "Not revoked" },
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
                new ValidationLayerResult { Layer = ValidationLayer.LivePresentation, Status = VerificationStatus.Verified, Headline = "Valid KB-JWT" },
                new ValidationLayerResult { Layer = ValidationLayer.IssuerSignature, Status = VerificationStatus.Verified, Headline = "Verified" },
                new ValidationLayerResult { Layer = ValidationLayer.Revocation, Status = VerificationStatus.Verified, Headline = "Not revoked" },
            ],
        };
    }

    private static VerificationOutcome BuildRejectedOutcomeWithClaims()
    {
        // A rejected outcome that STILL carries disclosed claims — the panel must not present these as
        // trusted identity on a fail (design §2.3).
        return new VerificationOutcome
        {
            Accepted = false,
            DisclosedClaims = new Dictionary<string, object?>
            {
                ["fullName"] = "Stuart Fraser",
                ["portrait"] = "aGVsbG8=",
            },
            Errors = ["nonce mismatch"],
            CompletedAt = DateTimeOffset.UtcNow,
            IssuerSignature = IssuerSignatureStatus.NotVerified,
            Layers =
            [
                new ValidationLayerResult { Layer = ValidationLayer.LivePresentation, Status = VerificationStatus.Failed, Headline = "nonce mismatch" },
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
                new ValidationLayerResult { Layer = ValidationLayer.LivePresentation, Status = VerificationStatus.Verified, Headline = "Valid KB-JWT" },
                new ValidationLayerResult { Layer = ValidationLayer.IssuerSignature, Status = VerificationStatus.Unverified, Headline = "Issuer key unresolved" },
                new ValidationLayerResult { Layer = ValidationLayer.Revocation, Status = VerificationStatus.Verified, Headline = "Not revoked" },
            ],
        };
    }

    private static VerificationOutcome BuildAgeRejectedOutcomeWithClaims()
    {
        // A rejected age outcome that STILL carries a positive age answer + portrait — the hero must not
        // present it as confirmed (§2.3), and must name the reason.
        return new VerificationOutcome
        {
            Accepted = false,
            DisclosedClaims = new Dictionary<string, object?>
            {
                ["age_over_18"] = true,
                ["portrait"] = "aGVsbG8=",
            },
            Errors = ["nonce mismatch"],
            CompletedAt = DateTimeOffset.UtcNow,
            IssuerSignature = IssuerSignatureStatus.NotVerified,
            Layers =
            [
                new ValidationLayerResult { Layer = ValidationLayer.LivePresentation, Status = VerificationStatus.Failed, Headline = "nonce mismatch" },
            ],
        };
    }

    private static VerificationOutcome BuildAgeWarnOutcome()
    {
        // Accepted but the issuer signature could not be checked — a warn. The hero must name the
        // reduced assurance (SC-4), not read as a plain pass.
        return new VerificationOutcome
        {
            Accepted = true,
            DisclosedClaims = new Dictionary<string, object?>
            {
                ["age_over_18"] = true,
                ["portrait"] = "aGVsbG8=",
            },
            Errors = [],
            CompletedAt = DateTimeOffset.UtcNow,
            IssuerSignature = IssuerSignatureStatus.NotVerified,
            Layers =
            [
                new ValidationLayerResult { Layer = ValidationLayer.LivePresentation, Status = VerificationStatus.Verified, Headline = "Valid KB-JWT" },
                new ValidationLayerResult { Layer = ValidationLayer.IssuerSignature, Status = VerificationStatus.Unverified, Headline = "Issuer key unresolved" },
                new ValidationLayerResult { Layer = ValidationLayer.Revocation, Status = VerificationStatus.Verified, Headline = "Not revoked" },
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
                Status = VerificationStatus.Verified,
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
    public void AgeTreatment_Fail_DoesNotPresentConfirmedAnswer_AndNamesReason()
    {
        // §2.3 — a rejected age outcome carrying age_over_18 == true must NOT read as confirmed.
        var verdict = VerdictViewModel.From(AgePreset, BuildAgeRejectedOutcomeWithClaims());
        var cut = Render(verdict);

        var hero = cut.Find("[data-testid=age-hero]");
        hero.GetAttribute("class").Should().Contain("verdict-fail");
        hero.TextContent.Should().NotContain("confirmed");
        hero.TextContent.Should().NotContain("proved the threshold");
        hero.TextContent.Should().Contain("Not verified");
        hero.TextContent.Should().Contain("nonce mismatch");
        cut.FindAll("[data-testid=portrait]").Should().BeEmpty();   // identity suppressed on fail
    }

    [Fact]
    public void AgeTreatment_Warn_ShowsReducedAssuranceText()
    {
        // SC-4 — a warn must be named, not merely coloured amber, so it can't read as a plain pass.
        var verdict = VerdictViewModel.From(AgePreset, BuildAgeWarnOutcome());
        var cut = Render(verdict);

        var hero = cut.Find("[data-testid=age-hero]");
        hero.GetAttribute("class").Should().Contain("verdict-warn");
        hero.TextContent.Should().Contain("reduced assurance");
        hero.TextContent.Should().NotContain("proved the threshold");
    }

    [Fact]
    public void IdentityTreatment_LeadsWithPortraitAndName_AndWithheldLine()
    {
        var verdict = VerdictViewModel.From(IdentityPreset, BuildIdentityOutcome());
        var cut = Render(verdict);

        cut.Find("[data-testid=holder-name]").TextContent.Should().Contain("Stuart Fraser");
        cut.Find("[data-testid=portrait]").Should().NotBeNull();
        cut.Find("[data-testid=withheld-claims]").TextContent.Should().Contain("Date of birth");
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
        // Design §2.3 — even when a rejected presentation carries disclosed claims, the panel must
        // NOT surface the portrait, holder name, or disclosure card as trusted identity.
        var verdict = VerdictViewModel.From(IdentityPreset, BuildRejectedOutcomeWithClaims());
        var cut = Render(verdict);

        cut.Find("[data-testid=verdict-banner]").GetAttribute("class").Should().Contain("verdict-fail");
        cut.FindAll("[data-testid=holder-name]").Should().BeEmpty();
        cut.FindAll("[data-testid=portrait]").Should().BeEmpty();
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
