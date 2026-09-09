// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Sorcha.Blueprint.Engine.Interfaces;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.Blueprint.Service.Storage;

namespace Sorcha.Blueprint.Service.Endpoints;

/// <summary>
/// The handler behind <c>POST /api/execution/validate</c> — the pre-flight "would this payload be
/// accepted?" helper (issue #1606).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why it is a named method rather than an inline lambda.</b> This endpoint answered
/// <c>isValid: true</c> with an empty <c>errors</c> array for <i>any</i> payload against <i>any</i>
/// action, for as long as it has existed, because the engine it delegates to validated
/// <c>Action.Form.Schema</c> — a property no published blueprint sets (#1573, fixed in #1604).
/// Unlike the other two sites in that issue, which failed silently, this one actively asserted the
/// opposite of the truth to a caller who had asked the question directly. A helper that regressed
/// that way once, with nothing noticing, needs a guard of its own, and a lambda inside
/// <c>Program.cs</c> is not reachable from a test.
/// </para>
/// <para>
/// <b>Which definition it answers for.</b> Two modes, and the caller chooses:
/// </para>
/// <list type="bullet">
/// <item>
/// <description>
/// With <c>instanceId</c> — resolves the definition that instance is <b>pinned</b> to
/// (<see cref="Sorcha.Blueprint.Service.Models.Instance.BlueprintDefinitionTxId"/>, Feature 194/195)
/// through the same <see cref="IActionResolverService"/> the execution path uses. This is the only
/// mode whose answer is guaranteed to match what submission actually does.
/// </description>
/// </item>
/// <item>
/// <description>
/// Without it — resolves the draft store by blueprint id, which is what this endpoint has always
/// done. It is the right contract for an authoring surface pre-flighting a payload against a
/// blueprint being edited, and the wrong one for pre-flighting against a running instance, so the
/// response says which definition it answered for rather than leaving the caller to assume.
/// </description>
/// </item>
/// </list>
/// <para>
/// Answering the wrong definition is the same class of defect as #1605: a pre-flight check that
/// confidently predicts a different verdict from the real one is worse than no check at all,
/// because people act on it.
/// </para>
/// </remarks>
public static class ExecutionValidationEndpoint
{
    /// <summary>
    /// Validates a payload against an action's declared data contract.
    /// </summary>
    /// <param name="request">The blueprint, action, payload and optional instance to answer for.</param>
    /// <param name="blueprintStore">Draft blueprint store, used only in the unpinned mode.</param>
    /// <param name="instanceStore">Instance store, used to read the pin when an instance is named.</param>
    /// <param name="actionResolver">Resolves the pinned definition, exactly as execution does.</param>
    /// <param name="executionEngine">The engine that applies the action's schemas.</param>
    /// <param name="logger">Logger for unexpected failures.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 with the verdict, or 400 naming what could not be resolved.</returns>
    public static async Task<IResult> HandleAsync(
        ValidateRequest request,
        IBlueprintStore blueprintStore,
        IInstanceStore instanceStore,
        IActionResolverService actionResolver,
        IExecutionEngine executionEngine,
        ILogger logger,
        CancellationToken ct = default)
    {
        try
        {
            Sorcha.Blueprint.Models.Blueprint? blueprint;
            string definitionScope;

            if (!string.IsNullOrWhiteSpace(request.InstanceId))
            {
                var instance = await instanceStore.GetAsync(request.InstanceId, ct);
                if (instance is null)
                {
                    return Results.BadRequest(new { error = "Instance not found" });
                }

                // Refuse rather than quietly answering about a different blueprint: the caller
                // supplied both, and a disagreement means one of them is not what they think.
                if (!string.Equals(instance.BlueprintId, request.BlueprintId, StringComparison.Ordinal))
                {
                    return Results.BadRequest(new
                    {
                        error = $"Instance '{request.InstanceId}' runs blueprint "
                                + $"'{instance.BlueprintId}', not '{request.BlueprintId}'."
                    });
                }

                if (string.IsNullOrEmpty(instance.BlueprintDefinitionTxId))
                {
                    // An unpinned instance predates Feature 194. Saying so is the diagnosis; a
                    // plausible substitute answer would read as healthy.
                    return Results.BadRequest(new
                    {
                        error = $"Instance '{request.InstanceId}' is not pinned to a published "
                                + "definition, so no pinned answer can be given. Omit instanceId to "
                                + "validate against the blueprint's current draft definition."
                    });
                }

                blueprint = await actionResolver.GetBlueprintAsync(
                    instance.BlueprintId, instance.BlueprintDefinitionTxId, ct);

                if (blueprint is null)
                {
                    return Results.BadRequest(new
                    {
                        error = $"The definition instance '{request.InstanceId}' is pinned to "
                                + $"('{instance.BlueprintDefinitionTxId}') could not be resolved on "
                                + "this node."
                    });
                }

                definitionScope = "pinned";
            }
            else
            {
                blueprint = await blueprintStore.GetAsync(request.BlueprintId);
                if (blueprint is null)
                {
                    return Results.BadRequest(new { error = "Blueprint not found" });
                }

                definitionScope = "draft";
            }

            if (!int.TryParse(request.ActionId, out var actionIdInt))
            {
                return Results.BadRequest(new { error = "Invalid action ID format" });
            }

            var action = blueprint.Actions.FirstOrDefault(a => a.Id == actionIdInt);
            if (action is null)
            {
                return Results.BadRequest(new { error = "Action not found in blueprint" });
            }

            var result = await executionEngine.ValidateAsync(request.Data, action, ct);

            return Results.Ok(new
            {
                isValid = result.IsValid,
                // Which definition the verdict is about. A pre-flight answer is only useful if the
                // caller knows what it was answered against.
                definitionScope,
                errors = result.Errors.Select(e => new
                {
                    path = e.InstanceLocation,
                    message = e.Message,
                    schemaLocation = e.SchemaLocation,
                    keyword = e.Keyword
                })
            });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Request failed");
            return Results.Problem("An error occurred processing the request.", statusCode: 400);
        }
    }
}
