// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Sorcha.ServiceDefaults;
using Sorcha.Wallet.Service.Services.Interfaces;

namespace Sorcha.Wallet.Service.Endpoints;

/// <summary>
/// Per-org VC issuance key lifecycle endpoints (Feature 120 US2 / T039).
/// </summary>
/// <remarks>
/// Exposes the lazy-derivation entry point so any service that mints credentials
/// can trigger the key + DID document publish without owning the wallet
/// infrastructure directly. Used by Sorcha.Haip.Service's /credential endpoint
/// to honor FR-004 'no later than first issuance' even when minting via the
/// pre-authorized_code flow.
/// </remarks>
public static class IssuanceKeyEndpoints
{
    /// <summary>Maps the issuance key endpoints into the application.</summary>
    public static IEndpointRouteBuilder MapIssuanceKeyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/orgs/{orgId:guid}/issuance-key")
            .WithTags("IssuanceKey")
            .RequireRateLimiting(RateLimitPolicies.Api);

        group.MapPost("/ensure", EnsureIssuanceKey)
            .WithName("EnsureOrgIssuanceKey")
            .WithSummary("Lazily derive (idempotent) the org's VC issuance key")
            .WithDescription(
                "Returns 200 with the active key's metadata after deriving it on first call " +
                "or returning the existing Active row on retry. Triggers DID document " +
                "regeneration on the Tenant side as a side effect. Designed for callers " +
                "that mint credentials outside the direct /credentials/issue path " +
                "(notably Sorcha.Haip.Service's pre-authorized_code flow).")
            .Produces<EnsureIssuanceKeyResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status500InternalServerError);

        group.MapPost("/sign", SignWithIssuanceKey)
            .WithName("SignWithOrgIssuanceKey")
            .WithSummary("Sign-on-behalf — produce a signature using the org's Active issuance key")
            .WithDescription(
                "Signs the supplied bytes with the org's Active issuance private key and " +
                "returns the signature plus the kid / issuer DID / algorithm to embed in " +
                "the JWS header. Used by Sorcha.Haip.Service to delegate credential signing " +
                "to wallet without transmitting private key material across services. " +
                "Returns 404 when no Active issuance key exists.")
            .Produces<SignWithIssuanceKeyResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status500InternalServerError);

        group.MapPost("/rotate", RotateIssuanceKey)
            .WithName("RotateOrgIssuanceKey")
            .WithSummary("Rotate the org's Active issuance key (Feature 120 US6)")
            .WithDescription(
                "Marks the existing Active key as Rotated and derives a new key at the next " +
                "rotation index. Triggers DID document regeneration. Body carries the " +
                "governance-op id that authorised the rotation (auditing).")
            .Produces<EnsureIssuanceKeyResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/revoke", RevokeIssuanceKey)
            .WithName("RevokeOrgIssuanceKey")
            .WithSummary("Revoke a specific issuance key by rotation index (Feature 120 US6)")
            .WithDescription(
                "Marks the named rotation as Revoked, records the reason and authorising " +
                "governance op, and triggers DID document regeneration. The published " +
                "DID document drops the revoked key from assertionMethod so verifiers " +
                "reject credentials signed by it. Idempotent on already-revoked keys.")
            .Produces<RevokeIssuanceKeyResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        // Anonymous DID-document resolution endpoint for did:sorcha:org:{addr}.
        // Verifiers (e.g. Sorcha.Haip.Service.HaipPresentationVerifier via SorchaDidResolver)
        // need the public key bytes for an org's issuance key, but the wallet GET endpoint
        // is auth-gated. This endpoint exposes only the public W3C DID document — no
        // private material — and is keyed on wallet address so resolvers don't need
        // service-to-service auth.
        app.MapGet("/api/v1/wallets/{address}/did-document", ResolveWalletDidDocument)
            .WithName("ResolveWalletDidDocument")
            .WithSummary("Public DID document resolution by wallet address (Feature 120 verifier path)")
            .WithDescription(
                "Returns the W3C DID document for did:sorcha:org:{address} including the " +
                "wallet's public key under the standard #key-1 verification method id and, " +
                "if an Active issuance key exists for the controlling org, an alias " +
                "#vc-issuance-{n} verification method pointing at the same key bytes.")
            .AllowAnonymous()
            .Produces(StatusCodes.Status200OK, contentType: "application/did+json")
            .Produces(StatusCodes.Status404NotFound)
            .RequireRateLimiting(RateLimitPolicies.Api);

        return app;
    }

    private static async Task<IResult> EnsureIssuanceKey(
        [FromRoute] Guid orgId,
        [FromServices] IIssuanceKeyService service,
        CancellationToken ct)
    {
        var state = await service.GetOrDeriveAsync(orgId, ct);
        if (state is null)
        {
            // F120 lazy derivation not applicable for this org (no provisioned master key).
            return Results.Ok(new { provisioned = false, organizationId = orgId });
        }

        // #1518: report whether the issuer DID document is actually published, rather than a bare
        // 200 that says nothing. It will NOT be, for a brand-new org, because the canonical wallet
        // it anchors on is provisioned by Tenant's 60-second reconciliation sweep — see #1525, where
        // the real fix is for an admin to create the org's wallet deliberately (and receive its
        // recovery phrase) instead of a timer doing it silently.
        //
        // Deliberately does not wait: #1523 tried, sat for 15s, and still lost the race.
        var didPublished = await service
            .PublishDidDocumentAsync(orgId, ct)
            .ConfigureAwait(false);

        return Results.Ok(new EnsureIssuanceKeyResponse(
            OrganizationId: state.OrganizationId,
            RotationIndex: state.RotationIndex,
            Algorithm: state.Algorithm,
            Thumbprint: state.Thumbprint,
            DerivedAt: state.DerivedAt,
            DidDocumentPublished: didPublished));
    }

    private static async Task<IResult> RotateIssuanceKey(
        [FromRoute] Guid orgId,
        [FromBody] RotateIssuanceKeyRequest request,
        [FromServices] IIssuanceKeyService service,
        CancellationToken ct)
    {
        var newRow = await service.RotateAsync(orgId, request.GovernanceOpId, ct);
        if (newRow is null)
            return Results.NotFound(new { error = "No Active issuance key to rotate" });

        return Results.Ok(new EnsureIssuanceKeyResponse(
            OrganizationId: newRow.OrganizationId,
            RotationIndex: newRow.RotationIndex,
            Algorithm: newRow.Algorithm,
            Thumbprint: newRow.Thumbprint,
            DerivedAt: newRow.DerivedAt));
    }

    private static async Task<IResult> RevokeIssuanceKey(
        [FromRoute] Guid orgId,
        [FromBody] RevokeIssuanceKeyRequest request,
        [FromServices] IIssuanceKeyService service,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            return Results.BadRequest(new { error = "Reason is required" });

        var row = await service.RevokeAsync(
            orgId, request.RotationIndex, request.Reason, request.GovernanceOpId, ct);
        if (row is null)
            return Results.NotFound(new { error = "No issuance key found for the supplied rotation index" });

        return Results.Ok(new RevokeIssuanceKeyResponse(
            OrganizationId: row.OrganizationId,
            RotationIndex: row.RotationIndex,
            Status: row.Status.ToString(),
            RevokedAt: row.RevokedAt));
    }

    private static async Task<IResult> ResolveWalletDidDocument(
        [FromRoute] string address,
        [FromServices] Sorcha.Wallet.Core.Repositories.Interfaces.IWalletRepository walletRepository,
        [FromServices] IIssuanceKeyService issuanceKeyService,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(address))
            return Results.BadRequest(new { error = "address is required" });

        var wallet = await walletRepository.GetByAddressAsync(address, cancellationToken: ct);
        if (wallet is null) return Results.NotFound();

        var did = $"did:sorcha:org:{address}";
        var publicKeyB64 = wallet.PublicKey ?? "";
        var algorithm = wallet.Algorithm;

        // Feature 120 US6 — emit one VM per issuance-key row, marking revoked rows by
        // EXCLUDING them from assertionMethod / authentication. Revoked VMs remain in the
        // document for verifiable history (a verifier inspecting an old credential's
        // signature can still read the public key) but cannot be used to assert new
        // credentials. Result: revocation enforcement is a property of the published
        // document shape, not a separate verifier-side lookup.
        IReadOnlyList<Sorcha.Wallet.Core.Domain.Entities.IssuanceKeyState>? allKeys = null;
        if (Guid.TryParse(wallet.Tenant, out var orgId))
        {
            allKeys = await issuanceKeyService.ListAllAsync(orgId, ct);
        }

        var vmId = $"{did}#key-1";
        var vmJwk = TryBuildJwkForResponse(algorithm, publicKeyB64);
        var vms = new List<object>
        {
            new
            {
                id = vmId,
                type = MapAlgorithmToKeyType(algorithm),
                controller = did,
                publicKeyJwk = vmJwk
            }
        };
        var assertionIds = new List<string> { vmId };

        if (allKeys is not null)
        {
            foreach (var k in allKeys)
            {
                var kid = $"{did}#vc-issuance-{k.RotationIndex}";
                vms.Add(new
                {
                    id = kid,
                    type = MapAlgorithmToKeyType(k.Algorithm),
                    controller = did,
                    publicKeyJwk = vmJwk
                });
                if (k.Status == Sorcha.Wallet.Core.Domain.Enums.IssuanceKeyStatus.Active)
                {
                    assertionIds.Add(kid);
                }
            }
        }

        var doc = new Dictionary<string, object>
        {
            ["@context"] = new[] { "https://www.w3.org/ns/did/v1", "https://w3id.org/security/jwk/v1" },
            ["id"] = did,
            ["verificationMethod"] = vms,
            ["assertionMethod"] = assertionIds.ToArray(),
            ["authentication"] = assertionIds.ToArray()
        };
        return Results.Json(doc, contentType: "application/did+json");
    }

    private static object? TryBuildJwkForResponse(string algorithm, string publicKeyBase64)
    {
        if (string.IsNullOrEmpty(publicKeyBase64)) return null;
        byte[] raw;
        try { raw = Convert.FromBase64String(publicKeyBase64); }
        catch (FormatException) { return null; }

        var b64u = (byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var algo = algorithm?.ToUpperInvariant();
        if (algo == "ED25519")
            return new { kty = "OKP", crv = "Ed25519", x = b64u(raw) };
        if ((algo is "NIST-P256" or "NISTP256" or "P-256" or "P256" or "ECDSA-P256")
            && raw.Length == 65 && raw[0] == 0x04)
        {
            return new
            {
                kty = "EC",
                crv = "P-256",
                x = b64u(raw.AsSpan(1, 32).ToArray()),
                y = b64u(raw.AsSpan(33, 32).ToArray())
            };
        }
        return null;
    }

    private static string MapAlgorithmToKeyType(string algorithm)
        => algorithm?.ToUpperInvariant() switch
        {
            "ED25519" => "Ed25519VerificationKey2020",
            "NIST-P256" or "NISTP256" or "P-256" or "P256" or "ECDSA-P256" => "JsonWebKey2020",
            _ => "JsonWebKey2020"
        };

    private static async Task<IResult> SignWithIssuanceKey(
        [FromRoute] Guid orgId,
        [FromBody] SignWithIssuanceKeyRequest request,
        [FromServices] IIssuanceKeyService service,
        [FromServices] Sorcha.Cryptography.SdJwt.ISdJwtSigner signer,
        CancellationToken ct)
    {
        if (request is null || string.IsNullOrEmpty(request.DataBase64Url))
            return Results.BadRequest(new { error = "DataBase64Url is required" });

        var material = await service.GetActiveSigningMaterialAsync(orgId, ct);
        if (material is null) return Results.NotFound();

        byte[] data;
        try
        {
            data = Sorcha.Cryptography.SdJwt.Base64UrlHelper.Decode(request.DataBase64Url);
        }
        catch (FormatException)
        {
            return Results.BadRequest(new { error = "DataBase64Url is not valid base64url" });
        }

        try
        {
            var signature = signer.Sign(data, material.PrivateKey, material.Algorithm);
            return Results.Ok(new SignWithIssuanceKeyResponse(
                SignatureBase64Url: Sorcha.Cryptography.SdJwt.Base64UrlHelper.Encode(signature),
                Kid: material.Kid,
                IssuerDid: material.IssuerDid,
                Algorithm: material.Algorithm,
                RotationIndex: material.RotationIndex));
        }
        finally
        {
            // Wipe the decrypted private key as soon as signing completes.
            CryptographicOperations.ZeroMemory(material.PrivateKey);
        }
    }
}

/// <summary>Response shape for <c>POST /api/v1/orgs/{orgId}/issuance-key/ensure</c>.</summary>
public sealed record EnsureIssuanceKeyResponse(
    Guid OrganizationId,
    int RotationIndex,
    string Algorithm,
    string Thumbprint,
    DateTimeOffset DerivedAt,
    /// <summary>
    /// Whether the org's issuer DID document is published and resolvable. False means issuance
    /// still works — it re-ensures before every signature and fails closed — but the issuer DID
    /// will not resolve until the org first signs something (#1518).
    /// </summary>
    bool DidDocumentPublished = true);

/// <summary>Request body for rotation — carries the authorising governance-op id.</summary>
public sealed record RotateIssuanceKeyRequest(Guid GovernanceOpId);

/// <summary>Request body for revocation.</summary>
public sealed record RevokeIssuanceKeyRequest(int RotationIndex, string Reason, Guid GovernanceOpId);

/// <summary>Response for revocation.</summary>
public sealed record RevokeIssuanceKeyResponse(
    Guid OrganizationId,
    int RotationIndex,
    string Status,
    DateTimeOffset? RevokedAt);

/// <summary>Request body for sign-on-behalf — base64url-encoded bytes to sign.</summary>
public sealed record SignWithIssuanceKeyRequest(string DataBase64Url);

/// <summary>Response shape for sign-on-behalf — signature + JWS header material.</summary>
public sealed record SignWithIssuanceKeyResponse(
    string SignatureBase64Url,
    string Kid,
    string IssuerDid,
    string Algorithm,
    int RotationIndex);
