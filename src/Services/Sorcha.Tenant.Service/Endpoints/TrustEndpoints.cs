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
            .RequireAuthorization("RequireAdministrator", "RequirePlatformAudience");

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
                "Feature 181 US5 — the server resolves the org's P-256 key itself (primary ES256 else HAIP " +
                "co-key), signs the internal cert with the tenant root, binds the org DID via SAN URI, and " +
                "re-issues with auditable history. Doubles as backfill. 422 CERT_KEY_NOT_ELIGIBLE for non-P-256 orgs.")
            .Produces<OrgCertificateView>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status422UnprocessableEntity)
            .RequireAuthorization("RequireAdministrator", "RequirePlatformAudience");

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
            .RequireAuthorization("RequireAdministrator", "RequirePlatformAudience");

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

        // Feature 181 US3 — ETSI TS 119 612 trusted-list snapshot import + lifecycle. Replaces the
        // Feature 135 placeholder PUT raw-roots route (clean break). Admin-scoped, strict rate limit.
        group.MapPost("/trustlists/import", ImportTrustList)
            .WithName("ImportTrustListSnapshot")
            .WithSummary("Import an ETSI TS 119 612 trusted-list document (upload or URL fetch-once)")
            .WithDescription(
                "Verifies the enveloped XMLDSig signature, extracts granted CA/QC anchors, and stores a " +
                "versioned snapshot. Sequence number must exceed the current Active snapshot for the same " +
                "trustListId. 400 TRUSTLIST_MALFORMED/_SIGNATURE_INVALID, 409 TRUSTLIST_SEQUENCE_REGRESSION.")
            .Produces<TrustListSnapshotSummaryResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status409Conflict)
            .DisableAntiforgery()
            .RequireAuthorization("RequireAdministrator", "RequirePlatformAudience")
            .RequireRateLimiting(RateLimitPolicies.Strict);

        group.MapGet("/trustlists", ListTrustLists)
            .WithName("ListTrustListSnapshots")
            .WithSummary("List trusted-list snapshots with freshness state")
            .WithDescription("Returns every Active snapshot's scheme identity, freshness, and anchor count.")
            .Produces<IReadOnlyList<TrustListSnapshotSummaryResponse>>(StatusCodes.Status200OK)
            .RequireAuthorization("RequireAdministrator", "RequirePlatformAudience")
            .RequireRateLimiting(RateLimitPolicies.Strict);

        group.MapGet("/trustlists/{trustListId}", GetTrustList)
            .WithName("GetTrustListSnapshot")
            .WithSummary("Get a trusted-list snapshot's detail, anchors and extraction summary")
            .Produces<TrustListSnapshotDetailResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization("RequireAdministrator", "RequirePlatformAudience")
            .RequireRateLimiting(RateLimitPolicies.Strict);

        group.MapDelete("/trustlists/{trustListId}", DeleteTrustList)
            .WithName("DeleteTrustListSnapshot")
            .WithSummary("Delete every version of a trusted-list snapshot")
            .WithDescription("Subsequent evaluations against this trustListId fail closed TRUSTLIST_UNAVAILABLE (SC-004).")
            .Produces(StatusCodes.Status204NoContent)
            .RequireAuthorization("RequireAdministrator", "RequirePlatformAudience")
            .RequireRateLimiting(RateLimitPolicies.Strict);

        // Feature 181 US4 — org-certificate lifecycle (CSR out / external cert in). Admin-scoped.
        group.MapGet("/tenants/{tenantId}/orgs/{orgWalletAddress}/certificates", ListOrgCertificates)
            .WithName("ListOrgCertificates")
            .WithSummary("List the org's certificates (internal + imported) with eligibility")
            .WithDescription(
                "Returns the org's certificates with status/validity/chain summary, plus the X.509-rail " +
                "eligibility verdict (CERT_KEY_NOT_ELIGIBLE when no P-256 key resolves).")
            .Produces<OrgCertificatesResponse>(StatusCodes.Status200OK)
            .RequireAuthorization("RequireAdministrator", "RequirePlatformAudience")
            .RequireRateLimiting(RateLimitPolicies.Strict);

        group.MapPost("/tenants/{tenantId}/orgs/{orgWalletAddress}/csr", GenerateOrgCsr)
            .WithName("GenerateOrgCsr")
            .WithSummary("Generate a CSR bound to the org's P-256 issuing key")
            .WithDescription(
                "The server resolves the org's P-256 key (primary ES256 else HAIP co-key) and signs the CSR " +
                "via the Wallet Service remote-signing generator — the private key never leaves custody. " +
                "422 CERT_KEY_NOT_ELIGIBLE when no P-256 key resolves.")
            .Produces<CsrResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status422UnprocessableEntity)
            .RequireAuthorization("RequireAdministrator", "RequirePlatformAudience")
            .RequireRateLimiting(RateLimitPolicies.Strict);

        group.MapPost("/tenants/{tenantId}/orgs/{orgWalletAddress}/certificates/import", ImportOrgCertificate)
            .WithName("ImportOrgCertificate")
            .WithSummary("Import an externally-issued certificate + chain")
            .WithDescription(
                "Validates key match / chain consistency / validity / signing suitability (FR-019); accepts " +
                "chain-with-root and chain-without-root. 422 CERT_KEY_MISMATCH | CERT_CHAIN_INVALID | " +
                "CERT_EXPIRED | CERT_UNSUITABLE.")
            .Produces<OrgCertificateView>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status422UnprocessableEntity)
            .RequireAuthorization("RequireAdministrator", "RequirePlatformAudience")
            .RequireRateLimiting(RateLimitPolicies.Strict);

        // Public (x5c chain material is public) — the Wallet Service resolves the external chain here at
        // issuance time for the x509-lotl anchor. 404 fails closed (never a tenant-root fallback, FR-020).
        group.MapGet("/tenants/{tenantId}/orgs/{orgWalletAddress}/imported-cert-chain", GetImportedCertChain)
            .WithName("GetImportedOrgCertChain")
            .WithSummary("Get the org's Active imported external certificate chain (leaf-first)")
            .WithDescription(
                "Returns the imported external chain for x509-lotl issuance. 404 when absent, expired, or " +
                "key-mismatched after rotation — the caller fails closed CERT_EXTERNAL_ANCHOR_UNAVAILABLE.")
            .Produces<ImportedChainResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .RequireRateLimiting(RateLimitPolicies.Api)
            .AllowAnonymous();

        group.MapDelete("/tenants/{tenantId}/orgs/{orgWalletAddress}/certificates/{certificateId:guid}", RetireOrgCertificate)
            .WithName("RetireOrgCertificate")
            .WithSummary("Retire an imported certificate (Status→Superseded)")
            .WithDescription("Idempotent. Internal certs use the /revoke route (CRL path).")
            .Produces(StatusCodes.Status204NoContent)
            .RequireAuthorization("RequireAdministrator", "RequirePlatformAudience")
            .RequireRateLimiting(RateLimitPolicies.Strict);

        group.MapGet("/trustlists/{trustListId}/anchors", GetTrustListAnchors)
            .WithName("GetTrustListAnchors")
            .WithSummary("Anchor distribution read for verifying services")
            .WithDescription(
                "Service-tier. Returns the Active snapshot's DER roots + sequence + freshness for the " +
                "caching HTTP ITrustListProvider. 404 TRUSTLIST_UNAVAILABLE when no snapshot exists.")
            .Produces<TrustListAnchorsResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization("RequireService")
            .RequireRateLimiting(RateLimitPolicies.Api);
    }

    internal static async Task<IResult> ImportTrustList(
        [FromForm] string trustListId,
        HttpContext http,
        TrustedListImportService importService,
        IHttpClientFactory httpClientFactory,
        ILoggerFactory loggerFactory,
        IFormFile? document,
        [FromForm] string? sourceUrl,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("Sorcha.Tenant.Service.Trust.TrustList");
        if (string.IsNullOrWhiteSpace(trustListId))
            return Results.BadRequest(new { error = "trustListId is required" });

        byte[] documentBytes;
        if (document is not null)
        {
            using var ms = new MemoryStream();
            await document.CopyToAsync(ms, ct);
            documentBytes = ms.ToArray();
        }
        else if (!string.IsNullOrWhiteSpace(sourceUrl))
        {
            if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                return Results.BadRequest(new { error = "sourceUrl must be an absolute HTTPS URL" });
            try
            {
                var client = httpClientFactory.CreateClient("trustlist-fetch");
                documentBytes = await client.GetByteArrayAsync(uri, ct);
            }
            catch (HttpRequestException ex)
            {
                return Results.BadRequest(new { error = TrustListErrorCodes.Malformed, error_description = $"Fetch failed: {ex.Message}" });
            }
        }
        else
        {
            return Results.BadRequest(new { error = "Provide either a document file or a sourceUrl" });
        }

        var importedBy = ResolvePlatformUserId(http.User);
        var result = await importService.ImportAsync(trustListId, documentBytes, sourceUrl, importedBy, ct);
        if (result.Success)
        {
            var snapshot = result.Snapshot!;
            TrustListMetrics.RecordSnapshotActive(snapshot.TrustListId, snapshot.SequenceNumber);
            logger.LogInformation(
                "Trusted-list snapshot imported: {TrustListId}#{Sequence} territory={Territory} anchors={AnchorCount} signer={Signer} importedBy={ImportedBy}",
                snapshot.TrustListId, snapshot.SequenceNumber, snapshot.SchemeTerritory,
                snapshot.Anchors.Count, snapshot.SignerCertSubject, importedBy);
            return Results.Created($"/api/v1/trust/trustlists/{trustListId}", ToSummary(snapshot));
        }

        logger.LogWarning("Trusted-list import rejected for {TrustListId}: {Code} {Message}",
            trustListId, result.ErrorCode, result.ErrorMessage);
        var problem = new { error = result.ErrorCode, error_description = result.ErrorMessage };
        return result.ErrorCode == TrustListErrorCodes.SequenceRegression
            ? Results.Json(problem, statusCode: StatusCodes.Status409Conflict)
            : Results.BadRequest(problem);
    }

    internal static async Task<IResult> ListTrustLists(
        Sorcha.Tenant.Service.Storage.ITrustedListSnapshotStore store, CancellationToken ct)
    {
        var snapshots = await store.ListActiveAsync(ct);
        return Results.Ok(snapshots.Select(ToSummary).ToList());
    }

    internal static async Task<IResult> GetTrustList(
        string trustListId, Sorcha.Tenant.Service.Storage.ITrustedListSnapshotStore store, CancellationToken ct)
    {
        var snapshot = await store.GetActiveAsync(trustListId, ct);
        return snapshot is null
            ? Results.NotFound(new { error = $"No trusted-list snapshot '{trustListId}'" })
            : Results.Ok(ToDetail(snapshot));
    }

    internal static async Task<IResult> DeleteTrustList(
        string trustListId, Sorcha.Tenant.Service.Storage.ITrustedListSnapshotStore store,
        ILoggerFactory loggerFactory, CancellationToken ct)
    {
        var removed = await store.DeleteAsync(trustListId, ct);
        TrustListMetrics.RecordSnapshotRemoved(trustListId);
        loggerFactory.CreateLogger("Sorcha.Tenant.Service.Trust.TrustList")
            .LogInformation("Trusted-list snapshot deleted: {TrustListId} (existed={Existed})", trustListId, removed);
        return Results.NoContent();
    }

    internal static async Task<IResult> GetTrustListAnchors(
        string trustListId, Sorcha.Tenant.Service.Storage.ITrustedListSnapshotStore store, CancellationToken ct)
    {
        var snapshot = await store.GetActiveAsync(trustListId, ct);
        if (snapshot is null)
        {
            return Results.NotFound(new { error = TrustListErrorCodes.Unavailable });
        }

        var freshness = TrustListFreshness.Compute(snapshot.NextUpdate, snapshot.ListIssueDateTime, DateTimeOffset.UtcNow);
        return Results.Ok(new TrustListAnchorsResponse
        {
            TrustListId = snapshot.TrustListId,
            SequenceNumber = snapshot.SequenceNumber,
            Freshness = freshness.ToString(),
            NextUpdate = snapshot.NextUpdate,
            RootsDerBase64 = snapshot.Anchors.Select(a => Convert.ToBase64String(a.CertificateDer)).ToList(),
        });
    }

    private static Guid ResolvePlatformUserId(System.Security.Claims.ClaimsPrincipal user)
    {
        var claim = user.FindFirst("platform_user_id")?.Value;
        return Guid.TryParse(claim, out var id) ? id : Guid.Empty;
    }

    private static TrustListSnapshotSummaryResponse ToSummary(Sorcha.Tenant.Service.Models.TrustedListSnapshot s) => new()
    {
        TrustListId = s.TrustListId,
        SequenceNumber = s.SequenceNumber,
        SchemeTerritory = s.SchemeTerritory,
        SchemeOperatorName = s.SchemeOperatorName,
        ListIssueDateTime = s.ListIssueDateTime,
        NextUpdate = s.NextUpdate,
        Freshness = TrustListFreshness.Compute(s.NextUpdate, s.ListIssueDateTime, DateTimeOffset.UtcNow).ToString(),
        AnchorCount = s.Anchors.Count,
        SignerCertSubject = s.SignerCertSubject,
        SignerCertThumbprint = s.SignerCertThumbprint,
        ImportedAt = s.ImportedAt,
        ImportedBy = s.ImportedByPlatformUserId,
        Status = s.Status.ToString(),
    };

    private static TrustListSnapshotDetailResponse ToDetail(Sorcha.Tenant.Service.Models.TrustedListSnapshot s) => new()
    {
        Summary = ToSummary(s),
        Anchors = s.Anchors.Select(a => new TrustListAnchorResponse
        {
            SubjectDn = a.SubjectDn,
            Thumbprint = a.Thumbprint,
            ServiceTypeIdentifier = a.ServiceTypeIdentifier,
            ServiceStatus = a.ServiceStatus,
            NotBefore = a.NotBefore,
            NotAfter = a.NotAfter,
        }).ToList(),
        ExtractionSummary = s.ExtractionSummary,
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
        [FromBody] EnrolOrgCertRequest? request,
        HttpContext http,
        Sorcha.Tenant.Service.Trust.IOrgCertificateService certService,
        CancellationToken ct)
    {
        // Feature 181 US5 (T048) — the server resolves the org's P-256 key itself (primary ES256 else HAIP
        // co-key); the caller no longer supplies a key (closes the unvalidated-key TODO). Doubles as the
        // backfill action for pre-existing orgs and re-issues with auditable history (FR-023d).
        var displayName = string.IsNullOrWhiteSpace(request?.OrgDisplayName)
            ? orgWalletAddress
            : request!.OrgDisplayName!;
        var createdBy = ResolvePlatformUserId(http.User);

        var result = await certService.EnrolInternalAsync(tenantId, orgWalletAddress, displayName, createdBy, ct);
        if (!result.Success)
        {
            // FR-024 — typed ineligibility replaces the ASN.1 500.
            return Results.Json(new { error = result.ErrorCode }, statusCode: StatusCodes.Status422UnprocessableEntity);
        }
        return Results.Ok(ToCertView(result.Certificate!));
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

    // ---- Feature 181 US4 — org-certificate lifecycle handlers ----

    internal static async Task<IResult> ListOrgCertificates(
        string tenantId, string orgWalletAddress,
        Sorcha.Tenant.Service.Trust.IOrgCertificateService certService, CancellationToken ct)
    {
        var eligibility = await certService.GetEligibilityAsync(tenantId, orgWalletAddress, ct);
        var certificates = await certService.ListAsync(tenantId, orgWalletAddress, ct);
        return Results.Ok(new OrgCertificatesResponse
        {
            Eligibility = new OrgCertEligibilityView
            {
                Eligible = eligibility.Eligible,
                Reason = eligibility.Reason,
                BoundKeySource = eligibility.BoundKeySource?.ToString(),
            },
            Certificates = certificates.Select(ToCertView).ToList(),
        });
    }

    internal static async Task<IResult> GenerateOrgCsr(
        string tenantId, string orgWalletAddress,
        [FromBody] GenerateCsrRequest? request,
        HttpContext http,
        Sorcha.Tenant.Service.Trust.IOrgCertificateService certService, CancellationToken ct)
    {
        var createdBy = ResolvePlatformUserId(http.User);
        var result = await certService.GenerateCsrAsync(tenantId, orgWalletAddress, request?.SubjectDn, createdBy, ct);
        if (!result.Success)
        {
            return Results.Json(new { error = result.ErrorCode }, statusCode: StatusCodes.Status422UnprocessableEntity);
        }
        return Results.Created(
            $"/api/v1/trust/tenants/{tenantId}/orgs/{orgWalletAddress}/csr",
            new CsrResponse
            {
                CsrPem = result.CsrPem!,
                BoundKeySource = result.BoundKeySource!.ToString()!,
                BoundPublicKeyThumbprint = result.BoundPublicKeyThumbprint!,
            });
    }

    internal static async Task<IResult> ImportOrgCertificate(
        string tenantId, string orgWalletAddress,
        [FromBody] ImportCertificateRequest request,
        HttpContext http,
        Sorcha.Tenant.Service.Trust.IOrgCertificateService certService, CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.CertificatePem))
        {
            return Results.BadRequest(new { error = "certificatePem is required" });
        }

        if (!TryDecodeCertificate(request.CertificatePem, out var leafDer))
        {
            return Results.BadRequest(new { error = "certificatePem is not valid PEM or base64 DER" });
        }

        var chainDer = new List<byte[]>();
        foreach (var entry in request.ChainPem ?? [])
        {
            if (!TryDecodeCertificate(entry, out var der))
            {
                return Results.BadRequest(new { error = "a chainPem entry is not valid PEM or base64 DER" });
            }
            chainDer.Add(der);
        }

        var createdBy = ResolvePlatformUserId(http.User);
        var result = await certService.ImportAsync(tenantId, orgWalletAddress, leafDer, chainDer, createdBy, ct);
        if (!result.Success)
        {
            return Results.Json(
                new { error = result.ErrorCode, error_description = result.ErrorMessage },
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }
        return Results.Created(
            $"/api/v1/trust/tenants/{tenantId}/orgs/{orgWalletAddress}/certificates/{result.Certificate!.Id}",
            ToCertView(result.Certificate!));
    }

    internal static async Task<IResult> GetImportedCertChain(
        string tenantId, string orgWalletAddress,
        Sorcha.Tenant.Service.Trust.IOrgCertificateService certService, CancellationToken ct)
    {
        var result = await certService.ResolveActiveImportedChainAsync(tenantId, orgWalletAddress, ct);
        if (!result.Available || result.Chain is null)
        {
            return Results.NotFound(new { error = result.ErrorCode ?? "CERT_EXTERNAL_ANCHOR_UNAVAILABLE" });
        }
        return Results.Ok(new ImportedChainResponse
        {
            ChainDerBase64 = result.Chain.Select(Convert.ToBase64String).ToList(),
        });
    }

    internal static async Task<IResult> RetireOrgCertificate(
        string tenantId, string orgWalletAddress, Guid certificateId,
        Sorcha.Tenant.Service.Trust.IOrgCertificateService certService, CancellationToken ct)
    {
        await certService.RetireImportedAsync(tenantId, orgWalletAddress, certificateId, ct);
        return Results.NoContent();
    }

    private static bool TryDecodeCertificate(string input, out byte[] der)
    {
        der = [];
        var trimmed = input.Trim();
        try
        {
            if (trimmed.Contains("-----BEGIN"))
            {
                using var cert = System.Security.Cryptography.X509Certificates.X509Certificate2
                    .CreateFromPem(trimmed);
                der = cert.RawData;
                return true;
            }
            der = Convert.FromBase64String(trimmed);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or System.Security.Cryptography.CryptographicException)
        {
            return false;
        }
    }

    private static OrgCertificateView ToCertView(Sorcha.Tenant.Service.Models.OrgCertificateRecord c) => new()
    {
        Id = c.Id,
        Provenance = c.Provenance.ToString(),
        Status = c.Status.ToString(),
        SerialNumber = c.SerialNumber,
        SubjectDn = c.SubjectDn,
        San = c.San,
        NotBefore = c.NotBefore,
        NotAfter = c.NotAfter,
        BoundKeySource = c.BoundKeySource.ToString(),
        ChainSummary = new List<byte[]> { c.CertificateDer }.Concat(c.ChainDer)
            .Select(der =>
            {
                using var x = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadCertificate(der);
                return x.Subject;
            }).ToList(),
        CertificateBase64 = Convert.ToBase64String(c.CertificateDer),
        CreatedAt = c.CreatedAt,
    };
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

/// <summary>Request to enrol an organisation certificate (Feature 181 US5 — key resolved server-side).</summary>
public class EnrolOrgCertRequest
{
    /// <summary>Optional display name for the certificate subject CN; defaults to the org wallet address.</summary>
    public string? OrgDisplayName { get; init; }
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

/// <summary>Feature 181 US3 — trusted-list snapshot summary (contract §TrustListSnapshotSummary).</summary>
public class TrustListSnapshotSummaryResponse
{
    public required string TrustListId { get; init; }
    public long SequenceNumber { get; init; }
    public string? SchemeTerritory { get; init; }
    public string? SchemeOperatorName { get; init; }
    public DateTimeOffset ListIssueDateTime { get; init; }
    public DateTimeOffset? NextUpdate { get; init; }
    public required string Freshness { get; init; }
    public int AnchorCount { get; init; }
    public required string SignerCertSubject { get; init; }
    public required string SignerCertThumbprint { get; init; }
    public DateTimeOffset ImportedAt { get; init; }
    public Guid ImportedBy { get; init; }
    public required string Status { get; init; }
}

/// <summary>Feature 181 US3 — trusted-list snapshot detail (summary + anchors + extraction summary).</summary>
public class TrustListSnapshotDetailResponse
{
    public required TrustListSnapshotSummaryResponse Summary { get; init; }
    public required List<TrustListAnchorResponse> Anchors { get; init; }
    public required string ExtractionSummary { get; init; }
}

/// <summary>Feature 181 US3 — one extracted CA anchor.</summary>
public class TrustListAnchorResponse
{
    public required string SubjectDn { get; init; }
    public required string Thumbprint { get; init; }
    public required string ServiceTypeIdentifier { get; init; }
    public required string ServiceStatus { get; init; }
    public DateTimeOffset NotBefore { get; init; }
    public DateTimeOffset NotAfter { get; init; }
}

/// <summary>Feature 181 US3 — anchor distribution read for verifying services.</summary>
public class TrustListAnchorsResponse
{
    public required string TrustListId { get; init; }
    public long SequenceNumber { get; init; }
    public required string Freshness { get; init; }
    public DateTimeOffset? NextUpdate { get; init; }
    public required List<string> RootsDerBase64 { get; init; }
}

/// <summary>Feature 181 US4 — the org's imported external chain (leaf-first, base64 DER).</summary>
public class ImportedChainResponse
{
    public required List<string> ChainDerBase64 { get; init; }
}

/// <summary>Feature 181 US4 — request to generate a CSR (optional subject-DN override).</summary>
public class GenerateCsrRequest
{
    /// <summary>Optional X.500 subject DN; defaults to a CN derived from the org.</summary>
    public string? SubjectDn { get; init; }
}

/// <summary>Feature 181 US4 — CSR generation response.</summary>
public class CsrResponse
{
    public required string CsrPem { get; init; }
    public required string BoundKeySource { get; init; }
    public required string BoundPublicKeyThumbprint { get; init; }
}

/// <summary>Feature 181 US4 — request to import an externally-issued certificate + chain.</summary>
public class ImportCertificateRequest
{
    /// <summary>Leaf certificate — PEM or base64 DER.</summary>
    public required string CertificatePem { get; init; }

    /// <summary>Intermediates and/or root, any order — PEM or base64 DER each.</summary>
    public List<string>? ChainPem { get; init; }
}

/// <summary>Feature 181 US4 — org certificate list + eligibility response.</summary>
public class OrgCertificatesResponse
{
    public required OrgCertEligibilityView Eligibility { get; init; }
    public required List<OrgCertificateView> Certificates { get; init; }
}

/// <summary>Feature 181 US4 — X.509-rail eligibility verdict.</summary>
public class OrgCertEligibilityView
{
    public bool Eligible { get; init; }
    public string? Reason { get; init; }
    public string? BoundKeySource { get; init; }
}

/// <summary>Feature 181 US4 — a single org certificate (contract §OrgCertificateView).</summary>
public class OrgCertificateView
{
    public Guid Id { get; init; }
    public required string Provenance { get; init; }
    public required string Status { get; init; }
    public required string SerialNumber { get; init; }
    public required string SubjectDn { get; init; }
    public required string San { get; init; }
    public DateTimeOffset NotBefore { get; init; }
    public DateTimeOffset NotAfter { get; init; }
    public required string BoundKeySource { get; init; }
    public required List<string> ChainSummary { get; init; }
    public required string CertificateBase64 { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
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
