// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
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
            .RequireAuthorization(AuthorizationPolicies.RequireService); // SEC-013

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
            .AllowAnonymous()
            .DisableAntiforgery(); // OID4VP direct_post — wallet-to-verifier, no browser CSRF

        // Internal — Blueprint Service polls for result
        app.MapGet("/api/v1/verifier/requests/{requestId:guid}/result", GetVerificationResult)
            .WithName("GetVerificationResult")
            .WithTags("HAIP Verifier")
            .WithSummary("Get verification result (service-to-service)")
            .Produces<VerificationResult>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization(AuthorizationPolicies.RequireService); // SEC-013
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

    private static async Task<IResult> GetRequestObject(
        Guid requestId,
        PresentationRequestStore store,
        RequestObjectSigner signer,
        CancellationToken ct)
    {
        var request = await store.GetAsync(requestId, ct);
        if (request == null)
            return Results.NotFound(new { error = $"Presentation request '{requestId}' not found" });

        if (request.ExpiresAt < DateTimeOffset.UtcNow)
            return Results.Json(new { error = "Presentation request has expired" }, statusCode: 410);

        // Build the Request Object payload per OpenID4VP.
        // iat is mandatory for signed Request Objects (RFC 9101 §10.8 — CSRF window).
        // iss is the verifier's identifier — same value as client_id for now; spec 096
        // will swap this to the verifier's DID / x509_san_uri.
        var nowSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var requestObjectPayload = new Dictionary<string, object>
        {
            ["iss"] = request.ClientId,
            ["aud"] = "https://self-issued.me/v2",
            ["iat"] = nowSeconds,
            ["exp"] = request.ExpiresAt.ToUnixTimeSeconds(),
            ["response_type"] = "vp_token",
            ["response_mode"] = "direct_post",
            ["response_uri"] = request.ResponseUri,
            ["client_id"] = request.ClientId,
            ["nonce"] = request.Nonce,
            ["state"] = request.Id.ToString(),
            ["presentation_definition"] = new Dictionary<string, object>
            {
                ["id"] = $"pd-{request.Id}",
                ["input_descriptors"] = new[]
                {
                    new Dictionary<string, object>
                    {
                        ["id"] = request.CredentialType,
                        ["format"] = new Dictionary<string, object>
                        {
                            ["vc+sd-jwt"] = new Dictionary<string, object>
                            {
                                ["alg"] = new[] { "ES256" }
                            }
                        },
                        ["constraints"] = new Dictionary<string, object>
                        {
                            ["fields"] = (request.RequiredClaims ?? new List<string>()).Select(c =>
                                new Dictionary<string, object>
                                {
                                    ["path"] = new[] { $"$.{c}" }
                                }).ToArray()
                        }
                    }
                }
            }
        };

        // HAIP 1.0 §6.1 and RFC 9101 §4 require the Request Object to be a signed JWT
        // with typ="oauth-authz-req+jwt". Wallets refuse to act on an unsigned JSON body.
        var requestObjectJwt = signer.Sign(requestObjectPayload);

        // Content type per RFC 9101 §4: application/oauth-authz-req+jwt. Wallets use this
        // to distinguish a signed Request Object from an unsigned JSON request.
        return Results.Text(requestObjectJwt, contentType: "application/oauth-authz-req+jwt");
    }

    private static async Task<IResult> HandleDirectPost(
        Guid requestId,
        [FromForm] string? vp_token,
        [FromForm] string? presentation_submission,
        [FromForm] string? state,
        PresentationRequestStore store,
        HaipPresentationVerifier verifier,
        PresentationCallbackRelay? callbackRelay,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("Sorcha.Haip.Service.Endpoints.VerifierEndpoints");

        var request = await store.GetAsync(requestId, ct);
        if (request == null)
            return Results.NotFound(new { error = "Presentation request not found" });

        if (request.ExpiresAt < DateTimeOffset.UtcNow)
            return Results.Json(new { error = "Presentation request has expired" }, statusCode: 410);

        // Validate state parameter against request ID (OID4VP §6.2 — required for CSRF protection)
        if (string.IsNullOrEmpty(state))
            return Results.BadRequest(new { error = "state parameter is required" });

        if (state != requestId.ToString())
            return Results.BadRequest(new { error = "state parameter does not match the request" });

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

        // Feature 111: relay the outcome to Blueprint Service for lifecycle transaction writing.
        if (callbackRelay is not null)
        {
            await callbackRelay.RelayAsync(requestId, result, ct);
        }

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
