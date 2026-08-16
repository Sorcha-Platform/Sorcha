// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

#pragma warning disable ASPDEPR002 // WithOpenApi is deprecated; using it for co-located endpoint examples until transformer API stabilizes

using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Xml;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;

using Sorcha.Blueprint.Models.Credentials;
using Sorcha.Cryptography.SdJwt;
using Sorcha.ServiceClients.Models;
using Sorcha.ServiceClients.Trust;
using Sorcha.Wallet.Core.Domain.Entities;
using Sorcha.Wallet.Core.Repositories.Interfaces;
using Sorcha.Wallet.Core.Services.Interfaces;
using Sorcha.Wallet.Service.Authorization;
using Sorcha.Wallet.Service.Credentials;
using Sorcha.Wallet.Service.Services.Implementation;
using Sorcha.Wallet.Service.Services.Interfaces;

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
        // G1 (catch-up security review 2026-07-29): CanManageWallets alone is NOT sufficient here.
        // It means "the token carries any non-empty org_id, or is a service token" and never looks
        // at {walletAddress}; consumer-tier citizen tokens carry org_id, so every authenticated
        // citizen passed it for every wallet. No handler below checked ownership either, so any
        // citizen could list, export (raw SD-JWT: name, DOB, address, portrait) and DELETE any
        // other citizen's credentials — browser-reachable, since the gateway proxies
        // /api/v1/wallets/{**catch-all} under RequireAuthenticated only.
        // RequireWalletOwnership binds the caller to the wallet in the route; service tokens are
        // still allowed through because issuance legitimately targets the issuing org's wallet.
        var credentialGroup = app.MapGroup("/api/v1/wallets/{walletAddress}/credentials")
            .WithTags("Credentials")
            .RequireAuthorization("CanManageWallets")
            .RequireWalletOwnership();

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
            .WithRequestValidation()
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
            .WithRequestValidation()
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
            .WithRequestValidation()
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
            .WithRequestValidation()
            .WithName("IssueCredential")
            .WithSummary("Issue a new credential using the wallet's signing key")
            .WithDescription("Creates and signs a new SD-JWT VC credential using the wallet's private key, stores it, and returns the issued credential.")
            .Produces<IssuedCredentialResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .WithOpenApi(operation =>
            {
                // Spec 117 FR-006 — credential issuance MUST carry at least one example for
                // request and response. Payload modelled on the trade-finance walkthrough's
                // VerifiedInvoiceCredential issued by the audited supplier wallet.
                OpenApiExamples.SetRequestExample(operation, """
                    {
                      "credentialType": "VerifiedInvoiceCredential",
                      "claims": {
                        "invoiceNumber": "INV-2026-00482",
                        "issuedTo": "did:sorcha:org:sorcha1buyer012345...",
                        "amount": 47500.00,
                        "currency": "EUR",
                        "dueDate": "2026-06-15",
                        "purchaseOrderRef": "PO-ACME-9921"
                      },
                      "recipientWallet": "sorcha1recipient67890abcdef...",
                      "expiryDuration": "P90D",
                      "disclosableClaims": ["invoiceNumber", "amount", "currency", "dueDate"]
                    }
                    """);
                OpenApiExamples.SetResponseExample(operation, "200", """
                    {
                      "id": "urn:uuid:8e2c1b94-7a31-4f12-9bb8-a3e2f5c14a99",
                      "type": "VerifiedInvoiceCredential",
                      "issuerDid": "did:sorcha:org:sorcha1supplier789ghi012...",
                      "subjectDid": "did:sorcha:org:sorcha1buyer012345...",
                      "issuedAt": "2026-05-02T11:30:00Z",
                      "expiresAt": "2026-07-31T11:30:00Z",
                      "rawToken": "eyJhbGciOiJFZERTQSIsInR5cCI6InZjK3NkLWp3dCJ9.eyJpc3MiOiJkaWQ6c29yY2hhOm9yZzpzb3JjaGExc3VwcGxpZXI3ODlnaGkwMTIuLi4iLCJpYXQiOjE3NjI4MzQwMDAsImV4cCI6MTc3MDYxMDAwMCwidmN0IjoiVmVyaWZpZWRJbnZvaWNlQ3JlZGVudGlhbCIsImNuZiI6e319.SIGNATURE",
                      "status": "Active"
                    }
                    """);
                return operation;
            });

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

        var response = filtered.Select(c =>
        {
            // Single projection per credential — feeds BOTH the claim tree and the
            // disclosable set. A credential ingested before the nested-disclosure
            // decoder fix (SdJwtClaimProjection) still carries a badly-decoded
            // CredentialEntity.ClaimsJson in the store (e.g. a raw {"_sd":[…]}
            // digest array where "address" should be). RawToken is always the
            // source of truth, so re-projecting on every read heals it — no
            // backfill, no migration, no re-issue.
            //
            // BUT RawToken is only guaranteed non-null, not guaranteed to be a
            // compact SD-JWT: StoreCredential accepts any RawToken/ClaimsJson pair
            // from the caller, and Feature 185 proximity sharing stores mdoc/CBOR
            // credentials this way (a W3C JSON-LD VC or a truncated token can land
            // here too). SdJwtClaimProjection.Project never throws, but it DOES
            // return SdJwtProjection.Empty for anything it can't decode — heal only
            // when the projection actually produced something, otherwise keep the
            // stored value. A row healed via a real (if stale) SD-JWT RawToken is
            // unaffected: Project only returns Empty on genuine decode failure.
            var projection = SdJwtClaimProjection.Project(c.RawToken);
            var healed = !ReferenceEquals(projection, SdJwtProjection.Empty);
            return new
            {
                c.Id,
                c.Type,
                c.IssuerDid,
                c.SubjectDid,
                c.IssuedAt,
                c.ExpiresAt,
                c.Status,
                c.IssuerOrgName,
                c.IssuanceBlueprintId,
                c.IssuanceTxId,
                c.IssuanceInstanceId,
                c.IssuanceActionId,
                c.ClaimActionId,
                c.RegisterId,
                // Holders need to see what's in a credential before they Accept/Decline
                // it — see CredentialAcceptCard. Without these fields the card renders
                // "0 claims" against a credential that does have claims, which actively
                // misleads the holder. Payload growth is bounded — claims are typically
                // <2KB and display config is smaller.
                ClaimsJson = healed ? projection.ClaimsJson : c.ClaimsJson,
                c.DisplayConfigJson,
                c.UsagePolicy,
                // Which claims the holder can withhold when presenting. Derived from the
                // stored raw token rather than persisted, so no column and no migration.
                // Without it every claim renders with an "always disclosed" padlock —
                // the exact opposite of the truth about what the holder must reveal.
                // When RawToken isn't a decodable SD-JWT there is no way to know what's
                // withholdable, so fall back to "nothing withholdable" (same
                // always-disclosed padlock the field defaulted to before it existed) —
                // never an empty ClaimsJson.
                DisclosableClaims = healed ? projection.DisclosableClaims : Array.Empty<string>(),
            };
        });

        return Results.Ok(response);
    }

    private static async Task<IResult> GetCredential(
        string walletAddress,
        string credentialId,
        ICredentialStore store,
        CancellationToken cancellationToken = default)
    {
        // Wallet-scoped lookup — credential IDs are not globally unique when a
        // credential exists on both the issuer's wallet (Active) and the
        // recipient's wallet (PendingAcceptance via InboundCredentialDetector).
        var credential = await store.GetByIdForWalletAsync(credentialId, walletAddress, cancellationToken);

        if (credential == null)
            return Results.NotFound();

        // Heal on read (see ListCredentials) — the shape stays CredentialEntity so
        // every existing consumer (detail dialog, ID-card face) keeps working
        // unchanged; only the value of ClaimsJson is corrected, in-memory only
        // (never written back — RawToken remains the persisted source of truth).
        // Only heal when RawToken actually decodes as an SD-JWT — a non-JWT
        // RawToken (mdoc/CBOR from Feature 185, a W3C JSON-LD VC, a truncated
        // token) projects to SdJwtProjection.Empty and must NOT wipe the stored
        // ClaimsJson, or the card silently shows "0 claims" for a credential that
        // has real claims (a stale-but-decodable SD-JWT RawToken still heals fine).
        var projection = SdJwtClaimProjection.Project(credential.RawToken);
        if (!ReferenceEquals(projection, SdJwtProjection.Empty))
        {
            credential.ClaimsJson = projection.ClaimsJson;
        }

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
        // Feature 120 US5 — async path honours DID alsoKnownAs equivalence.
        var matches = await matcher.MatchAsync(requirements, credentials, cancellationToken);

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
        Sorcha.Wallet.Service.Services.Implementation.IWalletInboxWriter inboxWriter,
        CancellationToken cancellationToken = default)
    {
        // Phase 2b of the Snackbar retirement — capture the credential Type
        // BEFORE the delete runs so the inbox entry can show the human-readable
        // credential type. The store-side delete removes the row so a post-delete
        // fetch would 404.
        var snapshotForInbox = await store.GetByIdForWalletAsync(credentialId, walletAddress, cancellationToken);

        // Wallet-scoped delete — credential IDs are not globally unique (Feature 106).
        // DeleteAsync(credentialId, walletAddress) performs the composite-key lookup and
        // delete atomically, removing the TOCTOU window of the former pre-check + delete pair.
        var deleted = await store.DeleteAsync(credentialId, walletAddress, cancellationToken);
        if (!deleted)
        {
            return Results.NotFound();
        }

        if (snapshotForInbox is not null)
        {
            await inboxWriter.WriteCredentialDeletedAsync(
                walletAddress: walletAddress,
                credentialId: credentialId,
                credentialType: snapshotForInbox.Type,
                ct: cancellationToken).ConfigureAwait(false);
        }

        return Results.NoContent();
    }

    private static async Task<IResult> ExportCredential(
        string walletAddress,
        string credentialId,
        ICredentialStore store,
        CancellationToken cancellationToken = default)
    {
        var credential = await store.GetByIdForWalletAsync(credentialId, walletAddress, cancellationToken);

        if (credential == null)
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

        var credential = await store.GetByIdForWalletAsync(credentialId, walletAddress, cancellationToken);

        if (credential == null)
            return Results.NotFound();

        var previousStatus = credential.Status;
        var updated = await store.UpdateStatusAsync(credentialId, walletAddress, targetStatus, cancellationToken);

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
        ILoggerFactory loggerFactory,
        Sorcha.Wallet.Service.Services.Implementation.IWalletInboxWriter inboxWriter,
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
        // Use the wallet-scoped lookup — credential IDs are NOT globally unique
        // when a credential is recorded on both the issuer's wallet (Active at
        // issuance time) and the recipient's wallet (PendingAcceptance via
        // InboundCredentialDetector). Looking up by credential-id alone returns
        // an arbitrary one of the two rows, then the wallet-address mismatch
        // check 404s the legitimate caller. Bug surfaced by the TradeFinance
        // walkthrough credential-fetch step.
        var existing = await store.GetByIdForWalletAsync(credentialId, walletAddress, cancellationToken);
        if (existing is null)
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

        // Modern path (Feature 118): WalletHub emits CredentialStatusChanged
        // directly; the legacy wallet:credential-status Redis bridge that fed
        // EventsHub was retired in T121. No publish here.
        _ = previousStatus; // reserved for future inbox-write enrichment if needed

        // Phase 2b of the Snackbar retirement — fire a durable inbox entry
        // on holder decline. Accept doesn't produce a new entry because the
        // existing "credential received" entry already covers the issuance;
        // accept is a holder-side state change, not a new event.
        if (targetStatus == CredentialStatus.Declined)
        {
            await inboxWriter.WriteCredentialDeclinedAsync(
                walletAddress: walletAddress,
                credentialId: credentialId,
                credentialType: updated.Type,
                ct: cancellationToken).ConfigureAwait(false);
        }

        return Results.Ok(updated);
    }

    private static async Task<IResult> IssueCredential(
        string walletAddress,
        [FromBody] IssueCredentialRequest request,
        IWalletRepository walletRepository,
        IKeyManagementService keyManagement,
        ISdJwtService sdJwtService,
        ICredentialStore store,
        ILoggerFactory loggerFactory,
        Sorcha.Wallet.Service.Services.Implementation.IWalletInboxWriter inboxWriter,
        Sorcha.Wallet.Service.Services.Interfaces.IIssuanceKeyService? issuanceKeyService = null,
        IOrgCertChainProvider? orgCertChainProvider = null,
        Sorcha.Wallet.Service.Services.Interfaces.IDeviceBoundCopyIssuanceCoordinator? deviceBoundCoordinator = null,
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
            && (!Uri.TryCreate(request.StatusListUrl, UriKind.Absolute, out var statusListUri)
                || statusListUri.Scheme != Uri.UriSchemeHttps))
        {
            // Spec 093 data-model: statusListCredential MUST be an absolute HTTPS
            // URL resolvable by external parties. The value is signed into the
            // credential, so a non-HTTPS URL is permanently broken for any
            // wallet that enforces HTTPS-only (the standard case in production).
            return Results.BadRequest(new
            {
                error = "statusListUrl must be an absolute HTTPS URL when supplied"
            });
        }

        // Issue #1447: sorcha.example is the codebase's reserved placeholder host. Every
        // deployment that never configured StatusList:BaseUrl signed this dead URL into
        // its credentials, making FailClosed revocation permanently unpassable. The
        // Blueprint Service now fails fast on the placeholder at startup; this is the
        // last line of defense at the signing seam for any other caller.
        if (!string.IsNullOrWhiteSpace(request.StatusListUrl)
            && Uri.TryCreate(request.StatusListUrl, UriKind.Absolute, out var statusListHostUri)
            && statusListHostUri.Host.Equals("sorcha.example", StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest(new
            {
                error = "statusListUrl points at the reserved placeholder host sorcha.example, " +
                        "which can never serve a status list — configure the deployment's public " +
                        "origin (StatusList:BaseUrl) instead (issue #1447)"
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

        // Feature 120 T039 — ensure the org's VC issuance key + published DID
        // document exist before signing (FR-004 "no later than first issuance").
        // Then attempt to swap to the issuance key for signing (kid-swap, #604):
        // when the org has an Active issuance key, the credential is signed with
        // it and the JWS kid header carries did:sorcha:org:{addr}#vc-issuance-{n}
        // so verifiers resolve to the published DID document.
        Sorcha.Wallet.Service.Services.Interfaces.IssuanceSigningMaterial? issuanceMaterial = null;
        if (issuanceKeyService is not null
            && Guid.TryParse(request.TenantId, out var issuanceOrgId))
        {
            try
            {
                _ = await issuanceKeyService.GetOrDeriveAsync(issuanceOrgId, cancellationToken);
                issuanceMaterial = await issuanceKeyService
                    .GetActiveSigningMaterialAsync(issuanceOrgId, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Issuance key path failed for org {OrgId} — falling through to wallet-key signing",
                    issuanceOrgId);
            }
        }

        // Feature 149: fail closed. A native credential MUST carry a resolvable issuer DID
        // (did:sorcha:org:{A}#vc-issuance-{n}). When no issuance material is available — the org
        // has no VC-issuance key (no Feature 083 master key) or no canonical operational wallet —
        // refuse to mint rather than fall back to signing with the raw wallet key under a bare,
        // unverifiable issuer address. The old wallet-key fallback produced credentials that no
        // conformant verifier could check.
        if (issuanceMaterial is null)
        {
            logger.LogWarning(
                "Refusing to issue '{CredentialType}' for org {TenantId}: no resolvable VC-issuance key. "
                + "Provision a Feature 083 org master key (Set-SorchaOrgMasterKey) so the issuer DID can be "
                + "anchored and published.",
                request.CredentialType, request.TenantId);
            return Results.Problem(
                title: "Issuer has no VC-issuance key",
                detail: "Cannot issue a verifiable credential: the issuing organisation has no active "
                    + "VC-issuance key. Provision a Feature 083 org master key (Set-SorchaOrgMasterKey) and retry.",
                statusCode: StatusCodes.Status409Conflict);
        }

        // 2. Signing material comes exclusively from the org's VC-issuance key (Feature 149 — the
        // wallet-key fallback is removed; the fail-closed guard above guarantees non-null material).
        var privateKey = issuanceMaterial.PrivateKey;
        var signingAlgorithm = issuanceMaterial.Algorithm;
        var signingIssuer = issuanceMaterial.IssuerDid;
        var signingKid = issuanceMaterial.Kid;

        logger.LogInformation(
            "Feature 120 kid-swap: signing credential with org issuance key kid={Kid} for org {OrgId}",
            signingKid, issuanceMaterial.OrganizationId);

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
        // SD-JWT VC (draft-ietf-oauth-sd-jwt-vc §3.2.2.1): vct is the SOLE type claim — no
        // `type` claim in the profile. Credential VCT decoupling: the vct URI (not the bare
        // CredentialType) is also stamped onto every stored CredentialEntity.Type and the
        // response Type below — because the SorchaLocalWallet register envelope copies the
        // response Type into credentialType, that is what propagates the URI to the citizen.
        var vct = string.IsNullOrWhiteSpace(request.Vct) ? request.CredentialType : request.Vct;
        var claims = new Dictionary<string, object>(request.Claims)
        {
            ["vct"] = vct
        };

        // Credential VCT decoupling: serialise the authored display name into the issuer-defined
        // display config so the citizen card shows credentialName without parsing the URI. Match
        // the plain JsonSerializer.Serialize used elsewhere in this handler; CredentialDisplayConfig
        // carries [JsonPropertyName("credentialName")] so the emitted key is stable.
        string? displayConfigJson = string.IsNullOrWhiteSpace(request.DisplayName)
            ? null
            : JsonSerializer.Serialize(new CredentialDisplayConfig { CredentialName = request.DisplayName });

        // Feature 1195 Phase 2 — device-bound copy cap + eviction. A device-bound AIAS copy
        // carries a cnf that is NOT the recipient's holder key; the coordinator detects this,
        // runs the max-3 LRU policy BEFORE signing, and allocates a wallet-owned (F114)
        // status-list slot so the copy is revocable. The holder-bound web root, a non-citizen
        // recipient, and unbound mints return null → issuance proceeds unchanged. A thrown
        // reconcile (eviction revoke failed) aborts the mint — no credential is signed or stored.
        var effectiveStatusListUrl = request.StatusListUrl;
        var effectiveStatusListIndex = request.StatusListIndex;
        var effectiveStatusForm = request.StatusClaimForm;

        if (deviceBoundCoordinator is not null
            && request.HolderJwk.HasValue
            && Guid.TryParse(request.TenantId, out var deviceBoundOrgId))
        {
            DeviceBoundMintPlan? deviceBoundPlan;
            try
            {
                deviceBoundPlan = await deviceBoundCoordinator.PrepareAsync(
                    request.RecipientWallet, vct, request.HolderJwk.Value, deviceBoundOrgId, cancellationToken);
            }
            catch (DeviceBoundPolicyRefusalException ex)
            {
                // Policy REFUSAL — the cap could not be honoured (eviction/replacement revoke
                // failed). 409: retrying as-is won't help until the copy state changes.
                logger.LogWarning(ex,
                    "Device-bound copy policy refused issuance of '{Vct}' for recipient {Recipient} — no credential issued",
                    vct, request.RecipientWallet);
                return Results.Problem(
                    title: "Device-bound credential policy failed",
                    detail: "The device-bound credential copy could not be issued: the eviction policy "
                        + "could not revoke an existing copy. No credential was issued.",
                    statusCode: StatusCodes.Status409Conflict);
            }
            catch (Exception ex)
            {
                // Infrastructure FAULT (holder-key resolution, status-slot allocation, …) —
                // transient and retryable, so 503. Generic detail: never leak internals.
                logger.LogError(ex,
                    "Device-bound copy coordination faulted issuing '{Vct}' for recipient {Recipient} — no credential issued",
                    vct, request.RecipientWallet);
                return Results.Problem(
                    title: "Credential issuance temporarily unavailable",
                    detail: "The credential could not be issued right now. Please retry shortly. "
                        + "No credential was issued.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            if (deviceBoundPlan is not null)
            {
                // M-3 (#1195 Phase 2) — eviction-then-mint-failure window: PrepareAsync ran the
                // LRU policy and may already have EVICTED (revoked) the oldest copy before we reach
                // the signing below. If signing then fails, the user is momentarily below the cap
                // (e.g. 2 copies instead of 3). This is fail-safe BY DESIGN: the invariant is
                // "never MORE than 3, never an orphaned status slot" — being temporarily under is
                // harmless and self-heals on retry (the citizen re-binds and a fresh slot is taken).
                // We deliberately do NOT try to "un-evict": resurrecting a revoked copy would be the
                // more dangerous failure mode.
                //
                // Override any engine-supplied allocation with the wallet-owned F114 slot so
                // the citizen status-list publisher can flip it on eviction. F114 lists are
                // IETF Token Status List (statuslist+jwt), so force the IETF claim shape.
                effectiveStatusListUrl = deviceBoundPlan.StatusListUrl;
                effectiveStatusListIndex = deviceBoundPlan.StatusListIndex;
                effectiveStatusForm = Sorcha.Wallet.Service.Models.StatusClaimForm.IetfTokenStatusList;
            }
        }

        if (!string.IsNullOrEmpty(effectiveStatusListUrl) && effectiveStatusListIndex.HasValue)
        {
            var purpose = string.IsNullOrWhiteSpace(request.StatusListPurpose)
                ? "revocation"
                : request.StatusListPurpose!;

            // Feature 095 US3: HAIP-path callers select the IETF shape so external
            // wallets can read status via IETF Token Status List semantics; internal
            // callers keep the W3C shape to preserve spec 093 behaviour. Exactly one
            // shape is embedded per credential — callers cannot opt into both.
            if (effectiveStatusForm == Sorcha.Wallet.Service.Models.StatusClaimForm.IetfTokenStatusList)
            {
                claims["status"] = new Dictionary<string, object>
                {
                    ["status_list"] = new Dictionary<string, object>
                    {
                        ["uri"] = effectiveStatusListUrl!,
                        ["idx"] = effectiveStatusListIndex.Value,
                    },
                };
            }
            else
            {
                claims["credentialStatus"] = new Dictionary<string, object>
                {
                    ["id"] = $"{effectiveStatusListUrl}#{effectiveStatusListIndex.Value}",
                    ["type"] = "BitstringStatusListEntry",
                    ["statusPurpose"] = purpose,
                    ["statusListIndex"] = effectiveStatusListIndex.Value.ToString(),
                    ["statusListCredential"] = effectiveStatusListUrl!
                };
            }
        }

        // Embed the org cert chain in the JWS x5c header when the caller supplies
        // a tenant id. Absence of either the provider or the tenant id falls back
        // to DID-only verifiability — the existing Sorcha-internal default.
        IReadOnlyList<byte[]>? x5cChain;
        try
        {
            x5cChain = await Credentials.IssueCredentialChainResolver.ResolveChainAsync(
                orgCertChainProvider,
                request.TenantId,
                walletAddress,
                logger,
                cancellationToken,
                request.TrustAnchor);
        }
        catch (Credentials.ExternalAnchorUnavailableException ex)
        {
            // FR-020 — external anchor requested but no valid imported cert; fail closed (never tenant fallback).
            logger.LogWarning(ex, "Credential issuance failed closed on x509-lotl anchor for wallet {Wallet}", walletAddress);
            return Results.Json(
                new { error = "CERT_EXTERNAL_ANCHOR_UNAVAILABLE" },
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        // Feature 137 — when the caller supplies the recipient's holder JWK, bind the
        // credential to it via the SD-JWT cnf claim (key confirmation). Absent → unbound
        // credential (pre-137 behaviour). cnf is always non-disclosable.
        var token = request.HolderJwk.HasValue
            ? await sdJwtService.CreateTokenAsync(
                claims,
                request.DisclosableClaims,
                issuer: signingIssuer,
                subject: request.RecipientWallet,
                signingKey: privateKey,
                algorithm: signingAlgorithm,
                holderJwk: request.HolderJwk.Value,
                expiresAt: expiresAt,
                cancellationToken: cancellationToken,
                x5cChain: x5cChain,
                kid: signingKid)
            : await sdJwtService.CreateTokenAsync(
                claims,
                request.DisclosableClaims,
                issuer: signingIssuer,
                subject: request.RecipientWallet,
                signingKey: privateKey,
                algorithm: signingAlgorithm,
                expiresAt: expiresAt,
                cancellationToken: cancellationToken,
                x5cChain: x5cChain,
                kid: signingKid);

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
            Type = vct,
            IssuerDid = walletAddress,
            SubjectDid = request.RecipientWallet,
            ClaimsJson = claimsJson,
            IssuedAt = issuedAt,
            ExpiresAt = expiresAt,
            RawToken = token.RawToken,
            Status = CredentialStatus.Active,
            IssuanceBlueprintId = request.IssuanceBlueprintId,
            IssuerOrgName = request.IssuerOrgName,
            WalletAddress = walletAddress,
            CreatedAt = DateTimeOffset.UtcNow,
            StatusListUrl = effectiveStatusListUrl,
            StatusListIndex = effectiveStatusListIndex,
            DisplayConfigJson = displayConfigJson
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
                    Type = vct,
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
                    StatusListUrl = effectiveStatusListUrl,
                    StatusListIndex = effectiveStatusListIndex,
                    DisplayConfigJson = displayConfigJson
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

        // Feature 118 / US3 follow-up #2: drop a durable inbox entry on the
        // recipient's user. Fail-safe: any write error is swallowed by the
        // writer so credential issuance is unaffected.
        await inboxWriter.WriteCredentialReceivedAsync(
            recipientWalletAddress: request.RecipientWallet,
            credentialId: credentialId,
            credentialType: request.CredentialType,
            issuerOrgName: request.IssuerOrgName,
            ct: cancellationToken).ConfigureAwait(false);

        return Results.Ok(new IssuedCredentialResponse
        {
            CredentialId = credentialId,
            Type = vct,
            IssuerDid = walletAddress,
            SubjectDid = request.RecipientWallet,
            Claims = claims,
            IssuedAt = issuedAt,
            ExpiresAt = expiresAt,
            RawToken = token.RawToken,
            DisplayConfigJson = displayConfigJson
        });
    }
}

/// <summary>
/// Request to store a pre-issued credential in a wallet.
/// </summary>
public class StoreCredentialRequest
{
    [Required(AllowEmptyStrings = false)]
    [StringLength(512)]
    public required string CredentialId { get; init; }

    [Required(AllowEmptyStrings = false)]
    [StringLength(256)]
    public required string Type { get; init; }

    [Required(AllowEmptyStrings = false)]
    public required string IssuerDid { get; init; }

    [Required(AllowEmptyStrings = false)]
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
    [Required(AllowEmptyStrings = false)]
    [StringLength(256)]
    public required string CredentialType { get; init; }

    /// <summary>
    /// Canonical SD-JWT VC type identifier (the <c>vct</c> claim). MUST be an absolute URI,
    /// case-sensitive exact match per SD-JWT VC §3.2.2.1. When null, <see cref="CredentialType"/>
    /// is used as the vct (defensive fallback for legacy callers).
    /// </summary>
    [StringLength(512)]
    public string? Vct { get; init; }

    /// <summary>
    /// Authored human-readable credential name shown on the wallet card (e.g. "Assured Identity"),
    /// decoupled from the vct URI. When supplied, the issuer serialises it as <c>credentialName</c>
    /// in the credential's display config so the citizen card never depends on parsing the URI.
    /// </summary>
    [StringLength(256)]
    public string? DisplayName { get; init; }

    [Required]
    public required Dictionary<string, object> Claims { get; init; }

    [Required(AllowEmptyStrings = false)]
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
    /// Feature 095 US3 — selects which status-list claim shape to embed in the
    /// signed SD-JWT payload. Defaults to the spec 093 W3C form; HAIP-path callers
    /// set <see cref="Sorcha.Wallet.Service.Models.StatusClaimForm.IetfTokenStatusList"/>
    /// so external wallets can read the status via IETF semantics
    /// (<c>status.status_list.uri</c> + <c>idx</c>). Requires
    /// <see cref="StatusListUrl"/> and <see cref="StatusListIndex"/> to be set;
    /// when the allocation is absent this field is ignored (no claim embedded).
    /// </summary>
    public Sorcha.Wallet.Service.Models.StatusClaimForm StatusClaimForm { get; init; }
        = Sorcha.Wallet.Service.Models.StatusClaimForm.W3cBitstringStatusListEntry;

    /// <summary>
    /// Feature 106: When true, the credential is stored only in the issuer's wallet — not
    /// in the recipient's. Used for SorchaLocalWallet delivery where the credential should
    /// arrive via the register disclosure (InboundCredentialDetector) as PendingAcceptance,
    /// not be pre-stored as Active on the same node.
    /// </summary>
    public bool SkipRecipientStore { get; init; }

    /// <summary>
    /// Human-readable name of the issuing organisation. Captured from the issuer's
    /// JWT org_name claim at action execution time.
    /// </summary>
    public string? IssuerOrgName { get; init; }

    /// <summary>
    /// Tenant id (org_id Guid as string) used to fetch the issuer's X.509
    /// certificate chain from the Tenant Service trust client. When supplied AND
    /// the wallet service has an <c>IOrgCertChainProvider</c> registered, the
    /// resulting chain is embedded in the JWS <c>x5c</c> header so external HAIP
    /// verifiers can validate the issuer key against the tenant trust anchor
    /// without DID resolution. Null falls back to DID-only verifiability (the
    /// existing Sorcha-internal default).
    /// </summary>
    public string? TenantId { get; init; }

    /// <summary>
    /// Feature 137 — recipient holder's public JWK (slot 108) for the SD-JWT
    /// <c>cnf</c> (key confirmation) binding. When supplied, the issued credential
    /// is cryptographically bound to the holder key so only the holder can present
    /// it. Null leaves the credential unbound (the pre-137 behaviour). Public
    /// material only — never a private key.
    /// </summary>
    public JsonElement? HolderJwk { get; init; }

    /// <summary>
    /// Feature 181 US4 — the credential's X.509 trust anchor (<c>register</c> | <c>x509-tenant</c> |
    /// <c>x509-lotl</c>). Drives the x5c chain-attach: <c>x509-lotl</c> resolves the org's imported
    /// external chain and fails closed (<c>CERT_EXTERNAL_ANCHOR_UNAVAILABLE</c>) when absent/expired/
    /// key-mismatched — never falling back to the tenant root (FR-020). <c>x509-tenant</c> attaches the
    /// tenant chain (unchanged, FR-021); <c>register</c>/null attaches no chain (DID-only). Defaults to
    /// the tenant-chain behaviour that existed before this field.
    /// </summary>
    public string? TrustAnchor { get; init; }
}

/// <summary>
/// Request to update a credential's status.
/// </summary>
public class UpdateStatusRequest
{
    [Required(AllowEmptyStrings = false)]
    [StringLength(64)]
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

    /// <summary>
    /// Serialised issuer-defined display config (<c>CredentialDisplayConfig</c> JSON) carrying the
    /// authored <c>credentialName</c>. Null when no display name was supplied. Threaded so the
    /// SorchaLocalWallet register envelope can carry it to the citizen entity.
    /// </summary>
    public string? DisplayConfigJson { get; init; }
}
