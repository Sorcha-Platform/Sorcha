// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

#pragma warning disable ASPDEPR002 // WithOpenApi is deprecated; using it for co-located endpoint examples until transformer API stabilizes

using System.Security.Claims;
using Sorcha.Blueprint.Service.Models;
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
            var walletAddresses = await ResolveUserWalletAddressesAsync(
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
            var walletAddresses = await ResolveUserWalletAddressesAsync(
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

        return routes;
    }

    /// <summary>
    /// Resolves the set of wallet addresses the authenticated user owns, in
    /// priority order:
    /// <list type="number">
    /// <item><description>The <c>wallet_address</c> JWT claim (fast path, zero-RTT).</description></item>
    /// <item><description>Live lookup via <see cref="IWalletServiceClient.GetWalletsByOwnerAsync"/>
    /// keyed on the <c>sub</c> / <see cref="ClaimTypes.NameIdentifier"/> claim,
    /// used when the JWT claim is absent (e.g. because the token was issued
    /// before the user created their first wallet and no refresh has happened
    /// since).</description></item>
    /// </list>
    /// The fallback is what makes the pending-actions query self-healing —
    /// without it, a consumer who signs up and then creates a wallet in the
    /// same session would see an empty pending-actions list forever, even
    /// though the server has the action and knows which wallet it belongs to.
    /// Returns an empty list when neither path yields a wallet — the caller
    /// should surface an empty result rather than a 401.
    /// </summary>
    private static async Task<IReadOnlyList<string>> ResolveUserWalletAddressesAsync(
        HttpContext httpContext,
        IWalletServiceClient walletClient,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var claim = httpContext.User.FindFirst("wallet_address")?.Value;
        if (!string.IsNullOrEmpty(claim))
        {
            return new[] { claim };
        }

        var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? httpContext.User.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Array.Empty<string>();
        }

        try
        {
            var wallets = await walletClient.GetWalletsByOwnerAsync(userId, cancellationToken);
            if (wallets.Count == 0)
            {
                return Array.Empty<string>();
            }

            return wallets.Select(w => w.Address).Where(a => !string.IsNullOrEmpty(a)).ToArray();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Non-fatal: surface an empty list so the endpoint still returns
            // a clean 200 rather than cascading a 500 to the user. The user
            // will see "no pending actions" which is the same visible
            // outcome as before this fix — the upside is that when the
            // wallet service IS reachable the list now populates correctly.
            // Cancellation is explicitly NOT caught here — tab-close / abort
            // must propagate cleanly so ASP.NET's request pipeline can short
            // circuit without a bogus 200 response.
            logger.LogWarning(ex,
                "Failed to resolve wallet addresses for user {UserId} via Wallet Service fallback; returning empty list",
                userId);
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Marker type for <see cref="ILogger{T}"/> categorisation. Kept internal
    /// and purpose-only so the log category is stable and obvious in Serilog
    /// output, and so the action endpoints don't need to resolve
    /// <c>ILoggerFactory</c> just to create a named logger.
    /// </summary>
    internal sealed class ActionEndpointsLogCategory { }
}
