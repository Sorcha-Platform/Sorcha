// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Reflection;
using System.Text.Json.Nodes;
using Sorcha.Blueprint.Models;
using Sorcha.Blueprint.Models.Credentials;
using Sorcha.Blueprint.Service.Storage;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;

namespace Sorcha.Blueprint.Service.Tests.Credentials;

/// <summary>
/// Publish-time guard for issue #1551: an action that models a DECISION but declares a
/// <c>credentialIssuanceConfig</c> with no <c>issuanceCondition</c> mints unconditionally,
/// because minting runs BEFORE routing. A terminal reject route stops the credential being
/// handed over — it does not stop it being minted and delivered.
/// </summary>
/// <remarks>
/// Confirmed live on n1 by an A/B of two blueprints differing only in
/// <c>issuanceCondition</c>: with it, a Fail decision issued nothing; without it, a
/// credential was minted and delivered into the rejected applicant's wallet.
/// </remarks>
public class UnconditionalIssuanceWarningTests
{
    private const string Code = ValidationWarningCodes.UnconditionalIssuanceOnDecision;

    // ValidateBlueprint is private on PublishService; the two required deps are interfaces,
    // and the method reads only the blueprint it is handed.
    private static (List<string> Errors, List<string> Warnings) Validate(BlueprintModel blueprint)
    {
        var service = new PublishService(
            new Mock<IBlueprintStore>().Object,
            new Mock<IPublishedBlueprintStore>().Object);

        var method = typeof(PublishService).GetMethod(
            "ValidateBlueprint", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("PublishService.ValidateBlueprint not found — has it been renamed?");

        return ((List<string> Errors, List<string> Warnings))method.Invoke(service, new object[] { blueprint })!;
    }

    /// <summary>Two-action decision workflow: applicant applies, assessor passes or fails.</summary>
    private static BlueprintModel BuildDecisionBlueprint(
        JsonNode? issuanceCondition,
        IEnumerable<Route>? routes)
    {
        return new BlueprintModel
        {
            Id = "decision-bp",
            Title = "Decision Blueprint",
            Description = "An approve/reject decision that issues a credential.",
            Participants =
            [
                new Participant { Id = "applicant", Name = "Applicant" },
                new Participant { Id = "assessor",  Name = "Assessor"  }
            ],
            Actions =
            [
                new Sorcha.Blueprint.Models.Action
                {
                    Id = 1,
                    Title = "Apply",
                    Sender = "applicant",
                    IsStartingAction = true,
                    Disclosures = [new Disclosure("applicant", ["/*"])],
                    Routes = [new Route { Id = "to-assess", NextActionIds = [2], IsDefault = true }]
                },
                new Sorcha.Blueprint.Models.Action
                {
                    Id = 2,
                    Title = "Assess",
                    Sender = "assessor",
                    Disclosures = [new Disclosure("assessor", ["/*"])],
                    Routes = routes,
                    CredentialIssuanceConfig = new CredentialIssuanceConfig
                    {
                        CredentialType = "DecisionCredential",
                        Vct = "https://sorcha.dev/vc/decision/v1",
                        RecipientParticipantId = "applicant",
                        IssuanceCondition = issuanceCondition
                    }
                }
            ]
        };
    }

    private static Route PassRoute() => new()
    {
        Id = "pass-terminal",
        NextActionIds = [],
        Condition = JsonNode.Parse("""{ "==": [{ "var": "decision" }, "Pass"] }""")
    };

    private static Route FailRoute() => new() { Id = "fail-terminal", NextActionIds = [], IsDefault = true };

    [Fact]
    public void DecisionAction_WithNoIssuanceCondition_Warns()
    {
        var (_, warnings) = Validate(BuildDecisionBlueprint(
            issuanceCondition: null,
            routes: [PassRoute(), FailRoute()]));

        warnings.Should().ContainSingle(w => w.Contains(Code),
            "an approve/reject action that mints unconditionally issues to the rejected applicant too — " +
            "minting runs before routing, so the terminal reject route does not prevent it");
    }

    [Fact]
    public void DecisionAction_WithIssuanceCondition_DoesNotWarn()
    {
        var (_, warnings) = Validate(BuildDecisionBlueprint(
            issuanceCondition: JsonNode.Parse("""{ "==": [{ "var": "decision" }, "Pass"] }"""),
            routes: [PassRoute(), FailRoute()]));

        warnings.Should().NotContain(w => w.Contains(Code),
            "issuanceCondition is exactly the fix; warning anyway would train authors to ignore the code");
    }

    [Fact]
    public void SingleUnconditionalRoute_DoesNotWarn()
    {
        var (_, warnings) = Validate(BuildDecisionBlueprint(
            issuanceCondition: null,
            routes: [new Route { Id = "only", NextActionIds = [], IsDefault = true }]));

        warnings.Should().NotContain(w => w.Contains(Code),
            "one unconditional route is genuinely unconditional issuance, not a decision — " +
            "most shipped blueprints are this shape and must stay quiet");
    }

    [Fact]
    public void ActionWithNoIssuanceConfig_DoesNotWarn()
    {
        var blueprint = BuildDecisionBlueprint(null, [PassRoute(), FailRoute()]);
        blueprint.Actions[1].CredentialIssuanceConfig = null;

        var (_, warnings) = Validate(blueprint);

        warnings.Should().NotContain(w => w.Contains(Code),
            "the rule is about credential issuance; an action that issues nothing cannot fail open");
    }

    /// <summary>
    /// The three blueprints that shipped with this defect, as a regression shape. If the rule is
    /// ever narrowed so it stops firing on a conditional-route-plus-default pair, this fails.
    /// </summary>
    [Fact]
    public void ShippedDefectShape_ConditionalApproveRoutePlusDefaultDecline_Warns()
    {
        // ForestryCertification action 2: approve => terminal (conditional), decline => terminal (default)
        var (_, warnings) = Validate(BuildDecisionBlueprint(
            issuanceCondition: null,
            routes:
            [
                new Route
                {
                    Id = "approved-terminal",
                    NextActionIds = [],
                    Condition = JsonNode.Parse("""{ "==": [{ "var": "decision" }, "approve"] }""")
                },
                new Route { Id = "declined-terminal", NextActionIds = [], IsDefault = true }
            ]));

        warnings.Should().ContainSingle(w => w.Contains(Code));
    }
}
