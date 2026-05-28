// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Nodes;
using Sorcha.Blueprint.Engine.Implementation;
using Sorcha.Blueprint.Engine.Interfaces;
using Sorcha.Blueprint.Engine.Models;
using Sorcha.Blueprint.Models.Credentials;
using BpModels = Sorcha.Blueprint.Models;

namespace Sorcha.Blueprint.Engine.Tests;

/// <summary>
/// Feature 142 / US2 (T020): drives the portable engine through a multi-step walk-through using the
/// engine-local <see cref="InMemoryWalkState"/> stub (no register, no backend), and asserts:
/// the validate→calc→route→disclose step sequence + routing decisions + disclosure outcomes are
/// correct and deterministic; the dry-run path reproduces the engine's canonical routing/disclosure
/// (it must not diverge); and credential prerequisite/issuance steps are flagged
/// "checked in full rehearsal" rather than executed.
/// </summary>
public class DryRunStepperTests
{
    private static (IExecutionEngine engine, DryRunStepper stepper, InMemoryWalkState state) CreateHarness()
    {
        var schemaValidator = new SchemaValidator();
        var jsonLogicEvaluator = new JsonLogicEvaluator();
        var disclosureProcessor = new DisclosureProcessor();
        var routingEngine = new RoutingEngine(jsonLogicEvaluator);
        var actionProcessor = new ActionProcessor(
            schemaValidator, jsonLogicEvaluator, disclosureProcessor, routingEngine);
        var engine = new ExecutionEngine(
            actionProcessor, schemaValidator, jsonLogicEvaluator, disclosureProcessor, routingEngine);

        var state = new InMemoryWalkState();
        var stepper = new DryRunStepper(engine, state);
        return (engine, stepper, state);
    }

    /// <summary>
    /// apply (action 0) → review (action 1, routes on decision) → issue (action 2).
    /// Action 0 routes to 1 unconditionally; action 1 routes to 2 when decision == "approve",
    /// else completes; action 2 issues a credential.
    /// </summary>
    private static BpModels.Blueprint CreateApplyReviewIssueBlueprint()
    {
        return new BpModels.Blueprint
        {
            Id = "dry-run-bp",
            Title = "Apply Review Issue",
            Description = "Three-step walk-through with a routing decision and credential issuance",
            Version = 1,
            Participants =
            [
                new BpModels.Participant { Id = "applicant", Name = "Applicant", WalletAddress = "wallet-applicant" },
                new BpModels.Participant { Id = "reviewer", Name = "Reviewer", WalletAddress = "wallet-reviewer" },
                new BpModels.Participant { Id = "issuer", Name = "Issuer", WalletAddress = "wallet-issuer" },
            ],
            Actions =
            [
                new BpModels.Action
                {
                    Id = 0,
                    Title = "Apply",
                    Sender = "applicant",
                    IsStartingAction = true,
                    Form = new BpModels.Control
                    {
                        Schema = JsonNode.Parse("""
                        {
                            "type": "object",
                            "properties": {
                                "amount": { "type": "integer" },
                                "name": { "type": "string" }
                            },
                            "required": ["amount", "name"]
                        }
                        """)
                    },
                    Disclosures =
                    [
                        new BpModels.Disclosure("reviewer", ["/name", "/amount"]),
                    ],
                    Routes =
                    [
                        new BpModels.Route { Id = "to-review", NextActionIds = [1], IsDefault = true },
                    ],
                },
                new BpModels.Action
                {
                    Id = 1,
                    Title = "Review",
                    Sender = "reviewer",
                    Form = new BpModels.Control
                    {
                        Schema = JsonNode.Parse("""
                        {
                            "type": "object",
                            "properties": { "decision": { "type": "string" } },
                            "required": ["decision"]
                        }
                        """)
                    },
                    Disclosures =
                    [
                        new BpModels.Disclosure("issuer", ["/name", "/decision"]),
                    ],
                    Routes =
                    [
                        new BpModels.Route
                        {
                            Id = "approve",
                            NextActionIds = [2],
                            Condition = JsonNode.Parse("""{"==":[{"var":"decision"},"approve"]}"""),
                        },
                        new BpModels.Route { Id = "reject", NextActionIds = [], IsDefault = true },
                    ],
                },
                new BpModels.Action
                {
                    Id = 2,
                    Title = "Issue",
                    Sender = "issuer",
                    Disclosures =
                    [
                        new BpModels.Disclosure("applicant", ["/*"]),
                    ],
                    CredentialIssuanceConfig = new CredentialIssuanceConfig
                    {
                        CredentialType = "AssuredIdentityCredential",
                    },
                },
            ],
        };
    }

    [Fact]
    public async Task DryRun_ApproveBranch_ProducesCorrectStepSequenceAndRouting()
    {
        // Arrange
        var (_, stepper, state) = CreateHarness();
        var blueprint = CreateApplyReviewIssueBlueprint();

        // Act — walk apply → review(approve) → issue
        var start = DryRunStepper.ResolveStartAction(blueprint);
        start!.Id.Should().Be(0);

        var applyStep = await stepper.ProcessStepAsync(
            blueprint, start,
            new Dictionary<string, object> { ["amount"] = 100, ["name"] = "Alice" });

        var afterApply = DryRunStepper.ResolveNextAction(blueprint, applyStep.RoutingOutcome!);
        afterApply!.Id.Should().Be(1);

        var reviewStep = await stepper.ProcessStepAsync(
            blueprint, afterApply,
            new Dictionary<string, object> { ["decision"] = "approve" });

        var afterReview = DryRunStepper.ResolveNextAction(blueprint, reviewStep.RoutingOutcome!);
        afterReview!.Id.Should().Be(2);

        var issueStep = await stepper.ProcessStepAsync(
            blueprint, afterReview,
            new Dictionary<string, object>());

        var afterIssue = DryRunStepper.ResolveNextAction(blueprint, issueStep.RoutingOutcome!);

        // Assert — full sequence and routing decisions
        applyStep.Status.Should().Be(DryRunStepStatus.Done);
        applyStep.ActingRole.Should().Be("applicant");
        applyStep.RoutingOutcome!.NextActionId.Should().Be("1");

        reviewStep.Status.Should().Be(DryRunStepStatus.Done);
        reviewStep.ActingRole.Should().Be("reviewer");
        reviewStep.RoutingOutcome!.NextActionId.Should().Be("2");
        reviewStep.RoutingOutcome.IsWorkflowComplete.Should().BeFalse();

        issueStep.Status.Should().Be(DryRunStepStatus.Done);
        issueStep.RoutingOutcome!.IsWorkflowComplete.Should().BeTrue();
        afterIssue.Should().BeNull("the workflow is complete after issue");

        state.CompletedActionIds.Should().Equal(0, 1, 2);
    }

    [Fact]
    public async Task DryRun_RejectBranch_RoutesToWorkflowComplete()
    {
        // Arrange
        var (_, stepper, _) = CreateHarness();
        var blueprint = CreateApplyReviewIssueBlueprint();

        // Act
        var apply = await stepper.ProcessStepAsync(
            blueprint, DryRunStepper.ResolveStartAction(blueprint)!,
            new Dictionary<string, object> { ["amount"] = 50, ["name"] = "Bob" });

        var review = await stepper.ProcessStepAsync(
            blueprint, DryRunStepper.ResolveNextAction(blueprint, apply.RoutingOutcome!)!,
            new Dictionary<string, object> { ["decision"] = "reject" });

        // Assert — the reject default route has no next actions → workflow complete
        review.RoutingOutcome!.IsWorkflowComplete.Should().BeTrue();
        DryRunStepper.ResolveNextAction(blueprint, review.RoutingOutcome).Should().BeNull();
    }

    [Fact]
    public async Task DryRun_AccumulatedState_CarriesPriorPayloadAcrossSteps()
    {
        // Arrange — review routes on a field submitted at apply, proving cross-step state.
        var (_, stepper, state) = CreateHarness();
        var blueprint = CreateApplyReviewIssueBlueprint();
        // Make the review route depend on the apply step's "amount" (>= 100 → approve path).
        blueprint.Actions[1].Routes =
        [
            new BpModels.Route
            {
                Id = "high-value",
                NextActionIds = [2],
                Condition = JsonNode.Parse("""{">=":[{"var":"amount"},100]}"""),
            },
            new BpModels.Route { Id = "low-value", NextActionIds = [], IsDefault = true },
        ];

        // Act
        var apply = await stepper.ProcessStepAsync(
            blueprint, blueprint.Actions[0],
            new Dictionary<string, object> { ["amount"] = 100, ["name"] = "Carol" });

        var review = await stepper.ProcessStepAsync(
            blueprint, blueprint.Actions[1],
            new Dictionary<string, object> { ["decision"] = "approve" });

        // Assert — the review step routed on "amount" which only the APPLY step submitted.
        state.GetAccumulatedState().Should().ContainKey("amount").WhoseValue.Should().Be(100);
        review.RoutingOutcome!.NextActionId.Should().Be("2",
            "review routing must see the amount carried from the apply step");
    }

    [Fact]
    public async Task DryRun_CredentialIssuanceStep_IsFlaggedNotExecuted()
    {
        // Arrange
        var (_, stepper, _) = CreateHarness();
        var blueprint = CreateApplyReviewIssueBlueprint();

        await stepper.ProcessStepAsync(blueprint, blueprint.Actions[0],
            new Dictionary<string, object> { ["amount"] = 100, ["name"] = "Dave" });
        await stepper.ProcessStepAsync(blueprint, blueprint.Actions[1],
            new Dictionary<string, object> { ["decision"] = "approve" });

        // Act — the issue action declares CredentialIssuanceConfig
        var issueStep = await stepper.ProcessStepAsync(
            blueprint, blueprint.Actions[2], new Dictionary<string, object>());

        // Assert — flagged, NOT executed
        issueStep.HasDeferredCredentialChecks.Should().BeTrue();
        issueStep.CredentialNote.Should().Contain("checked in full rehearsal");
        issueStep.CredentialNote.Should().Contain("issuance");
    }

    [Fact]
    public async Task DryRun_CredentialPrerequisiteStep_IsFlaggedNotExecuted()
    {
        // Arrange — add a credential prerequisite to the review step.
        var (_, stepper, _) = CreateHarness();
        var blueprint = CreateApplyReviewIssueBlueprint();
        blueprint.Actions[1].CredentialRequirements =
        [
            new CredentialRequirement { Type = "ProfessionalLicence" },
        ];

        await stepper.ProcessStepAsync(blueprint, blueprint.Actions[0],
            new Dictionary<string, object> { ["amount"] = 100, ["name"] = "Eve" });

        // Act — no credential presentation supplied; dry-run must NOT fail on the missing proof.
        var reviewStep = await stepper.ProcessStepAsync(
            blueprint, blueprint.Actions[1],
            new Dictionary<string, object> { ["decision"] = "approve" });

        // Assert — step succeeds (proof not verified), but is flagged.
        reviewStep.Status.Should().Be(DryRunStepStatus.Done,
            "credential prerequisites are not verified in a dry-run");
        reviewStep.HasDeferredCredentialChecks.Should().BeTrue();
        reviewStep.CredentialNote.Should().Contain("prerequisite");
        reviewStep.CredentialNote.Should().Contain("checked in full rehearsal");
    }

    [Fact]
    public async Task DryRun_NonCredentialStep_HasNoCredentialNote()
    {
        var (_, stepper, _) = CreateHarness();
        var blueprint = CreateApplyReviewIssueBlueprint();

        var apply = await stepper.ProcessStepAsync(blueprint, blueprint.Actions[0],
            new Dictionary<string, object> { ["amount"] = 100, ["name"] = "Frank" });

        apply.HasDeferredCredentialChecks.Should().BeFalse();
        apply.CredentialNote.Should().BeNull();
    }

    [Fact]
    public async Task DryRun_FailedValidation_DoesNotAdvanceState()
    {
        // Arrange
        var (_, stepper, state) = CreateHarness();
        var blueprint = CreateApplyReviewIssueBlueprint();

        // Act — omit required "name" → validation fails
        var apply = await stepper.ProcessStepAsync(
            blueprint, blueprint.Actions[0],
            new Dictionary<string, object> { ["amount"] = 100 });

        // Assert
        apply.Status.Should().Be(DryRunStepStatus.Failed);
        apply.Validation!.IsValid.Should().BeFalse();
        apply.RoutingOutcome.Should().BeNull("routing must not run when validation fails");
        state.CompletedActionIds.Should().BeEmpty("a failed step must not advance accumulated state");
    }

    [Fact]
    public async Task DryRun_RoutingAndDisclosure_MatchEngineCanonicalOutcomes()
    {
        // This is the dry-run vs full-fidelity check: the routing & disclosure outcomes the
        // dry-run path produces for each step MUST equal the engine's canonical outcomes for the
        // same (action, processed-data) — the dry-run faithfully reproduces real execution.
        var (engine, stepper, _) = CreateHarness();
        var blueprint = CreateApplyReviewIssueBlueprint();

        // --- Step apply via the dry-run path ---
        var applyPayload = new Dictionary<string, object> { ["amount"] = 100, ["name"] = "Grace" };
        var applyStep = await stepper.ProcessStepAsync(blueprint, blueprint.Actions[0], applyPayload);

        // Independently compute the engine's canonical outcomes for the same processed input.
        var applyProcessed = await engine.ApplyCalculationsAsync(applyPayload, blueprint.Actions[0]);
        var canonicalApplyRouting = await engine.DetermineRoutingAsync(blueprint, blueprint.Actions[0], applyProcessed);
        var canonicalApplyDisclosure = engine.ApplyDisclosures(applyProcessed, blueprint.Actions[0]);

        applyStep.RoutingOutcome!.NextActionId.Should().Be(canonicalApplyRouting.NextActionId);
        applyStep.RoutingOutcome.IsWorkflowComplete.Should().Be(canonicalApplyRouting.IsWorkflowComplete);
        applyStep.DisclosureOutcome.Should().HaveCount(canonicalApplyDisclosure.Count);
        applyStep.DisclosureOutcome![0].ParticipantId.Should().Be(canonicalApplyDisclosure[0].ParticipantId);
        applyStep.DisclosureOutcome[0].DisclosedData.Keys.Should()
            .BeEquivalentTo(canonicalApplyDisclosure[0].DisclosedData.Keys);

        // --- Step review via the dry-run path (with accumulated state) ---
        var reviewPayload = new Dictionary<string, object> { ["decision"] = "approve" };
        var reviewStep = await stepper.ProcessStepAsync(blueprint, blueprint.Actions[1], reviewPayload);

        // Canonical: review routing/disclosure see apply's payload too (accumulated state).
        var reviewInput = new Dictionary<string, object>(applyProcessed) { ["decision"] = "approve" };
        var reviewProcessed = await engine.ApplyCalculationsAsync(reviewInput, blueprint.Actions[1]);
        var canonicalReviewRouting = await engine.DetermineRoutingAsync(blueprint, blueprint.Actions[1], reviewProcessed);
        var canonicalReviewDisclosure = engine.ApplyDisclosures(reviewProcessed, blueprint.Actions[1]);

        reviewStep.RoutingOutcome!.NextActionId.Should().Be(canonicalReviewRouting.NextActionId);
        reviewStep.DisclosureOutcome!.Select(d => d.ParticipantId).Should()
            .BeEquivalentTo(canonicalReviewDisclosure.Select(d => d.ParticipantId));
    }

    [Fact]
    public async Task DryRun_IsDeterministic_AcrossRepeatedRuns()
    {
        var blueprint = CreateApplyReviewIssueBlueprint();

        async Task<List<string?>> RunOnce()
        {
            var (_, stepper, _) = CreateHarness();
            var apply = await stepper.ProcessStepAsync(blueprint, blueprint.Actions[0],
                new Dictionary<string, object> { ["amount"] = 100, ["name"] = "Heidi" });
            var review = await stepper.ProcessStepAsync(blueprint, blueprint.Actions[1],
                new Dictionary<string, object> { ["decision"] = "approve" });
            var issue = await stepper.ProcessStepAsync(blueprint, blueprint.Actions[2],
                new Dictionary<string, object>());
            return
            [
                apply.RoutingOutcome!.NextActionId,
                review.RoutingOutcome!.NextActionId,
                issue.RoutingOutcome!.IsWorkflowComplete.ToString(),
            ];
        }

        var run1 = await RunOnce();
        var run2 = await RunOnce();

        run1.Should().Equal(run2);
    }

    [Fact]
    public void InMemoryWalkState_Reset_ClearsAccumulatedStateAndPointer()
    {
        var state = new InMemoryWalkState();
        state.SetCurrentAction(1);
        state.RecordCompletedAction(1, new Dictionary<string, object> { ["x"] = 1 });

        state.GetAccumulatedState().Should().ContainKey("x");
        state.CurrentActionId.Should().Be(1);

        state.Reset();

        state.GetAccumulatedState().Should().BeEmpty();
        state.CompletedActionIds.Should().BeEmpty();
        state.CurrentActionId.Should().BeNull();
    }
}
