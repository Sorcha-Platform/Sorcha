// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Json.Schema;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Sorcha.Blueprint.Schemas.DTOs;
using Sorcha.Blueprint.Schemas.Mappers;
using Sorcha.Blueprint.Schemas.Models;
using Sorcha.Blueprint.Schemas.Services;
using Sorcha.Blueprint.Service.Extensions;

namespace Sorcha.Blueprint.Service.Endpoints;

/// <summary>
/// Schema Store API endpoints.
/// </summary>
public static class SchemaEndpoints
{
    /// <summary>
    /// Maps schema endpoints to the application.
    /// </summary>
    public static void MapSchemaEndpoints(this WebApplication app)
    {
        var schemaGroup = app.MapGroup("/api/v1/schemas")
            .WithTags("Schemas");

        // GET /api/v1/schemas/system - Get all system schemas
        schemaGroup.MapGet("/system", GetSystemSchemas)
            .WithName("GetSystemSchemas")
            .WithSummary("Get all system schemas")
            .WithDescription("Retrieve the four core system schemas: Installation, Organisation, Participant, Register")
            .Produces<IReadOnlyList<SchemaEntryDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .RequireAuthorization();

        // GET /api/v1/schemas/{identifier} - Get schema by identifier
        schemaGroup.MapGet("/{identifier}", GetSchemaByIdentifier)
            .WithName("GetSchemaByIdentifier")
            .WithSummary("Get schema by identifier")
            .WithDescription("Retrieve a specific schema by its unique identifier. Returns 304 Not Modified if ETag matches.")
            .Produces<SchemaContentDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status304NotModified)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        // GET /api/v1/schemas - List all accessible schemas with filtering
        schemaGroup.MapGet("/", ListSchemas)
            .WithName("ListSchemas")
            .WithSummary("List all accessible schemas")
            .WithDescription("Retrieve schemas filtered by category, status, and search term")
            .Produces<SchemaListResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .RequireAuthorization();

        // GET /api/v1/schemas/external/search - Search external schema sources
        schemaGroup.MapGet("/external/search", SearchExternalSchemas)
            .WithName("SearchExternalSchemas")
            .WithSummary("Search external schema sources")
            .WithDescription("Search for schemas from external sources like SchemaStore.org")
            .Produces<ExternalSchemaSearchResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status503ServiceUnavailable)
            .RequireAuthorization();

        // POST /api/v1/schemas/external/import - Import external schema
        schemaGroup.MapPost("/external/import", ImportExternalSchema)
            .WithName("ImportExternalSchema")
            .WithSummary("Import external schema")
            .WithDescription("Import a schema from an external source into the local schema store")
            .Produces<SchemaContentDto>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status503ServiceUnavailable)
            .RequireAuthorization("RequireAdministrator");

        // POST /api/v1/schemas - Create custom schema
        schemaGroup.MapPost("/", CreateSchema)
            .WithName("CreateSchema")
            .WithSummary("Create a custom schema")
            .WithDescription("Create a new custom schema in the organization's schema store")
            .Produces<SchemaContentDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status409Conflict)
            .RequireAuthorization("CanManageBlueprints");

        // PUT /api/v1/schemas/{identifier} - Update schema
        schemaGroup.MapPut("/{identifier}", UpdateSchema)
            .WithName("UpdateSchema")
            .WithSummary("Update a custom schema")
            .WithDescription("Update an existing custom schema's content or metadata")
            .Produces<SchemaContentDto>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization("CanManageBlueprints");

        // DELETE /api/v1/schemas/{identifier} - Delete schema
        schemaGroup.MapDelete("/{identifier}", DeleteSchema)
            .WithName("DeleteSchema")
            .WithSummary("Delete a custom schema")
            .WithDescription("Delete a custom schema from the organization's schema store")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization("CanManageBlueprints");

        // POST /api/v1/schemas/{identifier}/deprecate - Deprecate schema
        schemaGroup.MapPost("/{identifier}/deprecate", DeprecateSchema)
            .WithName("DeprecateSchema")
            .WithSummary("Deprecate a schema")
            .WithDescription("Mark a schema as deprecated, signaling it should no longer be used")
            .Produces<SchemaContentDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization("CanManageBlueprints");

        // POST /api/v1/schemas/{identifier}/activate - Activate schema
        schemaGroup.MapPost("/{identifier}/activate", ActivateSchema)
            .WithName("ActivateSchema")
            .WithSummary("Activate a schema")
            .WithDescription("Reactivate a deprecated schema, making it available for use again")
            .Produces<SchemaContentDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization("CanManageBlueprints");

        // POST /api/v1/schemas/{identifier}/publish - Publish schema globally
        schemaGroup.MapPost("/{identifier}/publish", PublishSchema)
            .WithName("PublishSchema")
            .WithSummary("Publish a schema globally")
            .WithDescription("Make a custom schema available to all organizations")
            .Produces<SchemaContentDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .RequireAuthorization("RequireAdministrator");
    }

    /// <summary>
    /// GET /api/v1/schemas/system
    /// Returns all system schemas.
    /// </summary>
    private static async Task<Ok<IReadOnlyList<SchemaEntryDto>>> GetSystemSchemas(
        ISchemaStore schemaStore,
        CancellationToken cancellationToken)
    {
        var schemas = await schemaStore.GetSystemSchemasAsync(cancellationToken);
        var dtos = schemas.Select(s => s.ToEntryDto()).ToList();
        return TypedResults.Ok<IReadOnlyList<SchemaEntryDto>>(dtos);
    }

    /// <summary>
    /// GET /api/v1/schemas/{identifier}
    /// Returns a specific schema with content and ETag support.
    /// </summary>
    private static async Task<IResult> GetSchemaByIdentifier(
        [FromRoute] string identifier,
        [FromHeader(Name = "If-None-Match")] string? ifNoneMatch,
        HttpContext context,
        ISchemaStore schemaStore,
        CancellationToken cancellationToken)
    {
        // Extract organization ID from claims if available
        var organizationId = context.GetOrganizationId();

        var schema = await schemaStore.GetByIdentifierAsync(identifier, organizationId, cancellationToken);
        if (schema is null)
        {
            return Results.NotFound();
        }

        // Check ETag for cache validation (T033)
        var etag = schema.GetETag();
        if (!string.IsNullOrEmpty(ifNoneMatch) && ifNoneMatch == etag)
        {
            return Results.StatusCode(StatusCodes.Status304NotModified);
        }

        // Set ETag header
        context.Response.Headers.ETag = etag;
        context.Response.Headers.CacheControl = "private, max-age=300";

        return Results.Ok(schema.ToContentDto());
    }

    /// <summary>
    /// GET /api/v1/schemas
    /// Lists schemas with filtering options.
    /// </summary>
    private static async Task<Ok<SchemaListResponse>> ListSchemas(
        HttpContext context,
        ISchemaStore schemaStore,
        [FromQuery] string? category = null,
        [FromQuery] string? status = null,
        [FromQuery] string? search = null,
        [FromQuery] int limit = 50,
        [FromQuery] string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        // Parse category enum
        SchemaCategory? categoryFilter = null;
        if (!string.IsNullOrEmpty(category) && Enum.TryParse<SchemaCategory>(category, true, out var cat))
        {
            categoryFilter = cat;
        }

        // Parse status enum (default to Active)
        SchemaStatus? statusFilter = SchemaStatus.Active;
        if (!string.IsNullOrEmpty(status) && Enum.TryParse<SchemaStatus>(status, true, out var stat))
        {
            statusFilter = stat;
        }

        // Extract organization ID from claims
        var organizationId = context.GetOrganizationId();

        var (schemas, totalCount, nextCursor) = await schemaStore.ListAsync(
            categoryFilter,
            statusFilter,
            search,
            organizationId,
            limit,
            cursor,
            cancellationToken);

        return TypedResults.Ok(schemas.ToListResponse(totalCount, nextCursor));
    }

    /// <summary>
    /// GET /api/v1/schemas/external/search
    /// Searches external schema sources.
    /// </summary>
    private static async Task<IResult> SearchExternalSchemas(
        [FromQuery] string query,
        IExternalSchemaProvider externalProvider,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Results.BadRequest("Search query is required");
        }

        var logger = loggerFactory.CreateLogger("SchemaEndpoints");

        try
        {
            // Check if provider is available (T056 - graceful fallback)
            if (!await externalProvider.IsAvailableAsync(cancellationToken))
            {
                logger.LogWarning("External schema provider {Provider} is unavailable", externalProvider.ProviderName);
                return Results.Json(
                    new ExternalSchemaSearchResponse([], 0, externalProvider.ProviderName, query, IsPartialResult: true),
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var result = await externalProvider.SearchAsync(query, cancellationToken);
            return Results.Ok(result);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "External schema search failed for query '{Query}'", query);
            return Results.Json(
                new ExternalSchemaSearchResponse([], 0, externalProvider.ProviderName, query, IsPartialResult: true),
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    /// <summary>
    /// POST /api/v1/schemas/external/import
    /// Imports an external schema into the local store.
    /// </summary>
    private static async Task<IResult> ImportExternalSchema(
        [FromBody] ImportExternalSchemaRequest request,
        HttpContext context,
        ISchemaStore schemaStore,
        IExternalSchemaProvider externalProvider,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.SchemaUrl))
        {
            return Results.BadRequest("Schema URL is required");
        }

        var logger = loggerFactory.CreateLogger("SchemaEndpoints");

        // Extract organization ID from claims
        var organizationId = context.GetOrganizationId();
        if (string.IsNullOrEmpty(organizationId))
        {
            return Results.Forbid();
        }

        try
        {
            // Fetch the external schema
            var externalSchema = await externalProvider.GetSchemaAsync(request.SchemaUrl, cancellationToken);
            if (externalSchema?.Content == null)
            {
                return Results.NotFound($"Schema not found at URL: {request.SchemaUrl}");
            }

            // T055 - Validate the schema using JsonSchema.Net
            var validationResult = ValidateJsonSchema(externalSchema.Content);
            if (!validationResult.IsValid)
            {
                logger.LogWarning("Invalid JSON Schema from {Url}: {Errors}",
                    request.SchemaUrl, string.Join(", ", validationResult.Errors));
                return Results.BadRequest(new
                {
                    Error = "Invalid JSON Schema",
                    Details = validationResult.Errors
                });
            }

            // Determine identifier
            var identifier = request.Identifier ?? GenerateIdentifier(externalSchema.Name);

            // Check if already exists
            if (!request.OverwriteExisting && await schemaStore.ExistsAsync(identifier, organizationId, cancellationToken))
            {
                return Results.Conflict($"Schema with identifier '{identifier}' already exists");
            }

            // Create the schema entry
            var schemaEntry = new SchemaEntry
            {
                Identifier = identifier,
                Title = externalSchema.Name,
                Description = externalSchema.Description,
                Version = "1.0.0",
                Category = ParseCategory(request.Category),
                Status = SchemaStatus.Active,
                Content = externalSchema.Content,
                Source = new SchemaSource
                {
                    Type = SourceType.External,
                    Uri = request.SchemaUrl,
                    Provider = externalProvider.ProviderName
                },
                OrganizationId = organizationId,
                DateAdded = DateTimeOffset.UtcNow,
                DateModified = DateTimeOffset.UtcNow
            };

            var created = await schemaStore.CreateAsync(schemaEntry, cancellationToken);
            logger.LogInformation("Imported external schema '{Identifier}' from {Url}",
                identifier, request.SchemaUrl);

            return Results.Created($"/api/v1/schemas/{identifier}", created.ToContentDto());
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Failed to fetch external schema from {Url}", request.SchemaUrl);
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already exists"))
        {
            return Results.Conflict(ex.Message);
        }
    }

    /// <summary>
    /// Validates a JsonElement as a valid JSON Schema.
    /// </summary>
    private static (bool IsValid, List<string> Errors) ValidateJsonSchema(JsonElement content)
    {
        using var doc = JsonDocument.Parse(content.GetRawText());
        return ValidateJsonSchema(doc);
    }

    /// <summary>
    /// Converts a JsonElement to a JsonDocument.
    /// </summary>
    private static JsonDocument ToJsonDocument(JsonElement element)
    {
        return JsonDocument.Parse(element.GetRawText());
    }

    /// <summary>
    /// Validates a JSON document as a valid JSON Schema (T055).
    /// </summary>
    private static (bool IsValid, List<string> Errors) ValidateJsonSchema(JsonDocument content)
    {
        var errors = new List<string>();

        try
        {
            // Check if it has required schema properties
            var root = content.RootElement;

            // A valid JSON Schema should be an object
            if (root.ValueKind != JsonValueKind.Object)
            {
                errors.Add("JSON Schema must be an object");
                return (false, errors);
            }

            // Check for $schema or type property (basic validation)
            var hasSchemaProperty = root.TryGetProperty("$schema", out _);
            var hasTypeProperty = root.TryGetProperty("type", out _);
            var hasPropertiesProperty = root.TryGetProperty("properties", out _);
            var hasAllOfProperty = root.TryGetProperty("allOf", out _);
            var hasAnyOfProperty = root.TryGetProperty("anyOf", out _);
            var hasOneOfProperty = root.TryGetProperty("oneOf", out _);
            var hasRefProperty = root.TryGetProperty("$ref", out _);

            // Schema must have at least one schema-defining property
            if (!hasSchemaProperty && !hasTypeProperty && !hasPropertiesProperty &&
                !hasAllOfProperty && !hasAnyOfProperty && !hasOneOfProperty && !hasRefProperty)
            {
                errors.Add("JSON Schema must have at least one of: $schema, type, properties, allOf, anyOf, oneOf, or $ref");
                return (false, errors);
            }

            // Try to parse it as a JSON Schema
            var rawText = content.RootElement.GetRawText();
            var schema = JsonSchema.FromText(rawText);

            // If we get here, the schema is syntactically valid
            return (true, errors);
        }
        catch (JsonException ex)
        {
            errors.Add($"Invalid JSON: {ex.Message}");
            return (false, errors);
        }
        catch (Exception ex)
        {
            errors.Add($"Invalid JSON Schema: {ex.Message}");
            return (false, errors);
        }
    }

    /// <summary>
    /// Generates a safe identifier from a schema name.
    /// </summary>
    private static string GenerateIdentifier(string name)
    {
        // Convert to lowercase, replace spaces with hyphens, remove invalid chars
        var identifier = name.ToLowerInvariant()
            .Replace(" ", "-")
            .Replace(".", "-")
            .Replace("_", "-");

        // Remove any characters that aren't alphanumeric or hyphens
        identifier = new string(identifier.Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray());

        // Remove consecutive hyphens
        while (identifier.Contains("--"))
        {
            identifier = identifier.Replace("--", "-");
        }

        return identifier.Trim('-');
    }

    /// <summary>
    /// Parses a category string to enum.
    /// </summary>
    private static SchemaCategory ParseCategory(string category)
    {
        if (Enum.TryParse<SchemaCategory>(category, true, out var result))
        {
            return result;
        }
        return SchemaCategory.External;
    }

    /// <summary>
    /// POST /api/v1/schemas
    /// Creates a new custom schema.
    /// </summary>
    private static async Task<IResult> CreateSchema(
        [FromBody] CreateSchemaRequest request,
        HttpContext context,
        ISchemaStore schemaStore,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("SchemaEndpoints");

        // Validate required fields
        if (string.IsNullOrWhiteSpace(request.Identifier))
        {
            return Results.BadRequest("Schema identifier is required");
        }

        if (request.Content.ValueKind == JsonValueKind.Undefined)
        {
            return Results.BadRequest("Schema content is required");
        }

        // Extract organization ID from claims
        var organizationId = context.GetOrganizationId();

        if (string.IsNullOrEmpty(organizationId))
        {
            return Results.Forbid();
        }

        // Validate the JSON schema
        var validationResult = ValidateJsonSchema(request.Content);
        if (!validationResult.IsValid)
        {
            return Results.BadRequest(new
            {
                Error = "Invalid JSON Schema",
                Details = validationResult.Errors
            });
        }

        try
        {
            var schemaEntry = new SchemaEntry
            {
                Identifier = request.Identifier,
                Title = request.Title ?? request.Identifier,
                Description = request.Description,
                Version = request.Version ?? "1.0.0",
                Category = SchemaCategory.Custom,
                Status = SchemaStatus.Active,
                Content = ToJsonDocument(request.Content),
                Source = SchemaSource.Custom(),
                OrganizationId = organizationId,
                DateAdded = DateTimeOffset.UtcNow,
                DateModified = DateTimeOffset.UtcNow
            };

            var created = await schemaStore.CreateAsync(schemaEntry, cancellationToken);
            logger.LogInformation("Created custom schema '{Identifier}' for org {OrganizationId}",
                request.Identifier, organizationId);

            return Results.Created($"/api/v1/schemas/{created.Identifier}", created.ToContentDto());
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already exists") || ex.Message.Contains("conflicts"))
        {
            return Results.Conflict(ex.Message);
        }
    }

    /// <summary>
    /// PUT /api/v1/schemas/{identifier}
    /// Updates an existing custom schema.
    /// </summary>
    private static async Task<IResult> UpdateSchema(
        [FromRoute] string identifier,
        [FromBody] UpdateSchemaRequest request,
        HttpContext context,
        ISchemaStore schemaStore,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("SchemaEndpoints");

        // Extract organization ID from claims
        var organizationId = context.GetOrganizationId();

        if (string.IsNullOrEmpty(organizationId))
        {
            return Results.Forbid();
        }

        // Get existing schema
        var existing = await schemaStore.GetByIdentifierAsync(identifier, organizationId, cancellationToken);
        if (existing is null)
        {
            return Results.NotFound($"Schema '{identifier}' not found");
        }

        if (existing.Category != SchemaCategory.Custom)
        {
            return Results.BadRequest("Only custom schemas can be updated");
        }

        if (existing.OrganizationId != organizationId)
        {
            return Results.Forbid();
        }

        // Validate new content if provided
        if (request.Content is { } contentElement)
        {
            var validationResult = ValidateJsonSchema(contentElement);
            if (!validationResult.IsValid)
            {
                return Results.BadRequest(new
                {
                    Error = "Invalid JSON Schema",
                    Details = validationResult.Errors
                });
            }
            existing.Content = ToJsonDocument(contentElement);
        }

        // Update fields
        if (!string.IsNullOrWhiteSpace(request.Title))
        {
            existing.Title = request.Title;
        }

        if (request.Description is not null)
        {
            existing.Description = request.Description;
        }

        if (!string.IsNullOrWhiteSpace(request.Version))
        {
            existing.Version = request.Version;
        }

        existing.DateModified = DateTimeOffset.UtcNow;

        try
        {
            var updated = await schemaStore.UpdateAsync(existing, cancellationToken);
            logger.LogInformation("Updated custom schema '{Identifier}'", identifier);
            return Results.Ok(updated.ToContentDto());
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound($"Schema '{identifier}' not found");
        }
    }

    /// <summary>
    /// DELETE /api/v1/schemas/{identifier}
    /// Deletes a custom schema.
    /// </summary>
    private static async Task<IResult> DeleteSchema(
        [FromRoute] string identifier,
        HttpContext context,
        ISchemaStore schemaStore,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("SchemaEndpoints");

        // Extract organization ID from claims
        var organizationId = context.GetOrganizationId();

        if (string.IsNullOrEmpty(organizationId))
        {
            return Results.Forbid();
        }

        try
        {
            var deleted = await schemaStore.DeleteAsync(identifier, organizationId, cancellationToken);
            if (!deleted)
            {
                return Results.NotFound($"Schema '{identifier}' not found");
            }

            logger.LogInformation("Deleted custom schema '{Identifier}'", identifier);
            return Results.NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// POST /api/v1/schemas/{identifier}/deprecate
    /// Deprecates a schema.
    /// </summary>
    private static async Task<IResult> DeprecateSchema(
        [FromRoute] string identifier,
        HttpContext context,
        ISchemaStore schemaStore,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("SchemaEndpoints");

        // Extract organization ID from claims
        var organizationId = context.GetOrganizationId();

        try
        {
            var deprecated = await schemaStore.DeprecateAsync(identifier, organizationId, cancellationToken);
            logger.LogInformation("Deprecated schema '{Identifier}'", identifier);
            return Results.Ok(deprecated.ToContentDto());
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound($"Schema '{identifier}' not found");
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// POST /api/v1/schemas/{identifier}/activate
    /// Reactivates a deprecated schema.
    /// </summary>
    private static async Task<IResult> ActivateSchema(
        [FromRoute] string identifier,
        HttpContext context,
        ISchemaStore schemaStore,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("SchemaEndpoints");

        // Extract organization ID from claims
        var organizationId = context.GetOrganizationId();

        try
        {
            var activated = await schemaStore.ActivateAsync(identifier, organizationId, cancellationToken);
            logger.LogInformation("Activated schema '{Identifier}'", identifier);
            return Results.Ok(activated.ToContentDto());
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound($"Schema '{identifier}' not found");
        }
    }

    /// <summary>
    /// POST /api/v1/schemas/{identifier}/publish
    /// Publishes a custom schema globally.
    /// </summary>
    private static async Task<IResult> PublishSchema(
        [FromRoute] string identifier,
        HttpContext context,
        ISchemaStore schemaStore,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("SchemaEndpoints");

        // Extract organization ID from claims
        var organizationId = context.GetOrganizationId();

        if (string.IsNullOrEmpty(organizationId))
        {
            return Results.Forbid();
        }

        try
        {
            var published = await schemaStore.PublishGloballyAsync(identifier, organizationId, cancellationToken);
            logger.LogInformation("Published schema '{Identifier}' globally", identifier);
            return Results.Ok(published.ToContentDto());
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound($"Schema '{identifier}' not found");
        }
        catch (InvalidOperationException ex)
        {
            return Results.Conflict(ex.Message);
        }
    }
}
