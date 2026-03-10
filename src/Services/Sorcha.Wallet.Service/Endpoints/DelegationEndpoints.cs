// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Mvc;
using Sorcha.Wallet.Service.Mappers;
using Sorcha.Wallet.Service.Models;
using Sorcha.Wallet.Core.Domain;
using Sorcha.Wallet.Core.Exceptions;
using Sorcha.Wallet.Core.Services.Implementation;
using System.Security.Claims;

namespace Sorcha.Wallet.Service.Endpoints;

/// <summary>
/// Wallet delegation and access control minimal API endpoints
/// </summary>
public static class DelegationEndpoints
{
    /// <summary>
    /// Map all delegation-related endpoints
    /// </summary>
    public static IEndpointRouteBuilder MapDelegationEndpoints(this IEndpointRouteBuilder app)
    {
        var delegationGroup = app.MapGroup("/api/v1/wallets/{walletAddress}/access")
            .WithTags("Delegation")
            .RequireAuthorization("CanManageWallets");

        // POST /api/v1/wallets/{walletAddress}/access - Grant access
        delegationGroup.MapPost("/", GrantAccess)
            .WithName("GrantAccess")
            .WithSummary("Grant access to a wallet")
            .WithDescription("Grant read or write access to a wallet for a specific subject (user or service)")
            .Produces<WalletAccessDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        // GET /api/v1/wallets/{walletAddress}/access - List access grants
        delegationGroup.MapGet("/", GetAccess)
            .WithName("GetAccess")
            .WithSummary("List active access grants")
            .WithDescription("Retrieve all active access grants for a specific wallet")
            .Produces<IEnumerable<WalletAccessDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        // DELETE /api/v1/wallets/{walletAddress}/access/{subject} - Revoke access
        delegationGroup.MapDelete("/{subject}", RevokeAccess)
            .WithName("RevokeAccess")
            .WithSummary("Revoke access to a wallet")
            .WithDescription("Revoke a subject's access to a wallet")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        // GET /api/v1/wallets/{walletAddress}/access/{subject}/check - Check access
        delegationGroup.MapGet("/{subject}/check", CheckAccess)
            .WithName("CheckAccess")
            .WithSummary("Check if subject has access")
            .WithDescription("Verify whether a subject has the required access level to a wallet")
            .Produces<AccessCheckResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }

    /// <summary>
    /// Grant access to a wallet
    /// </summary>
    private static async Task<IResult> GrantAccess(
        string walletAddress,
        [FromBody] GrantAccessRequest request,
        DelegationService delegationService,
        HttpContext context,
        ILogger<Program> logger,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var grantedBy = GetCurrentUser(context);
            if (grantedBy is null)
                return Results.Unauthorized();

            if (!Enum.TryParse<AccessRight>(request.AccessRight, out var accessRight))
            {
                return Results.BadRequest(new ProblemDetails
                {
                    Title = "Invalid Access Right",
                    Detail = $"Invalid access right: {request.AccessRight}. Valid values are: Owner, ReadWrite, ReadOnly",
                    Status = StatusCodes.Status400BadRequest
                });
            }

            logger.LogInformation(
                "Granting {AccessRight} access on wallet {WalletAddress} to {Subject}",
                accessRight, walletAddress, request.Subject);

            var access = await delegationService.GrantAccessAsync(
                walletAddress,
                request.Subject,
                accessRight,
                grantedBy,
                request.Reason,
                request.ExpiresAt,
                cancellationToken);

            return Results.Created(
                $"/api/v1/wallets/{walletAddress}/access",
                access.ToDto());
        }
        catch (ArgumentException ex)
        {
            logger.LogWarning(ex, "Invalid access grant request");
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid Request",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }
        catch (WalletNotFoundException)
        {
            return Results.NotFound();
        }
        catch (WalletAccessAlreadyExistsException ex)
        {
            return Results.Conflict(new ProblemDetails
            {
                Title = "Access Already Exists",
                Detail = ex.Message,
                Status = StatusCodes.Status409Conflict
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to grant access on wallet {WalletAddress}", walletAddress);
            return Results.Problem(
                title: "Access Grant Failed",
                detail: "An error occurred while granting access",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// List active access grants for a wallet
    /// </summary>
    private static async Task<IResult> GetAccess(
        string walletAddress,
        DelegationService delegationService,
        ILogger<Program> logger,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var access = await delegationService.GetActiveAccessAsync(walletAddress, cancellationToken);
            return Results.Ok(access.Select(a => a.ToDto()));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get access for wallet {WalletAddress}", walletAddress);
            return Results.Problem(
                title: "Failed to Retrieve Access",
                detail: "An error occurred while retrieving access grants",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Revoke access to a wallet
    /// </summary>
    private static async Task<IResult> RevokeAccess(
        string walletAddress,
        string subject,
        DelegationService delegationService,
        HttpContext context,
        ILogger<Program> logger,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var revokedBy = GetCurrentUser(context);
            if (revokedBy is null)
                return Results.Unauthorized();

            logger.LogInformation(
                "Revoking access for {Subject} on wallet {WalletAddress}",
                subject, walletAddress);

            await delegationService.RevokeAccessAsync(
                walletAddress,
                subject,
                revokedBy,
                cancellationToken);

            return Results.NoContent();
        }
        catch (WalletNotFoundException)
        {
            return Results.NotFound();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to revoke access for {Subject} on wallet {WalletAddress}",
                subject, walletAddress);
            return Results.Problem(
                title: "Access Revocation Failed",
                detail: "An error occurred while revoking access",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Check if a subject has access to a wallet
    /// </summary>
    private static async Task<IResult> CheckAccess(
        string walletAddress,
        string subject,
        DelegationService delegationService,
        ILogger<Program> logger,
        [FromQuery] string requiredRight = "ReadOnly",
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!Enum.TryParse<AccessRight>(requiredRight, out var accessRight))
            {
                return Results.BadRequest(new ProblemDetails
                {
                    Title = "Invalid Access Right",
                    Detail = $"Invalid access right: {requiredRight}. Valid values are: Owner, ReadWrite, ReadOnly",
                    Status = StatusCodes.Status400BadRequest
                });
            }

            var hasAccess = await delegationService.HasAccessAsync(
                walletAddress,
                subject,
                accessRight,
                cancellationToken);

            return Results.Ok(new AccessCheckResponse
            {
                WalletAddress = walletAddress,
                Subject = subject,
                RequiredRight = requiredRight,
                HasAccess = hasAccess
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to check access for {Subject} on wallet {WalletAddress}",
                subject, walletAddress);
            return Results.Problem(
                title: "Access Check Failed",
                detail: "An error occurred while checking access",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    // Helper methods for authentication/authorization
    private static string? GetCurrentUser(HttpContext context)
    {
        return context.User.FindFirstValue(ClaimTypes.NameIdentifier);
    }
}

/// <summary>
/// Response model for access check
/// </summary>
public class AccessCheckResponse
{
    /// <summary>
    /// Wallet address
    /// </summary>
    public required string WalletAddress { get; set; }

    /// <summary>
    /// Subject identifier
    /// </summary>
    public required string Subject { get; set; }

    /// <summary>
    /// Required access right
    /// </summary>
    public required string RequiredRight { get; set; }

    /// <summary>
    /// Whether subject has the required access
    /// </summary>
    public bool HasAccess { get; set; }
}
