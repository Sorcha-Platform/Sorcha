// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Mvc;
using Sorcha.ServiceClients.Trust;
using Sorcha.ServiceDefaults;
using Sorcha.Tenant.Service.Trust;

namespace Sorcha.Tenant.Service.Endpoints;

/// <summary>
/// REST endpoints for X.509 trust anchor and organisation certificate management.
/// </summary>
public static class TrustEndpoints
{
    /// <summary>
    /// Maps trust endpoints under /api/v1/trust.
    /// </summary>
    public static void MapTrustEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/trust")
            .WithTags("Trust");

        // Provisioning — requires SystemAdmin or TenantAdmin
        group.MapPost("/tenants/{tenantId}/provision", ProvisionTrustAnchor)
            .WithName("ProvisionTrustAnchor")
            .WithSummary("Provision a self-signed root CA for a tenant")
            .WithDescription(
                "Creates a self-signed X.509 root CA certificate for the tenant. " +
                "Idempotent — returns the existing root if already provisioned.")
            .Produces<TrustAnchorResponse>(StatusCodes.Status200OK)
            .RequireAuthorization("RequireAdministrator");

        // Trust anchor — public (verifiers need to fetch the root cert)
        group.MapGet("/tenants/{tenantId}/trust-anchor", GetTrustAnchor)
            .WithName("GetTrustAnchor")
            .WithSummary("Get the tenant's trust anchor certificate (DER)")
            .WithDescription(
                "Returns the DER-encoded self-signed root CA certificate. " +
                "Public endpoint — verifiers use it to validate organisation certificate chains.")
            .Produces(StatusCodes.Status200OK, contentType: "application/pkix-cert")
            .Produces(StatusCodes.Status404NotFound)
            .AllowAnonymous();

        // Org cert enrolment — requires auth
        group.MapPost("/tenants/{tenantId}/orgs/{orgWalletAddress}/enrol", EnrolOrgCert)
            .WithName("EnrolOrgCert")
            .WithSummary("Issue an organisation certificate signed by the tenant root CA")
            .WithDescription(
                "Issues an X.509 certificate for the organisation's HAIP classical co-key, " +
                "signed by the tenant's root CA. Binds the org's DID to the certificate via SAN URI.")
            .Produces<OrgCertEnrolmentResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .RequireAuthorization("RequireAdministrator");

        // Org cert chain — public
        group.MapGet("/tenants/{tenantId}/orgs/{orgWalletAddress}/cert-chain", GetOrgCertChain)
            .WithName("GetOrgCertChain")
            .WithSummary("Get the organisation's certificate chain (leaf + root)")
            .WithDescription(
                "Returns the organisation certificate and the tenant root CA as base64-encoded DER. " +
                "Used by the Wallet Service to populate the x5c JWS header on HAIP-path credentials.")
            .Produces<CertChainResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .AllowAnonymous();

        // Org cert revocation — requires auth (Feature 096 US4)
        group.MapPost("/tenants/{tenantId}/orgs/{orgWalletAddress}/revoke", RevokeOrgCert)
            .WithName("RevokeOrgCert")
            .WithSummary("Revoke an organisation certificate")
            .WithDescription(
                "Marks the organisation certificate as revoked and regenerates the tenant CRL. " +
                "Subsequent CRL fetches will include the revoked serial. Idempotent.")
            .Produces<OrgCertEnrolmentResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization("RequireAdministrator");

        // CRL — public, cacheable (Feature 096 US4, Category 5 gap closure)
        group.MapGet("/tenants/{tenantId}/crl", GetTenantCrl)
            .WithName("GetTenantCrl")
            .WithSummary("Get the tenant's Certificate Revocation List (DER)")
            .WithDescription(
                "Returns the DER-encoded signed CRL for the tenant root CA. Served as " +
                "application/pkix-crl with a Cache-Control max-age aligned to the CRL's nextUpdate. " +
                "Public endpoint — strict X.509 validators embed the CDP URL in org certs and fetch " +
                "this endpoint during chain validation.")
            .Produces(StatusCodes.Status200OK, contentType: "application/pkix-crl")
            .Produces(StatusCodes.Status404NotFound)
            // Public but rate-limited (SEC-002): the CRL is embedded as a CDP URL in every
            // org cert so any verifier may fetch it. The Api policy keeps abusive clients
            // off the CA signing path.
            .RequireRateLimiting(RateLimitPolicies.Api)
            .AllowAnonymous();

        // Feature 135 (US2) — operator trust-list snapshot management. Snapshots are consulted by
        // the `trustlist` trust source (external EUDI anchors). Admin-scoped, strict rate limit.
        group.MapPut("/trustlists/{trustListId}", PutTrustList)
            .WithName("PutTrustListSnapshot")
            .WithSummary("Upload or replace a trust-list snapshot")
            .WithDescription(
                "Stores an operator-curated set of trusted X.509 root certificates (base64 DER) under " +
                "the given id. Referenced by a credential requirement's trust policy `trustlist` source; " +
                "the snapshot id + freshness are copied into the trust evidence on every decision that " +
                "used the list. A live LOTL feed is a future provider behind the same seam.")
            .Produces<TrustListSummaryResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .RequireAuthorization("RequireAdministrator")
            .RequireRateLimiting(RateLimitPolicies.Strict);

        group.MapGet("/trustlists/{trustListId}", GetTrustList)
            .WithName("GetTrustListSnapshot")
            .WithSummary("Get trust-list snapshot metadata")
            .WithDescription(
                "Returns the snapshot id, root count, source, and freshness so operators can audit " +
                "what is loaded. Not used per-verification (the trust source caches the roots).")
            .Produces<TrustListSummaryResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization("RequireAdministrator")
            .RequireRateLimiting(RateLimitPolicies.Strict);

        group.MapGet("/trustlists", ListTrustLists)
            .WithName("ListTrustListSnapshots")
            .WithSummary("List available trust-list snapshots")
            .WithDescription("Returns the id + freshness of every loaded trust-list snapshot.")
            .Produces<IReadOnlyList<TrustListSummaryResponse>>(StatusCodes.Status200OK)
            .RequireAuthorization("RequireAdministrator")
            .RequireRateLimiting(RateLimitPolicies.Strict);
    }

    internal static IResult PutTrustList(
        string trustListId,
        [FromBody] UploadTrustListRequest request,
        OperatorSnapshotTrustListProvider provider)
    {
        if (request.Roots is null || request.Roots.Count == 0)
            return Results.BadRequest(new { error = "At least one root certificate is required" });

        var roots = new List<byte[]>(request.Roots.Count);
        foreach (var base64 in request.Roots)
        {
            try
            {
                roots.Add(Convert.FromBase64String(base64));
            }
            catch (FormatException)
            {
                return Results.BadRequest(new { error = "A root certificate entry is not valid Base64 DER" });
            }
        }

        var snapshot = new TrustListSnapshot
        {
            Id = trustListId,
            Roots = roots,
            Source = request.Source ?? "operator-upload",
            CreatedAt = DateTimeOffset.UtcNow,
            Freshness = request.Freshness ?? DateTimeOffset.UtcNow
        };
        provider.Upsert(snapshot);

        return Results.Ok(ToSummary(snapshot));
    }

    internal static IResult GetTrustList(string trustListId, OperatorSnapshotTrustListProvider provider)
    {
        var snapshot = provider.List().FirstOrDefault(s => string.Equals(s.Id, trustListId, StringComparison.Ordinal));
        return snapshot is null
            ? Results.NotFound(new { error = $"No trust-list snapshot '{trustListId}'" })
            : Results.Ok(ToSummary(snapshot));
    }

    internal static IResult ListTrustLists(OperatorSnapshotTrustListProvider provider)
        => Results.Ok(provider.List().Select(ToSummary).ToList());

    private static TrustListSummaryResponse ToSummary(TrustListSnapshot s) => new()
    {
        TrustListId = s.Id,
        RootCount = s.Roots.Count,
        Source = s.Source,
        CreatedAt = s.CreatedAt,
        Freshness = s.Freshness
    };

    private static async Task<IResult> ProvisionTrustAnchor(
        string tenantId,
        ITrustProvider trustProvider,
        CancellationToken ct)
    {
        var root = await trustProvider.ProvisionTrustAnchorAsync(tenantId, ct: ct);

        return Results.Ok(new TrustAnchorResponse
        {
            TenantId = root.TenantId,
            SerialNumber = root.SerialNumber,
            SubjectDn = root.SubjectDn,
            Algorithm = root.Algorithm,
            NotBefore = root.NotBefore,
            NotAfter = root.NotAfter,
            CertificateBase64 = Convert.ToBase64String(root.CertificateDer)
        });
    }

    private static async Task<IResult> GetTrustAnchor(
        string tenantId,
        ITrustProvider trustProvider,
        CancellationToken ct)
    {
        var root = await trustProvider.GetTrustAnchorAsync(tenantId, ct);
        if (root == null)
            return Results.NotFound(new { error = $"No trust anchor provisioned for tenant '{tenantId}'" });

        return Results.Bytes(root.CertificateDer, "application/pkix-cert");
    }

    private static async Task<IResult> EnrolOrgCert(
        string tenantId,
        string orgWalletAddress,
        [FromBody] EnrolOrgCertRequest request,
        ITrustProvider trustProvider,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.OrgPublicKeyBase64))
            return Results.BadRequest(new { error = "OrgPublicKeyBase64 is required" });
        if (string.IsNullOrWhiteSpace(request.OrgDisplayName))
            return Results.BadRequest(new { error = "OrgDisplayName is required" });

        // TODO(096-#15): Verify the submitted public key matches the org wallet's
        // HaipIssuerCoKey via IHaipIssuerCoKeyService. Currently accepts any key.
        byte[] orgPublicKey;
        try
        {
            orgPublicKey = Convert.FromBase64String(request.OrgPublicKeyBase64);
        }
        catch (FormatException)
        {
            return Results.BadRequest(new { error = "OrgPublicKeyBase64 is not valid Base64" });
        }

        try
        {
            var enrolment = await trustProvider.IssueOrgCertAsync(
                tenantId, orgWalletAddress, orgPublicKey, request.OrgDisplayName, ct);

            return Results.Ok(new OrgCertEnrolmentResponse
            {
                OrgWalletAddress = enrolment.OrgWalletAddress,
                SerialNumber = enrolment.SerialNumber,
                SubjectDn = enrolment.SubjectDn,
                SanUri = enrolment.SanUri,
                NotBefore = enrolment.NotBefore,
                NotAfter = enrolment.NotAfter,
                CertificateBase64 = Convert.ToBase64String(enrolment.CertificateDer)
            });
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> GetOrgCertChain(
        string tenantId,
        string orgWalletAddress,
        ITrustProvider trustProvider,
        CancellationToken ct)
    {
        var chain = await trustProvider.GetOrgCertChainAsync(tenantId, orgWalletAddress, ct);
        if (chain == null)
            return Results.NotFound(new { error = $"No active cert for org '{orgWalletAddress}' under tenant '{tenantId}'" });

        return Results.Ok(new CertChainResponse
        {
            OrgCertBase64 = Convert.ToBase64String(chain.Value.OrgCertDer),
            RootCertBase64 = Convert.ToBase64String(chain.Value.RootCertDer)
        });
    }

    private static async Task<IResult> RevokeOrgCert(
        string tenantId,
        string orgWalletAddress,
        [FromBody] RevokeOrgCertRequest? request,
        ITrustProvider trustProvider,
        CancellationToken ct)
    {
        try
        {
            var enrolment = await trustProvider.RevokeOrgCertAsync(
                tenantId, orgWalletAddress, request?.Reason, ct);

            return Results.Ok(new OrgCertEnrolmentResponse
            {
                OrgWalletAddress = enrolment.OrgWalletAddress,
                SerialNumber = enrolment.SerialNumber,
                SubjectDn = enrolment.SubjectDn,
                SanUri = enrolment.SanUri,
                NotBefore = enrolment.NotBefore,
                NotAfter = enrolment.NotAfter,
                CertificateBase64 = Convert.ToBase64String(enrolment.CertificateDer),
            });
        }
        catch (InvalidOperationException ex)
        {
            return Results.NotFound(new { error = ex.Message });
        }
    }

    private static async Task<IResult> GetTenantCrl(
        string tenantId,
        ITrustProvider trustProvider,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var crl = await trustProvider.GetOrPublishCrlAsync(tenantId, ct);
        if (crl is null)
        {
            return Results.NotFound(new
            {
                error = $"Tenant '{tenantId}' has no provisioned root CA — provision before fetching CRL",
            });
        }

        // Cache-Control aligned to nextUpdate — strict validators expect caches to
        // expire at the same instant the CRL declares stale.
        var maxAge = Math.Max(60, (int)(crl.NextUpdate - DateTimeOffset.UtcNow).TotalSeconds);
        httpContext.Response.Headers.CacheControl = $"public, max-age={maxAge}";

        return Results.Bytes(crl.CrlDer, "application/pkix-crl");
    }
}

/// <summary>Response for trust anchor provisioning.</summary>
public class TrustAnchorResponse
{
    public required string TenantId { get; init; }
    public required string SerialNumber { get; init; }
    public required string SubjectDn { get; init; }
    public required string Algorithm { get; init; }
    public DateTimeOffset NotBefore { get; init; }
    public DateTimeOffset NotAfter { get; init; }
    public required string CertificateBase64 { get; init; }
}

/// <summary>Request to enrol an organisation certificate.</summary>
public class EnrolOrgCertRequest
{
    public required string OrgPublicKeyBase64 { get; init; }
    public required string OrgDisplayName { get; init; }
}

/// <summary>Response for org cert enrolment.</summary>
public class OrgCertEnrolmentResponse
{
    public required string OrgWalletAddress { get; init; }
    public required string SerialNumber { get; init; }
    public required string SubjectDn { get; init; }
    public required string SanUri { get; init; }
    public DateTimeOffset NotBefore { get; init; }
    public DateTimeOffset NotAfter { get; init; }
    public required string CertificateBase64 { get; init; }
}

/// <summary>Response for cert chain retrieval.</summary>
public class CertChainResponse
{
    public required string OrgCertBase64 { get; init; }
    public required string RootCertBase64 { get; init; }
}

/// <summary>Request to upload/replace a trust-list snapshot (feature 135 US2).</summary>
public class UploadTrustListRequest
{
    /// <summary>Provenance, e.g. "EU LOTL 2026-Q2 manual export".</summary>
    public string? Source { get; init; }

    /// <summary>Trusted root certificates, base64-encoded DER.</summary>
    public required List<string> Roots { get; init; }

    /// <summary>Operator-asserted as-of time copied into trust evidence; defaults to now.</summary>
    public DateTimeOffset? Freshness { get; init; }
}

/// <summary>Trust-list snapshot metadata (feature 135 US2).</summary>
public class TrustListSummaryResponse
{
    public required string TrustListId { get; init; }
    public int RootCount { get; init; }
    public required string Source { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset Freshness { get; init; }
}

/// <summary>Request to revoke an organisation certificate.</summary>
public class RevokeOrgCertRequest
{
    /// <summary>
    /// Optional human-readable revocation reason (e.g. "keyCompromise",
    /// "cessationOfOperation"). Stored for audit; not yet surfaced in the
    /// CRL entry extensions.
    /// </summary>
    public string? Reason { get; init; }
}
