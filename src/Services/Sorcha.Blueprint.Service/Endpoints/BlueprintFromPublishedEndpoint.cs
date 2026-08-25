// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;

using Sorcha.Blueprint.Service.Extensions;
using Sorcha.Blueprint.Service.Services.Implementation;
using Sorcha.ServiceClients.Register;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;

namespace Sorcha.Blueprint.Service.Endpoints;

/// <summary>
/// Feature 142 (T056 / US6) — REST endpoint for the amend / clone-to-draft flow.
/// <c>POST /api/blueprints/from-published</c> derives a fresh draft from an already-published
/// version, stamping lineage metadata so the designer can repopulate
/// <see cref="Sorcha.UI.Core.Services.Designer.AmendContext"/>-shaped state on open. The new
/// draft opens with Go live re-locked: its executable-definition hash has no recorded
/// <c>RehearsalPass</c> until a fresh rehearsal runs against the amend draft.
/// </summary>
/// <remarks>
/// Authorisation mirrors the publish path's hard governance gate: the caller MUST hold a
/// publish-governance role (Owner / Admin / Designer) on the SOURCE register's roster.
/// "Open published service for amend" is just as authority-bearing as "publish a new version to
/// that register" — and amend leads to re-publish, so refusing it at the gate keeps the
/// boundary symmetrical. Tier is platform (<c>RequirePlatformAudience</c>).
/// </remarks>
public static class BlueprintFromPublishedEndpoint
{
    /// <summary>
    /// Lineage metadata keys carried on the amend draft. Public so tests and the designer can
    /// pick them up without string drift; T057 reads these on draft load to populate
    /// <c>LifecycleState.AmendContext</c>.
    /// </summary>
    public const string SourceRegisterMetadataKey = "x-source-register";

    /// <summary>The published blueprint id the amend draft derives from.</summary>
    public const string SourceBlueprintMetadataKey = "x-source-blueprint-id";

    /// <summary>The published version number the amend draft derives from.</summary>
    public const string SourceVersionMetadataKey = "x-source-version";

    /// <summary>
    /// Maps <c>POST /api/blueprints/from-published</c>.
    /// </summary>
    /// <param name="app">The web application to map the endpoint on.</param>
    public static void MapBlueprintFromPublishedEndpoint(this WebApplication app)
    {
        app.MapPost("/api/blueprints/from-published", FromPublishedAsync)
            .WithName("CloneBlueprintFromPublished")
            .WithTags("Blueprints")
            .WithSummary("Amend — derive a new draft from a published version")
            .WithDescription(
                "Fetches the specified published Blueprint version, clones it to a new draft, " +
                "stamps lineage metadata (x-source-register / x-source-blueprint-id / x-source-version), " +
                "and returns the new draft id. The amend draft opens with Go live re-locked pending a " +
                "fresh rehearsal — its executable-definition hash has no recorded RehearsalPass. The " +
                "caller MUST hold a publish-governance role on the source register; otherwise 403 is " +
                "returned and no draft is written. Returns 404 when the (registerId, blueprintId, " +
                "version) triple does not resolve to a published blueprint.")
            .Accepts<CloneFromPublishedRequestBody>("application/json")
            .Produces<CloneFromPublishedResponseBody>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization("CanManageBlueprints", "RequirePlatformAudience");
    }

    /// <summary>
    /// Handles <c>POST /api/blueprints/from-published</c>. Resolves the source published blueprint,
    /// enforces the source-register governance check, deep-clones the Blueprint POCO via a JSON
    /// round-trip (preserves DataSchemas / Actions / Routes / Disclosures), reassigns the id and
    /// zeroes Version / PublishedAt, stamps lineage metadata, and persists via
    /// <see cref="IBlueprintStore.AddAsync"/>.
    /// </summary>
    /// <param name="body">The clone request: source register + blueprint id + version.</param>
    /// <param name="httpContext">The current HTTP context (used to resolve the caller identity).</param>
    /// <param name="publishedStore">The published-blueprint store (source of truth for the version).</param>
    /// <param name="draftStore">The draft blueprint store (target for the new draft).</param>
    /// <param name="registerClient">The Register Service client used for the governance roster lookup.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// 201 + new draft id + lineage echo on success; 400 on a malformed body; 403 when the caller
    /// lacks a publish-governance role on the source register; 404 when the triple is unknown.
    /// </returns>
    private static async Task<IResult> FromPublishedAsync(
        CloneFromPublishedRequestBody body,
        HttpContext httpContext,
        IBlueprintStore draftStore,
        IPublishedBlueprintStore publishedStore,
        IRegisterServiceClient registerClient,
        ILogger<CloneFromPublishedRequestBody> logger,
        CancellationToken cancellationToken)
    {
        // -- 1) Validate body -------------------------------------------------
        if (body is null)
        {
            return Results.BadRequest(new { error = "Request body is required." });
        }

        var validationResults = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        if (!Validator.TryValidateObject(body, new ValidationContext(body), validationResults, validateAllProperties: true))
        {
            return Results.BadRequest(new
            {
                error = "Invalid request body.",
                details = validationResults.Select(v => v.ErrorMessage).ToArray(),
            });
        }

        // -- 2) Resolve the source published DEFINITION ------------------------
        //
        // Feature 195 — selected by PUBLICATION ID, not by ordinal. The ordinal is assigned from
        // in-memory insert order and re-derived on every recovery, so "amend v2" could clone a
        // different definition before and after a restart.
        var published = await publishedStore.GetByPublicationAsync(body.BlueprintId, body.PublicationTxId);
        if (published is null
            || published.Blueprint is null
            || !string.Equals(published.RegisterId, body.RegisterId, StringComparison.OrdinalIgnoreCase))
        {
            // Mismatched register => the requested version exists but lives on a different register.
            // The (registerId, blueprintId, version) triple did not resolve — 404.
            return Results.NotFound(new
            {
                error = "No published blueprint matches the supplied (registerId, blueprintId, version).",
            });
        }

        // -- 3) Governance HARD gate on the SOURCE register -------------------
        var roster = await registerClient.GetGovernanceRosterAsync(body.RegisterId, cancellationToken);
        var caller = ResolveCaller(httpContext);
        if (!CallerHoldsPublishingRole(roster, caller))
        {
            logger.LogWarning(
                "Amend refused (governance) — caller (user {UserId}, org {OrgId}) lacks a publish-governance role on register {RegisterId}",
                caller.PlatformUserId, caller.OrganizationId, body.RegisterId);

            return Results.Json(
                new { error = "You do not hold a publish-governance role (Owner, Admin, or Designer) on the source register." },
                statusCode: StatusCodes.Status403Forbidden);
        }

        // -- 4) Deep-clone the Blueprint POCO ---------------------------------
        // JSON round-trip clones the entire executable + presentational state (DataSchemas as
        // JsonDocument, Actions, Routes, Disclosures, Participants, BlueprintInstructions,
        // InstanceReference, PresentationConfig). Cheaper than hand-coded copy logic and avoids
        // drift as new fields land on the model.
        var clone = JsonSerializer.Deserialize<BlueprintModel>(
            JsonSerializer.Serialize(published.Blueprint))
            ?? throw new InvalidOperationException("Failed to clone the published blueprint payload.");

        // Feature 195 — the clone keeps the SAME blueprint id.
        //
        // It used to mint a fresh GUID, which made "Amend" a FORK rather than a new version: the
        // amendment never appeared in the source blueprint's version history, and the platform had
        // two unrelated upgrade paths with no stated relationship between them. One button that
        // looks like versioning and is not.
        clone.Id = body.BlueprintId;
        clone.CreatedAt = DateTimeOffset.UtcNow;
        clone.UpdatedAt = DateTimeOffset.UtcNow;
        // Carry forward the caller's organisation so the new draft is org-scoped to the amender,
        // matching the create-blueprint surface (org_id from the JWT).
        clone.OrganizationId = httpContext.IsServiceToken() ? clone.OrganizationId : httpContext.GetOrganizationId();

        // -- 5) Stamp lineage metadata so the designer can repopulate AmendContext --
        clone.Metadata ??= new Dictionary<string, string>();
        clone.Metadata[SourceRegisterMetadataKey] = body.RegisterId;
        clone.Metadata[SourceBlueprintMetadataKey] = body.BlueprintId;
        clone.Metadata[SourceVersionMetadataKey] = body.PublicationTxId;

        // -- 6) Persist as THE draft for this blueprint ------------------------
        //
        // Upsert, not add: the draft is the editor's buffer for a blueprint, and "amend this
        // published definition" is an explicit instruction to load it there. AddAsync would mint a
        // new id (it assigns one unconditionally), which is the fork this change removes.
        var stored = await draftStore.UpdateAsync(body.BlueprintId, clone)
                     ?? await draftStore.AddAsync(clone);

        if (!string.Equals(stored.Id, body.BlueprintId, StringComparison.Ordinal))
        {
            // AddAsync assigns its own id. If we reached it, the blueprint had no draft and the
            // store has just renamed our clone — correct that, or the amendment silently forks
            // exactly as it used to.
            stored.Id = body.BlueprintId;
            stored = await draftStore.UpdateAsync(body.BlueprintId, stored) ?? stored;
        }

        logger.LogInformation(
            "Amend draft {DraftId} derived from definition {PublicationTxId} of blueprint {SourceBlueprintId} on register {RegisterId} by user {UserId}",
            stored.Id, body.PublicationTxId, body.BlueprintId, body.RegisterId, caller.PlatformUserId);

        var responseBody = new CloneFromPublishedResponseBody(
            DraftBlueprintId: stored.Id,
            SourcePublicationTxId: body.PublicationTxId,
            RegisterId: body.RegisterId);

        return Results.Created($"/api/blueprints/{stored.Id}", responseBody);
    }

    /// <summary>
    /// Resolves the caller's identity for the governance roster check, mirroring
    /// <see cref="PublishCaller"/>.
    /// </summary>
    private static PublishCaller ResolveCaller(HttpContext httpContext)
    {
        var sub = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? httpContext.User.FindFirst("sub")?.Value;
        _ = Guid.TryParse(sub, out var platformUserId);

        return new PublishCaller(
            PlatformUserId: platformUserId,
            OrganizationId: httpContext.GetOrganizationId(),
            WalletAddress: httpContext.User.FindFirst("wallet_address")?.Value);
    }

    /// <summary>
    /// Mirrors <see cref="PublishGate"/>'s membership check: substring-match the caller's wallet
    /// or organisation against the roster member subject, restricted to publish-governance roles.
    /// </summary>
    private static bool CallerHoldsPublishingRole(GovernanceRosterResponse? roster, PublishCaller caller)
    {
        if (roster?.Members is null || roster.Members.Count == 0)
        {
            return false;
        }

        foreach (var member in roster.Members)
        {
            if (!PublishingRoles.Contains(member.Role))
            {
                continue;
            }

            if (SubjectMatchesCaller(member.Subject, caller))
            {
                return true;
            }
        }

        return false;
    }

    private static bool SubjectMatchesCaller(string subject, PublishCaller caller)
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(caller.WalletAddress)
            && subject.Contains(caller.WalletAddress, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(caller.OrganizationId)
            && subject.Contains(caller.OrganizationId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static readonly HashSet<string> PublishingRoles =
        new(StringComparer.OrdinalIgnoreCase) { "Owner", "Admin", "Designer" };
}

/// <summary>
/// Request body for the amend / clone-to-draft endpoint (Feature 142). Mirrors the wire shape
/// in <c>specs/142-blueprint-lifecycle/contracts/blueprint-lifecycle.openapi.yaml</c>.
/// </summary>
public sealed record CloneFromPublishedRequestBody
{
    /// <summary>The source register the published version lives on.</summary>
    [Required, JsonPropertyName("registerId")]
    public string RegisterId { get; init; } = string.Empty;

    /// <summary>The published blueprint id (the original / source).</summary>
    [Required, JsonPropertyName("blueprintId")]
    public string BlueprintId { get; init; } = string.Empty;

    /// <summary>
    /// The published definition to amend — the id of the transaction that published it
    /// (Feature 195).
    /// </summary>
    /// <remarks>
    /// Replaces an ordinal <c>version</c>. The ordinal is assigned from in-memory insert order and
    /// re-derived on every recovery, so selecting by it could clone a different definition before and
    /// after a restart.
    /// </remarks>
    [Required, JsonPropertyName("publicationTxId")]
    public string PublicationTxId { get; init; } = string.Empty;
}

/// <summary>
/// Response body for the amend / clone-to-draft endpoint (Feature 142). Returned with HTTP 201
/// and a <c>Location</c> header pointing at the new draft.
/// </summary>
/// <param name="DraftBlueprintId">The id of the newly-created draft.</param>
/// <param name="DraftBlueprintId">The draft id — the SAME blueprint id, since an amendment is a new version of it.</param>
/// <param name="SourcePublicationTxId">The published definition the draft was derived from (lineage).</param>
/// <param name="RegisterId">The source register (lineage).</param>
public sealed record CloneFromPublishedResponseBody(
    [property: JsonPropertyName("draftBlueprintId")] string DraftBlueprintId,
    [property: JsonPropertyName("sourcePublicationTxId")] string SourcePublicationTxId,
    [property: JsonPropertyName("registerId")] string RegisterId);
