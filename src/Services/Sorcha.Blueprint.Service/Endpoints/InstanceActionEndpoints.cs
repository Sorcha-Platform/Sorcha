// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Blueprint.Service.Models.Responses;
using Sorcha.Blueprint.Service.Services.Infrastructure;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.Blueprint.Service.Storage;
using Sorcha.ServiceClients.Wallet;

namespace Sorcha.Blueprint.Service.Endpoints;

/// <summary>
/// P0 fix (<c>fix/pwa-p0-claim-and-camera</c>) — an instance-scoped, consumer-readable route to a
/// blueprint instance's current action schema. Feature 147 correctly narrowed
/// <c>GET /api/blueprints/{id}</c> (authoring) to service/platform-tier callers only; the Wallet PWA
/// was (mis)using that same door merely to read the form schema for the action a citizen was filling
/// in, so every consumer-tier citizen 403'd and the PWA folded that into a fabricated "offline" state.
/// This endpoint gives the citizen a narrow, participant-gated read instead of reopening authoring.
///
/// The participant gate originally read only the <c>wallet_address</c> JWT claim — but under
/// Feature 136 a consumer-tier token (every real PWA sign-in) deliberately OMITS that claim, so the
/// gate was unconditionally false for the exact population the endpoint exists to serve. Fixed by
/// routing through <see cref="ParticipantWalletResolver.ResolveUserWalletAddressesAsync"/> (the same
/// claim-then-Wallet-Service-fallback seam <c>GetPendingActions</c> / <c>GetPendingActionCount</c> /
/// <c>WorkflowDisclosureEndpoints</c> already rely on) and checking membership against the full set
/// of the caller's resolved wallets, not a single claim value.
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
                + "rules, other participants, or any other action's content. The caller's wallet(s) are "
                + "resolved from the wallet_address claim when present, else via a Wallet-Service lookup "
                + "by owner (consumer-tier tokens carry no wallet_address per Feature 136) — 403 if none "
                + "of the caller's wallets is a participant on the instance; 404 if the instance, "
                + "blueprint, or action is not found.")
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
        IWalletServiceClient walletClient,
        ILogger<InstanceActionEndpointsLogCategory> logger,
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
        //
        // A consumer-tier token (every real PWA sign-in, Feature 136) never carries wallet_address, so
        // the caller's wallet(s) must be resolved via ParticipantWalletResolver (claim fast-path, then
        // Wallet-Service-by-owner fallback) rather than read off a single claim. A caller can hold more
        // than one wallet, so membership is checked against the FULL resolved set — matching any one of
        // them is enough. An empty resolved set means "couldn't resolve any wallet for this caller", not
        // "didn't look" (ParticipantWalletResolver only returns empty after exhausting the claim and the
        // owner-keyed Wallet Service lookup) — so it fails closed to 403 like any other non-match.
        var callerWallets = await ParticipantWalletResolver.ResolveUserWalletAddressesAsync(
            httpContext, walletClient, logger, cancellationToken);
        var isParticipant = callerWallets.Count > 0
            && instance.ParticipantWallets.Values.Any(participantWallet =>
                callerWallets.Any(w => string.Equals(w, participantWallet, StringComparison.OrdinalIgnoreCase)));
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

    /// <summary>
    /// Marker type for <see cref="ILogger{T}"/> categorisation — <see cref="InstanceActionEndpoints"/>
    /// is static, so it cannot itself be used as a generic type argument. Mirrors
    /// <c>ActionEndpoints.ActionEndpointsLogCategory</c>.
    /// </summary>
    internal sealed class InstanceActionEndpointsLogCategory { }
}
