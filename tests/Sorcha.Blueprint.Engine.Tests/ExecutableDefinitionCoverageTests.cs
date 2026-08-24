// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Reflection;
using FluentAssertions;
using Sorcha.Blueprint.Engine.Implementation;
using Sorcha.Blueprint.Models;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;
using ActionModel = Sorcha.Blueprint.Models.Action;
using Xunit;

namespace Sorcha.Blueprint.Engine.Tests;

/// <summary>
/// Feature 195 (#1566) — every property on the blueprint object graph is explicitly classified as
/// behavioural or presentational, and the behavioural ones all reach the executable-definition
/// projection.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why reflection rather than a list of tests.</b> The projection
/// (<c>ExecutableDefinitionHasher.BuildExecutableDefinition</c>) is a hand-written, field-by-field
/// rebuild. A property added to <see cref="BlueprintModel"/>, <see cref="ActionModel"/>,
/// <see cref="Route"/> or <see cref="Participant"/> and forgotten there does not fail to compile and
/// does not fail any existing test — it simply stops contributing to the signature. A probe run
/// during the Feature 195 investigation found <b>nine</b> such properties at once, every one of them
/// affecting execution.
/// </para>
/// <para>
/// <b>Unclassified fails the build.</b> The deny-list below is exhaustive by construction: a property
/// that appears in neither list fails this test rather than defaulting to either. Defaulting to
/// "behavioural" would silently re-lock the F142 rehearsal gate on cosmetic edits; defaulting to
/// "presentational" would silently reintroduce exactly the defect this guards. Neither default is
/// safe, so there is none.
/// </para>
/// <para>
/// <b>What the signature is FOR, after Feature 195.</b> It no longer identifies a definition — the
/// publication transaction id does that, and it addresses the whole definition. This value answers
/// one narrower question: <i>did behaviour change?</i>, which decides whether a recorded F142
/// rehearsal pass survives a republish. That is why presentational properties are excluded at all.
/// </para>
/// </remarks>
public class ExecutableDefinitionCoverageTests
{
    /// <summary>
    /// Properties that do not affect how a workflow executes. Each entry is a claim that changing it
    /// cannot change which payloads validate, which route is taken, what is disclosed, what is
    /// issued, or how the instance is identified.
    /// </summary>
    private static readonly Dictionary<Type, HashSet<string>> Presentational = new()
    {
        [typeof(BlueprintModel)] = new(StringComparer.Ordinal)
        {
            "Title",                // display
            "Description",          // display
            "Version",              // ordinal display label; removed from the hash by F194
            "VersionMajor",         // dead
            "VersionMinor",         // dead
            "CreatedAt",            // provenance, not behaviour
            "UpdatedAt",            // provenance, not behaviour
            "OrganizationId",       // ownership, enforced outside the definition
            "JsonLdContext",        // semantic-web annotation
            "JsonLdType",           // semantic-web annotation
            "Instructions",         // authoring guidance shown to designers
        },
        [typeof(ActionModel)] = new(StringComparer.Ordinal)
        {
            "Title",
            "Description",
            "JsonLdType",
            "Published",            // draft-state flag, not part of a published definition
            "Instructions",
            "Form",                 // presentation of the schema, not the schema
            "PreviousTxId",         // runtime chain data that has no business on a definition
            "PreviousData",         // ditto
            "BlueprintId",          // back-reference to the owning blueprint
            "AdditionalProperties", // free-form annotation bag
            "AdditionalRecipients", // INERT — see the dedicated test below
        },
        [typeof(Route)] = new(StringComparer.Ordinal)
        {
            "Description",
        },
        [typeof(Participant)] = new(StringComparer.Ordinal)
        {
            "Name",
            "Organisation",
            "JsonLdType",
            "Instructions",
            "AdditionalProperties",
            "VerifiableCredential",
        },
    };

    /// <summary>
    /// Properties that DO affect execution and must therefore reach the projection.
    /// </summary>
    private static readonly Dictionary<Type, HashSet<string>> Behavioural = new()
    {
        [typeof(BlueprintModel)] = new(StringComparer.Ordinal)
        {
            "Id",
            "Participants",
            "Actions",
            "DataSchemas",
            "PresentationConfig",   // validity window / abandonment / outcome detail
            "InstanceReference",    // generates the instance's public metadata
            "Metadata",             // carries hasCycles and other execution-affecting keys
        },
        [typeof(ActionModel)] = new(StringComparer.Ordinal)
        {
            "Id",
            "Sender",
            "Target",
            "IsStartingAction",
            "RequiredPriorActions",
            "RequiredActionData",       // validation fallback when no dataSchemas are declared
            "Calculations",
            "Condition",
            "Disclosures",
            "DataSchemas",
            "Routes",
            "Participants",             // legacy condition-based routing — still live
            "RejectionConfig",          // a real routing edge the validator reads
            "CredentialRequirements",
            "CredentialIssuanceConfig",
            "Notification",
        },
        [typeof(Route)] = new(StringComparer.Ordinal)
        {
            "Id",
            "NextActionIds",
            "Condition",
            "IsDefault",
            "OutputMapping",
            "BranchDeadline",       // parallel-branch deadline
            "DecisionNotice",       // F184/F186 outcome catalogue, resolved from the pinned definition
        },
        [typeof(Participant)] = new(StringComparer.Ordinal)
        {
            "Id",
            "WalletAddress",
            "DidUri",
            "UseStealthAddress",
        },
    };

    public static TheoryData<Type> GraphTypes() =>
    [
        typeof(BlueprintModel),
        typeof(ActionModel),
        typeof(Route),
        typeof(Participant),
    ];

    [Theory]
    [MemberData(nameof(GraphTypes))]
    public void EveryProperty_IsClassified(Type type)
    {
        var declared = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        var classified = new HashSet<string>(Presentational[type], StringComparer.Ordinal);
        classified.UnionWith(Behavioural[type]);

        var unclassified = declared.Except(classified).OrderBy(n => n, StringComparer.Ordinal).ToList();

        unclassified.Should().BeEmpty(
            "every property on {0} must be explicitly classified as behavioural or presentational. " +
            "There is no default: guessing 'presentational' silently reintroduces the defect this " +
            "guards, and guessing 'behavioural' silently re-locks the rehearsal gate on cosmetic " +
            "edits. Add {1} to one of the two lists in this file — deliberately.",
            type.Name, string.Join(", ", unclassified));
    }

    [Theory]
    [MemberData(nameof(GraphTypes))]
    public void NoClassifiedProperty_HasBeenRemovedFromTheModel(Type type)
    {
        var declared = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        var classified = new HashSet<string>(Presentational[type], StringComparer.Ordinal);
        classified.UnionWith(Behavioural[type]);

        var stale = classified.Except(declared).OrderBy(n => n, StringComparer.Ordinal).ToList();

        stale.Should().BeEmpty(
            "the classification lists must not name properties {0} no longer declares — a stale entry " +
            "makes the coverage check pass over something that does not exist",
            type.Name);
    }

    [Fact]
    public void TheTwoLists_DoNotOverlap()
    {
        foreach (var type in Presentational.Keys)
        {
            Presentational[type].Intersect(Behavioural[type]).Should().BeEmpty(
                "a property on {0} cannot be both behavioural and presentational", type.Name);
        }
    }

    // =============================================================================================
    // Coverage: a behavioural property must actually reach the projection.
    //
    // WHAT THIS PAIR PROVES, AND WHAT IT DOES NOT.
    //
    // The classification tests above are the completeness guard: a property added to the model and
    // classified by nobody fails the build. That is the regression that actually happens — someone
    // adds Action.Foo, forgets the hand-written projection, and nothing notices.
    //
    // The tests below are the correctness guard for the properties #1566 was raised about: each one
    // was found omitted by a probe during the Feature 195 investigation, and each is exercised here
    // with a concrete, hand-authored edit rather than a reflected one.
    //
    // A generic "mutate every property by reflection" pass was tried first and REMOVED, because it
    // could not honestly do the job: for collection and complex properties it produced values that
    // serialise identically to the baseline (an empty list where the baseline held null), so it
    // reported dozens of failures that were artefacts of the mutator rather than gaps in the
    // projection. A guard that cannot tell its own artefacts from real findings is worse than a
    // narrower one that can.
    // =============================================================================================

    private static (string Baseline, string Edited) HashPair(System.Action<BlueprintModel> edit)
    {
        var hasher = new ExecutableDefinitionHasher();
        var baseline = Sample();
        var edited = Sample();
        edit(edited);
        return (hasher.ComputeHash(baseline), hasher.ComputeHash(edited));
    }

    [Fact]
    public void RejectionConfig_ChangesTheSignature()
    {
        // A real routing edge: the validator reads RejectionConfig.TargetActionId as a structural
        // successor (VAL_ROUTING_001) and in VAL_BP_003 reachability.
        var (baseline, edited) = HashPair(bp => bp.Actions[0].RejectionConfig =
            new RejectionConfig { TargetActionId = 1, IsTerminal = true, RequireReason = true });

        edited.Should().NotBe(baseline);
    }

    [Fact]
    public void LegacyParticipantRouting_ChangesTheSignature()
    {
        // Action.Participants carries the legacy condition-based routing model, still live in
        // RoutingEngine. A blueprint routed this way had ZERO routing coverage in its signature.
        var (baseline, edited) = HashPair(bp => bp.Actions[0].Participants =
            [new Condition { Principal = "sender", Criteria = ["{\"==\":[{\"var\":\"decision\"},\"yes\"]}"] }]);

        edited.Should().NotBe(baseline);
    }

    [Fact]
    public void RequiredActionData_ChangesTheSignature()
    {
        // The validation fallback used when an action declares no dataSchemas.
        var (baseline, edited) = HashPair(bp => bp.Actions[0].RequiredActionData = ["applicantName"]);

        edited.Should().NotBe(baseline);
    }

    [Fact]
    public void BranchDeadline_ChangesTheSignature()
    {
        var (baseline, edited) = HashPair(bp => bp.Actions[0].Routes!.First().BranchDeadline = "P7D");

        edited.Should().NotBe(baseline);
    }

    [Fact]
    public void DecisionNotice_ChangesTheSignature()
    {
        // F184/F186 — the citizen-facing outcome catalogue, resolved FROM THE PINNED DEFINITION, so
        // two definitions differing only here give a rejected applicant different reasons.
        var (baseline, edited) = HashPair(bp => bp.Actions[0].Routes!.First().DecisionNotice =
            new DecisionNotice
            {
                RecipientParticipantId = "receiver",
                ReasonCodeField = "/reasonCode",
                FallbackMessage = "Your application was not approved.",
            });

        edited.Should().NotBe(baseline);
    }

    [Fact]
    public void PresentationConfig_ChangesTheSignature()
    {
        var (baseline, edited) = HashPair(bp => bp.PresentationConfig =
            new BlueprintPresentationConfig { RecordAbandonment = true, PresentationValidityWindowSeconds = 900 });

        edited.Should().NotBe(baseline);
    }

    [Fact]
    public void InstanceReference_ChangesTheSignature()
    {
        var (baseline, edited) = HashPair(bp => bp.InstanceReference =
            new InstanceReferenceTemplate { Prefix = "GV" });

        edited.Should().NotBe(baseline);
    }

    [Fact]
    public void Metadata_ChangesTheSignature()
    {
        // Carries hasCycles, which the publish path writes and which changes how the blueprint runs.
        var (baseline, edited) = HashPair(bp => bp.Metadata =
            new Dictionary<string, string> { ["hasCycles"] = "true" });

        edited.Should().NotBe(baseline);
    }

    [Fact]
    public void ActionNotification_ChangesTheSignature()
    {
        var (baseline, edited) = HashPair(bp => bp.Actions[0].Notification =
            new NotificationConfig { SummaryTemplate = "You have an action waiting." });

        edited.Should().NotBe(baseline);
    }

    // --- and the other direction: a presentational edit must NOT move the signature -------------

    [Fact]
    public void Relabelling_DoesNotChangeTheSignature()
    {
        var (baseline, edited) = HashPair(bp =>
        {
            bp.Title = "A completely different title";
            bp.Description = "Reworded for clarity.";
            bp.Actions[0].Title = "Submit your application";
            bp.Actions[0].Description = "Tell us about the work.";
            bp.Actions[0].Routes!.First().Description = "Onward, rephrased";
            bp.Participants[0].Name = "The Applicant";
        });

        edited.Should().Be(baseline,
            "a relabel must leave a recorded F142 rehearsal pass valid — otherwise the designer is " +
            "asked to re-rehearse a cosmetic edit");
    }

    [Fact]
    public void TheOrdinalVersion_DoesNotChangeTheSignature()
    {
        // F194 removed it from the projection: an ordinal assigned from in-memory insert order has no
        // business inside a content address, and an author renumbering a draft would otherwise have
        // stranded every in-flight instance.
        var (baseline, edited) = HashPair(bp => bp.Version = 99);

        edited.Should().Be(baseline);
    }

    [Fact]
    public void Timestamps_DoNotChangeTheSignature()
    {
        var (baseline, edited) = HashPair(bp =>
        {
            bp.CreatedAt = new DateTimeOffset(2030, 5, 5, 0, 0, 0, TimeSpan.Zero);
            bp.UpdatedAt = new DateTimeOffset(2030, 5, 5, 0, 0, 0, TimeSpan.Zero);
        });

        edited.Should().Be(baseline,
            "provenance is not behaviour — and note this is the OPPOSITE of the publication id, " +
            "which addresses the whole definition and therefore does move with these");
    }

    [Fact]
    public void AdditionalRecipients_DoesNotChangeTheSignature()
    {
        // Checked during the investigation and deliberately left out. A probe flagged it as omitted
        // and it was nearly written up as a disclosure-scope defect; its only readers are
        // McpServer's BlueprintGetTool (display) and a doc comment. Recorded as a test so the next
        // person does not re-litigate it from the probe output alone.
        var (baseline, edited) = HashPair(bp => bp.Actions[0].AdditionalRecipients = ["ws1-observer"]);

        edited.Should().Be(baseline,
            "AdditionalRecipients is inert — nothing reads it at execution time");
    }

    /// <summary>A blueprint exercising every graph node the projection walks.</summary>
    private static BlueprintModel Sample() => new()
    {
        Id = "coverage-bp",
        Title = "Coverage",
        Description = "Exercises every graph node.",
        CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        UpdatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        Participants =
        [
            new Participant { Id = "sender", Name = "Sender" },
            new Participant { Id = "receiver", Name = "Receiver" },
        ],
        Actions =
        [
            new ActionModel
            {
                Id = 0,
                Title = "Submit",
                Sender = "sender",
                IsStartingAction = true,
                Routes = [new Route { Id = "onward", NextActionIds = [1], IsDefault = true }],
            },
            new ActionModel { Id = 1, Title = "Review", Sender = "receiver", Routes = [] },
        ],
    };
}
