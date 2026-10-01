// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;
using System.Text.Json;

using Sorcha.Blueprint.Models;
using Sorcha.Register.Service.Authorization;
using Sorcha.Register.Service.Services;
using Sorcha.ServiceClients.Audit;

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

        group.MapGet("/drift", async (
            ISystemBlueprintDriftReporter reporter,
            TimeProvider time,
            CancellationToken ct) =>
        {
            // Always computed on demand, never the monitor's snapshot: an operator reads /drift right
            // after a publish or a deploy to decide the next step, and a snapshot up to one monitor
            // cycle old would report the state BEFORE that action. The computation is cheap and the
            // route is SystemAdmin-only. The health check and the gauge keep the snapshot.
            var entries = await reporter.ComputeAsync(ct);

            var checkedAt = entries.Count > 0 ? entries.Max(e => e.CheckedAt) : time.GetUtcNow();
            return Results.Ok(new SystemBlueprintDriftReport { CheckedAt = checkedAt, Entries = entries });
        })
        // Stricter than the group's CanManageRegisters (both apply): SystemAdmin on the platform tier only.
        .RequireAuthorization("RequireSystemAdmin", "RequirePlatformAudience")
        .WithName("GetSystemBlueprintDrift")
        .WithSummary("Report drift between this node's system blueprints and the system register")
        .WithDescription(
            "Returns one entry per catalogued system blueprint, classifying the definition shipped in this " +
            "node's image against the system register's current publication (in-sync, image-behind, image-ahead, " +
            "missing, unknown). Computed on demand at request time, so it reflects a publish or deploy made " +
            "moments earlier. Requires a SystemAdmin on a platform-tier token.")
        .Produces<SystemBlueprintDriftReport>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        group.MapPost("/blueprints/{blueprintId}/publish", async (
            string blueprintId,
            SystemBlueprintPublishRequest? request,
            ISystemBlueprintPublishService publisher,
            HttpContext http,
            ILogger<Program> logger,
            CancellationToken ct) =>
        {
            var operatorId = OperatorIdentity.Resolve(http.User);
            if (string.IsNullOrWhiteSpace(operatorId))
            {
                return Results.Unauthorized();
            }

            PublishDecision decision;
            try
            {
                decision = await publisher.PublishAsync(
                    blueprintId, request?.DryRun ?? false, request?.ExpectedCurrent, operatorId, ct);
            }
            catch (ValidatorRejectedSubmissionException ex)
            {
                // Thrown only where the validator rejects the submission (SystemRegisterService.PublishBlueprintAsync);
                // Anything else is unexpected and falls through to the sanitized exception handler (CLAUDE.md 20).
                logger.LogError(ex, "System blueprint {BlueprintId} publish submission was rejected", blueprintId);
                SystemBlueprintMetrics.RecordPublish(PublishOutcomeNames.Rejected);
                await SystemBlueprintRefusalAudit.ReportAsync(
                    http, RefusalAuditActions.SystemBlueprintPublish, blueprintId, "validator rejected the submission");
                return Results.Problem(
                    title: "publish submission rejected",
                    detail: "the publication was rejected by the validator; see the register-service log",
                    statusCode: StatusCodes.Status502BadGateway);
            }

            SystemBlueprintPublishResult Body(SystemBlueprintPublishResultOutcome outcome) => new(
                blueprintId, outcome, decision.DriftState ?? SystemBlueprintDriftState.Unknown,
                decision.CurrentPublicationTxId, decision.CandidatePublicationTxId, decision.TransactionId);

            IResult Refused(int status, string title, string reason)
            {
                if (status == StatusCodes.Status503ServiceUnavailable)
                {
                    http.Response.Headers.RetryAfter = "30";
                }

                return Results.Problem(
                    title: title,
                    detail: decision.Reason,
                    statusCode: status,
                    extensions: new Dictionary<string, object?> { ["reason"] = reason });
            }

            async Task<IResult> AuditedAsync(int status, string title, string reason)
            {
                await SystemBlueprintRefusalAudit.ReportAsync(
                    http, RefusalAuditActions.SystemBlueprintPublish, blueprintId, decision.Reason);
                return Refused(status, title, reason);
            }

            switch (decision.Outcome)
            {
                case SystemBlueprintPublishOutcome.DryRun:
                    return Results.Json(Body(SystemBlueprintPublishResultOutcome.DryRun));
                case SystemBlueprintPublishOutcome.Noop:
                    return Results.Json(Body(SystemBlueprintPublishResultOutcome.Noop));
                case SystemBlueprintPublishOutcome.Submitted:
                    logger.LogInformation(
                        "SystemBlueprintPublished {BlueprintId} {PreviousTxId} v{PreviousVersion} -> {NewTxId} "
                        + "v{NewVersion} by {Operator} via publishing wallet {PublisherWalletAddress}",
                        blueprintId, decision.CurrentPublicationTxId, decision.PreviousVersion,
                        decision.TransactionId, decision.NewVersion, operatorId, decision.PublisherWalletAddress);
                    return Results.Json(
                        Body(SystemBlueprintPublishResultOutcome.Submitted), statusCode: StatusCodes.Status202Accepted);
                case SystemBlueprintPublishOutcome.NotFound:
                    // Intentionally not audited: an unknown id is not a refused publish.
                    return Refused(StatusCodes.Status404NotFound, "system blueprint not found", "not-found");
                case SystemBlueprintPublishOutcome.NoPublishingKey:
                    return await AuditedAsync(StatusCodes.Status403Forbidden, "no publishing key", "no-publishing-key");
                case SystemBlueprintPublishOutcome.RefusedRollback:
                    return await AuditedAsync(StatusCodes.Status409Conflict, "publish refused", "rollback");
                case SystemBlueprintPublishOutcome.RefusedConcurrency:
                    return await AuditedAsync(StatusCodes.Status409Conflict, "publish refused", "concurrency");
                default:
                    return await AuditedAsync(
                        StatusCodes.Status503ServiceUnavailable, "system register state unknown", "state-unknown");
            }
        })
        // Same gate as /drift (both apply with the group's CanManageRegisters). The policy 403 never reaches
        // this handler, so the marker has the auditing authorisation result handler report it (SC-004).
        .RequireAuthorization("RequireSystemAdmin", "RequirePlatformAudience")
        .WithMetadata(new SystemBlueprintPublishAuditMetadata(RefusalAuditActions.SystemBlueprintPublish))
        .WithName("PublishSystemBlueprintFromCatalogue")
        .WithSummary("Publish this node's catalogued definition of a system blueprint")
        .WithDescription(
            "Loads the definition from this node's image catalogue (the request carries none) and submits it to " +
            "the system register unless a guard refuses: rollback (409 reason rollback), expectedCurrent mismatch " +
            "(409 reason concurrency), no active sorcha:blueprint-publish roster key on this node (403), unreadable " +
            "register state (503). dryRun and an already-current definition return 200; a submission returns 202. " +
            "Every refusal is reported to the caller's organisation audit log. Requires a SystemAdmin on a platform-tier token.")
        .Produces<SystemBlueprintPublishResult>(StatusCodes.Status200OK)
        .Produces<SystemBlueprintPublishResult>(StatusCodes.Status202Accepted)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status502BadGateway)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
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
