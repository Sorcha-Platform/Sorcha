// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
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
        var group = app.MapGroup("/api/v1/wallets")
            .WithTags("Persona Crypto (Internal)");

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
            return Results.BadRequest(new { error = "Wallet address is required" });
        if (request.Plaintext is null || request.Plaintext.Length == 0)
            return Results.BadRequest(new { error = "Plaintext is required" });

        try
        {
            var result = await service.EncryptAsync(address, request.Plaintext, ct);
            return Results.Ok(new PersonaEncryptResponse(
                Ciphertext: result.Ciphertext,
                Nonce: result.Nonce,
                WrappedKeyRef: result.WrappedKeyRef));
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound(new { error = $"Wallet not found: {address}" });
        }
        catch (CryptographicException ex)
        {
            return Results.Problem(
                title: "Persona encryption failed",
                detail: ex.Message,
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
            return Results.BadRequest(new { error = "Wallet address is required" });
        if (request.Ciphertext is null || request.Ciphertext.Length == 0)
            return Results.BadRequest(new { error = "Ciphertext is required" });
        if (request.Nonce is null || request.Nonce.Length == 0)
            return Results.BadRequest(new { error = "Nonce is required" });
        if (string.IsNullOrWhiteSpace(request.WrappedKeyRef))
            return Results.BadRequest(new { error = "WrappedKeyRef is required" });

        try
        {
            var plaintext = await service.DecryptAsync(
                address,
                request.Ciphertext,
                request.Nonce,
                request.WrappedKeyRef,
                ct);

            return Results.Ok(new PersonaDecryptResponse(plaintext));
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound(new { error = $"Wallet not found: {address}" });
        }
        catch (CryptographicException ex)
        {
            // Caller-side invariant violations (e.g. wrappedKeyRef does not
            // match the wallet address) are a 400, not a 500. Any other
            // cryptographic failure — corrupt ciphertext, auth tag mismatch
            // — is a server-side 500 with a sanitised body that does not
            // echo the ciphertext or key material.
            var isInvariantViolation = ex.Message.Contains("wrappedKeyRef", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("invariant", StringComparison.OrdinalIgnoreCase);
            return Results.Problem(
                title: isInvariantViolation ? "Persona decrypt request invalid" : "Persona decryption failed",
                detail: ex.Message,
                statusCode: isInvariantViolation
                    ? StatusCodes.Status400BadRequest
                    : StatusCodes.Status500InternalServerError);
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
