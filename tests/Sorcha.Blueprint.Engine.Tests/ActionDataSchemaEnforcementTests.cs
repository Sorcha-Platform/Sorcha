// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Sorcha.Blueprint.Engine.Implementation;
using Sorcha.Blueprint.Engine.Models;
using BpModels = Sorcha.Blueprint.Models;

namespace Sorcha.Blueprint.Engine.Tests;

/// <summary>
/// Issue #1573 — the engine validated <c>Action.Form.Schema</c> while blueprints declare
/// <c>Action.DataSchemas</c>, so every payload validated vacuously.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every action here is built the way a PUBLISHED blueprint actually is</b> — schemas on
/// <c>dataSchemas</c>, <c>form</c> left at its layout-only default with a null <c>Schema</c>.
/// That is not incidental: the pre-existing tests hand-built <c>Form.Schema</c>, a shape no
/// blueprint in the repository produces, so they proved a code path production never took.
/// Building the fixture from the real shape is what stops this regressing.
/// </para>
/// <para>
/// The fixture mirrors <c>walkthroughs/VersionPinning/blueprints/version-pinning-v2.json</c>,
/// whose action 2 is the case that sealed in violation of its own schema on n1.
/// </para>
/// </remarks>
public class ActionDataSchemaEnforcementTests
{
    private static BpModels.Action PublishedShapeAction(params string[] schemaJson) => new()
    {
        Id = 2,
        Title = "Review",
        DataSchemas = schemaJson.Select(j => JsonDocument.Parse(j)).ToList(),
        // Form deliberately left at its default: a layout-only Control whose Schema is null.
    };

    private const string ReviewSchema = """
    {
      "type": "object",
      "properties": {
        "reviewNote":    { "type": "string", "minLength": 1 },
        "complianceRef": { "type": "string", "minLength": 1 }
      },
      "required": ["reviewNote", "complianceRef"]
    }
    """;

    private static ExecutionEngine NewEngine()
    {
        var jsonLogic = new JsonLogicEvaluator();
        var routing = new RoutingEngine(jsonLogic);
        var validator = new SchemaValidator();
        return new ExecutionEngine(
            new ActionProcessor(validator, jsonLogic, new DisclosureProcessor(), routing),
            validator, jsonLogic, new DisclosureProcessor(), routing);
    }

    [Fact]
    public async Task ValidateAsync_PayloadMissingRequiredDataSchemasField_ReturnsInvalid()
    {
        var action = PublishedShapeAction(ReviewSchema);
        action.Form!.Schema.Should().BeNull("the fixture must keep the shape published blueprints have");

        var result = await NewEngine().ValidateAsync(
            new Dictionary<string, object> { ["reviewNote"] = "Missing the required field." }, action);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_ConformingPayload_ReturnsValid()
    {
        var result = await NewEngine().ValidateAsync(
            new Dictionary<string, object> { ["reviewNote"] = "Reviewed.", ["complianceRef"] = "CR-1" },
            PublishedShapeAction(ReviewSchema));

        result.IsValid.Should().BeTrue("enforcement must not become a blanket refusal");
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_PayloadViolatingConstraintOtherThanRequired_ReturnsInvalid()
    {
        // minLength, not just presence — proves the whole schema is applied, not a required-key check.
        var result = await NewEngine().ValidateAsync(
            new Dictionary<string, object> { ["reviewNote"] = "", ["complianceRef"] = "CR-1" },
            PublishedShapeAction(ReviewSchema));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_MultipleDataSchemas_PayloadMustSatisfyEveryOne()
    {
        // The Validator applies every entry in dataSchemas and the payload must pass ALL of them.
        // The engine has to agree, or the two halves of the platform disagree about the contract.
        var action = PublishedShapeAction(
            """{ "type": "object", "required": ["reviewNote"] }""",
            """{ "type": "object", "required": ["complianceRef"] }""");

        var result = await NewEngine().ValidateAsync(
            new Dictionary<string, object> { ["reviewNote"] = "only the first schema is satisfied" }, action);

        result.IsValid.Should().BeFalse("the second declared schema is not satisfied");
    }

    [Fact]
    public async Task ValidateAsync_NoDataSchemasAndNoFormSchema_ReturnsValid()
    {
        var result = await NewEngine().ValidateAsync(
            new Dictionary<string, object> { ["anything"] = "goes" },
            new BpModels.Action { Id = 1, Title = "Unconstrained" });

        result.IsValid.Should().BeTrue("an action that declares no schema constrains nothing");
    }

    [Fact]
    public async Task ValidateAsync_LegacyFormSchemaOnly_IsStillHonoured()
    {
        // Retained fallback: nothing in the repo publishes this shape, but hand-built actions
        // (tests, the Fluent API, the demo app) do, and silently dropping their only schema
        // would be a second fail-open in the opposite direction.
        var action = new BpModels.Action
        {
            Id = 1,
            Title = "Legacy",
            Form = new BpModels.Control { Schema = JsonNode.Parse(ReviewSchema) }
        };

        var result = await NewEngine().ValidateAsync(
            new Dictionary<string, object> { ["reviewNote"] = "no complianceRef" }, action);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task ProcessAsync_PayloadMissingRequiredDataSchemasField_FailsAndDoesNotRoute()
    {
        var result = await ProcessReviewAsync(
            new Dictionary<string, object> { ["reviewNote"] = "Missing the required field." });

        result.Success.Should().BeFalse();
        result.Validation!.IsValid.Should().BeFalse();
        result.Errors.Should().Contain("Action data failed schema validation");
        result.Routing.NextActionId.Should().BeNull(
            "the processor must short-circuit before routing a payload that fails its schema");
    }

    [Fact]
    public async Task ProcessAsync_ConformingPayload_SucceedsAndRoutes()
    {
        // The counterfactual for the test above: without it, "did not route" proves nothing,
        // because a processor that routed nothing ever would also satisfy it.
        var result = await ProcessReviewAsync(
            new Dictionary<string, object> { ["reviewNote"] = "Reviewed.", ["complianceRef"] = "CR-1" });

        result.Success.Should().BeTrue();
        result.Routing.NextActionId.Should().Be("3");
    }

    private static async Task<ActionExecutionResult> ProcessReviewAsync(Dictionary<string, object> payload)
    {
        var action = PublishedShapeAction(ReviewSchema);
        action.Routes = [new BpModels.Route { Id = "next", NextActionIds = [3], IsDefault = true }];
        action.Disclosures = [new BpModels.Disclosure { ParticipantAddress = "officer", DataPointers = ["/*"] }];
        var blueprint = new BpModels.Blueprint { Id = "bp", Title = "Test", Actions = [action] };

        var jsonLogic = new JsonLogicEvaluator();
        var processor = new ActionProcessor(
            new SchemaValidator(), jsonLogic, new DisclosureProcessor(), new RoutingEngine(jsonLogic));

        return await processor.ProcessAsync(new Engine.Models.ExecutionContext
        {
            Blueprint = blueprint,
            Action = action,
            ActionData = payload,
            ParticipantId = "officer",
            WalletAddress = "ws1test"
        });
    }
}
