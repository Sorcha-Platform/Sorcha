// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

#pragma warning disable ASPDEPR002 // WithOpenApi is deprecated; using it for co-located endpoint examples until transformer API stabilizes

using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Services.Infrastructure;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.Blueprint.Service.Storage;
using Sorcha.ServiceClients.Wallet;

namespace Sorcha.Blueprint.Service.Endpoints;

/// <summary>
/// Endpoints for querying pending actions across blueprint instances.
/// </summary>
public static class ActionEndpoints
{
    /// <summary>
    /// Maps action-related endpoints to the application.
    /// </summary>
    public static IEndpointRouteBuilder MapActionEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/actions")
            .WithTags("Actions")
            .RequireAuthorization();

        group.MapGet("/pending", async (
            HttpContext httpContext,
            IInstanceStore instanceStore,
            IWalletServiceClient walletClient,
            ILogger<ActionEndpointsLogCategory> logger,
            int page = 1,
            int pageSize = 20) =>
        {
            var walletAddresses = await ParticipantWalletResolver.ResolveUserWalletAddressesAsync(
                httpContext, walletClient, logger, httpContext.RequestAborted);

            if (walletAddresses.Count == 0)
            {
                return Results.Ok(new { items = Array.Empty<object>(), totalCount = 0, page, pageSize });
            }

            var skip = (page - 1) * pageSize;
            var pageUpperBound = skip + pageSize;

            // Fan out across all of the user's wallets and merge. Consumer
            // accounts usually have one wallet, but multi-wallet is
            // allowed. We over-fetch each wallet up to pageUpperBound so
            // the final interleaved sort + skip + take yields a correct
            // page even if one wallet is ahead of another in the ordering.
            //
            // NOTE: The merged pagination is approximate for users with
            // multiple wallets. If any single wallet has more than
            // pageUpperBound items, items beyond that ceiling will never
            // appear in the merged result on deeper pages. For the
            // primary use case — consumers with a single wallet — this
            // is exact. A proper multi-wallet query would fan out at
            // the store layer and do server-side merge-sort; tracked
            // for a future iteration when multi-wallet consumer flows
            // become load-bearing.
            var mergedItems = new List<PendingActionSummary>();
            var totalCount = 0;
            foreach (var wallet in walletAddresses)
            {
                var items = await instanceStore.GetPendingActionsByWalletAsync(
                    wallet, skip: 0, take: pageUpperBound);
                mergedItems.AddRange(items);

                totalCount += await instanceStore.GetPendingActionCountByWalletAsync(wallet);
            }

            var paged = mergedItems
                .OrderByDescending(s => s.ReceivedAt)
                .Skip(skip)
                .Take(pageSize)
                .ToList();

            return Results.Ok(new
            {
                items = paged,
                totalCount,
                page,
                pageSize
            });
        })
        .WithName("GetPendingActions")
        .WithSummary("Get pending actions for the authenticated user")
        .WithDescription("Returns all pending actions across blueprint instances for every wallet the user owns. "
            + "Resolves the user's wallets from the `wallet_address` JWT claim when present (fast path), and "
            + "falls back to a live Wallet Service lookup keyed by the user's `sub` claim when the claim is "
            + "absent — this keeps the endpoint self-healing for users whose token was issued before their "
            + "first wallet was created.")
        .WithOpenApi(operation =>
        {
            OpenApiExamples.SetResponseExample(operation, "200", """
                {
                  "items": [
                    {
                      "instanceId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
                      "actionId": "review-application",
                      "blueprintTitle": "Construction Permit Application",
                      "participantRole": "BuildingInspector",
                      "status": "Pending",
                      "createdAt": "2026-03-15T10:30:00Z",
                      "dueDate": "2026-03-22T10:30:00Z"
                    }
                  ],
                  "totalCount": 1,
                  "page": 1,
                  "pageSize": 20
                }
                """);
            return operation;
        });

        group.MapGet("/pending/count", async (
            HttpContext httpContext,
            IInstanceStore instanceStore,
            IWalletServiceClient walletClient,
            ILogger<ActionEndpointsLogCategory> logger) =>
        {
            var walletAddresses = await ParticipantWalletResolver.ResolveUserWalletAddressesAsync(
                httpContext, walletClient, logger, httpContext.RequestAborted);

            if (walletAddresses.Count == 0)
            {
                return Results.Ok(new { count = 0, urgentCount = 0 });
            }

            var total = 0;
            foreach (var wallet in walletAddresses)
            {
                total += await instanceStore.GetPendingActionCountByWalletAsync(wallet);
            }

            // TODO: urgentCount requires urgency-aware query — tracked for next iteration
            return Results.Ok(new { count = total, urgentCount = 0 });
        })
        .WithName("GetPendingActionCount")
        .WithSummary("Get pending action count for badge display")
        .WithDescription("Returns the count of pending actions across every wallet the authenticated user owns. "
            + "Uses the same wallet resolution path as GetPendingActions. urgentCount is currently always 0 — "
            + "urgency-aware counting will be added in a future iteration.");

        // Issue #1446 — the discovery surface for Feature 103 OPEN starting actions.
        //
        // These deliberately do NOT appear in /api/actions/pending. An open starting action is an
        // invitation, not an assignment: the participant who may perform it has no wallet on the
        // instance yet (that is what late binding means), so there is nobody to put it in the inbox
        // OF. Placing it in every pre-bound participant's inbox is what n1 was doing — the tenant's
        // "Report Problem" listed as the housing officer's work — and putting it in every
        // authenticated wallet's inbox instead would have made every citizen's list carry every open
        // instance on the node.
        //
        // So it is asked for, not pushed: blueprintId is REQUIRED, which is what makes this a
        // deliberate question ("which instances of the service I operate are waiting to be started?")
        // rather than an unbounded feed.
        //
        // Authorization is the group's plain RequireAuthorization, matching
        // InstanceParticipantGate.IsAwaitingOpenParticipant — the carve-out that already lets ANY
        // authenticated caller read GET /api/instances/{id} while it awaits its open participant,
        // because the walk-in applicant is not yet a participant on their own instance. This endpoint
        // therefore discloses nothing that audience cannot already read one instance at a time.
        group.MapGet("/open-starting", async (
            IInstanceStore instanceStore,
            IActionResolverService actionResolver,
            string blueprintId,
            string? registerId = null,
            int page = 1,
            int pageSize = 20) =>
        {
            if (string.IsNullOrWhiteSpace(blueprintId))
            {
                return Results.BadRequest(new { error = "blueprintId is required" });
            }

            page = page < 1 ? 1 : page;
            pageSize = pageSize is < 1 or > 100 ? 20 : pageSize;
            var skip = (page - 1) * pageSize;

            // Over-fetch, because the register filter and the open-participant test are applied
            // after the store query. Approximate on very deep pages, exactly like the sibling
            // /pending endpoint's multi-wallet merge; say so rather than imply otherwise.
            var candidates = await instanceStore.GetByBlueprintAsync(
                blueprintId, InstanceState.Active, skip: 0, take: skip + pageSize + OpenStartingOverFetch);

            var matches = new List<PendingActionSummary>();
            foreach (var instance in candidates)
            {
                if (!string.IsNullOrWhiteSpace(registerId)
                    && !string.Equals(instance.RegisterId, registerId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Resolve by the instance's PIN, never "latest" (Feature 194/195): whether a
                // participant is open is a property of the definition this instance runs.
                var blueprint = string.IsNullOrWhiteSpace(instance.BlueprintDefinitionTxId)
                    ? null
                    : await actionResolver.GetBlueprintAsync(
                        instance.BlueprintId, instance.BlueprintDefinitionTxId);

                // Fails closed on an unresolvable definition — an open-participant claim that cannot
                // be verified is not granted.
                if (!InstanceParticipantGate.IsAwaitingOpenParticipant(instance, blueprint))
                {
                    continue;
                }

                foreach (var actionId in instance.CurrentActionIds)
                {
                    if (IsUnboundOpenSender(blueprint!, actionResolver, instance, actionId))
                    {
                        matches.Add(PendingActionProjection.ToSummary(instance, actionId, blueprint, actionResolver));
                    }
                }
            }

            var paged = matches
                .OrderByDescending(m => m.ReceivedAt)
                .Skip(skip)
                .Take(pageSize)
                .ToList();

            return Results.Ok(new { items = paged, totalCount = matches.Count, page, pageSize });
        })
        .WithName("GetOpenStartingActions")
        .WithSummary("Get starting actions awaiting their open (late-bound) participant")
        .WithDescription(
            "Returns the current starting actions of active instances of `blueprintId` whose sender is a "
            + "Feature 103 open participant that has not been late-bound yet — i.e. workflows waiting for "
            + "somebody to start them. Deliberately separate from /api/actions/pending, which only ever "
            + "carries work assigned to the caller. `blueprintId` is required; `registerId` narrows further. "
            + "totalCount reflects the over-fetched candidate window, so it is approximate on deep pages.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized);

        return routes;
    }

    /// <summary>
    /// How many extra candidate instances to pull beyond the requested page, to absorb those the
    /// register filter and the open-participant test discard.
    /// </summary>
    private const int OpenStartingOverFetch = 100;

    /// <summary>
    /// Whether <paramref name="actionId"/> is a starting action whose sender is an open participant
    /// with no binding yet — the exact complement of the case
    /// <c>EfCoreInstanceStore.IsActionForWallet</c> now excludes from the personal list, so an action
    /// never falls between the two surfaces or appears on both.
    /// </summary>
    internal static bool IsUnboundOpenSender(
        Sorcha.Blueprint.Models.Blueprint blueprint,
        IActionResolverService actionResolver,
        Instance instance,
        int actionId)
    {
        var action = actionResolver.GetActionDefinition(blueprint, actionId.ToString());
        if (action is null || !action.IsStartingAction || string.IsNullOrEmpty(action.Sender))
        {
            return false;
        }

        if (instance.ParticipantWallets.TryGetValue(action.Sender, out var bound) && !string.IsNullOrEmpty(bound))
        {
            return false;
        }

        var sender = blueprint.Participants
            .FirstOrDefault(p => string.Equals(p.Id, action.Sender, StringComparison.OrdinalIgnoreCase));

        return sender is not null && string.IsNullOrEmpty(sender.WalletAddress);
    }

    /// <summary>
    /// Marker type for <see cref="ILogger{T}"/> categorisation. Kept internal
    /// and purpose-only so the log category is stable and obvious in Serilog
    /// output, and so the action endpoints don't need to resolve
    /// <c>ILoggerFactory</c> just to create a named logger.
    /// </summary>
    internal sealed class ActionEndpointsLogCategory { }
}
