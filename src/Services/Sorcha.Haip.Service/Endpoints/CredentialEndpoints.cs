// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Sorcha.Haip.Service.Models;
using Sorcha.Haip.Service.Services;
using HaipCredentialMinter = Sorcha.Haip.Service.Services.HaipCredentialMinter;
using CredentialOfferService = Sorcha.Haip.Service.Services.CredentialOfferService;

namespace Sorcha.Haip.Service.Endpoints;

/// <summary>
/// OpenID4VCI credential endpoint — issues SD-JWT VCs to HAIP wallets.
/// </summary>
public static class CredentialEndpoints
{
    public static void MapCredentialEndpoints(this WebApplication app)
    {
        app.MapPost("/credential", IssueCredential)
            .WithName("IssueCredential")
            .WithTags("HAIP Credential")
            .WithSummary("Issue a credential to an external HAIP wallet")
            .WithDescription(
                "Accepts a JWT proof of possession from the wallet, validates it against the c_nonce, " +
                "mints an SD-JWT VC with cnf binding to the wallet's holder key, and returns it.")
            .Produces<object>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status501NotImplemented)
            .AllowAnonymous(); // TODO(097): require Bearer token once token store is wired
    }

    private static async Task<IResult> IssueCredential(
        [FromBody] CredentialRequest request,
        NonceStore nonceStore,
        HaipCredentialMinter minter,
        CredentialOfferService offerService,
        IConfiguration configuration,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("Sorcha.Haip.Service.Endpoints.CredentialEndpoints");

        // Validate format
        if (request.Format != "vc+sd-jwt")
        {
            return Results.BadRequest(new
            {
                error = "unsupported_credential_format",
                error_description = $"Format '{request.Format}' is not supported. Use 'vc+sd-jwt'."
            });
        }

        // Validate proof presence
        if (request.Proof == null || string.IsNullOrWhiteSpace(request.Proof.Jwt))
        {
            return Results.BadRequest(new
            {
                error = "invalid_proof",
                error_description = "JWT proof of possession is required"
            });
        }

        if (request.Proof.ProofType != "jwt")
        {
            return Results.BadRequest(new
            {
                error = "invalid_proof",
                error_description = $"Proof type '{request.Proof.ProofType}' is not supported. Use 'jwt'."
            });
        }

        // Parse the JWT proof to extract the c_nonce and holder key
        JsonElement proofPayload;
        JsonElement? holderJwk;
        try
        {
            var proofParts = request.Proof.Jwt.Split('.');
            if (proofParts.Length != 3)
            {
                return Results.BadRequest(new { error = "invalid_proof", error_description = "JWT proof must have 3 segments" });
            }

            // Parse header for holder key (jwk in header per OpenID4VCI)
            var headerBytes = Base64Url.DecodeFromChars(proofParts[0]);
            var header = JsonSerializer.Deserialize<JsonElement>(headerBytes);
            holderJwk = header.TryGetProperty("jwk", out var jwk) ? jwk : null;

            // Parse payload for nonce
            var payloadBytes = Base64Url.DecodeFromChars(proofParts[1]);
            proofPayload = JsonSerializer.Deserialize<JsonElement>(payloadBytes);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to parse JWT proof");
            return Results.BadRequest(new { error = "invalid_proof", error_description = "Malformed JWT proof" });
        }

        // Validate c_nonce binding
        if (!proofPayload.TryGetProperty("nonce", out var nonceElement))
        {
            return Results.BadRequest(new { error = "invalid_proof", error_description = "JWT proof must contain a nonce claim" });
        }

        var nonce = nonceElement.GetString();
        if (string.IsNullOrWhiteSpace(nonce) || !await nonceStore.ConsumeAsync(nonce, ct))
        {
            return Results.BadRequest(new
            {
                error = "invalid_proof",
                error_description = "c_nonce is invalid, expired, or already consumed"
            });
        }

        // Validate iat clock skew (±60s)
        if (proofPayload.TryGetProperty("iat", out var iatElement))
        {
            var iat = DateTimeOffset.FromUnixTimeSeconds(iatElement.GetInt64());
            var now = DateTimeOffset.UtcNow;
            if (iat < now.AddSeconds(-60) || iat > now.AddSeconds(60))
            {
                return Results.BadRequest(new { error = "invalid_proof", error_description = "JWT proof iat is outside the ±60s clock skew window" });
            }
        }

        logger.LogInformation("JWT proof validated, holder key present: {HasHolderKey}", holderJwk.HasValue);

        // Mint the SD-JWT VC credential
        // In production, the signing key comes from IHaipIssuerCoKeyService via
        // the Wallet Service. For now, use an ephemeral key if no config is set.
        var signingKeyBase64 = configuration.GetValue<string>("Haip:IssuerSigningKey");
        var signingAlgorithm = configuration.GetValue<string>("Haip:IssuerSigningAlgorithm") ?? "ES256";

        byte[] signingKey;
        if (!string.IsNullOrWhiteSpace(signingKeyBase64))
        {
            signingKey = Convert.FromBase64String(signingKeyBase64);
        }
        else
        {
            logger.LogWarning("Using ephemeral signing key — set Haip:IssuerSigningKey for production");
            using var ecdsa = System.Security.Cryptography.ECDsa.Create(
                System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
            signingKey = ecdsa.ExportECPrivateKey();
        }

        var issuerUrl = configuration.GetValue<string>("Haip:IssuerUrl")
            ?? "https://sorcha.example/haip";

        // Default claims if no specific offer is linked
        // TODO: Correlate the access token back to a specific offer for claim/type lookup
        var claims = new Dictionary<string, object>
        {
            ["type"] = "SorchaCredential"
        };
        var disclosablePaths = new List<string>();
        var credentialType = "SorchaCredential";

        try
        {
            var credential = await minter.MintCredentialAsync(
                issuerDid: issuerUrl,
                holderJwk: holderJwk ?? default,
                credentialType: credentialType,
                claims: claims,
                disclosablePaths: disclosablePaths,
                signingKey: signingKey,
                algorithm: signingAlgorithm,
                expiresAt: DateTimeOffset.UtcNow.AddYears(1),
                ct: ct);

            // Generate a fresh c_nonce for the next request
            var (newNonce, nonceExpiresIn) = await nonceStore.CreateAsync(ct);

            logger.LogInformation("Issued HAIP credential, token length={Length}", credential.Length);

            return Results.Ok(new
            {
                format = "vc+sd-jwt",
                credential,
                c_nonce = newNonce,
                c_nonce_expires_in = nonceExpiresIn
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to mint credential");
            return Results.Json(new
            {
                error = "server_error",
                error_description = "Credential minting failed"
            }, statusCode: 500);
        }
    }
}
