// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Sorcha.Wallet.Service.Models;
using Sorcha.Wallet.Service.Services;

namespace Sorcha.Wallet.Service.Endpoints;

/// <summary>
/// REST endpoints for OID4VP presentation request management.
/// </summary>
public static class PresentationEndpoints
{
    public static IEndpointRouteBuilder MapPresentationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/presentations")
            .WithTags("Presentations");

        group.MapPost("/request", CreateRequest)
            .WithName("CreatePresentationRequest")
            .WithSummary("Create a new presentation request")
            .WithDescription("A verifier creates a request for credential presentation. Returns request ID, nonce, and OID4VP URL.")
            .RequireAuthorization("CanManageWallets")
            .Produces<object>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapGet("/{requestId}", GetRequest)
            .WithName("GetPresentationRequest")
            .WithSummary("Get presentation request details")
            .WithDescription("Returns request details with matching credentials for the holder's wallet.")
            .RequireAuthorization("CanManageWallets")
            .Produces<object>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{requestId}/submit", SubmitPresentation)
            .WithName("SubmitPresentation")
            .WithSummary("Submit a credential presentation")
            .WithDescription("Holder submits a VP token with disclosed claims for verification.")
            .RequireAuthorization("CanManageWallets")
            .Produces<object>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{requestId}/deny", DenyRequest)
            .WithName("DenyPresentationRequest")
            .WithSummary("Deny a presentation request")
            .WithDescription("Holder declines to present the requested credential.")
            .RequireAuthorization("CanManageWallets")
            .Produces<object>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/{requestId}/result", GetResult)
            .WithName("GetPresentationResult")
            .WithSummary("Poll for verification result")
            .WithDescription("Verifier polls for the outcome of a presentation request.")
            .RequireAuthorization("CanManageWallets")
            .Produces<object>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> CreateRequest(
        [FromBody] CreatePresentationRequestBody body,
        IPresentationRequestService service,
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.CredentialType))
            return Results.BadRequest(new { error = "credentialType is required" });

        if (string.IsNullOrWhiteSpace(body.CallbackUrl))
            return Results.BadRequest(new { error = "callbackUrl is required" });

        if (!Uri.TryCreate(body.CallbackUrl, UriKind.Absolute, out var callbackUri) ||
            callbackUri.Scheme != "https")
            return Results.BadRequest(new { error = "callbackUrl must be a valid HTTPS URL" });

        var dto = new CreatePresentationRequestDto
        {
            CredentialType = body.CredentialType,
            AcceptedIssuers = body.AcceptedIssuers,
            RequiredClaims = body.RequiredClaims,
            CallbackUrl = body.CallbackUrl,
            TargetWalletAddress = body.TargetWalletAddress,
            TtlSeconds = body.TtlSeconds > 0 ? body.TtlSeconds : 300,
            VerifierIdentity = body.VerifierIdentity ?? "Unknown Verifier"
        };

        var request = await service.CreateRequestAsync(dto, ct);

        var baseUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";
        var requestUrl = $"{baseUrl}/api/v1/presentations/{request.Id}";
        var qrCodeUrl = $"openid4vp://authorize?request_uri={Uri.EscapeDataString(requestUrl)}&nonce={request.Nonce}";

        return Results.Created($"/api/v1/presentations/{request.Id}", new
        {
            requestId = request.Id,
            nonce = request.Nonce,
            requestUrl,
            qrCodeUrl,
            expiresAt = request.ExpiresAt
        });
    }

    private static async Task<IResult> GetRequest(
        string requestId,
        [FromQuery] string? walletAddress,
        IPresentationRequestService service,
        CancellationToken ct)
    {
        var request = await service.GetRequestAsync(requestId, ct);

        if (request == null)
            return Results.NotFound(new { error = "Presentation request not found" });

        if (request.Status == PresentationStatus.Expired)
            return Results.Json(new { error = "Presentation request has expired" }, statusCode: 410);

        var matchingCredentials = Array.Empty<MatchedCredentialInfo>();
        if (!string.IsNullOrWhiteSpace(walletAddress) &&
            request.Status == PresentationStatus.Pending)
        {
            matchingCredentials = (await service.FindMatchingCredentialsAsync(
                request, walletAddress, ct)).ToArray();
        }

        return Results.Ok(new
        {
            requestId = request.Id,
            verifierIdentity = request.VerifierIdentity,
            credentialType = request.CredentialType,
            acceptedIssuers = request.AcceptedIssuers,
            requiredClaims = request.RequiredClaims,
            nonce = request.Nonce,
            status = request.Status,
            expiresAt = request.ExpiresAt,
            matchingCredentials
        });
    }

    private static async Task<IResult> SubmitPresentation(
        string requestId,
        [FromBody] SubmitPresentationBody body,
        IPresentationRequestService service,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.CredentialId))
            return Results.BadRequest(new { error = "credentialId is required" });

        if (string.IsNullOrWhiteSpace(body.VpToken))
            return Results.BadRequest(new { error = "vpToken is required" });

        try
        {
            var request = await service.SubmitPresentationAsync(
                requestId, body.CredentialId, body.DisclosedClaims ?? [], body.VpToken, ct);

            var verification = !string.IsNullOrEmpty(request.VerificationResult)
                ? JsonSerializer.Deserialize<VerificationResult>(request.VerificationResult)
                : null;

            return Results.Ok(new
            {
                requestId = request.Id,
                status = request.Status,
                verificationResult = verification
            });
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound(new { error = "Presentation request not found" });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("expired"))
        {
            return Results.Json(new { error = "Presentation request has expired" }, statusCode: 410);
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> DenyRequest(
        string requestId,
        IPresentationRequestService service,
        CancellationToken ct)
    {
        var request = await service.DenyRequestAsync(requestId, ct);

        if (request == null)
            return Results.NotFound(new { error = "Presentation request not found" });

        return Results.Ok(new
        {
            requestId = request.Id,
            status = request.Status
        });
    }

    private static async Task<IResult> GetResult(
        string requestId,
        IPresentationRequestService service,
        CancellationToken ct)
    {
        var request = await service.GetRequestAsync(requestId, ct);

        if (request == null)
            return Results.NotFound(new { error = "Presentation request not found" });

        if (request.Status == PresentationStatus.Expired)
            return Results.Json(new { error = "Presentation request has expired" }, statusCode: 410);

        if (request.Status == PresentationStatus.Pending)
            return Results.Json(new { requestId = request.Id, status = request.Status }, statusCode: 202);

        var verification = !string.IsNullOrEmpty(request.VerificationResult)
            ? JsonSerializer.Deserialize<VerificationResult>(request.VerificationResult)
            : null;

        return Results.Ok(new
        {
            requestId = request.Id,
            status = request.Status,
            verificationResult = verification
        });
    }
}

/// <summary>
/// Request body for creating a presentation request.
/// </summary>
public class CreatePresentationRequestBody
{
    public required string CredentialType { get; init; }
    public string[]? AcceptedIssuers { get; init; }
    public ClaimConstraint[]? RequiredClaims { get; init; }
    public required string CallbackUrl { get; init; }
    public string? TargetWalletAddress { get; init; }
    public int TtlSeconds { get; init; } = 300;
    public string? VerifierIdentity { get; init; }
}

/// <summary>
/// Request body for submitting a presentation.
/// </summary>
public class SubmitPresentationBody
{
    public required string CredentialId { get; init; }
    public string[]? DisclosedClaims { get; init; }
    public required string VpToken { get; init; }
}
