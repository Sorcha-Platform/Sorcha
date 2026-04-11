// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Mvc;
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
    }

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
