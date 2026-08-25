// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Sorcha.Blueprint.Fluent;
using Sorcha.Blueprint.Models;
using Sorcha.Blueprint.Models.Credentials;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;

namespace Sorcha.Blueprint.Fluent.Tests;

/// <summary>
/// Issue #1547 — <see cref="BlueprintBuilder.FromBlueprint"/> must let a builder continue editing
/// an existing blueprint without losing anything, and issue #1549 — adding the same participant or
/// action twice must replace rather than duplicate.
/// </summary>
public class BlueprintBuilderFromBlueprintTests
{
    private static BlueprintModel ExistingBlueprint() => new()
    {
        Id = "existing-bp",
        Title = "Existing Blueprint",
        Description = "A blueprint that already has actions.",
        Participants =
        [
            new Participant { Id = "applicant", Name = "Applicant" },
            new Participant { Id = "assessor",  Name = "Assessor"  }
        ],
        Actions =
        [
            new Models.Action
            {
                Id = 1,
                Title = "Apply",
                Sender = "applicant",
                IsStartingAction = true,
                Disclosures = [new Disclosure("applicant", ["/*"])],
                DataSchemas = [JsonDocument.Parse("""{"type":"object","properties":{"name":{"type":"string"}}}""")],
                Routes = [new Route { Id = "to-assess", NextActionIds = [2], IsDefault = true }]
            },
            new Models.Action
            {
                Id = 2,
                Title = "Assess",
                Sender = "assessor",
                Disclosures = [new Disclosure("assessor", ["/*"])],
                Routes = [new Route { Id = "done", NextActionIds = [], IsDefault = true }],
                CredentialIssuanceConfig = new CredentialIssuanceConfig
                {
                    CredentialType = "AssessmentCredential",
                    Vct = "https://sorcha.dev/vc/assessment/v1",
                    RecipientParticipantId = "applicant"
                }
            }
        ]
    };

    [Fact]
    public void FromBlueprint_KeepsEveryAction()
    {
        var draft = BlueprintBuilder.FromBlueprint(ExistingBlueprint()).BuildDraft();

        draft.Actions.Should().HaveCount(2,
            "dropping actions is what stopped the designer iterating — every message after the " +
            "first saw an empty blueprint and the model rebuilt from scratch");
        draft.Actions.Select(a => a.Id).Should().Equal(1, 2);
    }

    [Fact]
    public void FromBlueprint_KeepsActionDetail_NotJustTheActions()
    {
        var draft = BlueprintBuilder.FromBlueprint(ExistingBlueprint()).BuildDraft();

        var apply = draft.Actions.Single(a => a.Id == 1);
        apply.Sender.Should().Be("applicant");
        apply.IsStartingAction.Should().BeTrue();
        apply.DataSchemas.Should().ContainSingle();
        apply.Routes.Should().ContainSingle(r => r.Id == "to-assess");
        apply.Disclosures.Should().ContainSingle();

        var assess = draft.Actions.Single(a => a.Id == 2);
        assess.CredentialIssuanceConfig.Should().NotBeNull();
        assess.CredentialIssuanceConfig!.Vct.Should().Be("https://sorcha.dev/vc/assessment/v1");
    }

    [Fact]
    public void FromBlueprint_WrapsRatherThanCopies_SoLaterMutationsLand()
    {
        var original = ExistingBlueprint();
        var builder = BlueprintBuilder.FromBlueprint(original);

        // This is how every mutating chat tool works: BuildDraft() then mutate the live object.
        builder.BuildDraft().Actions.Single(a => a.Id == 2).Title = "Assess (revised)";

        builder.BuildDraft().Actions.Single(a => a.Id == 2).Title.Should().Be("Assess (revised)");
        original.Actions.Single(a => a.Id == 2).Title.Should().Be("Assess (revised)",
            "the tools mutate the draft in place, so the builder must wrap it, not clone it");
    }

    [Fact]
    public void FromBlueprint_RehydratesParticipantsForActionBuilding()
    {
        var builder = BlueprintBuilder.FromBlueprint(ExistingBlueprint());

        // AddAction validates the sender against the builder's participant lookup. If FromBlueprint
        // did not rehydrate it, adding an action for an existing participant would fail.
        var act = () => builder.AddAction(3, a => a.WithTitle("Third").SentBy("assessor"));

        act.Should().NotThrow();
        builder.BuildDraft().Actions.Should().HaveCount(3);
    }

    [Fact]
    public void AddParticipant_Twice_ReplacesInsteadOfDuplicating()
    {
        var builder = BlueprintBuilder.Create().WithTitle("Dup").WithDescription("duplicate check");
        builder.AddParticipant("applicant", p => p.Named("First"));
        builder.AddParticipant("applicant", p => p.Named("Second"));

        var draft = builder.BuildDraft();
        draft.Participants.Should().ContainSingle(
            "two participants sharing an id cannot be disambiguated by action.sender (#1549)");
        draft.Participants[0].Name.Should().Be("Second", "the later definition wins");
    }

    [Fact]
    public void AddAction_Twice_ReplacesInsteadOfDuplicating()
    {
        var builder = BlueprintBuilder.Create().WithTitle("Dup").WithDescription("duplicate check");
        builder.AddParticipant("applicant", p => p.Named("Applicant"));
        builder.AddAction(1, a => a.WithTitle("First").SentBy("applicant"));
        builder.AddAction(1, a => a.WithTitle("Second").SentBy("applicant"));

        var draft = builder.BuildDraft();
        draft.Actions.Should().ContainSingle();
        draft.Actions[0].Title.Should().Be("Second");
    }

    [Fact]
    public void FromBlueprint_ThenReAddingAParticipant_DoesNotDuplicate()
    {
        // The exact shape observed live: the model rebuilds, re-adding participants that the
        // rehydrated builder already carries. Before #1549 this produced four participants.
        var builder = BlueprintBuilder.FromBlueprint(ExistingBlueprint());
        builder.AddParticipant("applicant", p => p.Named("Applicant"));
        builder.AddParticipant("assessor", p => p.Named("Assessor"));

        builder.BuildDraft().Participants.Should().HaveCount(2);
    }

    [Fact]
    public void FromBlueprint_RejectsNull()
    {
        var act = () => BlueprintBuilder.FromBlueprint(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
