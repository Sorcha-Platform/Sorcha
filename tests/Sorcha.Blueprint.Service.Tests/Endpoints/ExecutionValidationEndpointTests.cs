// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Blueprint.Engine.Implementation;
using Sorcha.Blueprint.Engine.Interfaces;
using Sorcha.Blueprint.Service.Endpoints;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.Blueprint.Service.Storage;
using BpModels = Sorcha.Blueprint.Models;

namespace Sorcha.Blueprint.Service.Tests.Endpoints;

/// <summary>
/// Issue #1606 — <c>POST /api/execution/validate</c> answered <c>isValid: true</c> with an empty
/// <c>errors</c> array for any payload against any action, because the engine it delegates to
/// validated <c>Action.Form.Schema</c>, which no published blueprint sets (#1573).
/// </summary>
/// <remarks>
/// <para>
/// The engine is the <b>real</b> one here, not a mock. A mocked <c>IExecutionEngine</c> would
/// assert only that the endpoint returns whatever it is handed — which is exactly the answer the
/// broken version gave, and would have stayed green throughout.
/// </para>
/// <para>
/// Blueprints declare their contract on <c>dataSchemas</c>, so every fixture below does too
/// (CLAUDE.md pattern 24). A fixture built on <c>Form.Schema</c> exercises the fallback arm of
/// <c>ActionSchemaValidation</c> and proves nothing about the published path.
/// </para>
/// </remarks>
public class ExecutionValidationEndpointTests
{
    private const string BlueprintId = "bp-1";
    private const string InstanceId = "inst-1";
    private const string PinTxId = "tx-pinned-definition";

    private static IExecutionEngine RealEngine()
    {
        var schemaValidator = new SchemaValidator();
        var jsonLogic = new JsonLogicEvaluator();
        var disclosure = new DisclosureProcessor();
        var routing = new RoutingEngine(jsonLogic);
        return new ExecutionEngine(
            new ActionProcessor(schemaValidator, jsonLogic, disclosure, routing),
            schemaValidator, jsonLogic, disclosure, routing);
    }

    private static BpModels.Blueprint BlueprintRequiring(params string[] required) => new()
    {
        Id = BlueprintId,
        Title = "Validate Fixture",
        Description = "Contract declared on dataSchemas, as published blueprints do",
        Version = 1,
        Participants = [new BpModels.Participant { Id = "applicant", Name = "Applicant" }],
        Actions =
        [
            new BpModels.Action
            {
                Id = 1,
                Title = "Apply",
                Sender = "applicant",
                IsStartingAction = true,
                DataSchemas =
                [
                    JsonDocument.Parse($$"""
                    {
                        "type": "object",
                        "properties": {
                            "amount": { "type": "integer" },
                            "name": { "type": "string" }
                        },
                        "required": {{JsonSerializer.Serialize(required)}}
                    }
                    """),
                ],
            },
        ],
    };

    private static Sorcha.Blueprint.Service.Models.Instance InstanceOn(
        string blueprintId, string pin) => new()
        {
            Id = InstanceId,
            BlueprintId = blueprintId,
            BlueprintVersion = 1,
            BlueprintDefinitionTxId = pin,
            RegisterId = "reg-1",
            TenantId = "tenant-1",
        };

    private static Task<IResult> InvokeAsync(
        ValidateRequest request,
        BpModels.Blueprint? draft = null,
        Sorcha.Blueprint.Service.Models.Instance? instance = null,
        BpModels.Blueprint? pinned = null,
        Mock<IActionResolverService>? resolver = null)
    {
        var blueprintStore = new Mock<IBlueprintStore>();
        blueprintStore.Setup(s => s.GetAsync(It.IsAny<string>())).ReturnsAsync(draft);

        var instanceStore = new Mock<IInstanceStore>();
        instanceStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);

        resolver ??= new Mock<IActionResolverService>();
        resolver.Setup(r => r.GetBlueprintAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(pinned);

        return ExecutionValidationEndpoint.HandleAsync(
            request, blueprintStore.Object, instanceStore.Object, resolver.Object,
            RealEngine(), NullLogger.Instance, CancellationToken.None);
    }

    private static (bool IsValid, string Scope, IReadOnlyList<string> Keywords) ReadOk(IResult result)
    {
        var value = result.Should().BeAssignableTo<IValueHttpResult>().Subject.Value!;
        var type = value.GetType();

        var keywords = ((IEnumerable)type.GetProperty("errors")!.GetValue(value)!)
            .Cast<object>()
            .Select(e => (string?)e.GetType().GetProperty("keyword")!.GetValue(e) ?? string.Empty)
            .ToList();

        return ((bool)type.GetProperty("isValid")!.GetValue(value)!,
                (string)type.GetProperty("definitionScope")!.GetValue(value)!,
                keywords);
    }

    private static string ReadError(IResult result)
    {
        var value = result.Should().BeAssignableTo<IValueHttpResult>().Subject.Value!;
        return (string)value.GetType().GetProperty("error")!.GetValue(value)!;
    }

    private static ValidateRequest Request(Dictionary<string, object> data, string? instanceId = null) =>
        new() { BlueprintId = BlueprintId, ActionId = "1", Data = data, InstanceId = instanceId };

    [Fact]
    public async Task Validate_PayloadOmittingARequiredField_IsInvalidAndNamesTheConstraint()
    {
        var result = await InvokeAsync(
            Request(new Dictionary<string, object> { ["amount"] = 100 }),
            draft: BlueprintRequiring("amount", "name"));

        var (isValid, scope, keywords) = ReadOk(result);

        isValid.Should().BeFalse(
            "a pre-flight check that cannot fail asserts the opposite of the truth to a caller who "
            + "asked the question directly (#1606)");
        keywords.Should().Contain("required", "the caller needs to know WHICH constraint failed");
        scope.Should().Be("draft");
    }

    [Fact]
    public async Task Validate_ConformingPayload_IsValid()
    {
        // Anti-vacuity: without this, an endpoint that rejected everything would satisfy the test
        // above.
        var result = await InvokeAsync(
            Request(new Dictionary<string, object> { ["amount"] = 100, ["name"] = "Ada" }),
            draft: BlueprintRequiring("amount", "name"));

        var (isValid, _, keywords) = ReadOk(result);

        isValid.Should().BeTrue();
        keywords.Should().BeEmpty();
    }

    [Fact]
    public async Task Validate_WithInstanceId_AnswersForThePinnedDefinition_NotTheDraft()
    {
        var resolver = new Mock<IActionResolverService>();

        // The draft has been relaxed since the instance started; the definition it is pinned to
        // still requires "name". A caller pre-flighting for that instance must get the pinned
        // verdict, or the endpoint predicts an acceptance that submission will refuse.
        var result = await InvokeAsync(
            Request(new Dictionary<string, object> { ["amount"] = 100 }, InstanceId),
            draft: BlueprintRequiring("amount"),
            instance: InstanceOn(BlueprintId, PinTxId),
            pinned: BlueprintRequiring("amount", "name"),
            resolver: resolver);

        var (isValid, scope, _) = ReadOk(result);

        isValid.Should().BeFalse("the pinned definition still requires \"name\"");
        scope.Should().Be("pinned");
        resolver.Verify(r => r.GetBlueprintAsync(BlueprintId, PinTxId, It.IsAny<CancellationToken>()),
            Times.Once, "the pin is what identifies the definition an instance runs (Feature 194)");
    }

    [Fact]
    public async Task Validate_WithInstanceIdWhoseBlueprintDisagrees_IsRefused()
    {
        var result = await InvokeAsync(
            Request(new Dictionary<string, object>(), InstanceId),
            instance: InstanceOn("some-other-blueprint", PinTxId));

        result.Should().BeAssignableTo<IStatusCodeHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        ReadError(result).Should().Contain("some-other-blueprint",
            "answering about a different blueprint than the caller named is the failure mode, not a "
            + "convenience");
    }

    [Fact]
    public async Task Validate_WithUnpinnedInstance_IsRefusedRatherThanAnsweredFromTheDraft()
    {
        var result = await InvokeAsync(
            Request(new Dictionary<string, object>(), InstanceId),
            draft: BlueprintRequiring("amount"),
            instance: InstanceOn(BlueprintId, pin: string.Empty));

        result.Should().BeAssignableTo<IStatusCodeHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        ReadError(result).Should().Contain("not pinned",
            "an unpinned instance IS the diagnosis; a plausible substitute answer would read as "
            + "healthy");
    }

    [Fact]
    public async Task Validate_WithInstanceIdWhosePinCannotBeResolved_IsRefused()
    {
        var result = await InvokeAsync(
            Request(new Dictionary<string, object>(), InstanceId),
            draft: BlueprintRequiring("amount"),
            instance: InstanceOn(BlueprintId, PinTxId),
            pinned: null);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        ReadError(result).Should().Contain(PinTxId);
    }
}
