// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

#pragma warning disable ASPDEPR002 // WithOpenApi is deprecated; using it for co-located endpoint examples until transformer API stabilizes

using System.ComponentModel.DataAnnotations;
using System.Text.Json;

using Sorcha.Blueprint.Models;
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
            "display name, initialization status, blueprint count, and creation timestamp.")
        .Produces<object>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/initialize", async (
            SystemRegisterService service,
            ILogger<Program> logger,
            CancellationToken ct) =>
        {
            try
            {
                var wasInitialized = await service.InitializeSystemRegisterAsync(ct);
                var info = await service.GetSystemRegisterInfoAsync(ct);

                return wasInitialized
                    ? Results.Ok(new { message = "System register initialized successfully", status = info })
                    : Results.Ok(new { message = "System register was already initialized", status = info });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to initialize system register via API");
                return Results.Problem(
                    detail: $"Failed to initialize system register: {ex.Message}",
                    statusCode: StatusCodes.Status500InternalServerError);
            }
        })
        .WithName("InitializeSystemRegister")
        .WithSummary("Initialize the system register")
        .WithDescription(
            "Seeds the system register with default blueprints. " +
            "This operation is idempotent — calling it on an already-initialized register is safe.")
        .Produces<object>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status500InternalServerError);

        group.MapPost("/publish", async (
            PublishBlueprintRequest request,
            SystemRegisterService service,
            ILogger<Program> logger,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.BlueprintId))
            {
                return Results.BadRequest(new { error = "blueprintId is required" });
            }

            if (request.Blueprint.ValueKind == JsonValueKind.Undefined)
            {
                return Results.BadRequest(new { error = "blueprint is required" });
            }

            try
            {
                var entry = await service.PublishBlueprintAsync(
                    request.BlueprintId,
                    request.Blueprint,
                    "api-user",
                    request.Metadata,
                    ct);

                return Results.Created(
                    $"/api/system-register/blueprints/{entry.BlueprintId}",
                    new PublishBlueprintResponse
                    {
                        TransactionId = entry.PublicationTransactionId!,
                        BlueprintId = entry.BlueprintId,
                        Version = entry.Version,
                        PublishedAt = entry.PublishedAt
                    });
            }
            catch (InvalidOperationException ex)
            {
                logger.LogError(ex, "Failed to publish blueprint {BlueprintId}", request.BlueprintId);
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: StatusCodes.Status500InternalServerError);
            }
        })
        .WithRequestValidation()
        .WithName("PublishBlueprint")
        .WithSummary("Publish a blueprint to the system register")
        .WithDescription(
            "Publishes a new blueprint to the system register as a signed control-chain transaction. " +
            "The blueprint JSON is stored on the ledger with a deterministic transaction ID, " +
            "signed by the system wallet. Returns the transaction ID and blueprint metadata on success.")
        .Accepts<PublishBlueprintRequest>("application/json")
        .Produces<PublishBlueprintResponse>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status500InternalServerError)
        .WithOpenApi(operation =>
        {
            OpenApiExamples.SetRequestExample(operation, """
                {
                  "blueprintId": "construction-permit-v1",
                  "blueprint": {
                    "title": "Construction Permit Application",
                    "participants": [
                      { "role": "Applicant", "description": "The person applying for a permit" },
                      { "role": "BuildingInspector", "description": "Reviews and approves the application" }
                    ],
                    "actions": [
                      { "id": "submit-application", "actor": "Applicant", "type": "Submit" },
                      { "id": "review-application", "actor": "BuildingInspector", "type": "Review" }
                    ]
                  },
                  "metadata": {
                    "changeType": "structural",
                    "author": "admin@acme.corp"
                  }
                }
                """);
            OpenApiExamples.SetResponseExample(operation, "201", """
                {
                  "transactionId": "tx:system-register:construction-permit-v1:a1b2c3d4",
                  "blueprintId": "construction-permit-v1",
                  "version": 1,
                  "publishedAt": "2026-03-15T10:30:00Z"
                }
                """);
            return operation;
        });

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

            // Newest first. Sorted on the publication time rather than Version, which counts
            // publications OF ONE blueprint and so says nothing about how two different blueprints
            // order against each other (#1515).
            var items = allBlueprints
                .OrderByDescending(b => b.PublishedAt)
                .ThenBy(b => b.BlueprintId, StringComparer.Ordinal)
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
            "Supports page and pageSize query parameters (default: page=1, pageSize=20, max=100).")
        .Produces<object>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized);

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
            "Returns 404 if the blueprint does not exist.")
        .Produces<object>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status401Unauthorized);

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
            "Returns 404 if the blueprint or version does not exist.")
        .Produces<object>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status401Unauthorized);

        group.MapGet("/blueprints/{blueprintId}/versions", async (
            SystemRegisterService service,
            StructuralDiffService diffService,
            string blueprintId,
            CancellationToken ct) =>
        {
            // TODO: add server-side filtering by blueprintId to avoid loading entire catalogue
            var transactions = await service.GetAllBlueprintsAsync(ct);
            var matching = transactions
                .Where(t => t.BlueprintId == blueprintId)
                .OrderBy(t => t.PublishedAt)
                .ToList();

            if (matching.Count == 0)
            {
                return Results.NotFound(new { error = $"Blueprint '{blueprintId}' not found in system register" });
            }

            var versions = new List<BlueprintVersion>();
            int currentMajor = 0, currentMinor = 0;

            foreach (var entry in matching)
            {
                var changeType = entry.Metadata?.GetValueOrDefault("changeType") ?? "structural";
                var hash = entry.Metadata?.GetValueOrDefault("structuralHash") ?? "";

                if (versions.Count == 0)
                {
                    currentMajor = 1;
                    currentMinor = 0;
                }
                else if (changeType == "documentation")
                {
                    currentMinor++;
                }
                else
                {
                    currentMajor++;
                    currentMinor = 0;
                }

                versions.Add(new BlueprintVersion
                {
                    Major = currentMajor,
                    Minor = currentMinor,
                    ChangeType = changeType,
                    StructuralHash = hash,
                    PublishedAt = entry.PublishedAt,
                    PublishedBy = entry.PublishedBy,
                    TransactionId = entry.PublicationTransactionId ?? ""
                });
            }

            return Results.Ok(new
            {
                blueprintId,
                latestVersion = new { major = currentMajor, minor = currentMinor },
                versions
            });
        })
        .WithName("GetBlueprintVersions")
        .WithSummary("Query version history for a published blueprint")
        .WithDescription(
            "Returns the complete version history for a blueprint including change type " +
            "(structural vs documentation), structural hash, signer identity, and timestamps.")
        .Produces<object>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/blueprints/{blueprintId}/classify-change", async (
            SystemRegisterService service,
            StructuralDiffService diffService,
            string blueprintId,
            ClassifyChangeRequest request,
            CancellationToken ct) =>
        {
            var latestEntry = await service.GetBlueprintAsync(blueprintId, ct);

            string newHash = diffService.ComputeStructuralHash(request.NewBlueprint);

            if (latestEntry is null)
            {
                // First publish
                return Results.Ok(new
                {
                    changeType = "structural",
                    currentVersion = (object?)null,
                    proposedVersion = new { major = 1, minor = 0 },
                    structuralHashNew = newHash,
                    structuralFieldsChanged = true
                });
            }

            // Compute current structural hash from stored blueprint
            string currentHash = "";
            if (latestEntry.Document is not null)
            {
                currentHash = diffService.ComputeStructuralHash(latestEntry.Document.RootElement);
            }

            var changeType = StructuralDiffService.ClassifyChange(currentHash, newHash);

            // Reconstruct current version from metadata
            var currentMajor = int.TryParse(
                latestEntry.Metadata?.GetValueOrDefault("versionMajor"), out var cm) ? cm : 1;
            var currentMinor = int.TryParse(
                latestEntry.Metadata?.GetValueOrDefault("versionMinor"), out var cmin) ? cmin : 0;

            var (proposedMajor, proposedMinor) = VersionCalculator.ComputeNextVersion(
                currentMajor, currentMinor, changeType);

            return Results.Ok(new
            {
                changeType,
                currentVersion = new { major = currentMajor, minor = currentMinor },
                proposedVersion = new { major = proposedMajor, minor = proposedMinor },
                structuralHashCurrent = currentHash,
                structuralHashNew = newHash,
                structuralFieldsChanged = changeType == "structural"
            });
        })
        .WithName("ClassifyBlueprintChange")
        .WithSummary("Classify a proposed blueprint change as structural or documentation")
        .WithDescription(
            "Compares a new blueprint against the latest published version to determine " +
            "whether the change is structural (major bump) or documentation-only (minor bump). " +
            "Returns the proposed next version number.")
        .Accepts<ClassifyChangeRequest>("application/json")
        .Produces<object>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized);
    }

    /// <summary>
    /// Request body for classifying a blueprint change.
    /// </summary>
    private record ClassifyChangeRequest
    {
        /// <summary>The new blueprint JSON to compare against the current published version.</summary>
        public required JsonElement NewBlueprint { get; init; }
    }

    /// <summary>
    /// Request body for publishing a blueprint to the system register.
    /// </summary>
    private record PublishBlueprintRequest
    {
        /// <summary>Unique blueprint identifier.</summary>
        [Required(AllowEmptyStrings = false)]
        [StringLength(200)]
        public required string BlueprintId { get; init; }

        /// <summary>Blueprint JSON document to publish.</summary>
        public required JsonElement Blueprint { get; init; }

        /// <summary>Optional previous transaction ID for explicit chain linking.</summary>
        public string? PreviousTransactionId { get; init; }

        /// <summary>Optional metadata key-value pairs.</summary>
        public Dictionary<string, string>? Metadata { get; init; }
    }

    /// <summary>
    /// Response returned after successfully publishing a blueprint.
    /// </summary>
    private record PublishBlueprintResponse
    {
        /// <summary>Transaction ID of the published blueprint.</summary>
        public required string TransactionId { get; init; }

        /// <summary>Blueprint identifier.</summary>
        public required string BlueprintId { get; init; }

        /// <summary>Version number assigned to the published blueprint.</summary>
        public long Version { get; init; }

        /// <summary>UTC timestamp when published.</summary>
        public DateTime PublishedAt { get; init; }
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
