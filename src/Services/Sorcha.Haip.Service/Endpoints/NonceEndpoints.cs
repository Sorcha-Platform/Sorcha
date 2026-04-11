// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Haip.Service.Services;

namespace Sorcha.Haip.Service.Endpoints;

/// <summary>
/// Nonce endpoint — provides fresh c_nonce values for credential request proof binding.
/// </summary>
public static class NonceEndpoints
{
    /// <summary>
    /// Maps the HAIP nonce endpoint.
    /// </summary>
    public static void MapNonceEndpoints(this WebApplication app)
    {
        app.MapPost("/nonce", GetNonce)
            .WithName("GetNonce")
            .WithTags("HAIP Nonce")
            .WithSummary("Get a fresh c_nonce")
            .WithDescription(
                "Returns a fresh c_nonce for binding into the JWT proof of possession " +
                "in a subsequent credential request.")
            .Produces<object>(StatusCodes.Status200OK)
            .AllowAnonymous();
    }

    private static async Task<IResult> GetNonce(
        NonceStore nonceStore,
        CancellationToken ct)
    {
        var (nonce, expiresIn) = await nonceStore.CreateAsync(ct);

        return Results.Ok(new
        {
            c_nonce = nonce,
            c_nonce_expires_in = expiresIn
        });
    }
}
