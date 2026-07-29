// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Sorcha.ServiceClients.OrgDidDocument;
using Sorcha.ServiceDefaults;
using Sorcha.Tenant.Service.Data.Repositories;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Endpoints;

/// <summary>
/// HTTP endpoints for per-organisation DID documents (Feature 120 US2).
/// Contract: <c>specs/120-production-issuer-signature-verification/contracts/org-did-document-endpoint.openapi.yaml</c>.
/// </summary>
public static class OrgDidDocumentEndpoints
{
    /// <summary>Maps the org DID document endpoints into the application.</summary>
    public static IEndpointRouteBuilder MapOrgDidDocumentEndpoints(this IEndpointRouteBuilder app)
    {
        // Public W3C-conformant endpoint — anonymous, cacheable, application/did+json.
        app.MapGet("/orgs/{orgId:guid}/did.json", ResolveDocument)
            .WithName("ResolveOrgDidDocument")
            .WithSummary("Resolve the published DID document for an organisation")
            .WithDescription(
                "Returns the W3C DID Core document published for the organisation as " +
                "application/did+json. The document declares the canonical did:sorcha:org:* " +
                "identifier alongside the federated did:web:{platform}:orgs:{orgId} form via " +
                "alsoKnownAs, and lists every Active issuance key under both versioned and " +
                "RFC 7638 thumbprint kid styles. Anonymous, cacheable for 6h.")
            .AllowAnonymous()
            .Produces(StatusCodes.Status200OK, contentType: "application/did+json")
            .Produces(StatusCodes.Status404NotFound)
            .RequireRateLimiting(RateLimitPolicies.Api);

        // Feature 149 — resolve by DID (verifier holds did:sorcha:org:{A}, not the org GUID).
        app.MapGet("/orgs/by-did/{did}/did.json", ResolveDocumentByDid)
            .WithName("ResolveOrgDidDocumentByDid")
            .WithSummary("Resolve the published DID document for an organisation by its DID")
            .WithDescription(
                "Returns the published W3C DID document whose canonical id equals the supplied " +
                "did:sorcha:org:{walletAddress}. Lets a verifier fetch the document from the issuer " +
                "DID alone — the by-orgId route requires the GUID, which a verifier does not hold. " +
                "Backed by the existing PrimaryDid index. Anonymous, cacheable for 6h.")
            .AllowAnonymous()
            .Produces(StatusCodes.Status200OK, contentType: "application/did+json")
            .Produces(StatusCodes.Status404NotFound)
            .RequireRateLimiting(RateLimitPolicies.Api);

        // Internal regenerate endpoint — wallet → tenant trigger after a key event.
        //
        // C1 (catch-up security review 2026-07-29): this MUST be RequireService. It shipped with
        // no authorization attribute at all, and the Tenant Service configures no fallback policy,
        // so it was anonymous on the published Tenant port. The document it writes is the issuer
        // key material every verifier trusts for this org, and the handler takes the caller's
        // WalletAddress + JWKs verbatim — so an anonymous POST carrying a victim's orgId and
        // wallet address (both public values) with an attacker's JWK made attacker-signed
        // credentials verify as that organisation's. The two GETs above are deliberately
        // AllowAnonymous (public DID resolution); only this write path is privileged.
        app.MapPost("/orgs/{orgId:guid}/did-document/regenerate", RegenerateDocument)
            .WithName("RegenerateOrgDidDocument")
            .WithSummary("Regenerate the published DID document for an organisation")
            .WithDescription(
                "Internal endpoint used by Wallet Service after a key event (issuance key " +
                "derivation / rotation / revocation). Rebuilds the document from the supplied " +
                "key snapshot, persists it, and returns the new version. Idempotent — returns " +
                "the existing row unchanged if the recomputed key-version fingerprint matches. " +
                "Requires a service-tier token: this is an internal service-to-service call, " +
                "never a user-initiated one.")
            .RequireAuthorization(AuthorizationPolicies.RequireService)
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .RequireRateLimiting(RateLimitPolicies.Api);

        return app;
    }

    private static async Task<IResult> ResolveDocument(
        HttpContext http,
        [FromRoute] Guid orgId,
        [FromServices] IOrgDidDocumentService service,
        CancellationToken ct)
    {
        var doc = await service.GetAsync(orgId, ct);
        if (doc is null) return Results.NotFound();

        // 6 hours per FR-007 — DID documents change infrequently and cache aggressively.
        http.Response.Headers.CacheControl = "public, max-age=21600";

        return Results.Content(
            content: doc.DocumentJson,
            contentType: "application/did+json",
            contentEncoding: System.Text.Encoding.UTF8,
            statusCode: StatusCodes.Status200OK);
    }

    private static async Task<IResult> ResolveDocumentByDid(
        HttpContext http,
        [FromRoute] string did,
        [FromServices] IOrgDidDocumentService service,
        CancellationToken ct)
    {
        var doc = await service.GetByPrimaryDidAsync(did, ct);
        if (doc is null) return Results.NotFound();

        // 6 hours — matches the by-orgId route; DID documents change infrequently.
        http.Response.Headers.CacheControl = "public, max-age=21600";

        return Results.Content(
            content: doc.DocumentJson,
            contentType: "application/did+json",
            contentEncoding: System.Text.Encoding.UTF8,
            statusCode: StatusCodes.Status200OK);
    }

    private static async Task<IResult> RegenerateDocument(
        [FromRoute] Guid orgId,
        [FromBody] OrgDidRegenerateRequest request,
        [FromServices] IOrgDidDocumentService service,
        [FromServices] IOrganizationRepository organizations,
        [FromServices] ILogger<OrgDidDocument> logger,
        CancellationToken ct)
    {
        if (request.OrganizationId != orgId)
            return Results.BadRequest(new { error = "Route orgId does not match payload OrganizationId." });

        // C1 (catch-up security review 2026-07-29) — defence in depth behind RequireService.
        // The canonical identifier is built verbatim as did:sorcha:org:{WalletAddress} from this
        // body, so the address must be the organisation's own and not merely well-formed.
        var org = await organizations.GetByIdAsync(orgId, ct);
        if (org is null)
            return Results.BadRequest(new { error = "Unknown organisation." });

        if (string.IsNullOrWhiteSpace(org.WalletAddress))
        {
            // Deliberately permissive, NOT an oversight: an org whose canonical wallet address has
            // not been recorded yet has nothing to compare against, and refusing here would break
            // first-time DID publication — a failure the Wallet side swallows into `false`, so it
            // would surface only as credentials that silently never verify. RequireService already
            // bounds who can reach this. Tighten to a hard refusal once the provisioning order
            // guarantees WalletAddress is always set before the first key event.
            logger.LogWarning(
                "Organisation {OrgId} has no recorded canonical wallet address; publishing DID "
                + "document from the supplied address {WalletAddress} without verification.",
                orgId, request.WalletAddress);
        }
        else if (!string.Equals(org.WalletAddress, request.WalletAddress, StringComparison.Ordinal))
        {
            logger.LogError(
                "REFUSED DID-document regeneration for organisation {OrgId}: supplied wallet address "
                + "{Supplied} is not the organisation's canonical address {Canonical}. This is what an "
                + "issuer-impersonation attempt looks like.",
                orgId, request.WalletAddress, org.WalletAddress);

            return Results.BadRequest(new
            {
                error = "WalletAddress does not match the organisation's canonical wallet address."
            });
        }

        if (service is OrgDidDocumentService impl)
        {
            var row = await impl.RegenerateFromSnapshotAsync(request, ct);
            return Results.Ok(new { row.Version, row.LastRegeneratedAt, row.KeyVersionFingerprint });
        }

        return Results.Problem("Snapshot-based regeneration not supported by registered service.");
    }
}
