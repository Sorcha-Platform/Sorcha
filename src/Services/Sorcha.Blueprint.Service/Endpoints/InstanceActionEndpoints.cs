// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Blueprint.Service.Models.Responses;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.Blueprint.Service.Storage;

namespace Sorcha.Blueprint.Service.Endpoints;

/// <summary>
/// P0 fix (<c>fix/pwa-p0-claim-and-camera</c>) — an instance-scoped, consumer-readable route to a
/// blueprint instance's current action schema. Feature 147 correctly narrowed
/// <c>GET /api/blueprints/{id}</c> (authoring) to service/platform-tier callers only; the Wallet PWA
/// was (mis)using that same door merely to read the form schema for the action a citizen was filling
/// in, so every consumer-tier citizen 403'd and the PWA folded that into a fabricated "offline" state.
/// This endpoint gives the citizen a narrow, participant-gated read instead of reopening authoring.
/// </summary>
public static class InstanceActionEndpoints
{
    /// <summary>
    /// Maps <c>GET /{instanceId}/actions/{actionId}</c> onto the group it's called on (the existing
    /// <c>/api/instances</c> group in <c>Program.cs</c>, already gated by <c>CanExecuteBlueprints</c>).
    /// </summary>
    public static void MapInstanceActionSchemaEndpoint(this IEndpointRouteBuilder group)
    {
        group.MapGet("/{instanceId}/actions/{actionId}", GetInstanceActionSchema)
            .WithName("GetInstanceActionSchema")
            .WithSummary("Get the renderable schema for an instance's action")
            .WithDescription(
                "Returns the form-relevant subset of a single action definition — schema, layout, "
                + "calculations, and this action's own credential gate — for a blueprint instance the "
                + "caller's wallet participates in. Unlike GET /api/blueprints/{id} (authoring, "
                + "service/platform-tier only), this endpoint is reachable by consumer-tier tokens so a "
                + "citizen can render the action currently assigned to them. Does not return routing "
                + "rules, other participants, or any other action's content. 403 if the caller's wallet "
                + "is not a participant on the instance; 404 if the instance, blueprint, or action is "
                + "not found.")
            .Produces<InstanceActionSchemaResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);
    }

    /// <summary>
    /// Handler for <see cref="MapInstanceActionSchemaEndpoint"/>. Internal (not private) so tests can
    /// reach it by reflection without a <c>WebApplicationFactory</c> — see
    /// <c>tests/Sorcha.Blueprint.Service.Tests/Endpoints/InstanceActionEndpointsTests.cs</c>.
    /// </summary>
    internal static async Task<IResult> GetInstanceActionSchema(
        HttpContext httpContext,
        string instanceId,
        int actionId,
        IInstanceStore instanceStore,
        IActionResolverService actionResolver,
        CancellationToken cancellationToken)
    {
        var instance = await instanceStore.GetAsync(instanceId, cancellationToken);
        if (instance is null)
        {
            return Results.NotFound(new { error = "Instance not found" });
        }

        // Participant gate — CanExecuteBlueprints only asserts "any authenticated user"; this
        // endpoint returns action content (form schema, credential config) so it adds its own check
        // on top, using the same "wallet is a participant on this instance" test the pending-actions
        // list already relies on (EfCoreInstanceStore.ContainsWalletAddress /
        // GetPendingActionsByWalletAsync) — a wallet only ever sees this instance as "pending" once it
        // is recorded as a participant, so this gate does not regress the flow it's fixing.
        var walletAddress = httpContext.User.FindFirst("wallet_address")?.Value;
        var isParticipant = !string.IsNullOrEmpty(walletAddress)
            && instance.ParticipantWallets.Values.Any(w => string.Equals(w, walletAddress, StringComparison.OrdinalIgnoreCase));
        if (!isParticipant)
        {
            return Results.Problem(
                "You are not a participant on this instance.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        var blueprint = await actionResolver.GetBlueprintAsync(instance.BlueprintId, cancellationToken);
        if (blueprint is null)
        {
            return Results.NotFound(new
            {
                error = "Blueprint is not available on this node yet.",
                blueprintId = instance.BlueprintId,
            });
        }

        var action = actionResolver.GetActionDefinition(blueprint, actionId.ToString());
        if (action is null)
        {
            return Results.NotFound(new { error = $"Action {actionId} not found in blueprint {instance.BlueprintId}." });
        }

        var response = new InstanceActionSchemaResponse
        {
            ActionId = action.Id,
            Title = action.Title,
            Form = action.Form,
            DataSchemas = action.DataSchemas,
            Calculations = action.Calculations,
            CredentialRequirements = action.CredentialRequirements,
            CredentialIssuanceConfig = action.CredentialIssuanceConfig,
        };

        return Results.Ok(response);
    }
}
