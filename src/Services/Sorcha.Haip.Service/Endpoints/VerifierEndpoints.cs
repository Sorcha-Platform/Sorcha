// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Mvc;
using Sorcha.Haip.Service.Models;
using Sorcha.Haip.Service.Services;

namespace Sorcha.Haip.Service.Endpoints;

/// <summary>
/// OpenID4VP verifier endpoints — Authorization Request creation, Request Object
/// serving, direct_post callback, and verification result retrieval.
/// </summary>
public static class VerifierEndpoints
{
    /// <summary>
    /// Maps verifier endpoints under /api/v1/verifier.
    /// </summary>
    public static void MapVerifierEndpoints(this WebApplication app)
    {
        // Internal — Blueprint Service creates presentation requests
        app.MapPost("/api/v1/verifier/requests", CreatePresentationRequest)
            .WithName("CreatePresentationRequest")
            .WithTags("HAIP Verifier")
            .WithSummary("Create a Presentation Request (service-to-service)")
            .WithDescription(
                "Creates an OpenID4VP Presentation Request for an external HAIP wallet. " +
                "Returns the Authorization Request URI for QR code rendering.")
            .Produces<object>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .RequireAuthorization("RequireService");

        // Public — wallet fetches the signed Request Object
        app.MapGet("/api/v1/verifier/requests/{requestId:guid}/request-object", GetRequestObject)
            .WithName("GetRequestObject")
            .WithTags("HAIP Verifier")
            .WithSummary("Get the signed Request Object JWT")
            .WithDescription(
                "Returns the signed JWT Request Object containing the presentation_definition, " +
                "nonce, and response_mode. Wallets fetch this via the request_uri from the QR code.")
            .Produces(StatusCodes.Status503ServiceUnavailable)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status410Gone)
            .AllowAnonymous();

        // Public — wallet submits vp_token via direct_post
        app.MapPost("/api/v1/verifier/requests/{requestId:guid}/direct-post", HandleDirectPost)
            .WithName("HandleDirectPost")
            .WithTags("HAIP Verifier")
            .WithSummary("Submit a VP Token via direct_post")
            .WithDescription(
                "HAIP wallet submits the vp_token and presentation_submission via direct_post. " +
                "The verifier validates the presentation and stores the result.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status410Gone)
            .AllowAnonymous();

        // Internal — Blueprint Service polls for result
        app.MapGet("/api/v1/verifier/requests/{requestId:guid}/result", GetVerificationResult)
            .WithName("GetVerificationResult")
            .WithTags("HAIP Verifier")
            .WithSummary("Get verification result (service-to-service)")
            .Produces<VerificationResult>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization("RequireService");
    }

    private static async Task<IResult> CreatePresentationRequest(
        [FromBody] CreatePresentationRequestBody request,
        PresentationRequestStore store,
        IConfiguration configuration,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.CredentialType))
            return Results.BadRequest(new { error = "CredentialType is required" });

        var issuerUrl = configuration.GetValue<string>("Haip:IssuerUrl")
            ?? "https://sorcha.example/haip";

        // TODO(098): client_id should be the verifier's DID or x509_san_uri, not the base URL
        var presRequest = await store.CreateAsync(
            clientId: issuerUrl,
            credentialType: request.CredentialType,
            requiredClaims: request.RequiredClaims,
            acceptedIssuers: request.AcceptedIssuers,
            baseUrl: issuerUrl,
            ct: ct);

        var requestUri = $"{issuerUrl}/api/v1/verifier/requests/{presRequest.Id}/request-object";
        var authzRequestUri = $"openid4vp://authorize?client_id={Uri.EscapeDataString(issuerUrl)}&request_uri={Uri.EscapeDataString(requestUri)}";

        return Results.Created($"/api/v1/verifier/requests/{presRequest.Id}", new
        {
            requestId = presRequest.Id,
            authorizationRequestUri = authzRequestUri,
            requestUri,
            nonce = presRequest.Nonce,
            expiresAt = presRequest.ExpiresAt,
            state = presRequest.State.ToString()
        });
    }

    private static IResult GetRequestObject(
        Guid requestId)
    {
        // HAIP requires the Request Object to be a signed JWT (application/oauth-authz-req+jwt).
        // Serving unsigned JSON would be silently rejected by compliant wallets.
        // TODO(098): Sign the Request Object with the verifier's key once HaipCredentialMinter lands.
        return Results.Json(
            new { error = "Request Object signing is not yet configured. The verifier cannot serve unsigned request objects." },
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    private static async Task<IResult> HandleDirectPost(
        Guid requestId,
        [FromForm] string? vp_token,
        [FromForm] string? presentation_submission,
        [FromForm] string? state,
        PresentationRequestStore store,
        HaipPresentationVerifier verifier,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("Sorcha.Haip.Service.Endpoints.VerifierEndpoints");

        var request = await store.GetAsync(requestId, ct);
        if (request == null)
            return Results.NotFound(new { error = "Presentation request not found" });

        if (request.ExpiresAt < DateTimeOffset.UtcNow)
            return Results.Json(new { error = "Presentation request has expired" }, statusCode: 410);

        // Validate state parameter against request ID
        if (!string.IsNullOrEmpty(state))
        {
            if (state != requestId.ToString())
                return Results.BadRequest(new { error = "state parameter does not match the request" });
        }
        else
        {
            logger.LogWarning(
                "direct_post for request {RequestId} did not include a state parameter",
                requestId);
        }

        if (string.IsNullOrWhiteSpace(vp_token))
            return Results.BadRequest(new { error = "vp_token is required" });

        // Run the verification pipeline
        var result = await verifier.VerifyAsync(
            vp_token,
            expectedNonce: request.Nonce,
            expectedAudience: request.ClientId,
            requiredCredentialType: request.CredentialType,
            requiredClaims: request.RequiredClaims,
            ct: ct);

        // Store the result
        await store.MarkCompletedAsync(requestId, result, ct);

        if (result.IsValid)
        {
            logger.LogInformation(
                "Presentation verified for request {RequestId}: {ClaimCount} claims",
                requestId, result.VerifiedClaims.Count);
            return Results.Ok(new { redirect_uri = (string?)null });
        }
        else
        {
            logger.LogWarning(
                "Presentation verification failed for request {RequestId}: {Errors}",
                requestId, string.Join("; ", result.Errors));
            return Results.BadRequest(new
            {
                error = "invalid_presentation",
                error_description = string.Join("; ", result.Errors)
            });
        }
    }

    private static async Task<IResult> GetVerificationResult(
        Guid requestId,
        PresentationRequestStore store,
        CancellationToken ct)
    {
        var request = await store.GetAsync(requestId, ct);
        if (request == null)
            return Results.NotFound(new { error = "Presentation request not found" });

        if (request.Result == null)
        {
            return Results.Ok(new
            {
                requestId = request.Id,
                state = request.State.ToString(),
                result = (VerificationResult?)null
            });
        }

        return Results.Ok(new
        {
            requestId = request.Id,
            state = request.State.ToString(),
            result = request.Result
        });
    }
}

/// <summary>Request to create a presentation request.</summary>
public class CreatePresentationRequestBody
{
    public required string CredentialType { get; init; }
    public List<string>? RequiredClaims { get; init; }
    public List<string>? AcceptedIssuers { get; init; }
}
