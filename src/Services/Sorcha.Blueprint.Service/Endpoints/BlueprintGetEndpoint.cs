// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Blueprint.Service.Extensions;
using Sorcha.Blueprint.Service.JsonLd;
using Sorcha.Blueprint.Service.Services.Infrastructure;
using Sorcha.ServiceClients.Register;
using Sorcha.ServiceClients.Wallet;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;

namespace Sorcha.Blueprint.Service.Endpoints;

/// <summary>
/// #1683 fix — <c>GET /api/blueprints/{id}</c> used to serve only the org-scoped DRAFT store
/// (<see cref="IBlueprintService.GetByIdAsync"/>), so the counterparty in a two-party exchange — a
/// bound participant on a register carrying a PUBLISHED definition of this blueprint, but not a
/// member of the publishing organisation — got a 404 for a blueprint that plainly exists. Same class
/// as #1673: a scoping refusal presented as an absence.
/// </summary>
/// <remarks>
/// <para>
/// Fix: when the caller's own organisation does not own the draft, fall back to the PUBLISHED
/// definition on a register on which one of the caller's resolved wallets is an active participant.
/// This reuses existing plumbing rather than inventing a second path — Feature 195's
/// <see cref="IPublishedBlueprintStore"/> (already populated node-wide, not org-scoped, by
/// <c>BlueprintRecoveryService</c> from ledger content) plus
/// <see cref="IRegisterServiceClient.GetPublishedParticipantByAddressAsync"/> (the same "is this
/// wallet a participant on this register" primitive the execution path already relies on).
/// </para>
/// <para>
/// A published definition is already a ledger fact visible to that register's participants — serving
/// it here does not widen what the caller is entitled to read, it only stops re-deriving it from a
/// door (the org-scoped draft) that was never meant to gate it.
/// </para>
/// <para>
/// Returns 403 — not 404 — when a published definition exists but the caller is not a participant on
/// any register that carries it: that is a refusal, not an absence, and publication is public ledger
/// content, so naming the refusal does not disclose anything secret. 404 is reserved for the case
/// where nothing resolves at all (no draft, no publication this node knows of).
/// </para>
/// </remarks>
public static class BlueprintGetEndpoint
{
    /// <summary>Maps <c>GET /{id}</c> onto the group it's called on (the existing <c>/api/blueprints</c> group).</summary>
    public static void MapBlueprintGetEndpoint(this IEndpointRouteBuilder group)
    {
        group.MapGet("/{id}", GetBlueprintById)
            .WithName("GetBlueprintById")
            .WithSummary("Get blueprint by ID")
            .WithDescription(
                "Retrieve a specific blueprint by its unique identifier. Supports JSON-LD via Accept: "
                + "application/ld+json header. Resolves the caller's own organisation's draft first; "
                + "if that does not resolve, falls back to the published definition on a register the "
                + "caller participates in (#1683), so the counterparty in a two-party exchange can "
                + "read the contract it is acting under. Returns 403 (not 404) when a published "
                + "definition exists but the caller is not a participant on any register carrying it; "
                + "404 only when nothing resolves at all.")
            .Produces<BlueprintModel>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .CacheOutput(policy => policy.Expire(TimeSpan.FromMinutes(5)).Tag("blueprints"));
    }

    /// <summary>
    /// Handler for <see cref="MapBlueprintGetEndpoint"/>. Internal (not private) so tests can reach
    /// it by reflection without a <c>WebApplicationFactory</c> — see
    /// <c>tests/Sorcha.Blueprint.Service.Tests/Endpoints/BlueprintGetEndpointTests.cs</c>.
    /// </summary>
    internal static async Task<IResult> GetBlueprintById(
        HttpContext context,
        string id,
        IBlueprintService service,
        IPublishedBlueprintStore publishedStore,
        IWalletServiceClient walletClient,
        IRegisterServiceClient registerClient,
        ILogger<BlueprintGetEndpointLogCategory> logger,
        CancellationToken cancellationToken)
    {
        var orgId = context.IsServiceToken() ? null : context.GetOrganizationId();
        var draft = await service.GetByIdAsync(id, orgId);
        if (draft is not null)
        {
            if (context.AcceptsJsonLd())
            {
                draft = JsonLdHelper.EnsureJsonLdContext(draft);
            }

            return Results.Ok(draft);
        }

        // Not resolvable via the org-scoped draft (either genuinely absent, or owned by another
        // organisation). Fall back to a published definition the caller is entitled to see.
        var publishedVersions = (await publishedStore.GetVersionsAsync(id)).ToList();
        if (publishedVersions.Count == 0)
        {
            // Nothing this node knows of under this id at all — no draft, no publication. A plain
            // 404 discloses nothing that isn't already true.
            return Results.NotFound();
        }

        var callerWallets = await ParticipantWalletResolver.ResolveUserWalletAddressesAsync(
            context, walletClient, logger, cancellationToken);

        PublishedBlueprint? best = null;
        if (callerWallets.Count > 0)
        {
            foreach (var published in publishedVersions)
            {
                if (string.IsNullOrEmpty(published.RegisterId))
                {
                    continue;
                }

                var isParticipant = false;
                foreach (var wallet in callerWallets)
                {
                    var record = await registerClient.GetPublishedParticipantByAddressAsync(
                        published.RegisterId, wallet, cancellationToken);
                    if (record is not null
                        && !string.Equals(record.Status, "revoked", StringComparison.OrdinalIgnoreCase))
                    {
                        isParticipant = true;
                        break;
                    }
                }

                // Several registers may carry different published versions of this blueprint id;
                // serve the highest version the caller can actually see, not merely the first
                // participant match iterated.
                if (isParticipant && (best is null || published.Version > best.Version))
                {
                    best = published;
                }
            }
        }

        if (best is not null)
        {
            var result = best.Blueprint;
            if (context.AcceptsJsonLd())
            {
                result = JsonLdHelper.EnsureJsonLdContext(result);
            }

            logger.LogInformation(
                "Blueprint {BlueprintId} served from the PUBLISHED store (register {RegisterId}) to a "
                + "caller outside its owning organisation, via register participation (#1683)",
                id, best.RegisterId);

            return Results.Ok(result);
        }

        // A published definition exists somewhere, but the caller is not a participant on any
        // register that carries it. This is a REFUSAL, not an absence — say so, rather than
        // returning the same 404 a genuinely nonexistent blueprint would (#1673 class).
        return Results.Problem(
            "This blueprint is not available to your organisation. It has not been published to a "
            + "register you participate in.",
            statusCode: StatusCodes.Status403Forbidden);
    }

    /// <summary>Marker type purely to give <see cref="ILogger{TCategoryName}"/> a well-scoped category.</summary>
    public sealed class BlueprintGetEndpointLogCategory
    {
        private BlueprintGetEndpointLogCategory()
        {
        }
    }
}
