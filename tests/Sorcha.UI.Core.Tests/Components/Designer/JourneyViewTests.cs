// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using Sorcha.Blueprint.Models;
using Sorcha.Blueprint.Models.Credentials;
using Sorcha.UI.Core.Components.Designer;
using Sorcha.UI.Core.Services.Designer;
using Xunit;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;
using ActionModel = Sorcha.Blueprint.Models.Action;

namespace Sorcha.UI.Core.Tests.Components.Designer;

/// <summary>
/// bUnit tests for the Feature 142 <see cref="JourneyView"/> canvas (T016 / T013 journey portion):
/// one step card per Action in flow order, "Must prove" / "Issues" credential badges (FR-007),
/// re-render on Blueprint change, and manual step selection driving <see cref="DesignerContext"/>.
/// </summary>
public class JourneyViewTests : BunitContext
{
    public JourneyViewTests()
    {
        Services.AddMudServices();
        Services.AddScoped<DesignerContext>();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    /// <summary>
    /// A two-action blueprint: action 1 ("Apply") is the credential-gated starting action and
    /// requires an "IdentityCredential"; action 2 ("Assess") issues a "PermitCredential".
    /// </summary>
    private static BlueprintModel GatedAndIssuingBlueprint() => new()
    {
        Id = "bp-journey-1",
        Title = "Permit Application",
        Description = "A workflow for applying for a permit.",
        Version = 1,
        Participants =
        [
            new Participant { Id = "applicant", Name = "Applicant", WalletAddress = null },
            new Participant { Id = "assessor", Name = "Assessor", WalletAddress = "ws11qassessor" },
        ],
        Actions =
        [
            new ActionModel
            {
                Id = 1,
                Title = "Apply",
                Description = "Submit the application.",
                Sender = "applicant",
                IsStartingAction = true,
                Disclosures = [new Disclosure("assessor", ["/projectName"])],
                Routes = [new Route { Id = "r1", NextActionIds = [2], IsDefault = true }],
                CredentialRequirements = [new CredentialRequirement { Type = "IdentityCredential" }],
            },
            new ActionModel
            {
                Id = 2,
                Title = "Assess",
                Description = "Assess the application and issue the permit.",
                Sender = "assessor",
                IsStartingAction = false,
                Disclosures = [new Disclosure("applicant", ["/decision"])],
                CredentialIssuanceConfig = new CredentialIssuanceConfig { CredentialType = "PermitCredential" },
            },
        ],
    };

    /// <summary>A single-action blueprint with no credential affordances (different badge profile).</summary>
    private static BlueprintModel PlainBlueprint() => new()
    {
        Id = "bp-journey-2",
        Title = "Simple Notice",
        Description = "A single-step notice with no credentials.",
        Version = 1,
        Participants =
        [
            new Participant { Id = "citizen", Name = "Citizen", WalletAddress = null },
        ],
        Actions =
        [
            new ActionModel
            {
                Id = 1,
                Title = "Notify",
                Description = "Send a notice.",
                Sender = "citizen",
                IsStartingAction = true,
            },
        ],
    };

    [Fact]
    public void Render_NoBlueprint_RendersEmptyHintWithoutCrash()
    {
        var cut = Render<JourneyView>();

        cut.Find("[data-testid='journey-view']").Should().NotBeNull();
        cut.FindAll("[data-testid^='journey-step-']").Should().BeEmpty();
    }

    [Fact]
    public void Render_TwoActions_RendersOneStepCardPerActionInFlowOrder()
    {
        var ctx = Services.GetRequiredService<DesignerContext>();
        ctx.SetBlueprint(GatedAndIssuingBlueprint());

        var cut = Render<JourneyView>();

        cut.Find("[data-testid='journey-view']").Should().NotBeNull();

        var steps = cut.FindAll("[data-testid^='journey-step-']");
        steps.Should().HaveCount(2);
        steps[0].GetAttribute("data-testid").Should().Be("journey-step-1", "starting action comes first in flow order");
        steps[1].GetAttribute("data-testid").Should().Be("journey-step-2");
    }

    [Fact]
    public void Render_CredentialGatedStep_ShowsMustProveChip()
    {
        var ctx = Services.GetRequiredService<DesignerContext>();
        ctx.SetBlueprint(GatedAndIssuingBlueprint());

        var cut = Render<JourneyView>();

        var mustProve = cut.FindAll("[data-testid='journey-badge-mustprove']");
        mustProve.Should().HaveCount(1, "only the starting action has a credential requirement");
        mustProve[0].TextContent.Should().Contain("IdentityCredential");
    }

    [Fact]
    public void Render_IssuingStep_ShowsIssuesChip()
    {
        var ctx = Services.GetRequiredService<DesignerContext>();
        ctx.SetBlueprint(GatedAndIssuingBlueprint());

        var cut = Render<JourneyView>();

        var issues = cut.FindAll("[data-testid='journey-badge-issues']");
        issues.Should().HaveCount(1, "only the assess action issues a credential");
        issues[0].TextContent.Should().Contain("PermitCredential");
    }

    [Fact]
    public void SetBlueprint_DifferentBlueprint_ReRendersJourney()
    {
        var ctx = Services.GetRequiredService<DesignerContext>();
        ctx.SetBlueprint(GatedAndIssuingBlueprint());

        var cut = Render<JourneyView>();
        cut.FindAll("[data-testid^='journey-step-']").Should().HaveCount(2);
        cut.FindAll("[data-testid='journey-badge-mustprove']").Should().HaveCount(1);
        cut.FindAll("[data-testid='journey-badge-issues']").Should().HaveCount(1);

        // Swap to a plain single-step blueprint — badge counts must change.
        ctx.SetBlueprint(PlainBlueprint());

        cut.FindAll("[data-testid^='journey-step-']").Should().HaveCount(1, "the journey re-renders on Blueprint change");
        cut.FindAll("[data-testid='journey-badge-mustprove']").Should().BeEmpty();
        cut.FindAll("[data-testid='journey-badge-issues']").Should().BeEmpty();
    }

    [Fact]
    public void Click_Step_SetsActiveActionId()
    {
        var ctx = Services.GetRequiredService<DesignerContext>();
        ctx.SetBlueprint(GatedAndIssuingBlueprint());

        var cut = Render<JourneyView>();
        cut.Find("[data-testid='journey-step-2']").Click();

        ctx.ActiveActionId.Should().Be("2", "clicking a step selects its action in the designer context");
    }
}
