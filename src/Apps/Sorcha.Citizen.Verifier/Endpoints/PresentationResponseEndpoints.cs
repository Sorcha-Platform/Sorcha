// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Sorcha.Citizen.Verifier.Services;
using Sorcha.Citizen.Verifier.Services.Models;

namespace Sorcha.Citizen.Verifier.Endpoints;

/// <summary>
/// Verifier callback surface — the wallet POSTs its <c>vp_token</c> here after the citizen
/// approves disclosure (Feature 114, T091). Anonymous; integrity comes from the KB-JWT
/// + delegation chain inside the body, not bearer auth.
/// </summary>
public static class PresentationResponseEndpoints
{
    /// <summary>Maps the verifier callback endpoint.</summary>
    public static IEndpointRouteBuilder MapPresentationResponseEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/verify/r/{sessionId}/response", HandleResponseAsync)
            .WithName("CitizenVerifierPresentationResponse")
            .WithSummary("Wallet POSTs an OID4VP vp_token + delegation here.")
            .WithDescription(
                "Validates the SD-JWT VC + holder→device delegation chain offline, " +
                "stores the outcome on the verifier session, and returns 204.")
            .Accepts<VerificationCallbackBody>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        return routes;
    }

    private static async Task<IResult> HandleResponseAsync(
        string sessionId,
        VerificationCallbackBody body,
        IVerifierSessionStore store,
        IVerifiablePresentationValidator validator,
        ILogger<PresentationRequestBuilder> logger,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.VpToken))
        {
            return Results.BadRequest(new { error = "vp_token is required" });
        }

        var session = store.Get(sessionId);
        if (session is null)
        {
            logger.LogWarning(
                "Presentation response posted for unknown / expired session {SessionId}", sessionId);
            return Results.NotFound(new { error = "session not found or expired" });
        }
        if (session.Outcome is not null)
        {
            // Idempotent: a second submission against an already-decided session returns the original outcome.
            return Results.NoContent();
        }

        var outcome = await validator.ValidateAsync(session, body.VpToken, body.Delegation, ct);
        store.Update(session with { Outcome = outcome });
        return Results.NoContent();
    }
}

/// <summary>Body shape for the verifier callback.</summary>
/// <param name="VpToken">The SD-JWT VC compact form including disclosures and KB-JWT.</param>
/// <param name="Delegation">The device delegation credential (separate compact JWT).</param>
public sealed record VerificationCallbackBody(string VpToken, string? Delegation);
