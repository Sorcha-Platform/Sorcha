// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Linq;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;

namespace Sorcha.Blueprint.Service.Endpoints;

/// <summary>
/// Feature 154 (B) — the citizen service catalogue: the consumer-tier read surface listing the
/// published services a citizen can start. A service is citizen-startable when its first action's
/// sender participant is open (no hard-coded wallet), so the citizen can initiate it by binding their
/// own wallet. Starting a service reuses the existing <c>POST /api/instances/</c> (CreateInstance).
/// </summary>
public static class CatalogueEndpoints
{
    /// <summary>Maps <c>GET /api/catalogue</c>.</summary>
    public static IEndpointRouteBuilder MapCatalogueEndpoints(this IEndpointRouteBuilder routes)
    {
        // Cross-tier "any-human" surface (like /api/actions/pending) — a consumer-tier citizen token
        // is accepted. Lists only startable services; starting still goes through CreateInstance authz.
        routes.MapGet("/api/catalogue", async (IPublishedBlueprintStore store) =>
        {
            var published = await store.GetAllLatestAsync();
            return Results.Ok(BuildCatalogue(published));
        })
        .RequireAuthorization()
        .WithName("GetServiceCatalogue")
        .WithSummary("List the services a citizen can start")
        .WithDescription(
            "Returns the published services whose first action can be initiated by the citizen "
            + "(an open first participant), each with a name, description, and the register it runs "
            + "on. Consumer-tier; start a service via POST /api/instances/.");

        return routes;
    }

    /// <summary>
    /// Filters published blueprints to the citizen catalogue: only those that are citizen-startable
    /// and have a register to run on, mapped to <see cref="CatalogueItem"/> and sorted by title.
    /// </summary>
    public static IReadOnlyList<CatalogueItem> BuildCatalogue(IEnumerable<PublishedBlueprint> published) =>
        published
            .Where(p => !string.IsNullOrEmpty(p.RegisterId) && p.Blueprint is not null && IsCitizenStartable(p.Blueprint))
            .Select(p => new CatalogueItem(
                BlueprintId: p.BlueprintId,
                Title: string.IsNullOrWhiteSpace(p.Blueprint.Title) ? p.BlueprintId : p.Blueprint.Title,
                Description: string.IsNullOrWhiteSpace(p.Blueprint.Description) ? null : p.Blueprint.Description,
                RegisterId: p.RegisterId!))
            .OrderBy(i => i.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// True when the blueprint's first action (lowest <c>Id</c>) has a sender participant that is
    /// open (null/empty <c>WalletAddress</c>), so a citizen can initiate it. False when there is no
    /// action, the sender can't be resolved, or the first action's sender is bound to a wallet.
    /// </summary>
    public static bool IsCitizenStartable(BlueprintModel blueprint)
    {
        if (blueprint.Actions is null || blueprint.Actions.Count == 0)
        {
            return false;
        }

        var first = blueprint.Actions.OrderBy(a => a.Id).First();
        if (string.IsNullOrEmpty(first.Sender))
        {
            return false;
        }

        var sender = blueprint.Participants?.FirstOrDefault(p => p.Id == first.Sender);
        if (sender is null)
        {
            return false;
        }

        return string.IsNullOrEmpty(sender.WalletAddress);
    }
}

/// <summary>Feature 154 — a startable service in the citizen catalogue.</summary>
/// <param name="BlueprintId">The service's blueprint id.</param>
/// <param name="Title">Display name.</param>
/// <param name="Description">Short description, if any.</param>
/// <param name="RegisterId">The register the service runs on (needed to start it).</param>
public sealed record CatalogueItem(string BlueprintId, string Title, string? Description, string RegisterId);
