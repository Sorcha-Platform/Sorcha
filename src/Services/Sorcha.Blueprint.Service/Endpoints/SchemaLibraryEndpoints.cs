// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Sorcha.Blueprint.Schemas.Models;
using Sorcha.Blueprint.Service.Extensions;
using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Services;
using Sorcha.Blueprint.Service.Services.Interfaces;

namespace Sorcha.Blueprint.Service.Endpoints;

/// <summary>
/// Schema Library API endpoints for index search, sectors, provider health, and derived schemas.
/// </summary>
public static class SchemaLibraryEndpoints
{
    /// <summary>
    /// Maps schema library endpoints to the application.
    /// </summary>
    public static void MapSchemaLibraryEndpoints(this WebApplication app)
    {
        var indexGroup = app.MapGroup("/api/v1/schemas/index")
            .WithTags("Schema Index")
            .RequireAuthorization();

        var sectorGroup = app.MapGroup("/api/v1/schemas/sectors")
            .WithTags("Sectors")
            .RequireAuthorization();

        var providerGroup = app.MapGroup("/api/v1/schemas/providers")
            .WithTags("Provider Health")
            .RequireAuthorization("RequireAdministrator");

        var derivedGroup = app.MapGroup("/api/v1/schemas/derived")
            .WithTags("Derived Schemas")
            .RequireAuthorization();

        // === Schema Index endpoints ===

        // GET /api/v1/schemas/index - Search the schema index
        indexGroup.MapGet("/", SearchSchemaIndex)
            .WithName("SearchSchemaIndex")
            .WithSummary("Search the schema index")
            .WithDescription("Full-text search across all indexed schemas. Results are filtered by the requesting user's organisation sector preferences.")
            .Produces<SchemaIndexSearchResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        // GET /api/v1/schemas/index/{shortCode} - Get entry with full content
        indexGroup.MapGet("/{shortCode}", GetSchemaIndexEntry)
            .WithName("GetSchemaIndexEntry")
            .WithSummary("Get a specific schema index entry with full content")
            .WithDescription("Returns full schema index entry metadata plus the JSON Schema content by short code.")
            .Produces<object>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        // GET /api/v1/schemas/index/content/{shortCode} - Get raw JSON Schema
        indexGroup.MapGet("/content/{shortCode}", GetSchemaContent)
            .WithName("GetSchemaContent")
            .WithSummary("Get the full JSON Schema content for an indexed schema")
            .WithDescription("Returns the normalised JSON Schema draft-2020-12 content for a schema by short code.")
            .Produces<object>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        // === Sector endpoints ===

        // GET /api/v1/schemas/sectors - List all sectors
        sectorGroup.MapGet("/", ListSectors)
            .WithName("ListSectors")
            .WithSummary("List all available schema sectors")
            .WithDescription("Returns the platform-curated list of schema sectors.")
            .Produces<IReadOnlyList<SchemaSectorDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        // GET /api/v1/schemas/sectors/preferences - Get org preferences
        sectorGroup.MapGet("/preferences", GetSectorPreferences)
            .WithName("GetSectorPreferences")
            .WithSummary("Get organisation's sector visibility preferences")
            .WithDescription("Returns which sectors are enabled for the requesting user's organisation.")
            .Produces<OrganisationSectorPreferencesDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        // PUT /api/v1/schemas/sectors/preferences - Update org preferences
        sectorGroup.MapPut("/preferences", UpdateSectorPreferences)
            .WithName("UpdateSectorPreferences")
            .WithSummary("Update organisation's sector visibility preferences")
            .WithDescription("Sets which sectors are visible to designers. Requires Administrator role.")
            .Produces<OrganisationSectorPreferencesDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .RequireAuthorization("RequireAdministrator");

        // === Provider Health endpoints ===

        // GET /api/v1/schemas/providers - List providers with health
        providerGroup.MapGet("/", ListProviders)
            .WithName("ListSchemaProviders")
            .WithSummary("List all schema providers with health status")
            .WithDescription("Returns the status of all configured schema providers including health, last fetch time, schema count, and error details.")
            .Produces<IReadOnlyList<SchemaProviderStatusDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        // POST /api/v1/schemas/providers/{providerName}/refresh - Trigger refresh
        providerGroup.MapPost("/{providerName}/refresh", RefreshProvider)
            .WithName("RefreshSchemaProvider")
            .WithSummary("Trigger manual refresh for a specific provider")
            .WithDescription("Triggers an immediate index refresh for the specified provider. Returns immediately — refresh runs in background.")
            .Produces(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status429TooManyRequests);
    }

    // === Handler implementations ===

    private static async Task<Ok<SchemaIndexSearchResponse>> SearchSchemaIndex(
        HttpContext context,
        ISchemaIndexService indexService,
        [FromQuery] string? search = null,
        [FromQuery] string? sectors = null,
        [FromQuery] string? provider = null,
        [FromQuery] string? status = null,
        [FromQuery] int limit = 25,
        [FromQuery] string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var organizationId = context.GetOrganizationId();

        // Parse sectors
        string[]? sectorArray = null;
        if (!string.IsNullOrWhiteSpace(sectors))
        {
            sectorArray = sectors.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        // Parse status
        SchemaIndexStatus? statusFilter = null;
        if (!string.IsNullOrEmpty(status) && Enum.TryParse<SchemaIndexStatus>(status, true, out var parsedStatus))
        {
            statusFilter = parsedStatus;
        }

        // Clamp limit
        limit = Math.Clamp(limit, 1, 100);

        var result = await indexService.SearchAsync(
            search, sectorArray, provider, statusFilter, limit, cursor, organizationId, cancellationToken);

        return TypedResults.Ok(result);
    }

    private static async Task<IResult> GetSchemaIndexEntry(
        string shortCode,
        ISchemaIndexService indexService,
        CancellationToken cancellationToken)
    {
        var entry = await indexService.GetByShortCodeAsync(shortCode, cancellationToken);
        if (entry is null)
        {
            return Results.NotFound();
        }
        return Results.Ok(entry);
    }

    private static async Task<IResult> GetSchemaContent(
        string shortCode,
        ISchemaIndexService indexService,
        CancellationToken cancellationToken)
    {
        var content = await indexService.GetContentByShortCodeAsync(shortCode, cancellationToken);
        if (content is null)
        {
            return Results.NotFound();
        }
        return Results.Ok(content);
    }

    private static Ok<IReadOnlyList<SchemaSectorDto>> ListSectors()
    {
        var sectors = SchemaSector.All.Select(s => new SchemaSectorDto(
            s.Id, s.DisplayName, s.Description, s.Icon)).ToList();
        return TypedResults.Ok<IReadOnlyList<SchemaSectorDto>>(sectors);
    }

    private static async Task<IResult> GetSectorPreferences(
        HttpContext context,
        ISectorFilterService sectorFilterService,
        CancellationToken cancellationToken)
    {
        var organizationId = context.GetOrganizationId();
        if (string.IsNullOrEmpty(organizationId))
        {
            // No org claim — return all-sectors-enabled default without persisting state
            return Results.Ok(new OrganisationSectorPreferencesDto(
                "unknown", null, AllSectorsEnabled: true, LastModifiedAt: null));
        }

        var prefs = await sectorFilterService.GetPreferencesAsync(organizationId, cancellationToken);

        return Results.Ok(prefs);
    }

    private static async Task<IResult> UpdateSectorPreferences(
        HttpContext context,
        [FromBody] UpdateSectorPreferencesRequest request,
        ISectorFilterService sectorFilterService,
        CancellationToken cancellationToken)
    {
        var organizationId = context.GetOrganizationId();
        if (string.IsNullOrEmpty(organizationId))
        {
            return Results.Forbid();
        }

        try
        {
            var result = await sectorFilterService.UpdatePreferencesAsync(
                organizationId, request.EnabledSectors, cancellationToken);
            return Results.Ok(result);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(ex.Message);
        }
    }

    private static Ok<IReadOnlyList<SchemaProviderStatusDto>> ListProviders(
        ISchemaIndexService indexService)
    {
        var statuses = indexService.GetProviderStatuses();
        return TypedResults.Ok(statuses);
    }

    private static async Task<IResult> RefreshProvider(
        string providerName,
        ISchemaIndexService indexService,
        SchemaIndexRefreshService refreshService,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        // Check if provider is in backoff
        if (indexService is SchemaIndexService concreteService && concreteService.IsProviderInBackoff(providerName))
        {
            return Results.StatusCode(StatusCodes.Status429TooManyRequests);
        }

        try
        {
            var logger = loggerFactory.CreateLogger("SchemaLibraryEndpoints");
            // Fire and forget — run in background with independent cancellation
            _ = Task.Run(async () =>
            {
                try
                {
                    await refreshService.RefreshProviderManuallyAsync(providerName, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    logger?.LogError(ex, "Background refresh of provider {Provider} failed", providerName);
                }
            }, CancellationToken.None);

            return Results.Accepted();
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
    }
}
