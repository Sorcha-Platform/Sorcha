// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;
using Sorcha.Register.Core.LocalRelationship;
using Sorcha.Register.Core.Observations;
using Sorcha.Register.Core.Storage;
using Sorcha.Register.Core.SyncState;
using Sorcha.Register.Models.LocalRelationship;

namespace Sorcha.Register.Service.Endpoints;

/// <summary>
/// Feature 108 — endpoints exposing derived per-register state:
/// <list type="bullet">
///   <item><c>GET /api/registers/{registerId}/local-relationship</c></item>
///   <item><c>GET /api/registers/{registerId}/sync-state</c></item>
///   <item><c>GET /api/internal/my-validated-registers</c></item>
/// </list>
/// </summary>
public static class RelationshipEndpoints
{
    public static IEndpointRouteBuilder MapRelationshipEndpoints(this IEndpointRouteBuilder app)
    {
        // Public-ish read — requires standard transaction-read policy.
        app.MapGet("/api/registers/{registerId}/local-relationship", async (
            string registerId,
            IRegisterLocalRelationshipService relationshipService,
            CancellationToken ct) =>
        {
            var relationship = await relationshipService.DeriveAsync(registerId, ct);
            return relationship is null
                ? Results.NotFound(new { error = $"Register '{registerId}' not known locally" })
                : Results.Ok(relationship);
        })
        .WithName("GetRegisterLocalRelationship")
        .WithTags("Registers — Feature 108")
        .WithSummary("Get this node's derived role set for a register (Feature 108)")
        .WithDescription("Returns the local-installation's role set on the register, derived from the latest control record and local identity. Cached; recomputed on control-transaction seal.")
        // Read-only endpoint: the standard transaction-read policy (any authenticated caller that
        // can read the register), NOT the write policy CanWriteDockets — which 403'd legitimate
        // readers such as admins, subscribers, and AI agents (#910).
        .RequireAuthorization("CanReadTransactions")
        .Produces<RegisterLocalRelationship>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status404NotFound);

        // Sync-state endpoint — returns the typed view with observable inputs.
        app.MapGet("/api/registers/{registerId}/sync-state", async (
            string registerId,
            IReadOnlyRegisterRepository registerRepository,
            IObservationStore observationStore,
            IRegisterSyncStateResolver resolver,
            CancellationToken ct) =>
        {
            var register = await registerRepository.GetRegisterAsync(registerId, ct);
            if (register is null)
                return Results.NotFound(new { error = $"Register '{registerId}' not known locally" });

            var view = resolver.Resolve(
                registerId: registerId,
                localHeight: register.Height,
                observations: observationStore.GetRecentPeerHeights(registerId, resolver.StalenessWindow),
                validatorSealing: observationStore.GetLatestValidatorSealing(registerId),
                persistedState: register.SyncState,
                lastErrorMessage: null);

            return Results.Ok(view);
        })
        .WithName("GetRegisterSyncState")
        .WithTags("Registers — Feature 108")
        .WithSummary("Get typed sync state for a register with the inputs that derived it (Feature 108)")
        .WithDescription("Returns the resolved sync state for a register (synced, recovering, stalled) along with the inputs the resolver used: local docket height, recent peer-height observations within the staleness window, the latest validator sealing observation, and the persisted state. AI agents call this to decide whether a register is current enough to read from before consuming transactions or generating verification bundles.")
        // Read-only endpoint: the standard transaction-read policy, NOT the write policy
        // CanWriteDockets — which 403'd legitimate readers (admins, subscribers, AI agents) (#910).
        .RequireAuthorization("CanReadTransactions")
        .Produces<RegisterSyncStateView>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status404NotFound);

        // Internal — list registers whose roster includes the caller's validator key.
        app.MapGet("/api/internal/my-validated-registers", async (
            HttpContext ctx,
            IRegisterLocalRelationshipService relationshipService,
            CancellationToken ct) =>
        {
            if (!ctx.Request.Headers.TryGetValue("X-Validator-Public-Key", out var headerValues) ||
                headerValues.Count == 0 || string.IsNullOrWhiteSpace(headerValues[0]))
            {
                return Results.BadRequest(new { error = "X-Validator-Public-Key header is required" });
            }

            byte[] validatorKey;
            try
            {
                validatorKey = Convert.FromBase64String(headerValues[0]!);
            }
            catch (FormatException)
            {
                return Results.BadRequest(new { error = "X-Validator-Public-Key is not valid Base64" });
            }

            var all = await relationshipService.DeriveAllAsync(validatorKey, ct);
            var registerIds = all
                .Where(r => r.IsValidator)
                .Select(r => r.RegisterId)
                .ToArray();

            return Results.Ok(new { registerIds });
        })
        .WithName("GetMyValidatedRegisters")
        .WithTags("Registers — Feature 108")
        .WithSummary("List registers whose roster includes the caller's validator public key (Feature 108)")
        .WithDescription("Called by Validator.Service at startup and on relationship-change events to seed its monitoring enrolment. The caller supplies its public key via the X-Validator-Public-Key header.")
        .RequireAuthorization(AuthorizationPolicies.CanWriteDockets)
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .ExcludeFromDescription();

        return app;
    }
}
