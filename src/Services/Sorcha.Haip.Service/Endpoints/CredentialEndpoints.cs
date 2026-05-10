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
    /// <summary>
    /// Maps the HAIP credential issuance endpoint.
    /// </summary>
    public static void MapCredentialEndpoints(this WebApplication app)
    {
        app.MapPost("/credential", IssueCredential)
            .WithName("IssueCredential")
            .WithTags("HAIP Credential")
            .WithSummary("Issue a credential to an external HAIP wallet")
            .WithDescription(
                "Accepts a Bearer access token (correlating to a credential offer), a JWT proof of " +
                "possession from the wallet, and returns a minted SD-JWT VC bound to the wallet's " +
                "holder key via cnf.")
            .Produces<object>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .AllowAnonymous(); // Bearer token is required; validated inline so we can return OAuth-style error bodies rather than a blank 401 from the auth middleware.
    }

    private static async Task<IResult> IssueCredential(
        HttpContext httpContext,
        [FromBody] CredentialRequest request,
        NonceStore nonceStore,
        AccessTokenStore tokenStore,
        HaipCredentialMinter minter,
        CredentialOfferService offerService,
        IConfiguration configuration,
        ILoggerFactory loggerFactory,
        Sorcha.ServiceClients.IssuanceKey.IIssuanceKeyClient issuanceKeyClient,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("Sorcha.Haip.Service.Endpoints.CredentialEndpoints");

        // Require a Bearer access token and correlate it to a credential offer.
        // OpenID4VCI §7.2: the credential endpoint MUST reject requests without a
        // valid access token. The fallback-to-defaults behaviour we had before
        // would silently issue a "SorchaCredential" to any caller — security gap.
        var authHeader = httpContext.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authHeader) ||
            !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return Results.Json(new
            {
                error = "invalid_token",
                error_description = "Bearer access token is required",
            }, statusCode: StatusCodes.Status401Unauthorized);
        }

        var bearerToken = authHeader["Bearer ".Length..].Trim();
        var offerId = await tokenStore.LookupAsync(bearerToken, ct);
        if (!offerId.HasValue)
        {
            return Results.Json(new
            {
                error = "invalid_token",
                error_description = "Access token is invalid or expired",
            }, statusCode: StatusCodes.Status401Unauthorized);
        }

        var offer = offerService.GetOffer(offerId.Value);
        if (offer is null)
        {
            // Token is valid but the offer has been evicted from the store —
            // treat as an expired token rather than silently issuing.
            return Results.Json(new
            {
                error = "invalid_token",
                error_description = "Offer for this access token has expired",
            }, statusCode: StatusCodes.Status401Unauthorized);
        }

        // Validate format
        if (request.Format != "vc+sd-jwt")
        {
            return Results.BadRequest(new
            {
                error = "unsupported_credential_format",
                error_description = $"Format '{request.Format}' is not supported. Use 'vc+sd-jwt'."
            });
        }

        // Validate vct (credential type) matches the offer. The wallet must request
        // exactly the credential type that was offered — issuing a different type
        // would break the trust chain from the offer → token → minted credential.
        if (!string.IsNullOrWhiteSpace(request.Vct)
            && !string.Equals(request.Vct, offer.CredentialType, StringComparison.Ordinal))
        {
            return Results.BadRequest(new
            {
                error = "unsupported_credential_type",
                error_description = $"Requested vct '{request.Vct}' does not match the offer ('{offer.CredentialType}')",
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

        // Parse the JWT proof to extract the c_nonce, holder key, signing
        // input, and signature bytes. Everything that depends on the JWT
        // structure is computed here, under a single length guard, so later
        // code can access segments without reparsing.
        JsonElement proofPayload;
        JsonElement? holderJwk;
        byte[] signingInput;
        byte[] signature;
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

            // Compute the signing input + decoded signature for later verification.
            signingInput = Encoding.ASCII.GetBytes($"{proofParts[0]}.{proofParts[1]}");
            signature = Base64Url.DecodeFromChars(proofParts[2]);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to parse JWT proof");
            return Results.BadRequest(new { error = "invalid_proof", error_description = "Malformed JWT proof" });
        }

        // Verify the JWT proof signature using the holder's JWK
        if (holderJwk == null)
        {
            return Results.BadRequest(new { error = "invalid_proof", error_description = "JWT proof header must contain a jwk claim" });
        }

        try
        {
            var jwk = holderJwk.Value;
            var kty = jwk.TryGetProperty("kty", out var ktyProp) ? ktyProp.GetString() : null;
            var crv = jwk.TryGetProperty("crv", out var crvProp) ? crvProp.GetString() : null;

            if (kty == "EC" && crv == "P-256"
                && jwk.TryGetProperty("x", out var xProp) && jwk.TryGetProperty("y", out var yProp))
            {
                // ES256 / P-256 — sorcha-agent path
                var xBytes = Base64Url.DecodeFromChars(xProp.GetString()!);
                var yBytes = Base64Url.DecodeFromChars(yProp.GetString()!);

                using var ecdsa = ECDsa.Create(new ECParameters
                {
                    Curve = ECCurve.NamedCurves.nistP256,
                    Q = new ECPoint { X = xBytes, Y = yBytes }
                });

                if (!ecdsa.VerifyData(signingInput, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence)
                    && !ecdsa.VerifyData(signingInput, signature, HashAlgorithmName.SHA256))
                {
                    return Results.BadRequest(new { error = "invalid_proof", error_description = "JWT proof signature verification failed" });
                }
            }
            else if (kty == "OKP" && crv == "Ed25519"
                && jwk.TryGetProperty("x", out var edXProp))
            {
                // EdDSA / Ed25519 — local Sorcha wallet path (Feature 103 wave 13).
                // Sorcha wallets are typically ED25519, so the holder JWK
                // for "Receive on this device" needs an OKP/Ed25519 verifier.
                var publicKey = Base64Url.DecodeFromChars(edXProp.GetString()!);
                if (publicKey.Length != 32)
                {
                    return Results.BadRequest(new
                    {
                        error = "invalid_proof",
                        error_description = "Ed25519 public key must be 32 bytes"
                    });
                }

                if (!Sodium.PublicKeyAuth.VerifyDetached(signature, signingInput, publicKey))
                {
                    return Results.BadRequest(new { error = "invalid_proof", error_description = "JWT proof signature verification failed" });
                }
            }
            else
            {
                return Results.BadRequest(new
                {
                    error = "invalid_proof",
                    error_description = "Supported JWK shapes: EC P-256 (ES256) and OKP Ed25519 (EdDSA)"
                });
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "JWT proof signature verification error");
            return Results.BadRequest(new { error = "invalid_proof", error_description = "JWT proof signature verification failed" });
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

        // Validate aud — must match the credential issuer URL (HAIP §7.2.1.2)
        var expectedAud = configuration.GetValue<string>("Haip:IssuerUrl") ?? "https://sorcha.example/haip";
        if (proofPayload.TryGetProperty("aud", out var audElement))
        {
            var audValue = audElement.GetString();
            if (!string.Equals(audValue, expectedAud, StringComparison.Ordinal))
            {
                return Results.BadRequest(new
                {
                    error = "invalid_proof",
                    error_description = $"JWT proof aud must be '{expectedAud}'"
                });
            }
        }
        else
        {
            return Results.BadRequest(new
            {
                error = "invalid_proof",
                error_description = "JWT proof must contain an aud claim matching the credential issuer URL"
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

        // Feature 120 T039 — ensure the org's VC issuance key + published DID
        // document exist before signing. Lazy-derives on first issuance for
        // the org (FR-004), idempotent on retries, non-throwing — falls back
        // to ephemeral / config signing-key on any wallet failure.
        if (Guid.TryParse(offer.TenantId, out var issuanceOrgId))
        {
            try
            {
                await issuanceKeyClient.EnsureAsync(issuanceOrgId, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Issuance key ensure failed for org {OrgId} during HAIP credential mint — DID document publish skipped",
                    issuanceOrgId);
            }
        }

        // Use the offer resolved above — every branch below is guaranteed to have
        // a valid offer because the Bearer / lookup gates already rejected the
        // unauthenticated and expired-token cases.
        var claims = offer.Claims;
        var disclosablePaths = offer.DisclosablePaths;
        var credentialType = offer.CredentialType;
        logger.LogInformation("Resolved offer {OfferId}: type={Type}, claims={Count}",
            offerId, credentialType, claims.Count);

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

            // Inject issuer public key into JWS header for verifier key resolution.
            // In production this would be an x5c chain; for dev/walkthrough mode we
            // embed the issuer's public JWK so the verifier can self-resolve.
            credential = InjectIssuerJwkInHeader(credential, signingKey, signingAlgorithm);

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

    /// <summary>
    /// Injects the issuer's public key as a JWK into the JWS header.
    /// This enables the verifier to resolve the issuer key without x5c or DID.
    /// Development/walkthrough mode only — production should use x5c chains.
    /// </summary>
    private static string InjectIssuerJwkInHeader(string credential, byte[] signingKey, string algorithm)
    {
        try
        {
            var alg = algorithm.ToUpperInvariant();
            if (alg is not ("ES256" or "P-256" or "P256")) return credential;

            using var ecdsa = System.Security.Cryptography.ECDsa.Create();
            ecdsa.ImportECPrivateKey(signingKey, out _);
            var parameters = ecdsa.ExportParameters(includePrivateParameters: false);

            var issuerJwk = new Dictionary<string, string>
            {
                ["kty"] = "EC",
                ["crv"] = "P-256",
                ["x"] = System.Buffers.Text.Base64Url.EncodeToString(parameters.Q.X!),
                ["y"] = System.Buffers.Text.Base64Url.EncodeToString(parameters.Q.Y!)
            };

            // Parse the existing header, add jwk, re-sign
            var parts = credential.TrimEnd('~').Split('~');
            var jwtParts = parts[0].Split('.');

            var headerBytes = System.Buffers.Text.Base64Url.DecodeFromChars(jwtParts[0]);
            var header = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(headerBytes)!;
            header["jwk"] = issuerJwk;

            var newHeaderB64 = System.Buffers.Text.Base64Url.EncodeToString(
                System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(header));
            var signingInput = System.Text.Encoding.UTF8.GetBytes($"{newHeaderB64}.{jwtParts[1]}");
            var signature = ecdsa.SignData(signingInput, System.Security.Cryptography.HashAlgorithmName.SHA256);
            var signatureB64 = System.Buffers.Text.Base64Url.EncodeToString(signature);

            var newJwt = $"{newHeaderB64}.{jwtParts[1]}.{signatureB64}";
            return newJwt + "~" + string.Join("~", parts[1..]) + "~";
        }
        catch
        {
            return credential; // Return original on any failure
        }
    }
}
