// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using Sorcha.ServiceDefaults;
using Sorcha.Wallet.Service.Services.Interfaces;

namespace Sorcha.Wallet.Service.Endpoints;

/// <summary>
/// Internal-only persona crypto endpoints used by the Tenant Service to
/// encrypt and decrypt a user's persona ciphertext. These endpoints must NOT
/// be routed through the API Gateway — a gateway-config guard test asserts
/// the route is unreachable from outside the service mesh.
/// </summary>
public static class PersonaCryptoEndpoints
{
    /// <summary>
    /// Maps the persona crypto endpoints.
    /// </summary>
    public static IEndpointRouteBuilder MapPersonaCryptoEndpoints(this IEndpointRouteBuilder app)
    {
        // Even though these endpoints are service-mesh only, the centralised
        // rate limiting policy is still applied so a misconfigured gateway
        // rule or a compromised internal caller cannot hammer the key
        // derivation path. `Strict` is chosen because these are high-cost
        // cryptographic operations invoked once per profile save.
        var group = app.MapGroup("/api/v1/wallets")
            .WithTags("Persona Crypto (Internal)")
            .RequireRateLimiting(RateLimitPolicies.Strict);

        group.MapPost("/{address}/persona/encrypt", EncryptPersona)
            .WithName("EncryptPersona")
            .WithSummary("Encrypt a persona plaintext blob (internal, S2S only)")
            .WithDescription(
                "Derives the sorcha:persona-vault key for the wallet owner, encrypts the supplied " +
                "plaintext with XChaCha20-Poly1305, and returns the ciphertext, nonce, and an opaque " +
                "wrappedKeyRef for the Tenant Service to store alongside the ciphertext. Requires a " +
                "service-to-service JWT with the persona:crypto scope.")
            .RequireAuthorization("RequirePersonaCrypto")
            .Produces<PersonaEncryptResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{address}/persona/decrypt", DecryptPersona)
            .WithName("DecryptPersona")
            .WithSummary("Decrypt a stored persona blob (internal, S2S only)")
            .WithDescription(
                "Derives the sorcha:persona-vault key for the wallet owner and decrypts the supplied " +
                "ciphertext. Returns the plaintext bytes for the Tenant Service to wrap in its read " +
                "DTO. Requires a service-to-service JWT with the persona:crypto scope.")
            .RequireAuthorization("RequirePersonaCrypto")
            .Produces<PersonaDecryptResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status500InternalServerError);

        return app;
    }

    private static async Task<IResult> EncryptPersona(
        string address,
        PersonaEncryptRequest request,
        IPersonaCryptoService service,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(address))
            return TypedResults.BadRequest(new { error = "Wallet address is required" });
        if (request.Plaintext is null || request.Plaintext.Length == 0)
            return TypedResults.BadRequest(new { error = "Plaintext is required" });

        try
        {
            var result = await service.EncryptAsync(address, request.Plaintext, ct);
            return TypedResults.Ok(new PersonaEncryptResponse(
                Ciphertext: result.Ciphertext,
                Nonce: result.Nonce,
                WrappedKeyRef: result.WrappedKeyRef));
        }
        catch (KeyNotFoundException)
        {
            return TypedResults.NotFound(new { error = "Wallet not found" });
        }
        catch (CryptographicException)
        {
            // Sanitised response — never echo the exception message to the
            // caller, it may contain sensitive key material references.
            return TypedResults.Problem(
                title: "Persona encryption failed",
                detail: "An internal cryptographic error occurred.",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<IResult> DecryptPersona(
        string address,
        PersonaDecryptRequest request,
        IPersonaCryptoService service,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(address))
            return TypedResults.BadRequest(new { error = "Wallet address is required" });
        if (request.Ciphertext is null || request.Ciphertext.Length == 0)
            return TypedResults.BadRequest(new { error = "Ciphertext is required" });
        if (request.Nonce is null || request.Nonce.Length == 0)
            return TypedResults.BadRequest(new { error = "Nonce is required" });
        if (request.Nonce.Length != 24)
            return TypedResults.BadRequest(new { error = "Nonce must be exactly 24 bytes (XChaCha20-Poly1305)" });
        if (string.IsNullOrWhiteSpace(request.WrappedKeyRef))
            return TypedResults.BadRequest(new { error = "WrappedKeyRef is required" });

        try
        {
            var plaintext = await service.DecryptAsync(
                address,
                request.Ciphertext,
                request.Nonce,
                request.WrappedKeyRef,
                ct);

            return TypedResults.Ok(new PersonaDecryptResponse(plaintext));
        }
        catch (KeyNotFoundException)
        {
            return TypedResults.NotFound(new { error = "Wallet not found" });
        }
        catch (PersonaKeyRefMismatchException ex)
        {
            // Typed exception for the v1 wrappedKeyRef invariant — this is
            // a caller-side bug, so 400 with the (caller-supplied) detail.
            return TypedResults.Problem(
                title: "Persona decrypt request invalid",
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }
        catch (CryptographicException)
        {
            // Generic cryptographic failure (corrupt ciphertext, auth tag
            // mismatch, etc.) — sanitised 500. Never echo the exception
            // message to avoid leaking ciphertext references or key IDs.
            return TypedResults.Problem(
                title: "Persona decryption failed",
                detail: "An internal cryptographic error occurred.",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}

/// <summary>Request body for persona encrypt.</summary>
public sealed record PersonaEncryptRequest(byte[] Plaintext);

/// <summary>Response body for persona encrypt.</summary>
public sealed record PersonaEncryptResponse(byte[] Ciphertext, byte[] Nonce, string WrappedKeyRef);

/// <summary>Request body for persona decrypt.</summary>
public sealed record PersonaDecryptRequest(byte[] Ciphertext, byte[] Nonce, string WrappedKeyRef);

/// <summary>Response body for persona decrypt.</summary>
public sealed record PersonaDecryptResponse(byte[] Plaintext);
