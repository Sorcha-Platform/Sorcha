// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Sorcha.Blueprint.Models.Credentials;
using Sorcha.Blueprint.Service.Services;
using Sorcha.ServiceClients.Wallet;

namespace Sorcha.Blueprint.Service.Endpoints;

/// <summary>
/// REST endpoints for credential lifecycle operations in the Blueprint Service.
/// </summary>
public static class CredentialEndpoints
{
    /// <summary>
    /// Maps credential management endpoints.
    /// </summary>
    public static void MapCredentialEndpoints(this WebApplication app)
    {
        var credentialGroup = app.MapGroup("/api/v1/credentials")
            .WithTags("Credentials")
            .RequireAuthorization("CanManageBlueprints");

        credentialGroup.MapPost("/{credentialId}/revoke", RevokeCredential)
            .WithName("RevokeCredential")
            .WithSummary("Revoke a previously issued credential")
            .WithDescription(
                "Revokes a credential by updating its status to 'Revoked' in the wallet store. " +
                "Only the original issuer or register governance roles can revoke a credential.")
            .Produces<RevokeCredentialResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        credentialGroup.MapPost("/{credentialId}/suspend", SuspendCredential)
            .WithName("SuspendCredential")
            .WithSummary("Temporarily suspend a credential")
            .WithDescription("Suspends an Active credential (reversible). Only the original issuer or governance roles can suspend.")
            .Produces<object>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        credentialGroup.MapPost("/{credentialId}/reinstate", ReinstateCredential)
            .WithName("ReinstateCredential")
            .WithSummary("Reinstate a suspended credential")
            .WithDescription("Reinstates a Suspended credential to Active. Only the original issuer or governance roles can reinstate.")
            .Produces<object>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        credentialGroup.MapPost("/{credentialId}/refresh", RefreshCredential)
            .WithName("RefreshCredential")
            .WithSummary("Reissue an expired credential with a fresh expiry")
            .WithDescription("Consumes the expired credential and issues a new one with a fresh expiry period.")
            .Produces<object>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> RevokeCredential(
        string credentialId,
        [FromBody] RevokeCredentialRequest request,
        IWalletServiceClient walletClient,
        IStatusListManager statusListManager,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("Sorcha.Blueprint.Service.Endpoints.CredentialEndpoints");

        if (string.IsNullOrWhiteSpace(request.IssuerWallet))
            return Results.BadRequest(new { error = "IssuerWallet is required" });

        var credential = await GetAndVerifyIssuer(credentialId, request.IssuerWallet, walletClient, logger, cancellationToken);
        if (credential.Error != null) return credential.Error;

        // Active or Suspended credentials can be revoked
        if (credential.Value!.Status is not ("Active" or "Suspended"))
            return Results.BadRequest(new { error = "Credential must be in Active or Suspended state to revoke" });

        var updated = await walletClient.UpdateCredentialStatusAsync(
            request.IssuerWallet, credentialId, "Revoked", cancellationToken);

        if (!updated)
        {
            logger.LogInformation("Credential {CredentialId} status update returned false — may already be revoked",
                credentialId);
        }

        await TryUpdateRecipientStatus(credential.Value, request.IssuerWallet, credentialId, "Revoked", walletClient, logger, cancellationToken);

        // Update bitstring status list (set bit = revoked)
        var statusListUpdated = await TryUpdateStatusListBit(
            credential.Value, true, request.Reason ?? "Revoked", statusListManager, logger, cancellationToken);

        var revokedAt = DateTimeOffset.UtcNow;
        logger.LogInformation(
            "Revoked credential {CredentialId} by issuer {Issuer}. Reason: {Reason}",
            credentialId, request.IssuerWallet, request.Reason ?? "(none)");

        return Results.Ok(new RevokeCredentialResponse
        {
            CredentialId = credentialId,
            RevokedBy = request.IssuerWallet,
            RevokedAt = revokedAt,
            Reason = request.Reason,
            Status = "Revoked",
            StatusListUpdated = statusListUpdated
        });
    }

    private static async Task<IResult> SuspendCredential(
        string credentialId,
        [FromBody] LifecycleCredentialRequest request,
        IWalletServiceClient walletClient,
        IStatusListManager statusListManager,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("Sorcha.Blueprint.Service.Endpoints.CredentialEndpoints");

        if (string.IsNullOrWhiteSpace(request.IssuerWallet))
            return Results.BadRequest(new { error = "IssuerWallet is required" });

        var credential = await GetAndVerifyIssuer(credentialId, request.IssuerWallet, walletClient, logger, cancellationToken);
        if (credential.Error != null) return credential.Error;

        if (credential.Value!.Status != "Active")
            return Results.BadRequest(new { error = "Credential must be in Active state to suspend" });

        var updated = await walletClient.UpdateCredentialStatusAsync(
            request.IssuerWallet, credentialId, "Suspended", cancellationToken);

        if (!updated)
            return Results.Problem("Failed to suspend credential");

        await TryUpdateRecipientStatus(credential.Value, request.IssuerWallet, credentialId, "Suspended", walletClient, logger, cancellationToken);

        // Update bitstring status list (set bit = suspended)
        var statusListUpdated = await TryUpdateStatusListBit(
            credential.Value, true, request.Reason ?? "Suspended", statusListManager, logger, cancellationToken);

        logger.LogInformation("Suspended credential {CredentialId} by {Issuer}. Reason: {Reason}",
            credentialId, request.IssuerWallet, request.Reason ?? "(none)");

        return Results.Ok(new
        {
            credentialId,
            status = "Suspended",
            suspendedBy = request.IssuerWallet,
            suspendedAt = DateTimeOffset.UtcNow,
            reason = request.Reason,
            statusListUpdated
        });
    }

    private static async Task<IResult> ReinstateCredential(
        string credentialId,
        [FromBody] LifecycleCredentialRequest request,
        IWalletServiceClient walletClient,
        IStatusListManager statusListManager,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("Sorcha.Blueprint.Service.Endpoints.CredentialEndpoints");

        if (string.IsNullOrWhiteSpace(request.IssuerWallet))
            return Results.BadRequest(new { error = "IssuerWallet is required" });

        var credential = await GetAndVerifyIssuer(credentialId, request.IssuerWallet, walletClient, logger, cancellationToken);
        if (credential.Error != null) return credential.Error;

        if (credential.Value!.Status != "Suspended")
            return Results.BadRequest(new { error = "Credential must be in Suspended state to reinstate" });

        var updated = await walletClient.UpdateCredentialStatusAsync(
            request.IssuerWallet, credentialId, "Active", cancellationToken);

        if (!updated)
            return Results.Problem("Failed to reinstate credential");

        await TryUpdateRecipientStatus(credential.Value, request.IssuerWallet, credentialId, "Active", walletClient, logger, cancellationToken);

        // Clear bitstring status list (clear bit = active again)
        var statusListUpdated = await TryUpdateStatusListBit(
            credential.Value, false, request.Reason ?? "Reinstated", statusListManager, logger, cancellationToken);

        logger.LogInformation("Reinstated credential {CredentialId} by {Issuer}. Reason: {Reason}",
            credentialId, request.IssuerWallet, request.Reason ?? "(none)");

        return Results.Ok(new
        {
            credentialId,
            status = "Active",
            reinstatedBy = request.IssuerWallet,
            reinstatedAt = DateTimeOffset.UtcNow,
            reason = request.Reason,
            statusListUpdated
        });
    }

    private static async Task<IResult> RefreshCredential(
        string credentialId,
        [FromBody] RefreshCredentialRequest request,
        IWalletServiceClient walletClient,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("Sorcha.Blueprint.Service.Endpoints.CredentialEndpoints");

        if (string.IsNullOrWhiteSpace(request.IssuerWallet))
            return Results.BadRequest(new { error = "IssuerWallet is required" });

        var credential = await GetAndVerifyIssuer(credentialId, request.IssuerWallet, walletClient, logger, cancellationToken);
        if (credential.Error != null) return credential.Error;

        if (credential.Value!.Status != "Expired")
            return Results.BadRequest(new { error = "Credential must be in Expired state to refresh" });

        // Consume the old credential
        await walletClient.UpdateCredentialStatusAsync(
            request.IssuerWallet, credentialId, "Consumed", cancellationToken);

        // Issue new credential with fresh expiry
        var newCredential = await walletClient.IssueCredentialAsync(
            request.IssuerWallet,
            credential.Value.Type,
            credential.Value.Claims,
            credential.Value.SubjectDid,
            request.NewExpiryDuration ?? "P365D",
            cancellationToken: cancellationToken);

        logger.LogInformation(
            "Refreshed credential {OldId} → {NewId} by {Issuer}",
            credentialId, newCredential.CredentialId, request.IssuerWallet);

        return Results.Ok(new
        {
            originalCredentialId = credentialId,
            originalStatus = "Consumed",
            newCredential = new
            {
                newCredential.CredentialId,
                newCredential.Type,
                status = "Active",
                newCredential.IssuedAt,
                newCredential.ExpiresAt
            }
        });
    }

    /// <summary>
    /// Gets a credential and verifies the caller is the original issuer.
    /// </summary>
    private static async Task<(CredentialIssuanceResult? Value, IResult? Error)> GetAndVerifyIssuer(
        string credentialId,
        string issuerWallet,
        IWalletServiceClient walletClient,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        CredentialIssuanceResult? credential;
        try
        {
            credential = await walletClient.GetCredentialAsync(issuerWallet, credentialId, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to retrieve credential {CredentialId} from wallet {Wallet}",
                credentialId, issuerWallet);
            return (null, Results.Problem("Failed to verify credential ownership"));
        }

        if (credential == null)
            return (null, Results.NotFound());

        if (!string.Equals(credential.IssuerDid, issuerWallet, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("Operation denied: wallet {Wallet} is not the issuer of credential {CredentialId}",
                issuerWallet, credentialId);
            return (null, Results.Forbid());
        }

        return (credential, null);
    }

    /// <summary>
    /// Attempts to update the bitstring status list for a credential.
    /// Returns true if the bit was updated, false if no status list info is available.
    /// </summary>
    private static async Task<bool> TryUpdateStatusListBit(
        CredentialIssuanceResult credential,
        bool value,
        string reason,
        IStatusListManager statusListManager,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(credential.StatusListUrl) || credential.StatusListIndex == null)
            return false;

        try
        {
            // Extract listId from the StatusListUrl (last segment)
            var listId = credential.StatusListUrl.Split('/').LastOrDefault();
            if (string.IsNullOrWhiteSpace(listId))
                return false;

            await statusListManager.SetBitAsync(listId, credential.StatusListIndex.Value, value, reason, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Failed to update status list bit for credential {CredentialId}",
                credential.CredentialId);
            return false;
        }
    }

    /// <summary>
    /// Attempts to update the credential status in the recipient's wallet.
    /// </summary>
    private static async Task TryUpdateRecipientStatus(
        CredentialIssuanceResult credential,
        string issuerWallet,
        string credentialId,
        string newStatus,
        IWalletServiceClient walletClient,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(credential.SubjectDid, issuerWallet, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                await walletClient.UpdateCredentialStatusAsync(
                    credential.SubjectDid, credentialId, newStatus, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Failed to update credential {CredentialId} to {Status} in recipient wallet {Recipient}",
                    credentialId, newStatus, credential.SubjectDid);
            }
        }
    }
}

/// <summary>
/// Request for suspend, reinstate, or revoke operations.
/// </summary>
public class LifecycleCredentialRequest
{
    /// <summary>
    /// Wallet address of the issuing authority.
    /// </summary>
    public required string IssuerWallet { get; init; }

    /// <summary>
    /// Human-readable reason for the operation.
    /// </summary>
    public string? Reason { get; init; }
}

/// <summary>
/// Request to refresh/reissue an expired credential.
/// </summary>
public class RefreshCredentialRequest
{
    /// <summary>
    /// Wallet address of the issuing authority.
    /// </summary>
    public required string IssuerWallet { get; init; }

    /// <summary>
    /// ISO 8601 duration for the new expiry (e.g., "P365D"). Default: P365D.
    /// </summary>
    public string? NewExpiryDuration { get; init; }
}

/// <summary>
/// Request to revoke a credential.
/// </summary>
public class RevokeCredentialRequest
{
    /// <summary>
    /// Wallet address of the issuing authority requesting revocation.
    /// </summary>
    public required string IssuerWallet { get; init; }

    /// <summary>
    /// Human-readable reason for revocation.
    /// </summary>
    public string? Reason { get; init; }
}

/// <summary>
/// Response after successful credential revocation.
/// </summary>
public class RevokeCredentialResponse
{
    public required string CredentialId { get; init; }
    public required string RevokedBy { get; init; }
    public required DateTimeOffset RevokedAt { get; init; }
    public string? Reason { get; init; }
    public required string Status { get; init; }
    public bool StatusListUpdated { get; init; }
}
