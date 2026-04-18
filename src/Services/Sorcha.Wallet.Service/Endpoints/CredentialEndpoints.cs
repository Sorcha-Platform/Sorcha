// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

#pragma warning disable ASPDEPR002 // WithOpenApi is deprecated; using it for co-located endpoint examples until transformer API stabilizes

using System.Text.Json;
using System.Xml;

using Microsoft.AspNetCore.Mvc;

using Sorcha.Blueprint.Models.Credentials;
using Sorcha.Cryptography.SdJwt;
using Sorcha.ServiceClients.Models;
using Sorcha.Wallet.Core.Domain.Entities;
using Sorcha.Wallet.Core.Repositories.Interfaces;
using Sorcha.Wallet.Core.Services.Interfaces;
using Sorcha.Wallet.Service.Credentials;

using StackExchange.Redis;

namespace Sorcha.Wallet.Service.Endpoints;

/// <summary>
/// REST endpoints for managing verifiable credentials in a wallet.
/// </summary>
public static class CredentialEndpoints
{
    /// <summary>
    /// Maps credential management endpoints under /api/v1/wallets/{walletAddress}/credentials.
    /// </summary>
    public static IEndpointRouteBuilder MapCredentialEndpoints(this IEndpointRouteBuilder app)
    {
        var credentialGroup = app.MapGroup("/api/v1/wallets/{walletAddress}/credentials")
            .WithTags("Credentials")
            .RequireAuthorization("CanManageWallets");

        credentialGroup.MapGet("/", ListCredentials)
            .WithName("ListCredentials")
            .WithSummary("List all credentials for a wallet")
            .WithDescription("Returns all active verifiable credentials stored in the specified wallet.")
            .Produces<IEnumerable<object>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        credentialGroup.MapGet("/{credentialId}", GetCredential)
            .WithName("GetCredential")
            .WithSummary("Get a credential by ID")
            .WithDescription("Returns a specific credential by its DID URI identifier.")
            .Produces<CredentialEntity>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        credentialGroup.MapPost("/match", MatchCredentials)
            .WithName("MatchCredentials")
            .WithSummary("Match credentials against requirements")
            .WithDescription("Finds stored credentials that satisfy the given credential requirements.")
            .Produces<IEnumerable<object>>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized);

        credentialGroup.MapDelete("/{credentialId}", DeleteCredential)
            .WithName("DeleteCredential")
            .WithSummary("Delete a credential from wallet")
            .WithDescription("Permanently removes a credential from the wallet store.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        credentialGroup.MapGet("/{credentialId}/export", ExportCredential)
            .WithName("ExportCredential")
            .WithSummary("Export a credential as SD-JWT VC")
            .WithDescription("Returns the raw SD-JWT VC token for use in presentations.")
            .Produces<object>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        credentialGroup.MapPost("/", StoreCredential)
            .WithName("StoreCredential")
            .WithSummary("Store a credential in a wallet")
            .WithDescription("Stores a pre-issued verifiable credential in the specified wallet.")
            .Produces<object>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithOpenApi(operation =>
            {
                OpenApiExamples.SetRequestExample(operation, """
                    {
                      "credentialId": "urn:uuid:3fa85f64-5717-4562-b3fc-2c963f66afa6",
                      "type": "BuildingInspectorCertificate",
                      "issuerDid": "did:sorcha:org:sorcha1abc123def456...",
                      "subjectDid": "did:sorcha:org:sorcha1xyz789ghi012...",
                      "claimsJson": "{\"name\":\"Jane Doe\",\"licenseNumber\":\"BI-2026-0042\",\"jurisdiction\":\"Dublin City\"}",
                      "issuedAt": "2026-03-15T10:30:00Z",
                      "expiresAt": "2027-03-15T10:30:00Z",
                      "rawToken": "eyJhbGciOiJFZERTQSJ9.eyJpc3MiOiJkaWQ6c29yY2hhOm9yZzo..."
                    }
                    """);
                OpenApiExamples.SetResponseExample(operation, "201", """
                    {
                      "id": "urn:uuid:3fa85f64-5717-4562-b3fc-2c963f66afa6",
                      "type": "BuildingInspectorCertificate",
                      "issuerDid": "did:sorcha:org:sorcha1abc123def456...",
                      "subjectDid": "did:sorcha:org:sorcha1xyz789ghi012...",
                      "issuedAt": "2026-03-15T10:30:00Z",
                      "expiresAt": "2027-03-15T10:30:00Z",
                      "status": "Active"
                    }
                    """);
                return operation;
            });

        credentialGroup.MapPatch("/{credentialId}/status", UpdateCredentialStatus)
            .WithName("UpdateCredentialStatus")
            .WithSummary("Update a credential's status")
            .WithDescription("Updates the status of a credential (e.g., Active \u2192 Revoked).")
            .Produces<object>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        // Feature 106 Wave C — holder accept/decline PATCH endpoint.
        // Separate from the legacy /status endpoint above so it can enforce the
        // Feature 106 state machine (PendingAcceptance → Active / Declined) without
        // breaking existing SorchaInternal callers.
        credentialGroup.MapPatch("/{credentialId}", PatchCredentialStatus)
            .WithName("PatchCredentialStatus")
            .WithSummary("Holder accept or decline a pending credential (Feature 106)")
            .WithDescription(
                "Transitions a credential's status under the Feature 106 state machine. " +
                "Valid transitions: PendingAcceptance \u2192 Active (accept), PendingAcceptance \u2192 Declined (decline). " +
                "Returns 409 Conflict on disallowed transitions.")
            .Produces<CredentialEntity>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        credentialGroup.MapPost("/issue", IssueCredential)
            .WithName("IssueCredential")
            .WithSummary("Issue a new credential using the wallet's signing key")
            .WithDescription("Creates and signs a new SD-JWT VC credential using the wallet's private key, stores it, and returns the issued credential.")
            .Produces<IssuedCredentialResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> ListCredentials(
        string walletAddress,
        ICredentialStore store,
        CancellationToken cancellationToken = default,
        [FromQuery(Name = "status")] string? statusFilter = null)
    {
        // Feature 106 — optional ?status= query parameter.
        //   omitted       → backward-compat default: Active only
        //   Active, etc.  → filter by exact status
        //   All           → include every status (Active + PendingAcceptance + Declined + ...)
        //   invalid value → 400 Bad Request with allowed values listed
        CredentialStatus? requested = null;
        var includeAll = false;
        if (!string.IsNullOrWhiteSpace(statusFilter))
        {
            if (string.Equals(statusFilter, "All", StringComparison.OrdinalIgnoreCase))
            {
                includeAll = true;
            }
            else if (Enum.TryParse<CredentialStatus>(statusFilter, ignoreCase: true, out var parsed))
            {
                requested = parsed;
            }
            else
            {
                return Results.BadRequest(new
                {
                    error = $"Invalid status filter '{statusFilter}'. Allowed: Active, Expired, Revoked, Suspended, PendingAcceptance, Declined, Consumed, All."
                });
            }
        }

        var credentials = await store.GetByWalletAsync(walletAddress, cancellationToken);

        IEnumerable<CredentialEntity> filtered = includeAll
            ? credentials
            : requested.HasValue
                ? credentials.Where(c => c.Status == requested.Value)
                : credentials.Where(c => c.Status == CredentialStatus.Active);

        var response = filtered.Select(c => new
        {
            c.Id,
            c.Type,
            c.IssuerDid,
            c.SubjectDid,
            c.IssuedAt,
            c.ExpiresAt,
            c.Status,
            c.IssuanceBlueprintId,
            c.IssuanceTxId,
            c.IssuanceInstanceId,
            c.IssuanceActionId,
            c.ClaimActionId,
            c.RegisterId,
        });

        return Results.Ok(response);
    }

    private static async Task<IResult> GetCredential(
        string walletAddress,
        string credentialId,
        ICredentialStore store,
        CancellationToken cancellationToken = default)
    {
        var credential = await store.GetByIdAsync(credentialId, cancellationToken);

        if (credential == null || credential.WalletAddress != walletAddress)
            return Results.NotFound();

        return Results.Ok(credential);
    }

    private static async Task<IResult> MatchCredentials(
        string walletAddress,
        [FromBody] IEnumerable<CredentialRequirement> requirements,
        ICredentialStore store,
        CredentialMatcher matcher,
        CancellationToken cancellationToken = default)
    {
        var credentials = await store.GetByWalletAsync(walletAddress, cancellationToken);
        var matches = matcher.Match(requirements, credentials);

        var response = matches.Select(kvp => new
        {
            RequirementType = kvp.Key,
            Matched = kvp.Value != null,
            CredentialId = kvp.Value?.Id,
            IssuerDid = kvp.Value?.IssuerDid,
            ExpiresAt = kvp.Value?.ExpiresAt
        });

        return Results.Ok(response);
    }

    private static async Task<IResult> DeleteCredential(
        string walletAddress,
        string credentialId,
        ICredentialStore store,
        CancellationToken cancellationToken = default)
    {
        var credential = await store.GetByIdAsync(credentialId, cancellationToken);

        if (credential == null || credential.WalletAddress != walletAddress)
            return Results.NotFound();

        await store.DeleteAsync(credentialId, cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> ExportCredential(
        string walletAddress,
        string credentialId,
        ICredentialStore store,
        CancellationToken cancellationToken = default)
    {
        var credential = await store.GetByIdAsync(credentialId, cancellationToken);

        if (credential == null || credential.WalletAddress != walletAddress)
            return Results.NotFound();

        return Results.Ok(new
        {
            credential.Id,
            credential.Type,
            credential.RawToken
        });
    }

    private static async Task<IResult> StoreCredential(
        string walletAddress,
        [FromBody] StoreCredentialRequest request,
        ICredentialStore store,
        CancellationToken cancellationToken = default)
    {
        var entity = new CredentialEntity
        {
            Id = request.CredentialId,
            Type = request.Type,
            IssuerDid = request.IssuerDid,
            SubjectDid = request.SubjectDid,
            ClaimsJson = request.ClaimsJson,
            IssuedAt = request.IssuedAt,
            ExpiresAt = request.ExpiresAt,
            RawToken = request.RawToken,
            Status = CredentialStatus.Active,
            IssuanceTxId = request.IssuanceTxId,
            IssuanceBlueprintId = request.IssuanceBlueprintId,
            WalletAddress = walletAddress,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await store.StoreAsync(entity, cancellationToken);

        return Results.Created($"/api/v1/wallets/{walletAddress}/credentials/{entity.Id}", new
        {
            entity.Id,
            entity.Type,
            entity.IssuerDid,
            entity.SubjectDid,
            entity.IssuedAt,
            entity.ExpiresAt,
            entity.Status
        });
    }

    // Feature 106 note: PendingAcceptance and Declined are NOT valid targets via this legacy
    // status-update endpoint. Holder accept/decline for register-native credentials uses the
    // PATCH /api/v1/wallets/{walletAddress}/credentials/{credentialId} endpoint added by Wave C.
    private static readonly HashSet<CredentialStatus> AllowedClientStatusTargets =
    [
        CredentialStatus.Active,
        CredentialStatus.Suspended,
        CredentialStatus.Revoked,
        CredentialStatus.Consumed
    ];

    private static async Task<IResult> UpdateCredentialStatus(
        string walletAddress,
        string credentialId,
        [FromBody] UpdateStatusRequest request,
        ICredentialStore store,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.TryParse<CredentialStatus>(request.Status, ignoreCase: false, out var targetStatus)
            || !AllowedClientStatusTargets.Contains(targetStatus))
        {
            return Results.BadRequest(new { error = $"Invalid status value: {request.Status}. Allowed: Active, Suspended, Revoked, Consumed" });
        }

        var credential = await store.GetByIdAsync(credentialId, cancellationToken);

        if (credential == null || credential.WalletAddress != walletAddress)
            return Results.NotFound();

        var previousStatus = credential.Status;
        var updated = await store.UpdateStatusAsync(credentialId, targetStatus, cancellationToken);

        if (!updated)
            return Results.BadRequest(new { error = $"Invalid status transition from {previousStatus} to {targetStatus}" });

        return Results.Ok(new
        {
            credentialId,
            previousStatus = previousStatus.ToString(),
            newStatus = targetStatus.ToString(),
            updatedAt = DateTimeOffset.UtcNow
        });
    }

    // Feature 106 Wave C — holder accept/decline PATCH endpoint
    // POST body: { "status": "Active" }  → accept a pending credential
    //            { "status": "Declined" } → decline a pending credential
    // Only the Feature 106 transitions are permitted via this endpoint; other status
    // targets (Revoked, Suspended, etc.) continue to use the legacy /status endpoint.
    private static readonly HashSet<CredentialStatus> Feature106HolderTransitions =
    [
        CredentialStatus.Active,
        CredentialStatus.Declined,
    ];

    private static async Task<IResult> PatchCredentialStatus(
        string walletAddress,
        string credentialId,
        [FromBody] UpdateStatusRequest request,
        ICredentialStore store,
        IConnectionMultiplexer redis,
        IWalletRepository walletRepository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken = default)
    {
        var logger = loggerFactory.CreateLogger("Sorcha.Wallet.Service.Endpoints.PatchCredentialStatus");

        if (!Enum.TryParse<CredentialStatus>(request.Status, ignoreCase: false, out var targetStatus)
            || !Feature106HolderTransitions.Contains(targetStatus))
        {
            return Results.BadRequest(new
            {
                error = $"Invalid holder transition target '{request.Status}'. Allowed: Active (accept), Declined (decline)."
            });
        }

        // Read previous status for the SignalR event (needed BEFORE the patch runs).
        var existing = await store.GetByIdAsync(credentialId, cancellationToken);
        if (existing is null
            || !string.Equals(existing.WalletAddress, walletAddress, StringComparison.OrdinalIgnoreCase))
        {
            return Results.NotFound();
        }

        var previousStatus = existing.Status;

        CredentialEntity? updated;
        try
        {
            updated = await store.PatchStatusAsync(walletAddress, credentialId, targetStatus, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogInformation(
                "Rejected invalid Feature 106 transition for credential {CredentialId}: {Message}",
                credentialId, ex.Message);
            return Results.Conflict(new
            {
                error = "invalid-transition",
                from = previousStatus.ToString(),
                to = targetStatus.ToString(),
            });
        }

        if (updated is null)
            return Results.NotFound();

        // Resolve the owning user id so the SignalR bridge can route to the right group.
        // Best-effort — event publishing is non-fatal. If the wallet record is missing
        // (shouldn't happen since PatchStatusAsync just succeeded) we skip the publish
        // rather than fail the PATCH.
        string? userId = null;
        try
        {
            var wallet = await walletRepository.GetByAddressAsync(walletAddress, cancellationToken: cancellationToken);
            userId = wallet?.Owner;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Failed to resolve wallet owner for {WalletAddress} while publishing CredentialStatusChangedEvent",
                walletAddress);
        }

        try
        {
            var evt = new CredentialStatusChangedEvent
            {
                WalletAddress = walletAddress,
                CredentialId = credentialId,
                CredentialType = updated.Type,
                PreviousStatus = previousStatus.ToString(),
                NewStatus = updated.Status.ToString(),
                ChangedAt = DateTimeOffset.UtcNow,
                UserId = userId,
            };

            var subscriber = redis.GetSubscriber();
            var json = JsonSerializer.Serialize(evt, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            });
            await subscriber.PublishAsync(
                RedisChannel.Literal(Feature106CredentialStatusChannel),
                json);
        }
        catch (Exception ex)
        {
            // Publishing is best-effort — the authoritative state is already persisted.
            logger.LogWarning(ex,
                "Failed to publish CredentialStatusChangedEvent for credential {CredentialId}",
                credentialId);
        }

        return Results.Ok(updated);
    }

    /// <summary>
    /// Redis pub/sub channel for Feature 106 credential status transitions.
    /// Consumed by <c>EventsHubNotificationBridge</c> in Blueprint Service and
    /// forwarded to the holder's SignalR group as <c>CredentialStatusChanged</c>.
    /// </summary>
    public const string Feature106CredentialStatusChannel = "wallet:credential-status";

    private static async Task<IResult> IssueCredential(
        string walletAddress,
        [FromBody] IssueCredentialRequest request,
        IWalletRepository walletRepository,
        IKeyManagementService keyManagement,
        ISdJwtService sdJwtService,
        ICredentialStore store,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken = default)
    {
        // 1. Get the issuer wallet
        var wallet = await walletRepository.GetByAddressAsync(walletAddress, cancellationToken: cancellationToken);
        if (wallet == null)
            return Results.NotFound();

        if (wallet.Status != Sorcha.Wallet.Core.Domain.WalletStatus.Active)
            return Results.BadRequest(new { error = "Wallet is not in a valid state for this operation" });

        // Feature 093 US2: validate the status list embedding inputs BEFORE signing.
        // Anything baked into a signed credential is unfixable — the signature covers
        // it and the credential is permanently broken if a value is malformed.
        if (!string.IsNullOrWhiteSpace(request.StatusListUrl)
            && !Uri.TryCreate(request.StatusListUrl, UriKind.Absolute, out _))
        {
            return Results.BadRequest(new
            {
                error = "statusListUrl must be an absolute URI when supplied"
            });
        }

        if (request.StatusListIndex.HasValue && request.StatusListIndex.Value < 0)
        {
            return Results.BadRequest(new
            {
                error = "statusListIndex must be non-negative when supplied"
            });
        }

        // Round 3: only the W3C BitstringStatusListEntry status purposes are
        // accepted. Other values would be embedded in the signed payload as a
        // free-form string and silently confuse status consumers.
        if (!string.IsNullOrWhiteSpace(request.StatusListPurpose)
            && request.StatusListPurpose is not ("revocation" or "suspension"))
        {
            return Results.BadRequest(new
            {
                error = "statusListPurpose must be 'revocation' or 'suspension' when supplied"
            });
        }

        var logger = loggerFactory.CreateLogger("Sorcha.Wallet.Service.Endpoints.CredentialEndpoints");

        // 2. Decrypt the wallet's private key
        var privateKey = await keyManagement.DecryptPrivateKeyAsync(
            wallet.EncryptedPrivateKey, wallet.EncryptionKeyId);

        // 3. Calculate expiry
        var issuedAt = DateTimeOffset.UtcNow;
        DateTimeOffset? expiresAt = null;
        if (!string.IsNullOrWhiteSpace(request.ExpiryDuration))
        {
            try
            {
                expiresAt = issuedAt + XmlConvert.ToTimeSpan(request.ExpiryDuration);
            }
            catch (FormatException)
            {
                expiresAt = issuedAt + TimeSpan.FromDays(365);
            }
        }

        // 4. Create SD-JWT VC token.
        //    Feature 093 US2: if the caller supplied a pre-allocated status list URL + index,
        //    embed a W3C BitstringStatusListEntry credentialStatus claim BEFORE signing so
        //    external verifiers can determine lifecycle state from the token alone.
        var claims = new Dictionary<string, object>(request.Claims)
        {
            ["type"] = request.CredentialType,
            ["vct"] = request.CredentialType
        };

        if (!string.IsNullOrEmpty(request.StatusListUrl) && request.StatusListIndex.HasValue)
        {
            var purpose = string.IsNullOrWhiteSpace(request.StatusListPurpose)
                ? "revocation"
                : request.StatusListPurpose!;

            claims["credentialStatus"] = new Dictionary<string, object>
            {
                ["id"] = $"{request.StatusListUrl}#{request.StatusListIndex.Value}",
                ["type"] = "BitstringStatusListEntry",
                ["statusPurpose"] = purpose,
                ["statusListIndex"] = request.StatusListIndex.Value.ToString(),
                ["statusListCredential"] = request.StatusListUrl!
            };
        }

        var token = await sdJwtService.CreateTokenAsync(
            claims,
            request.DisclosableClaims,
            walletAddress,
            request.RecipientWallet,
            privateKey,
            wallet.Algorithm,
            expiresAt,
            cancellationToken);

        // 5. Generate credential ID
        var credentialId = $"urn:uuid:{Guid.NewGuid()}";

        // 6. Build and store credential in issuer's wallet.
        //    Feature 093 US2: when a status list allocation was supplied, populate the
        //    CredentialEntity row fields in lockstep with the embedded claim so the server
        //    side and the signed payload stay consistent.
        var claimsJson = JsonSerializer.Serialize(claims);
        var issuerEntity = new CredentialEntity
        {
            Id = credentialId,
            Type = request.CredentialType,
            IssuerDid = walletAddress,
            SubjectDid = request.RecipientWallet,
            ClaimsJson = claimsJson,
            IssuedAt = issuedAt,
            ExpiresAt = expiresAt,
            RawToken = token.RawToken,
            Status = CredentialStatus.Active,
            IssuanceBlueprintId = request.IssuanceBlueprintId,
            WalletAddress = walletAddress,
            CreatedAt = DateTimeOffset.UtcNow,
            StatusListUrl = request.StatusListUrl,
            StatusListIndex = request.StatusListIndex
        };
        await store.StoreAsync(issuerEntity, cancellationToken);

        // 7. Store copy in recipient's wallet (if different and exists).
        //    Feature 106: Skip for SorchaLocalWallet — the credential arrives via the
        //    register disclosure and InboundCredentialDetector as PendingAcceptance.
        if (!request.SkipRecipientStore
            && !string.Equals(walletAddress, request.RecipientWallet, StringComparison.OrdinalIgnoreCase))
        {
            var recipientWallet = await walletRepository.GetByAddressAsync(
                request.RecipientWallet, cancellationToken: cancellationToken);
            if (recipientWallet != null)
            {
                var recipientEntity = new CredentialEntity
                {
                    Id = credentialId,
                    Type = request.CredentialType,
                    IssuerDid = walletAddress,
                    SubjectDid = request.RecipientWallet,
                    ClaimsJson = claimsJson,
                    IssuedAt = issuedAt,
                    ExpiresAt = expiresAt,
                    RawToken = token.RawToken,
                    Status = CredentialStatus.Active,
                    IssuanceBlueprintId = request.IssuanceBlueprintId,
                    WalletAddress = request.RecipientWallet,
                    CreatedAt = DateTimeOffset.UtcNow,
                    StatusListUrl = request.StatusListUrl,
                    StatusListIndex = request.StatusListIndex
                };
                await store.StoreAsync(recipientEntity, cancellationToken);

                logger.LogInformation(
                    "Credential {CredentialId} stored in recipient wallet {RecipientWallet}",
                    credentialId, request.RecipientWallet);
            }
            else
            {
                logger.LogWarning(
                    "Recipient wallet {RecipientWallet} not found — credential stored only in issuer wallet",
                    request.RecipientWallet);
            }
        }

        logger.LogInformation(
            "Issued credential {CredentialId} of type {Type} from {Issuer} to {Recipient}",
            credentialId, request.CredentialType, walletAddress, request.RecipientWallet);

        return Results.Ok(new IssuedCredentialResponse
        {
            CredentialId = credentialId,
            Type = request.CredentialType,
            IssuerDid = walletAddress,
            SubjectDid = request.RecipientWallet,
            Claims = claims,
            IssuedAt = issuedAt,
            ExpiresAt = expiresAt,
            RawToken = token.RawToken
        });
    }
}

/// <summary>
/// Request to store a pre-issued credential in a wallet.
/// </summary>
public class StoreCredentialRequest
{
    public required string CredentialId { get; init; }
    public required string Type { get; init; }
    public required string IssuerDid { get; init; }
    public required string SubjectDid { get; init; }
    public required string ClaimsJson { get; init; }
    public required DateTimeOffset IssuedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public required string RawToken { get; init; }
    public string? IssuanceTxId { get; init; }
    public string? IssuanceBlueprintId { get; init; }
}

/// <summary>
/// Request to issue a new credential using the wallet's signing key.
/// </summary>
public class IssueCredentialRequest
{
    public required string CredentialType { get; init; }
    public required Dictionary<string, object> Claims { get; init; }
    public required string RecipientWallet { get; init; }
    public string? ExpiryDuration { get; init; }
    public List<string>? DisclosableClaims { get; init; }
    public string? IssuanceBlueprintId { get; init; }

    /// <summary>
    /// Optional pre-allocated status list URL. When provided together with
    /// <see cref="StatusListIndex"/>, the issuer embeds a W3C BitstringStatusListEntry
    /// credentialStatus claim in the signed SD-JWT payload (Feature 093 US2).
    /// </summary>
    public string? StatusListUrl { get; init; }

    /// <summary>
    /// Optional pre-allocated status list index. See <see cref="StatusListUrl"/>.
    /// </summary>
    public int? StatusListIndex { get; init; }

    /// <summary>
    /// Optional status purpose identifier (for example "revocation" or "suspension").
    /// Defaults to "revocation" when <see cref="StatusListUrl"/> and <see cref="StatusListIndex"/>
    /// are provided and this field is left null.
    /// </summary>
    public string? StatusListPurpose { get; init; }

    /// <summary>
    /// Feature 106: When true, the credential is stored only in the issuer's wallet — not
    /// in the recipient's. Used for SorchaLocalWallet delivery where the credential should
    /// arrive via the register disclosure (InboundCredentialDetector) as PendingAcceptance,
    /// not be pre-stored as Active on the same node.
    /// </summary>
    public bool SkipRecipientStore { get; init; }
}

/// <summary>
/// Request to update a credential's status.
/// </summary>
public class UpdateStatusRequest
{
    public required string Status { get; init; }
}

/// <summary>
/// Response from credential issuance.
/// </summary>
public class IssuedCredentialResponse
{
    public required string CredentialId { get; init; }
    public required string Type { get; init; }
    public required string IssuerDid { get; init; }
    public required string SubjectDid { get; init; }
    public required Dictionary<string, object> Claims { get; init; }
    public required DateTimeOffset IssuedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public required string RawToken { get; init; }
}
