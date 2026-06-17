// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Sorcha.Verifier.Services;
using Sorcha.Verifier.Engine.Models;using Sorcha.Verifier.Engine;


namespace Sorcha.Verifier.Endpoints;

/// <summary>
/// Verifier callback surface — the wallet POSTs its <c>vp_token</c> here after the citizen
/// approves disclosure (Feature 114, T091). Anonymous; integrity comes from the KB-JWT
/// + delegation chain inside the body, not bearer auth.
/// </summary>
public static class PresentationResponseEndpoints
{
    /// <summary>Maps the verifier callback + status endpoints.</summary>
    public static IEndpointRouteBuilder MapPresentationResponseEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/r/{sessionId}/response", HandleResponseAsync)
            .WithName("CitizenVerifierPresentationResponse")
            .WithSummary("Wallet POSTs an OID4VP vp_token + delegation here.")
            .WithDescription(
                "Validates the SD-JWT VC + holder→device delegation chain offline, " +
                "stores the outcome on the verifier session, and returns 204.")
            .Accepts<VerificationCallbackBody>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        routes.MapGet("/r/{sessionId}/status", HandleStatus)
            .WithName("CitizenVerifierPresentationStatus")
            .WithSummary("Read the current outcome of a verifier session.")
            .WithDescription("Used by the verifier UI to poll for completion. Returns 404 if the session is unknown or expired.")
            .Produces<SessionStatusResponse>()
            .Produces(StatusCodes.Status404NotFound);

        return routes;
    }

    private static IResult HandleStatus(string sessionId, IVerifierSessionStore store)
    {
        var session = store.Get(sessionId);
        if (session is null) return Results.NotFound();

        if (session.Outcome is null)
        {
            return Results.Ok(new SessionStatusResponse(
                Status: "pending", Purpose: session.Purpose,
                Accepted: null, Errors: null, DisclosedClaims: null,
                Layers: null, Issuer: null));
        }

        var issuerDid = session.Outcome.Layers
            .FirstOrDefault(l => l.Layer == ValidationLayer.IssuerSignature)?.Detail
            .GetValueOrDefault("iss");

        return Results.Ok(new SessionStatusResponse(
            Status: session.Outcome.Accepted ? "accepted" : "rejected",
            Purpose: session.Purpose,
            Accepted: session.Outcome.Accepted,
            Errors: session.Outcome.Errors,
            DisclosedClaims: session.Outcome.DisclosedClaims
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value?.ToString()),
            Layers: session.Outcome.Layers,
            Issuer: issuerDid is null ? null : new IssuerInfo(issuerDid, issuerDid)));
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

/// <summary>Status payload returned by <c>GET /verify/r/{sessionId}/status</c>.</summary>
/// <param name="Status">One of <c>pending</c>, <c>accepted</c>, <c>rejected</c>.</param>
/// <param name="Purpose">Verifier-supplied human-readable purpose.</param>
/// <param name="Accepted">Null while pending; true/false once decided.</param>
/// <param name="Errors">Rejection reasons; null on accept or pending.</param>
/// <param name="DisclosedClaims">Disclosed claim values, stringified for transport.</param>
/// <param name="Layers">Per-layer validation results for the trail (Feature 155); null while pending.</param>
/// <param name="Issuer">Resolved issuer identity surfaced on the verdict (Feature 155).</param>
public sealed record SessionStatusResponse(
    string Status,
    string Purpose,
    bool? Accepted,
    IReadOnlyList<string>? Errors,
    IReadOnlyDictionary<string, string?>? DisclosedClaims,
    IReadOnlyList<ValidationLayerResult>? Layers,
    IssuerInfo? Issuer);

/// <summary>Issuer identity surfaced on the verdict (Feature 155, FR-015).</summary>
/// <param name="DisplayName">Best-effort display name (falls back to the DID).</param>
/// <param name="Did">The credential issuer DID (<c>iss</c>).</param>
public sealed record IssuerInfo(string DisplayName, string Did);
