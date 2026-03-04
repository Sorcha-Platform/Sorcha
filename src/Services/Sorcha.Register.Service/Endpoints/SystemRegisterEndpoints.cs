// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Register.Service.Services;

namespace Sorcha.Register.Service.Endpoints;

/// <summary>
/// Minimal API endpoints for system register management including status queries
/// and blueprint retrieval.
/// </summary>
public static class SystemRegisterEndpoints
{
    /// <summary>
    /// Maps system register endpoints under <c>/api/system-register</c>.
    /// All endpoints require the <c>CanManageRegisters</c> authorization policy.
    /// </summary>
    /// <param name="app">The web application to map endpoints on</param>
    public static void MapSystemRegisterEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/system-register")
            .WithTags("System Register")
            .RequireAuthorization("CanManageRegisters");

        group.MapGet("/", async (SystemRegisterService service, CancellationToken ct) =>
        {
            var info = await service.GetSystemRegisterInfoAsync(ct);
            return Results.Ok(info);
        })
        .WithName("GetSystemRegisterInfo")
        .WithSummary("Get system register status and summary")
        .WithDescription(
            "Returns the current status of the system register including its deterministic ID, " +
            "display name, initialization status, blueprint count, and creation timestamp.");

        group.MapGet("/blueprints", async (
            SystemRegisterService service,
            int? page,
            int? pageSize,
            CancellationToken ct) =>
        {
            var allBlueprints = await service.GetAllBlueprintsAsync(ct);

            var effectivePage = Math.Max(1, page ?? 1);
            var effectivePageSize = Math.Clamp(pageSize ?? 20, 1, 100);

            var totalCount = allBlueprints.Count;
            var totalPages = (int)Math.Ceiling((double)totalCount / effectivePageSize);

            var items = allBlueprints
                .OrderByDescending(b => b.Version)
                .Skip((effectivePage - 1) * effectivePageSize)
                .Take(effectivePageSize)
                .Select(b => new BlueprintSummaryResponse
                {
                    BlueprintId = b.BlueprintId,
                    Version = b.Version,
                    PublishedAt = b.PublishedAt,
                    PublishedBy = b.PublishedBy,
                    IsActive = b.IsActive,
                    Metadata = b.Metadata
                })
                .ToList();

            return Results.Ok(new PaginatedBlueprintResponse
            {
                Items = items,
                Page = effectivePage,
                PageSize = effectivePageSize,
                TotalCount = totalCount,
                TotalPages = totalPages
            });
        })
        .WithName("GetSystemRegisterBlueprints")
        .WithSummary("List system register blueprints with pagination")
        .WithDescription(
            "Returns a paginated list of blueprints published to the system register. " +
            "Results are ordered by version descending (newest first). " +
            "Supports page and pageSize query parameters (default: page=1, pageSize=20, max=100).");

        group.MapGet("/blueprints/{blueprintId}", async (
            SystemRegisterService service,
            string blueprintId,
            CancellationToken ct) =>
        {
            var blueprint = await service.GetBlueprintAsync(blueprintId, ct);

            if (blueprint is null)
            {
                return Results.NotFound(new { error = $"Blueprint '{blueprintId}' not found in system register" });
            }

            return Results.Ok(blueprint);
        })
        .WithName("GetSystemRegisterBlueprint")
        .WithSummary("Get a specific blueprint from the system register")
        .WithDescription(
            "Retrieves a specific blueprint by its unique identifier from the system register. " +
            "Returns 404 if the blueprint does not exist.");

        group.MapGet("/blueprints/{blueprintId}/versions/{version:long}", async (
            SystemRegisterService service,
            string blueprintId,
            long version,
            CancellationToken ct) =>
        {
            // Get the blueprint and check version match
            var blueprint = await service.GetBlueprintAsync(blueprintId, ct);

            if (blueprint is null)
            {
                return Results.NotFound(new { error = $"Blueprint '{blueprintId}' not found in system register" });
            }

            if (blueprint.Version != version)
            {
                return Results.NotFound(new { error = $"Blueprint '{blueprintId}' version {version} not found. Current version is {blueprint.Version}" });
            }

            return Results.Ok(blueprint);
        })
        .WithName("GetSystemRegisterBlueprintVersion")
        .WithSummary("Get a specific version of a blueprint from the system register")
        .WithDescription(
            "Retrieves a specific version of a blueprint by its ID and version number. " +
            "Returns 404 if the blueprint or version does not exist.");
    }

    /// <summary>
    /// Summary response for a blueprint entry (without full document).
    /// </summary>
    private record BlueprintSummaryResponse
    {
        /// <summary>Blueprint unique identifier.</summary>
        public required string BlueprintId { get; init; }

        /// <summary>Blueprint version number.</summary>
        public long Version { get; init; }

        /// <summary>UTC timestamp when published.</summary>
        public DateTime PublishedAt { get; init; }

        /// <summary>Identity of the publisher.</summary>
        public required string PublishedBy { get; init; }

        /// <summary>Whether the blueprint is currently active.</summary>
        public bool IsActive { get; init; }

        /// <summary>Optional metadata key-value pairs.</summary>
        public Dictionary<string, string>? Metadata { get; init; }
    }

    /// <summary>
    /// Paginated response wrapper for blueprint listings.
    /// </summary>
    private record PaginatedBlueprintResponse
    {
        /// <summary>Blueprint items for the current page.</summary>
        public required List<BlueprintSummaryResponse> Items { get; init; }

        /// <summary>Current page number (1-based).</summary>
        public int Page { get; init; }

        /// <summary>Number of items per page.</summary>
        public int PageSize { get; init; }

        /// <summary>Total number of blueprints.</summary>
        public int TotalCount { get; init; }

        /// <summary>Total number of pages.</summary>
        public int TotalPages { get; init; }
    }
}
