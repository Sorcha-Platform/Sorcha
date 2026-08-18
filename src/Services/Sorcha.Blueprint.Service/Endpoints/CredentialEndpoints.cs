// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

#pragma warning disable ASPDEPR002 // WithOpenApi is deprecated; using it for co-located endpoint examples until transformer API stabilizes

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Microsoft.AspNetCore.Mvc;

using Sorcha.Blueprint.Models.Credentials;
using Sorcha.Blueprint.Service.Services;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Enums;
using Sorcha.ServiceClients.Register;
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
            .Produces(StatusCodes.Status404NotFound)
            .WithOpenApi(operation =>
            {
                OpenApiExamples.SetRequestExample(operation, """
                    {
                      "issuerWallet": "sorcha1abc123def456ghi789jkl012mno345",
                      "reason": "Key compromise detected during security audit"
                    }
                    """);
                OpenApiExamples.SetResponseExample(operation, "200", """
                    {
                      "credentialId": "urn:uuid:3fa85f64-5717-4562-b3fc-2c963f66afa6",
                      "revokedBy": "sorcha1abc123def456ghi789jkl012mno345",
                      "revokedAt": "2026-03-15T10:30:00Z",
                      "reason": "Key compromise detected during security audit",
                      "status": "Revoked",
                      "statusListUpdated": true
                    }
                    """);
                return operation;
            });

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
        IRegisterServiceClient registerClient,
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

        // Multi-node audit CRITICAL #2 — propagate the status change to the holder's node
        // via a register transaction. Direct UpdateRecipientStatus HTTP only reaches the
        // holder when issuer + holder share a wallet service; this is the cross-node path.
        await TrySubmitStatusChangeTransactionAsync(
            credential.Value, request.IssuerWallet, "Revoked", request.Reason, registerClient, logger, cancellationToken);

        // Update bitstring status list (set bit = revoked)
        var statusListUpdated = await TryUpdateStatusListBit(
            credential.Value, true, request.Reason ?? "Revoked", "revocation", statusListManager, logger, cancellationToken);

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
        IRegisterServiceClient registerClient,
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

        // Multi-node audit CRITICAL #2 — see RevokeCredential for the same path.
        await TrySubmitStatusChangeTransactionAsync(
            credential.Value, request.IssuerWallet, "Suspended", request.Reason, registerClient, logger, cancellationToken);

        // Update bitstring status list (set bit = suspended)
        var statusListUpdated = await TryUpdateStatusListBit(
            credential.Value, true, request.Reason ?? "Suspended", "suspension", statusListManager, logger, cancellationToken);

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
        IRegisterServiceClient registerClient,
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

        // Multi-node audit CRITICAL #2 — see RevokeCredential for the same path.
        await TrySubmitStatusChangeTransactionAsync(
            credential.Value, request.IssuerWallet, "Active", request.Reason, registerClient, logger, cancellationToken);

        // Clear bitstring status list (clear bit = active again)
        var statusListUpdated = await TryUpdateStatusListBit(
            credential.Value, false, request.Reason ?? "Reinstated", "suspension", statusListManager, logger, cancellationToken);

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
    /// <summary>
    /// Sets or clears this credential's bit in the status list for <paramref name="purpose"/>.
    /// </summary>
    /// <remarks>
    /// W3C Bitstring Status List defines <c>revocation</c> as NOT reversible and <c>suspension</c>
    /// as reversible, so each purpose has its own list and its own bit at the same index. Writing a
    /// suspension into the revocation list advertises the credential to every verifier as revoked,
    /// and lifting that suspension then clears a revocation bit the spec says can never be cleared.
    /// </remarks>
    private static async Task<bool> TryUpdateStatusListBit(
        CredentialIssuanceResult credential,
        bool value,
        string reason,
        string purpose,
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

            // The id ends with its purpose (…-revocation-1). Retarget rather than require a second
            // URL, so a credential that only ever knew its revocation list still resolves the sibling.
            listId = RetargetListIdToPurpose(listId, purpose);

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
    /// Submits a <see cref="TransactionType.CredentialStatusChange"/> register transaction
    /// so the holder's wallet node — which may be on a different node than the issuer —
    /// receives the lifecycle event via the existing InboundTransactionRouter pipeline and
    /// applies the new status to its locally cached credential row. Best-effort: any failure
    /// here is logged but does not break the lifecycle endpoint (the issuer's local row and
    /// the BitstringStatusList are already updated and remain the authoritative artefacts).
    /// </summary>
    /// <remarks>
    /// Multi-node audit CRITICAL #2 fix. The direct <c>TryUpdateRecipientStatus</c> HTTP
    /// call is retained as a same-node fast path — on cross-node deployments it silently
    /// no-ops (points at the issuer's wallet service), and this register-tx path is what
    /// actually reaches the holder.
    /// </remarks>
    /// <summary>
    /// Recovers the status-list identifier from the credential's status-list URL, whose final path
    /// segment IS the list id (<c>{issuerWallet}-{registerId}-{purpose}-1</c>).
    /// </summary>
    private static string? DeriveStatusListId(CredentialIssuanceResult credential)
    {
        var url = credential.StatusListUrl;
        if (string.IsNullOrWhiteSpace(url)) return null;

        var trimmed = url.TrimEnd('/');
        var slash = trimmed.LastIndexOf('/');
        return slash >= 0 && slash < trimmed.Length - 1 ? trimmed[(slash + 1)..] : null;
    }

    /// <summary>
    /// Rewrites a status-list id so it addresses <paramref name="purpose"/>. Ids are
    /// <c>{issuerWallet}-{registerId}-{purpose}-{version}</c> and the two purposes share an index.
    /// </summary>
    internal static string RetargetListIdToPurpose(string listId, string purpose)
    {
        var parts = listId.Split('-');
        if (parts.Length < 2) return listId;

        parts[^2] = purpose;
        return string.Join('-', parts);
    }

    private static async Task TrySubmitStatusChangeTransactionAsync(
        CredentialIssuanceResult credential,
        string issuerWallet,
        string newStatus,
        string? reason,
        IRegisterServiceClient registerClient,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(credential.RegisterId))
        {
            // Pre-Feature 106 credentials don't carry the originating RegisterId — there's
            // nowhere to post the lifecycle tx to. Verifier-side checks still work via the
            // BitstringStatusList; only the holder-side cached row will be stale.
            // WARNING, not Debug. This is a ledger platform declining to write a lifecycle event
            // to the ledger: the revocation becomes node-local, unauditable and unreplicable. It
            // logged at Debug for months, so on any node logging at Information it was completely
            // invisible — which is how #1482 hid. If this fires, the credential predates the
            // RegisterId being persisted on the issuer's row and its revocation will not propagate.
            logger.LogWarning(
                "No CredentialStatusChange register tx for {CredentialId}: the credential record "
                + "carries no RegisterId, so this {Status} change is local to this node and will "
                + "not replicate or appear in the audit trail",
                credential.CredentialId, newStatus);
            return;
        }

        try
        {
            var payload = new CredentialStatusChangePayload
            {
                CredentialId = credential.CredentialId,
                NewStatus = newStatus,
                IssuerWallet = issuerWallet,
                SubjectDid = credential.SubjectDid,
                Reason = reason,
                ChangedAt = DateTimeOffset.UtcNow,
                // Self-describing: a node folding this transaction cannot look the credential up in
                // the issuing node's wallet, so the event must carry the bit it changes (#1482).
                StatusListId = DeriveStatusListId(credential),
                StatusListIndex = credential.StatusListIndex,
            };

            var payloadJson = JsonSerializer.Serialize(payload);
            var payloadBytes = Encoding.UTF8.GetBytes(payloadJson);

            // Tx id = SHA-256(registerId : credentialId : newStatus : unix-ms). Deterministic
            // enough for log correlation while colliding only on duplicate same-millisecond
            // submissions (which would themselves be no-ops on the receiver).
            var txIdSource = $"{credential.RegisterId}:{credential.CredentialId}:{newStatus}:{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
            var txIdHash = SHA256.HashData(Encoding.UTF8.GetBytes(txIdSource));
            var txId = Convert.ToHexString(txIdHash).ToLowerInvariant();

            var tx = new TransactionModel
            {
                TxId = txId,
                RegisterId = credential.RegisterId,
                SenderWallet = issuerWallet,
                RecipientsWallets = new List<string> { credential.SubjectDid },
                TimeStamp = DateTime.UtcNow,
                PrevTxId = string.Empty,
                MetaData = new TransactionMetaData
                {
                    RegisterId = credential.RegisterId,
                    TransactionType = TransactionType.CredentialStatusChange,
                    TrackingData = new Dictionary<string, string>
                    {
                        ["credentialId"] = credential.CredentialId,
                        ["newStatus"] = newStatus,
                    },
                },
                PayloadCount = 1,
                Payloads = new[]
                {
                    new PayloadModel
                    {
                        Data = Convert.ToBase64String(payloadBytes),
                        ContentType = "application/json",
                        ContentEncoding = "base64",
                        WalletAccess = new[] { credential.SubjectDid },
                    },
                },
                Signature = issuerWallet,
            };

            await registerClient.SubmitTransactionAsync(credential.RegisterId, tx);

            logger.LogInformation(
                "CredentialStatusChange tx {TxId} submitted: credential {CredentialId} → {NewStatus} (subject {Subject})",
                txId, credential.CredentialId, newStatus, credential.SubjectDid);
        }
        catch (Exception ex)
        {
            // Best-effort — log and continue. The BitstringStatusList update is what
            // gates verifier-side checks; the holder UI will be stale until a future
            // rebuild or manual refresh.
            logger.LogWarning(ex,
                "Failed to submit CredentialStatusChange register tx for {CredentialId} → {NewStatus}",
                credential.CredentialId, newStatus);
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
