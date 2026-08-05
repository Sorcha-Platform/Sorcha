// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Provenance.Engine;
using Sorcha.Provenance.Engine.Seams;
using Sorcha.Register.Core.Storage;
using Sorcha.Register.Service.Provenance;

namespace Sorcha.Register.Service.Endpoints;

/// <summary>
/// Read-only provenance surfaces: a register's docket spine, and per-docket verification.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two endpoints rather than one, and the split is load-bearing</b> (plan D6, research R-006).
/// The spine runs NO verification; the trail verifies exactly one docket. Verification is O(n)
/// hashing per docket, so a spine that verified eagerly would be O(n·m) on a list view, and SC-007
/// requires a 5,000-docket register to stay usable. Separate routes make the expensive path
/// impossible to enter by accident, rather than relying on a caller remembering.
/// </para>
/// <para>
/// <b>Authorization is the tier gate composed ON the role gate</b>, not instead of it (CLAUDE.md
/// pattern #13): an administrator, holding a platform-tier token. FR-019 restricts these views to
/// administrators of the owning organisation; the external-auditor path is Phase 3's portable
/// export, not a loosened policy here.
/// </para>
/// <para>
/// <b>Missing evidence is a 200, not a 500.</b> A trail whose evidence cannot be fully assembled
/// returns rows marked unverified with reasons. An auditor needs to know <i>which</i> link could not
/// be established; a 500 tells them nothing (FR-020, SC-009). Genuine faults — unknown register,
/// unknown docket — keep their normal status codes.
/// </para>
/// </remarks>
public static class ProvenanceEndpoints
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;

    /// <summary>Maps the provenance endpoints.</summary>
    public static void MapProvenanceEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/provenance")
            .WithTags("Provenance")
            .RequireAuthorization(
                AuthorizationPolicies.RequireAdministrator,
                AuthorizationPolicies.RequirePlatformAudience);

        group.MapGet("/registers/{registerId}", GetRegisterSpine)
            .WithName("GetRegisterProvenanceSpine")
            .WithSummary("List a register's dockets from genesis")
            .WithDescription(
                "Returns docket summaries in order, each with its proposer, signer count, and whether " +
                "the validator set changed at that point. Runs NO verification — use the per-docket " +
                "trail endpoint for that. A signer count of zero is valid and expected on " +
                "single-validator deployments.")
            .Produces<RegisterSpineResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/registers/{registerId}/dockets/{docketNumber}", GetDocketTrail)
            .WithName("GetDocketProvenanceTrail")
            .WithSummary("Verify one docket and return its evidence trail")
            .WithDescription(
                "Runs the provenance checks for a single docket and returns one result per layer, each " +
                "stating what it was compared against. Returns 200 even when evidence is missing: " +
                "affected checks report 'unverified' with a reason, because an auditor needs to know " +
                "WHICH link could not be established.")
            .Produces<ProvenanceTrailResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/instances/{instanceId}", () => Results.StatusCode(StatusCodes.Status501NotImplemented))
            .WithName("GetInstanceProvenanceLineage")
            .WithSummary("Application lineage (Phase 2 — reserved, not implemented)")
            .WithDescription(
                "Reserved so the route shape is settled before Phase 2 implements it. Returns 501.")
            .Produces(StatusCodes.Status501NotImplemented);
    }

    /// <summary>
    /// The docket spine. Runs no checks — see the class remarks for why that is a design constraint
    /// rather than an optimisation deferred to later.
    /// </summary>
    private static async Task<IResult> GetRegisterSpine(
        string registerId,
        ulong? fromDocket,
        int? pageSize,
        IReadOnlyRegisterRepository repository,
        IRosterAsOfResolver rosterResolver,
        ProvenanceMetrics metrics,
        CancellationToken cancellationToken)
    {
        using var timer = metrics.TimeTrail("spine");

        if (!await repository.IsLocalRegisterAsync(registerId, cancellationToken))
        {
            return Results.NotFound(new { error = $"No register '{registerId}' on this node." });
        }

        var size = Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize);
        var start = fromDocket ?? 0;

        var dockets = (await repository.GetDocketsAsync(registerId, cancellationToken))
            .Where(d => d.Id >= start)
            .OrderBy(d => d.Id)
            .ToList();

        var rosterChanges = await rosterResolver.GetRosterChangeDocketsAsync(registerId, cancellationToken);

        var page = dockets.Take(size).ToList();
        var hasMore = dockets.Count > size;

        var entries = page
            .Select(d => new DocketSpineEntry(
                DocketNumber: d.Id,
                SealedAt: d.TimeStamp,
                ProposerValidatorId: d.ProposerValidatorId,
                SignerCount: d.Votes?.Count ?? 0,
                RosterChanged: rosterChanges.Contains(d.Id)))
            .ToList();

        return Results.Ok(new RegisterSpineResponse(
            registerId,
            entries,
            hasMore,
            hasMore ? page[^1].Id + 1 : null));
    }

    /// <summary>
    /// One docket's trail: assemble evidence, delegate to the engine, return the result.
    /// </summary>
    /// <remarks>
    /// Deliberately thin. Every decision about what a check may claim belongs in the engine, where it
    /// is unit-testable against hand-built tampered evidence; anything decided here would be
    /// reachable only through HTTP.
    /// </remarks>
    private static async Task<IResult> GetDocketTrail(
        string registerId,
        ulong docketNumber,
        IReadOnlyRegisterRepository repository,
        IDocketEvidenceAssembler assembler,
        IRosterAsOfResolver rosterResolver,
        IMerkleRootCalculator merkleRootCalculator,
        ProvenanceMetrics metrics,
        CancellationToken cancellationToken)
    {
        using var timer = metrics.TimeTrail("trail");

        if (!await repository.IsLocalRegisterAsync(registerId, cancellationToken))
        {
            return Results.NotFound(new { error = $"No register '{registerId}' on this node." });
        }

        var evidence = await assembler.AssembleAsync(registerId, docketNumber, cancellationToken);
        if (evidence is null)
        {
            return Results.NotFound(
                new { error = $"No docket {docketNumber} in register '{registerId}' on this node." });
        }

        // The engine is handed the roster that applied AT THIS DOCKET, and never the current one
        // (plan D5). Resolution failure arrives as null and renders as unverified, not as an error.
        var rosterAsOf = await rosterResolver.ResolveAsync(registerId, docketNumber, cancellationToken);

        var trail = new DocketProvenanceVerifier(merkleRootCalculator)
            .Verify(registerId, evidence.Docket, rosterAsOf, evidence.Anchor);

        metrics.RecordChecks(trail);

        return Results.Ok(ProvenanceContractMapper.ToResponse(trail));
    }
}
