// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Sorcha.Tenant.Models;
using Sorcha.Tenant.Service.Models.Dtos;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Endpoints;

/// <summary>
/// Participant identity management API endpoints.
/// </summary>
public static class ParticipantEndpoints
{
    /// <summary>
    /// Maps participant management endpoints to the application.
    /// </summary>
    public static IEndpointRouteBuilder MapParticipantEndpoints(this IEndpointRouteBuilder app)
    {
        var orgGroup = app.MapGroup("/api/organizations/{organizationId:guid}/participants")
            .WithTags("Participants")
            .RequireAuthorization();

        // Participant CRUD within organization
        orgGroup.MapPost("/", CreateParticipant)
            .WithName("CreateParticipant")
            .WithSummary("Register a user as a participant")
            .WithDescription("Registers a user as a participant in the organization. Requires administrator role.")
            .RequireAuthorization("RequireAdministrator")
            .Produces<ParticipantDetailResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status409Conflict);

        orgGroup.MapGet("/", ListParticipants)
            .WithName("ListParticipants")
            .WithSummary("List participants in organization")
            .WithDescription("Lists all participants in the organization with pagination.")
            .RequireAuthorization("RequireOrganizationMember")
            .Produces<ParticipantListResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        orgGroup.MapGet("/{id:guid}", GetParticipant)
            .WithName("GetParticipant")
            .WithSummary("Get participant details")
            .WithDescription("Gets details of a specific participant including linked wallets.")
            .RequireAuthorization("RequireOrganizationMember")
            .Produces<ParticipantDetailResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        orgGroup.MapPut("/{id:guid}", UpdateParticipant)
            .WithName("UpdateParticipant")
            .WithSummary("Update participant")
            .WithDescription("Updates a participant's information. Requires administrator role.")
            .RequireAuthorization("RequireAdministrator")
            .Produces<ParticipantDetailResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        orgGroup.MapDelete("/{id:guid}", DeactivateParticipant)
            .WithName("DeactivateParticipant")
            .WithSummary("Deactivate participant")
            .WithDescription("Soft deletes a participant. Audit trail preserved. Requires administrator role.")
            .RequireAuthorization("RequireAdministrator")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        orgGroup.MapPost("/{id:guid}/suspend", SuspendParticipant)
            .WithName("SuspendParticipant")
            .WithSummary("Suspend participant")
            .WithDescription("Temporarily suspends a participant. Requires administrator role.")
            .RequireAuthorization("RequireAdministrator")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        orgGroup.MapPost("/{id:guid}/reactivate", ReactivateParticipant)
            .WithName("ReactivateParticipant")
            .WithSummary("Reactivate participant")
            .WithDescription("Reactivates a suspended or inactive participant. Requires administrator role.")
            .RequireAuthorization("RequireAdministrator")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        // Participant publishing (on-register identity records)
        orgGroup.MapPost("/publish", PublishParticipantRecord)
            .WithName("PublishParticipantRecord")
            .WithSummary("Publish a participant record to a register")
            .WithDescription("Builds a Participant transaction, signs with the specified wallet, and submits via the validator pipeline.")
            .RequireAuthorization("RequireAdministrator")
            .Produces<ParticipantPublishResult>(StatusCodes.Status202Accepted)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status409Conflict);

        orgGroup.MapPut("/publish/{participantId}", UpdateParticipantRecord)
            .WithName("UpdateParticipantRecord")
            .WithSummary("Update a published participant record")
            .WithDescription("Publishes a new version of the participant record with updated fields. Version is auto-incremented.")
            .RequireAuthorization("RequireAdministrator")
            .Produces<ParticipantPublishResult>(StatusCodes.Status202Accepted)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        orgGroup.MapDelete("/publish/{participantId}", RevokeParticipantRecord)
            .WithName("RevokeParticipantRecord")
            .WithSummary("Revoke a published participant record")
            .WithDescription("Publishes a new version with status 'Revoked'. The participant is excluded from default queries.")
            .RequireAuthorization("RequireAdministrator")
            .Produces<ParticipantPublishResult>(StatusCodes.Status202Accepted)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        // Wallet link endpoints
        orgGroup.MapPost("/{id:guid}/wallet-links", InitiateWalletLink)
            .WithName("InitiateWalletLink")
            .WithSummary("Initiate wallet link")
            .WithDescription("Initiates a wallet link challenge. Returns a message that must be signed.")
            .RequireAuthorization("RequireOrganizationMember")
            .Produces<WalletLinkChallengeResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status409Conflict);

        orgGroup.MapPost("/{id:guid}/wallet-links/{challengeId:guid}/verify", VerifyWalletLink)
            .WithName("VerifyWalletLink")
            .WithSummary("Verify wallet link")
            .WithDescription("Verifies a wallet link challenge with a signature.")
            .RequireAuthorization("RequireOrganizationMember")
            .Produces<LinkedWalletAddressResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status409Conflict);

        orgGroup.MapGet("/{id:guid}/wallet-links", ListWalletLinks)
            .WithName("ListWalletLinks")
            .WithSummary("List wallet links")
            .WithDescription("Lists all linked wallet addresses for a participant.")
            .RequireAuthorization("RequireOrganizationMember")
            .Produces<List<LinkedWalletAddressResponse>>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

        orgGroup.MapDelete("/{id:guid}/wallet-links/{linkId:guid}", RevokeWalletLink)
            .WithName("RevokeWalletLink")
            .WithSummary("Revoke wallet link")
            .WithDescription("Revokes a linked wallet address (soft delete). Requires administrator or self.")
            .RequireAuthorization("RequireOrganizationMember")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        // Global participant endpoints
        var participantGroup = app.MapGroup("/api/participants")
            .WithTags("Participants")
            .RequireAuthorization();

        participantGroup.MapPost("/search", SearchParticipants)
            .WithName("SearchParticipants")
            .WithSummary("Search participants")
            .WithDescription("Searches participants across accessible organizations.")
            .Produces<ParticipantSearchResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized);

        participantGroup.MapGet("/by-wallet/{address}", GetParticipantByWallet)
            .WithName("GetParticipantByWallet")
            .WithSummary("Get participant by wallet address")
            .WithDescription("Finds a participant by their linked wallet address.")
            .Produces<ParticipantResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

        // User profile endpoints
        var meGroup = app.MapGroup("/api/me")
            .WithTags("User Profile")
            .RequireAuthorization();

        meGroup.MapGet("/participant-profiles", GetMyParticipantProfiles)
            .WithName("GetMyParticipantProfiles")
            .WithSummary("Get my participant profiles")
            .WithDescription("Gets all participant profiles for the current user across organizations.")
            .Produces<List<ParticipantDetailResponse>>()
            .Produces(StatusCodes.Status401Unauthorized);

        meGroup.MapPost("/organizations/{organizationId:guid}/self-register", SelfRegister)
            .WithName("SelfRegisterAsParticipant")
            .WithSummary("Self-register as participant")
            .WithDescription("Registers the current user as a participant in the organization.")
            .Produces<ParticipantDetailResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status409Conflict);

        // Service-internal endpoints (used by Blueprint Service for wallet ownership validation)
        var serviceGroup = app.MapGroup("/api/organizations/{organizationId:guid}/participants")
            .WithTags("Participants (Service)")
            .RequireAuthorization();

        serviceGroup.MapGet("/by-user/{userId:guid}", GetParticipantByUser)
            .WithName("GetParticipantByUser")
            .WithSummary("Get participant by user ID (service-to-service)")
            .WithDescription("Looks up a participant by user ID and organization. Used for wallet ownership validation.")
            .Produces<ParticipantDetailResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

        var serviceParticipantGroup = app.MapGroup("/api/participants")
            .WithTags("Participants (Service)");

        serviceParticipantGroup.MapGet("/{participantId:guid}/wallet-links", GetParticipantWalletLinks)
            .WithName("GetParticipantWalletLinks")
            .WithSummary("Get wallet links by participant ID (service-to-service)")
            .WithDescription("Gets linked wallet addresses for a participant. Used for wallet ownership validation.")
            .RequireAuthorization()
            .Produces<List<LinkedWalletAddressResponse>>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }


    private static async Task<Results<Created<ParticipantDetailResponse>, ValidationProblem, Conflict<string>>> CreateParticipant(
        Guid organizationId,
        CreateParticipantRequest request,
        IParticipantService participantService,
        ClaimsPrincipal user,
        HttpContext httpContext)
    {
        var actorId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";
        var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();

        try
        {
            var result = await participantService.RegisterAsync(organizationId, request, actorId, ipAddress);
            return TypedResults.Created($"/api/organizations/{organizationId}/participants/{result.Id}", result);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already registered"))
        {
            return TypedResults.Conflict(ex.Message);
        }
    }

    private static async Task<Ok<ParticipantListResponse>> ListParticipants(
        Guid organizationId,
        IParticipantService participantService,
        int page = 1,
        int pageSize = 20,
        ParticipantIdentityStatus? status = null)
    {
        var result = await participantService.ListAsync(organizationId, page, pageSize, status);
        return TypedResults.Ok(result);
    }

    private static async Task<Results<Ok<ParticipantDetailResponse>, NotFound>> GetParticipant(
        Guid organizationId,
        Guid id,
        IParticipantService participantService)
    {
        var result = await participantService.GetByIdAsync(id);
        if (result == null || result.OrganizationId != organizationId)
        {
            return TypedResults.NotFound();
        }
        return TypedResults.Ok(result);
    }

    private static async Task<Results<Ok<ParticipantDetailResponse>, NotFound, ValidationProblem>> UpdateParticipant(
        Guid organizationId,
        Guid id,
        UpdateParticipantRequest request,
        IParticipantService participantService,
        ClaimsPrincipal user,
        HttpContext httpContext)
    {
        var actorId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";
        var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();

        var result = await participantService.UpdateAsync(id, request, actorId, ipAddress);
        if (result == null || result.OrganizationId != organizationId)
        {
            return TypedResults.NotFound();
        }
        return TypedResults.Ok(result);
    }

    private static async Task<Results<NoContent, NotFound>> DeactivateParticipant(
        Guid organizationId,
        Guid id,
        IParticipantService participantService,
        ClaimsPrincipal user,
        HttpContext httpContext)
    {
        var actorId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";
        var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();

        // Verify participant belongs to org first
        var participant = await participantService.GetByIdAsync(id);
        if (participant == null || participant.OrganizationId != organizationId)
        {
            return TypedResults.NotFound();
        }

        var success = await participantService.DeactivateAsync(id, actorId, ipAddress);
        return success ? TypedResults.NoContent() : TypedResults.NotFound();
    }

    private static async Task<Results<NoContent, NotFound>> SuspendParticipant(
        Guid organizationId,
        Guid id,
        IParticipantService participantService,
        ClaimsPrincipal user,
        HttpContext httpContext)
    {
        var actorId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";
        var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();

        // Verify participant belongs to org first
        var participant = await participantService.GetByIdAsync(id);
        if (participant == null || participant.OrganizationId != organizationId)
        {
            return TypedResults.NotFound();
        }

        var success = await participantService.SuspendAsync(id, actorId, ipAddress);
        return success ? TypedResults.NoContent() : TypedResults.NotFound();
    }

    private static async Task<Results<NoContent, NotFound>> ReactivateParticipant(
        Guid organizationId,
        Guid id,
        IParticipantService participantService,
        ClaimsPrincipal user,
        HttpContext httpContext)
    {
        var actorId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";
        var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();

        // Verify participant belongs to org first
        var participant = await participantService.GetByIdAsync(id);
        if (participant == null || participant.OrganizationId != organizationId)
        {
            return TypedResults.NotFound();
        }

        var success = await participantService.ReactivateAsync(id, actorId, ipAddress);
        return success ? TypedResults.NoContent() : TypedResults.NotFound();
    }



    private static async Task<Results<Accepted<ParticipantPublishResult>, ValidationProblem, Conflict<string>>> PublishParticipantRecord(
        Guid organizationId,
        PublishParticipantRequest request,
        IParticipantPublishingService publishingService)
    {
        // Validate request
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.RegisterId))
            errors["registerId"] = ["Register ID is required"];

        if (string.IsNullOrWhiteSpace(request.ParticipantName))
            errors["participantName"] = ["Participant name is required"];
        else if (request.ParticipantName.Length > 200)
            errors["participantName"] = ["Participant name must be 200 characters or fewer"];

        if (string.IsNullOrWhiteSpace(request.OrganizationName))
            errors["organizationName"] = ["Organization name is required"];
        else if (request.OrganizationName.Length > 200)
            errors["organizationName"] = ["Organization name must be 200 characters or fewer"];

        if (request.Addresses == null || request.Addresses.Count == 0)
            errors["addresses"] = ["At least one address is required"];
        else if (request.Addresses.Count > 10)
            errors["addresses"] = ["Maximum 10 addresses allowed"];
        else
        {
            for (int i = 0; i < request.Addresses.Count; i++)
            {
                var addr = request.Addresses[i];
                if (string.IsNullOrWhiteSpace(addr.WalletAddress))
                    errors[$"addresses[{i}].walletAddress"] = ["Wallet address is required"];
                if (string.IsNullOrWhiteSpace(addr.PublicKey))
                    errors[$"addresses[{i}].publicKey"] = ["Public key is required"];
                if (string.IsNullOrWhiteSpace(addr.Algorithm))
                    errors[$"addresses[{i}].algorithm"] = ["Algorithm is required"];
            }
        }

        if (string.IsNullOrWhiteSpace(request.SignerWalletAddress))
            errors["signerWalletAddress"] = ["Signer wallet address is required"];

        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors);

        try
        {
            var result = await publishingService.PublishParticipantAsync(request);
            return TypedResults.Accepted($"/api/organizations/{organizationId}/participants/publish", result);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already claimed"))
        {
            return TypedResults.Conflict(ex.Message);
        }
    }

    private static async Task<Results<Accepted<ParticipantPublishResult>, ValidationProblem, NotFound<string>>> UpdateParticipantRecord(
        Guid organizationId,
        string participantId,
        UpdatePublishedParticipantRequest request,
        IParticipantPublishingService publishingService)
    {
        // Validate request
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.RegisterId))
            errors["registerId"] = ["Register ID is required"];

        if (string.IsNullOrWhiteSpace(request.ParticipantName))
            errors["participantName"] = ["Participant name is required"];
        else if (request.ParticipantName.Length > 200)
            errors["participantName"] = ["Participant name must be 200 characters or fewer"];

        if (string.IsNullOrWhiteSpace(request.OrganizationName))
            errors["organizationName"] = ["Organization name is required"];
        else if (request.OrganizationName.Length > 200)
            errors["organizationName"] = ["Organization name must be 200 characters or fewer"];

        if (request.Addresses == null || request.Addresses.Count == 0)
            errors["addresses"] = ["At least one address is required"];
        else if (request.Addresses.Count > 10)
            errors["addresses"] = ["Maximum 10 addresses allowed"];

        if (string.IsNullOrWhiteSpace(request.SignerWalletAddress))
            errors["signerWalletAddress"] = ["Signer wallet address is required"];

        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors);

        try
        {
            var result = await publishingService.UpdateParticipantAsync(request);
            return TypedResults.Accepted(
                $"/api/organizations/{organizationId}/participants/publish/{participantId}", result);
        }
        catch (KeyNotFoundException ex)
        {
            return TypedResults.NotFound(ex.Message);
        }
    }

    private static async Task<Results<Accepted<ParticipantPublishResult>, ValidationProblem, NotFound<string>>> RevokeParticipantRecord(
        Guid organizationId,
        string participantId,
        IParticipantPublishingService publishingService,
        string registerId,
        string signerWalletAddress)
    {
        // Validate required query params
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(registerId))
            errors["registerId"] = ["Register ID is required"];
        if (string.IsNullOrWhiteSpace(signerWalletAddress))
            errors["signerWalletAddress"] = ["Signer wallet address is required"];

        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors);

        try
        {
            var result = await publishingService.RevokeParticipantAsync(new RevokeParticipantRequest
            {
                RegisterId = registerId,
                ParticipantId = participantId,
                SignerWalletAddress = signerWalletAddress
            });
            return TypedResults.Accepted(
                $"/api/organizations/{organizationId}/participants/publish/{participantId}", result);
        }
        catch (KeyNotFoundException ex)
        {
            return TypedResults.NotFound(ex.Message);
        }
    }



    private static async Task<Results<Created<WalletLinkChallengeResponse>, NotFound<string>, Conflict<string>>> InitiateWalletLink(
        Guid organizationId,
        Guid id,
        InitiateWalletLinkRequest request,
        IParticipantService participantService,
        IWalletVerificationService walletVerificationService,
        ClaimsPrincipal user)
    {
        var actorId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";

        // Verify participant belongs to org
        var participant = await participantService.GetByIdAsync(id);
        if (participant == null || participant.OrganizationId != organizationId)
        {
            return TypedResults.NotFound($"Participant {id} not found");
        }

        try
        {
            var result = await walletVerificationService.InitiateLinkAsync(id, request, actorId);
            return TypedResults.Created($"/api/organizations/{organizationId}/participants/{id}/wallet-links/{result.ChallengeId}", result);
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.Conflict(ex.Message);
        }
        catch (KeyNotFoundException ex)
        {
            return TypedResults.NotFound(ex.Message);
        }
    }

    private static async Task<Results<Created<LinkedWalletAddressResponse>, NotFound, BadRequest<string>, Conflict<string>>> VerifyWalletLink(
        Guid organizationId,
        Guid id,
        Guid challengeId,
        VerifyWalletLinkRequest request,
        IParticipantService participantService,
        IWalletVerificationService walletVerificationService,
        ClaimsPrincipal user,
        HttpContext httpContext)
    {
        var actorId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";
        var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();

        // Verify participant belongs to org
        var participant = await participantService.GetByIdAsync(id);
        if (participant == null || participant.OrganizationId != organizationId)
        {
            return TypedResults.NotFound();
        }

        try
        {
            var result = await walletVerificationService.VerifyLinkAsync(id, challengeId, request, actorId, ipAddress);
            return TypedResults.Created($"/api/organizations/{organizationId}/participants/{id}/wallet-links/{result.Id}", result);
        }
        catch (KeyNotFoundException)
        {
            return TypedResults.NotFound();
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("expired") || ex.Message.Contains("failed") || ex.Message.Contains("not pending"))
        {
            return TypedResults.BadRequest(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.Conflict(ex.Message);
        }
    }

    private static async Task<Results<Ok<List<LinkedWalletAddressResponse>>, NotFound>> ListWalletLinks(
        Guid organizationId,
        Guid id,
        IParticipantService participantService,
        IWalletVerificationService walletVerificationService,
        bool includeRevoked = false)
    {
        // Verify participant belongs to org
        var participant = await participantService.GetByIdAsync(id);
        if (participant == null || participant.OrganizationId != organizationId)
        {
            return TypedResults.NotFound();
        }

        var result = await walletVerificationService.ListLinksAsync(id, includeRevoked);
        return TypedResults.Ok(result);
    }

    private static async Task<Results<NoContent, NotFound, ForbidHttpResult>> RevokeWalletLink(
        Guid organizationId,
        Guid id,
        Guid linkId,
        IParticipantService participantService,
        IWalletVerificationService walletVerificationService,
        ClaimsPrincipal user,
        HttpContext httpContext)
    {
        var actorId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";
        var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();

        // Verify participant belongs to org
        var participant = await participantService.GetByIdAsync(id);
        if (participant == null || participant.OrganizationId != organizationId)
        {
            return TypedResults.NotFound();
        }

        // Check authorization: must be admin or the participant themselves
        var isAdmin = user.IsInRole("Administrator");
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        var isOwner = userId != null && Guid.TryParse(userId, out var userGuid) && participant.UserId == userGuid;

        if (!isAdmin && !isOwner)
        {
            return TypedResults.Forbid();
        }

        var success = await walletVerificationService.RevokeLinkAsync(id, linkId, actorId, ipAddress);
        return success ? TypedResults.NoContent() : TypedResults.NotFound();
    }



    private static async Task<Ok<ParticipantSearchResponse>> SearchParticipants(
        ParticipantSearchRequest request,
        IParticipantService participantService,
        ClaimsPrincipal user)
    {
        // Extract accessible organizations from claims
        // In a real implementation, this would come from the user's org memberships
        var orgIdClaim = user.FindFirstValue("org_id");
        var isSystemAdmin = user.IsInRole("SystemAdmin");

        IReadOnlyList<Guid>? accessibleOrgs = null;
        if (!isSystemAdmin && !string.IsNullOrEmpty(orgIdClaim) && Guid.TryParse(orgIdClaim, out var orgId))
        {
            accessibleOrgs = new[] { orgId };
        }

        var result = await participantService.SearchAsync(request, accessibleOrgs, isSystemAdmin);
        return TypedResults.Ok(result);
    }

    private static async Task<Results<Ok<ParticipantResponse>, NotFound>> GetParticipantByWallet(
        string address,
        IParticipantService participantService)
    {
        var result = await participantService.GetByWalletAddressAsync(address);
        return result != null ? TypedResults.Ok(result) : TypedResults.NotFound();
    }



    private static async Task<Ok<List<ParticipantDetailResponse>>> GetMyParticipantProfiles(
        IParticipantService participantService,
        ClaimsPrincipal user)
    {
        var userIdClaim = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            return TypedResults.Ok(new List<ParticipantDetailResponse>());
        }

        var result = await participantService.GetMyProfilesAsync(userId);
        return TypedResults.Ok(result);
    }

    private static async Task<Results<Created<ParticipantDetailResponse>, BadRequest<string>, Conflict<string>>> SelfRegister(
        Guid organizationId,
        IParticipantService participantService,
        ClaimsPrincipal user,
        HttpContext httpContext,
        string? displayName = null)
    {
        var userIdClaim = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            return TypedResults.Conflict("Invalid user identity");
        }

        var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();

        try
        {
            var result = await participantService.SelfRegisterAsync(organizationId, userId, displayName, ipAddress);
            return TypedResults.Created($"/api/organizations/{organizationId}/participants/{result.Id}", result);
        }
        catch (ArgumentException ex)
        {
            return TypedResults.BadRequest(ex.Message);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already registered"))
        {
            return TypedResults.Conflict(ex.Message);
        }
    }



    private static async Task<Results<Ok<ParticipantDetailResponse>, NotFound>> GetParticipantByUser(
        Guid organizationId,
        Guid userId,
        IParticipantService participantService)
    {
        var result = await participantService.GetByUserAndOrgAsync(userId, organizationId);
        return result != null ? TypedResults.Ok(result) : TypedResults.NotFound();
    }

    private static async Task<Results<Ok<List<LinkedWalletAddressResponse>>, NotFound>> GetParticipantWalletLinks(
        Guid participantId,
        IWalletVerificationService walletVerificationService,
        IParticipantService participantService,
        bool includeRevoked = false)
    {
        var participant = await participantService.GetByIdAsync(participantId);
        if (participant == null)
        {
            return TypedResults.NotFound();
        }

        var result = await walletVerificationService.ListLinksAsync(participantId, includeRevoked);
        return TypedResults.Ok(result);
    }

}
