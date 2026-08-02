// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Services.Implementation;
using Sorcha.Blueprint.Service.Services.Infrastructure;
using Sorcha.Blueprint.Service.Storage;
using Sorcha.ServiceClients.Wallet;

namespace Sorcha.Blueprint.Service.Endpoints;

/// <summary>
/// Feature 186 (#1163) — the citizen's "My Applications" read surface: what they submitted, where it
/// got to, and what was decided.
/// </summary>
/// <remarks>
/// <para>
/// A sibling of <c>/api/instances</c> rather than a reshaping of it. Both endpoints on that group
/// have live consumers — the citizen wallet app binds <c>GET /api/instances/{id}</c> — so changing
/// their shape would break a working surface to serve a new one. This group returns a purpose-built
/// projection instead, and the raw model reads are untouched.
/// </para>
/// <para>
/// Placed under <c>/api/me/*</c>, the platform's established personal-scope convention (Tenant
/// Service's inbox, persona and 2FA channel groups). This is Blueprint Service's first.
/// </para>
/// <para>
/// <b>Authorization is deliberately plain.</b> Not <c>RequireConsumerAudience</c>: the same citizen
/// holds a consumer-tier token on <c>/wallet</c> and a platform-tier token on <c>/app</c>, and must
/// see the same applications from either. Per CLAUDE.md pattern #13 there is no "any-human" tier, so
/// genuinely cross-tier endpoints stay unclassified rather than being force-fitted to one.
/// </para>
/// </remarks>
public static class MeApplicationEndpoints
{
    /// <summary>Largest page a caller may request; larger values are clamped down to it.</summary>
    private const int MaxPageSize = 100;

    /// <summary>Log category anchor — mirrors the pattern in <see cref="InstanceReadEndpoints"/>.</summary>
    internal sealed class MeApplicationEndpointsLogCategory;

    /// <summary>
    /// Maps <c>GET /api/me/applications</c> and <c>GET /api/me/applications/{instanceId}</c>.
    /// </summary>
    /// <param name="app">The route builder to map onto.</param>
    /// <returns>The mapped group, for further configuration.</returns>
    public static RouteGroupBuilder MapMeApplicationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/me/applications")
            .WithTags("My Applications")
            .RequireAuthorization();

        group.MapGet("/", ListMyApplications)
            .WithName("ListMyApplications")
            .WithSummary("List the caller's own applications")
            .WithDescription(
                "Returns every workflow instance the authenticated caller participates in, including "
                + "those in terminal states, newest-first and deduplicated across all wallets the "
                + "caller controls. Each row carries the service title, human-readable reference, "
                + "lifecycle state as a NAME (not an integer), step position, whether the caller is "
                + "the next actor, and — where the taken route declared an x-decision-notice — the "
                + "citizen-facing decision title and reason resolved from the blueprint's own "
                + "catalogue. The internal reason code is never returned. A caller with no resolvable "
                + "wallet receives an empty page rather than an error.")
            .Produces<MyApplicationPage<MyApplicationSummary>>(StatusCodes.Status200OK);

        group.MapGet("/{instanceId}", GetMyApplication)
            .WithName("GetMyApplication")
            .WithSummary("Get one of the caller's own applications")
            .WithDescription(
                "Returns a single application with its step timeline. Readable only by a caller "
                + "controlling a wallet recorded as a participant on it. A caller who does not "
                + "participate receives exactly the response given for an application that does not "
                + "exist, so the endpoint cannot be used to probe which instance ids are real.")
            .Produces<MyApplicationDetail>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        return group;
    }

    /// <summary>
    /// Handler for <c>GET /api/me/applications</c>. Internal so tests can reach it by reflection,
    /// following the pattern established by <c>InstanceActionEndpointsTests</c>.
    /// </summary>
    internal static async Task<IResult> ListMyApplications(
        HttpContext httpContext,
        IInstanceStore instanceStore,
        IBlueprintStore blueprintStore,
        IWalletServiceClient walletClient,
        ILogger<MeApplicationEndpointsLogCategory> logger,
        InstanceState? status,
        // Nullable, not `int` with a default. A non-nullable minimal-API query parameter is
        // REQUIRED: omitting it returns 400 "Required parameter "int page" was not provided from
        // query string" — which is what a plain GET /api/me/applications did, and no unit test
        // could see because the reflection-invoked handler always received explicit values.
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        var requestedPage = page ?? 1;
        var requestedPageSize = pageSize ?? 20;
        var resolvedPage = requestedPage < 1 ? 1 : requestedPage;
        var resolvedPageSize = requestedPageSize < 1 ? 20 : Math.Min(requestedPageSize, MaxPageSize);

        var callerWallets = await ParticipantWalletResolver.ResolveUserWalletAddressesAsync(
            httpContext, walletClient, logger, cancellationToken);

        if (callerWallets.Count == 0)
        {
            // "Couldn't resolve", not "not allowed". A citizen who has not yet created a wallet has
            // no applications, and an empty list is the truthful answer to that.
            return Results.Ok(new MyApplicationPage<MyApplicationSummary>([], 0, resolvedPage, resolvedPageSize));
        }

        // A caller may control several wallets, so the page spans all of them. Deduplicated by
        // instance id — holding two participant wallets on one application must not list it twice —
        // and ordered newest-first with an id tiebreak, because the store promises no ordering of its
        // own and paging over an unstable order silently skips and repeats rows.
        var combined = new Dictionary<string, Instance>(StringComparer.Ordinal);
        foreach (var wallet in callerWallets)
        {
            foreach (var instance in await instanceStore.GetByParticipantWalletAsync(
                         wallet, status, 0, int.MaxValue, cancellationToken))
            {
                combined[instance.Id] = instance;
            }
        }

        var ordered = combined.Values
            .OrderByDescending(i => i.CreatedAt)
            .ThenBy(i => i.Id, StringComparer.Ordinal)
            .ToList();

        var pageItems = ordered.Skip((resolvedPage - 1) * resolvedPageSize).Take(resolvedPageSize).ToList();

        // One blueprint fetch per distinct blueprint on the page, not per row.
        var blueprints = await LoadBlueprintsAsync(pageItems, blueprintStore);

        var rows = pageItems
            .Select(i => MyApplicationProjector.Project(i, Lookup(blueprints, i.BlueprintId), callerWallets))
            .ToList();

        return Results.Ok(new MyApplicationPage<MyApplicationSummary>(
            rows, ordered.Count, resolvedPage, resolvedPageSize));
    }

    /// <summary>
    /// Handler for <c>GET /api/me/applications/{instanceId}</c>. Internal for the same reason as
    /// <see cref="ListMyApplications"/>.
    /// </summary>
    internal static async Task<IResult> GetMyApplication(
        HttpContext httpContext,
        string instanceId,
        IInstanceStore instanceStore,
        IBlueprintStore blueprintStore,
        IWalletServiceClient walletClient,
        ILogger<MeApplicationEndpointsLogCategory> logger,
        CancellationToken cancellationToken)
    {
        var instance = await instanceStore.GetAsync(instanceId, cancellationToken);
        if (instance is null)
        {
            return NotFound();
        }

        var isParticipant = await InstanceParticipantGate.IsParticipantAsync(
            httpContext, instance, walletClient, logger, cancellationToken);

        if (!isParticipant)
        {
            // Identical to the not-found response above, deliberately. This is the citizen's OWN
            // application list — unlike the /api/instances gate there is no open-participant arm,
            // because an unclaimed shell belongs to nobody and must not appear as anyone's.
            return NotFound();
        }

        var callerWallets = await ParticipantWalletResolver.ResolveUserWalletAddressesAsync(
            httpContext, walletClient, logger, cancellationToken);

        var blueprint = await blueprintStore.GetAsync(instance.BlueprintId);

        return Results.Ok(MyApplicationProjector.ProjectDetail(instance, blueprint, callerWallets));
    }

    /// <summary>
    /// Deliberately identical for "not yours" and "does not exist" (#1183), so the id space cannot be
    /// probed by reading the difference between two error bodies.
    /// </summary>
    private static IResult NotFound() =>
        Results.Problem("Application not found.", statusCode: StatusCodes.Status404NotFound);

    private static async Task<Dictionary<string, Sorcha.Blueprint.Models.Blueprint>> LoadBlueprintsAsync(
        IReadOnlyList<Instance> instances, IBlueprintStore blueprintStore)
    {
        var result = new Dictionary<string, Sorcha.Blueprint.Models.Blueprint>(StringComparer.Ordinal);

        foreach (var blueprintId in instances.Select(i => i.BlueprintId).Distinct(StringComparer.Ordinal))
        {
            // A blueprint absent from this node is expected in a federated deployment; the projector
            // degrades to the id rather than dropping the row.
            var blueprint = await blueprintStore.GetAsync(blueprintId);
            if (blueprint is not null)
            {
                result[blueprintId] = blueprint;
            }
        }

        return result;
    }

    private static Sorcha.Blueprint.Models.Blueprint? Lookup(
        Dictionary<string, Sorcha.Blueprint.Models.Blueprint> blueprints, string blueprintId) =>
        blueprints.TryGetValue(blueprintId, out var blueprint) ? blueprint : null;
}
