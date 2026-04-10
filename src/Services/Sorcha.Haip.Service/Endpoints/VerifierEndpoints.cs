// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text;
using System.Text.Json;
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
            .RequireAuthorization();

        // Public — wallet fetches the signed Request Object
        app.MapGet("/api/v1/verifier/requests/{requestId:guid}/request-object", GetRequestObject)
            .WithName("GetRequestObject")
            .WithTags("HAIP Verifier")
            .WithSummary("Get the signed Request Object JWT")
            .WithDescription(
                "Returns the signed JWT Request Object containing the presentation_definition, " +
                "nonce, and response_mode. Wallets fetch this via the request_uri from the QR code.")
            .Produces<string>(StatusCodes.Status200OK, "application/json") // TODO(098): application/oauth-authz-req+jwt once signed
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
            .RequireAuthorization();
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
        IConfiguration configuration,
        CancellationToken ct)
    {
        var request = await store.GetAsync(requestId, ct);
        if (request == null)
            return Results.NotFound(new { error = $"Presentation request '{requestId}' not found" });

        if (request.ExpiresAt < DateTimeOffset.UtcNow)
            return Results.Json(new { error = "Presentation request has expired" }, statusCode: 410);

        var issuerUrl = configuration.GetValue<string>("Haip:IssuerUrl")
            ?? "https://sorcha.example/haip";

        // Build the Request Object payload per OpenID4VP
        var requestObject = new Dictionary<string, object>
        {
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

        // For now, return as unsigned JSON (signed JWT Request Object requires
        // signing key wiring — deferred until HaipCredentialMinter lands)
        // TODO(098): Sign the Request Object with the verifier's key
        var json = JsonSerializer.Serialize(requestObject);
        return Results.Text(json, "application/json", statusCode: 200);
    }

    private static async Task<IResult> HandleDirectPost(
        Guid requestId,
        [FromForm] string? vp_token,
        [FromForm] string? presentation_submission,
        [FromForm] string? state,
        PresentationRequestStore store,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("Sorcha.Haip.Service.Endpoints.VerifierEndpoints");

        var request = await store.GetAsync(requestId, ct);
        if (request == null)
            return Results.NotFound(new { error = "Presentation request not found" });

        if (request.ExpiresAt < DateTimeOffset.UtcNow)
            return Results.Json(new { error = "Presentation request has expired" }, statusCode: 410);

        if (string.IsNullOrWhiteSpace(vp_token))
            return Results.BadRequest(new { error = "vp_token is required" });

        // Verification pipeline not yet wired — return 501 until HaipPresentationVerifier
        // connects the full pipeline (x5c chain walk, KB-JWT, status, claim matching).
        logger.LogWarning(
            "direct_post received for request {RequestId} — verification pipeline not yet wired",
            requestId);

        return Results.Json(new
        {
            error = "not_implemented",
            error_description = "Verification pipeline not yet wired. vp_token was received but not processed."
        }, statusCode: 501);
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
