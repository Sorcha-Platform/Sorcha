// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Sorcha.Wallet.Service.Services.Interfaces;

namespace Sorcha.Wallet.Service.Endpoints;

/// <summary>
/// Feature 181 US4 — internal service-to-service endpoints exposing an org's P-256 certificate-issuing
/// key to the Tenant Service. The Tenant Service resolves the key (eligibility + SPKI) to build a CSR and
/// validate an imported certificate, and delegates CSR signing here so the private key never leaves the
/// Wallet Service (FR-018, research R9/R10). <c>RequireService</c> — service principals only.
/// </summary>
public static class IssuerCertKeyInternalEndpoints
{
    /// <summary>Maps the internal issuer-cert-key endpoints.</summary>
    public static IEndpointRouteBuilder MapIssuerCertKeyInternalEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/internal/wallets/{walletAddress}/issuer-cert-key")
            .WithTags("Internal")
            .ExcludeFromDescription()
            .RequireAuthorization(AuthorizationPolicies.RequireService);

        group.MapGet("", Resolve)
            .WithName("ResolveOrgIssuerCertKey")
            .WithSummary("Resolve the org's P-256 cert-issuing public key (SPKI) + eligibility");

        group.MapPost("/sign", Sign)
            .WithName("SignWithOrgIssuerCertKey")
            .WithSummary("Sign a pre-computed digest with the org's P-256 cert-issuing key (raw r‖s)");

        return app;
    }

    private static async Task<IResult> Resolve(
        string walletAddress,
        IOrgIssuerCertKeyService service,
        CancellationToken ct)
    {
        var resolution = await service.ResolveAsync(walletAddress, ct);
        return Results.Ok(new IssuerCertKeyResolutionResponse(
            resolution.Eligible,
            resolution.Reason,
            resolution.BoundKeySource,
            resolution.PublicKeySpki is null ? null : Convert.ToBase64String(resolution.PublicKeySpki)));
    }

    private static async Task<IResult> Sign(
        string walletAddress,
        [FromBody] IssuerCertKeySignRequest request,
        IOrgIssuerCertKeyService service,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.DigestBase64))
        {
            return Results.BadRequest(new { error = "digestBase64 is required" });
        }

        byte[] digest;
        try
        {
            digest = Convert.FromBase64String(request.DigestBase64);
        }
        catch (FormatException)
        {
            return Results.BadRequest(new { error = "digestBase64 is not valid Base64" });
        }

        try
        {
            var signature = await service.SignPreHashedAsync(walletAddress, digest, ct);
            return Results.Ok(new IssuerCertKeySignResponse(Convert.ToBase64String(signature)));
        }
        catch (OrgIssuerCertKeyNotEligibleException)
        {
            return Results.Json(new { error = "CERT_KEY_NOT_ELIGIBLE" }, statusCode: StatusCodes.Status422UnprocessableEntity);
        }
    }

    /// <summary>Wire shape for the resolve response.</summary>
    public sealed record IssuerCertKeyResolutionResponse(
        bool Eligible, string? Reason, string? BoundKeySource, string? PublicKeySpkiBase64);

    /// <summary>Wire shape for the sign request.</summary>
    public sealed record IssuerCertKeySignRequest(string DigestBase64);

    /// <summary>Wire shape for the sign response (raw r‖s, base64).</summary>
    public sealed record IssuerCertKeySignResponse(string SignatureBase64);
}
