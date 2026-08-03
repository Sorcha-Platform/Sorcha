// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Blueprint.Models;
using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Services.Implementation;

using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;
using BlueprintAction = Sorcha.Blueprint.Models.Action;

namespace Sorcha.Blueprint.Service.Tests.Projection;

/// <summary>
/// Feature 186 — the citizen-facing projection. The load-bearing case is outcome derivation: a
/// refusal is expressed as a <i>route</i> carrying an <c>x-decision-notice</c>, not as a distinct
/// instance state, so an application refused on its final step folds to
/// <see cref="InstanceState.Completed"/> and is otherwise indistinguishable from an approved one.
/// Skip the derivation and a refused citizen is told their application "completed".
/// </summary>
public class MyApplicationProjectorTests
{
    private const string CitizenWallet = "ws-citizen";
    private const string AnalystWallet = "ws-analyst";

    private static readonly string[] CallerIsCitizen = [CitizenWallet];

    /// <summary>
    /// Three actions: citizen submits (1) → analyst decides (2) → citizen collects (3). Action 2
    /// carries two routes, one of which refuses with a reason catalogue.
    /// </summary>
    private static BlueprintModel Blueprint(string? severity = "Warning", bool withNotice = true) => new()
    {
        Id = "bp-1",
        Title = "Assured Identity",
        Participants =
        [
            new Participant { Id = "citizen", Name = "Citizen" },
            new Participant { Id = "analyst", Name = "Analyst", WalletAddress = AnalystWallet },
        ],
        Actions =
        [
            new BlueprintAction { Id = 1, Title = "Submit your details", Sender = "citizen", IsStartingAction = true },
            new BlueprintAction
            {
                Id = 2,
                Title = "Identity check",
                Sender = "analyst",
                Routes =
                [
                    new Route { Id = "route-approve", NextActionIds = [3] },
                    new Route
                    {
                        Id = "route-refuse",
                        NextActionIds = [],
                        DecisionNotice = withNotice
                            ? new DecisionNotice
                            {
                                RecipientParticipantId = "citizen",
                                Title = "We could not assure your identity",
                                Severity = severity,
                                ReasonCodeField = "/reasonCode",
                                Reasons = new Dictionary<string, string>
                                {
                                    ["DOC_UNREADABLE"] = "The document you provided could not be read clearly.",
                                },
                                FallbackMessage = "We were unable to proceed with your application.",
                            }
                            : null,
                    },
                ],
            },
            new BlueprintAction { Id = 3, Title = "Collect your credential", Sender = "citizen" },
        ],
    };

    private static Instance Instance(
        InstanceState state = InstanceState.Active,
        int[]? current = null,
        string? routeId = null,
        string? reasonCode = null,
        Dictionary<string, string>? metadata = null) => new()
        {
            Id = "inst-1",
            BlueprintId = "bp-1",
            BlueprintVersion = 1,
            RegisterId = "reg-1",
            TenantId = "tenant-1",
            State = state,
            CurrentActionIds = [.. current ?? [2]],
            ParticipantWallets = new Dictionary<string, string>
            {
                ["citizen"] = CitizenWallet,
                ["analyst"] = AnalystWallet,
            },
            DecisionRouteId = routeId,
            DecisionReasonCode = reasonCode,
            Metadata = metadata ?? new Dictionary<string, string>(),
        };

    // ---- Outcome derivation (data-model.md §5) ----

    [Fact]
    public void NoRecordedDecision_OutcomeIsTheStateName_AndNoReason()
    {
        var row = MyApplicationProjector.Project(Instance(), Blueprint(), CallerIsCitizen);

        row.State.Should().Be("Active");
        row.Outcome.Should().Be("Active");
        row.DecisionReason.Should().BeNull();
        row.DecisionTitle.Should().BeNull();
    }

    [Fact]
    public void AdverseDecisionOnATerminalApplication_OutcomeIsNotApproved()
    {
        // The whole point. State says Completed because nothing is left to do; the citizen must not
        // be told that.
        var row = MyApplicationProjector.Project(
            Instance(InstanceState.Completed, current: [], routeId: "route-refuse", reasonCode: "DOC_UNREADABLE"),
            Blueprint(),
            CallerIsCitizen);

        row.State.Should().Be("Completed");
        row.Outcome.Should().Be("NotApproved");
        row.DecisionReason.Should().Be("The document you provided could not be read clearly.");
        row.DecisionTitle.Should().Be("We could not assure your identity");
        row.DecisionSeverity.Should().Be("Warning");
    }

    [Fact]
    public void BenignSeverityNotice_KeepsTheStateName_ButStillCarriesTheMessage()
    {
        // A notice can carry good news. Severity is the discriminator the blueprint author controls,
        // so a Success notice must not be reported as a refusal.
        var row = MyApplicationProjector.Project(
            Instance(InstanceState.Completed, current: [], routeId: "route-refuse", reasonCode: "DOC_UNREADABLE"),
            Blueprint(severity: "Success"),
            CallerIsCitizen);

        row.Outcome.Should().Be("Completed");
        row.DecisionReason.Should().Be("The document you provided could not be read clearly.");
    }

    [Fact]
    public void RouteWithNoDecisionNotice_YieldsNoReasonAtAll()
    {
        // FR-013: no notice means no reason, and never invented wording.
        var row = MyApplicationProjector.Project(
            Instance(InstanceState.Completed, current: [], routeId: "route-refuse", reasonCode: "DOC_UNREADABLE"),
            Blueprint(withNotice: false),
            CallerIsCitizen);

        row.Outcome.Should().Be("Completed");
        row.DecisionReason.Should().BeNull();
        row.DecisionTitle.Should().BeNull();
    }

    [Fact]
    public void UnknownReasonCode_FallsBackToTheNoticesOwnFallback_NeverTheCode()
    {
        var row = MyApplicationProjector.Project(
            Instance(InstanceState.Completed, current: [], routeId: "route-refuse", reasonCode: "NOT_IN_CATALOGUE"),
            Blueprint(),
            CallerIsCitizen);

        row.DecisionReason.Should().Be("We were unable to proceed with your application.");
        row.DecisionReason.Should().NotContain("NOT_IN_CATALOGUE", "the raw code is never citizen-facing (FR-014)");
    }

    [Fact]
    public void UnresolvableBlueprint_StillProjectsARow()
    {
        // A blueprint not replicated on this node must degrade, not omit the application or error.
        var row = MyApplicationProjector.Project(
            Instance(InstanceState.Completed, current: [], routeId: "route-refuse", reasonCode: "DOC_UNREADABLE"),
            blueprint: null,
            CallerIsCitizen);

        row.BlueprintTitle.Should().Be("bp-1", "falls back to the id rather than rendering blank");
        row.Outcome.Should().Be("Completed");
        row.DecisionReason.Should().BeNull();
    }

    [Fact]
    public void RouteIdNotFoundInTheBlueprint_DegradesQuietly()
    {
        var row = MyApplicationProjector.Project(
            Instance(InstanceState.Completed, current: [], routeId: "route-that-no-longer-exists"),
            Blueprint(),
            CallerIsCitizen);

        row.Outcome.Should().Be("Completed");
        row.DecisionReason.Should().BeNull();
    }

    // ---- Titles, references, steps ----

    [Fact]
    public void BlueprintTitleComesFromInstanceMetadataFirst()
    {
        var row = MyApplicationProjector.Project(
            Instance(metadata: new Dictionary<string, string> { ["BlueprintTitle"] = "Cached Title" }),
            Blueprint(), CallerIsCitizen);

        row.BlueprintTitle.Should().Be("Cached Title");
    }

    [Fact]
    public void BlueprintTitleFallsBackToTheBlueprint_ForProjectorCreatedInstances()
    {
        // Instances created by the ledger projector never populate Metadata["BlueprintTitle"] —
        // only the imperative creation path does. This fallback is load-bearing, not defensive.
        var row = MyApplicationProjector.Project(Instance(), Blueprint(), CallerIsCitizen);

        row.BlueprintTitle.Should().Be("Assured Identity");
    }

    [Fact]
    public void InstanceReferenceIsSurfacedWhenPresent_AndOmittedBeforeTheFirstActionSeals()
    {
        var withRef = MyApplicationProjector.Project(
            Instance(metadata: new Dictionary<string, string> { ["instanceReference"] = "AI-CYB-14-A7K3" }),
            Blueprint(), CallerIsCitizen);
        withRef.InstanceReference.Should().Be("AI-CYB-14-A7K3");

        MyApplicationProjector.Project(Instance(), Blueprint(), CallerIsCitizen)
            .InstanceReference.Should().BeNull();
    }

    [Fact]
    public void StepPositionIsOneBasedAndCountsEveryAction()
    {
        var row = MyApplicationProjector.Project(Instance(current: [2]), Blueprint(), CallerIsCitizen);

        row.CurrentActionId.Should().Be(2);
        row.CurrentActionTitle.Should().Be("Identity check");
        row.StepNumber.Should().Be(2);
        row.TotalSteps.Should().Be(3);
    }

    [Fact]
    public void TerminalApplicationHasNoCurrentStep()
    {
        var row = MyApplicationProjector.Project(
            Instance(InstanceState.Completed, current: []), Blueprint(), CallerIsCitizen);

        row.CurrentActionId.Should().BeNull();
        row.StepNumber.Should().BeNull();
        row.TotalSteps.Should().Be(3, "the service still has a known length");
    }

    [Fact]
    public void DetailMarksEveryStepAgainstTheApplication()
    {
        var detail = MyApplicationProjector.ProjectDetail(
            Instance(current: [2]), Blueprint(), CallerIsCitizen);

        detail.Steps.Select(s => s.Status).Should().Equal("Completed", "Current", "Upcoming");
        detail.Steps.Select(s => s.ActionId).Should().Equal(1, 2, 3);
    }

    // ---- needsYou (this is what dissolves #1268) ----

    [Fact]
    public void NeedsYou_FalseWhenTheCurrentStepBelongsToSomeoneElse()
    {
        // The citizen has just submitted; action 2 is the analyst's. Offering the citizen an action
        // here is exactly the stale "Take Action" button #1268 recorded.
        MyApplicationProjector.Project(Instance(current: [2]), Blueprint(), CallerIsCitizen)
            .NeedsYou.Should().BeFalse();
    }

    [Fact]
    public void NeedsYou_TrueWhenTheCurrentStepIsTheCallers()
    {
        MyApplicationProjector.Project(Instance(current: [3]), Blueprint(), CallerIsCitizen)
            .NeedsYou.Should().BeTrue();
    }

    [Fact]
    public void NeedsYou_FalseOnATerminalApplication_EvenIfTheLastStepWasTheCallers()
    {
        MyApplicationProjector.Project(
                Instance(InstanceState.Completed, current: []), Blueprint(), CallerIsCitizen)
            .NeedsYou.Should().BeFalse();
    }

    [Fact]
    public void NeedsYou_FailsClosedWhenTheBlueprintIsUnresolvable()
    {
        MyApplicationProjector.Project(Instance(current: [3]), blueprint: null, CallerIsCitizen)
            .NeedsYou.Should().BeFalse();
    }

    // ---- what must never reach the wire ----

    [Fact]
    public void TheReasonCodeIsNeverProjected()
    {
        // FR-014. Asserted over the serialised row, because the guarantee is about the wire, not the
        // record's shape — a future member could reintroduce it without changing any property read
        // here.
        var row = MyApplicationProjector.Project(
            Instance(InstanceState.Completed, current: [], routeId: "route-refuse", reasonCode: "DOC_UNREADABLE"),
            Blueprint(), CallerIsCitizen);

        var json = System.Text.Json.JsonSerializer.Serialize(row);
        json.Should().NotContain("DOC_UNREADABLE");
        json.Should().NotContain(CitizenWallet, "participant wallet addresses are not citizen-facing either");
    }
}
