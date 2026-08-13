// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

#pragma warning disable ASPDEPR002 // WithOpenApi is deprecated; using it for co-located endpoint examples until transformer API stabilizes

using System.Security.Claims;
using System.Security.Cryptography;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using Sorcha.Cryptography.Enums;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Cryptography.Models;
using Sorcha.Cryptography.Utilities;
using Sorcha.Wallet.Core.Domain;
using Sorcha.Wallet.Core.Domain.Entities;
using Sorcha.Wallet.Core.Domain.Enums;
using Sorcha.Wallet.Core.Domain.ValueObjects;
using Sorcha.Wallet.Core.Services.Implementation;
using Sorcha.Wallet.Service.Services.Interfaces;
using Sorcha.ServiceClients.Participant;
using Sorcha.Wallet.Contracts.Models;
using Sorcha.Wallet.Service.Mappers;
using Sorcha.Wallet.Service.Models;
using Sorcha.Wallet.Service.Authorization;

namespace Sorcha.Wallet.Service.Endpoints;

/// <summary>
/// Wallet management minimal API endpoints
/// </summary>
public static class WalletEndpoints
{
    /// <summary>
    /// Map all wallet-related endpoints
    /// </summary>
    public static IEndpointRouteBuilder MapWalletEndpoints(this IEndpointRouteBuilder app)
    {
        var walletGroup = app.MapGroup("/api/v1/wallets")
            .WithTags("Wallets")
            .RequireAuthorization("CanManageWallets");

        // POST /api/v1/wallets/system - Create or retrieve system wallet (for validators)
        walletGroup.MapPost("/system", CreateOrRetrieveSystemWallet)
            .WithName("CreateOrRetrieveSystemWallet")
            .WithSummary("Create or retrieve system wallet")
            .WithDescription("Creates or retrieves a system wallet for a validator. Used by Validator Service for signing operations.")
            // Feature 147 / review H1: service-to-service only. Enforced in-code (not just at the
            // gateway, which is RequireAuthenticated) so a direct internal-network call is also gated.
            .RequireAuthorization(Microsoft.Extensions.Hosting.AuthorizationPolicies.RequireService)
            .Produces<SystemWalletResponse>(StatusCodes.Status200OK)
            .Produces<SystemWalletResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        // POST /api/v1/wallets/system/recover - Recover system wallet from a BIP39 mnemonic
        walletGroup.MapPost("/system/recover", RecoverSystemWallet)
            .WithName("RecoverSystemWallet")
            .WithSummary("Recover a system wallet from a mnemonic")
            .WithDescription("Recovers (or imports) a system wallet for a validator from a provided BIP39 mnemonic. " +
                "Used by 'sorcha system-register import-validator-key' to seat the genesis-ceremony validator wallet so " +
                "the Validator Service can sign system register dockets. Idempotent only when the existing wallet's seed " +
                "already matches the supplied mnemonic — otherwise returns 409 Conflict.")
            // Feature 147 / review H1: service-tier caller OR platform-tier administrator (the genesis
            // ceremony operator). Enforced in-code so a direct internal-network call is gated too.
            // The 409-on-exists guard in the handler remains as belt-and-braces.
            .RequireAuthorization("CanRecoverSystemWallet")
            .Produces<SystemWalletResponse>(StatusCodes.Status200OK)
            .Produces<SystemWalletResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();

        // POST /api/v1/wallets - Create new wallet
        walletGroup.MapPost("/", CreateWallet)
            .WithName("CreateWallet")
            .WithSummary("Create a new wallet")
            .WithDescription("Creates a new HD wallet with the specified algorithm and returns the mnemonic phrase for backup. " +
                "Optionally accepts a 'signingMode' parameter ('Local' or 'KmsResident') to override the server-side signing mode policy. " +
                "When signingMode is 'KmsResident', the private key is created and held within cloud KMS and never extracted. " +
                "The response includes 'signingMode' and 'kmsKeyId' fields indicating the wallet's key management configuration.")
            .Produces<CreateWalletResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithOpenApi(operation =>
            {
                OpenApiExamples.SetRequestExample(operation, """
                    {
                      "name": "My Primary Wallet",
                      "algorithm": "ED25519",
                      "wordCount": 12,
                      "enableHybrid": false,
                      "signingMode": "Local"
                    }
                    """);
                OpenApiExamples.SetResponseExample(operation, "201", """
                    {
                      "wallet": {
                        "address": "sorcha1abc123def456ghi789jkl012mno345",
                        "name": "My Primary Wallet",
                        "publicKey": "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2",
                        "algorithm": "ED25519",
                        "status": "Active",
                        "owner": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
                        "tenant": "acme-corp",
                        "signingMode": "Local",
                        "kmsKeyId": null,
                        "createdAt": "2026-03-15T10:30:00Z",
                        "updatedAt": "2026-03-15T10:30:00Z",
                        "metadata": {}
                      },
                      "mnemonicWords": [
                        "abandon", "ability", "able", "about",
                        "above", "absent", "absorb", "abstract",
                        "absurd", "abuse", "access", "accident"
                      ],
                      "warning": "IMPORTANT: Save your mnemonic phrase securely. It cannot be recovered if lost!"
                    }
                    """);
                return operation;
            });

        // POST /api/v1/wallets/recover - Recover wallet from mnemonic
        walletGroup.MapPost("/recover", RecoverWallet)
            .WithName("RecoverWallet")
            .WithSummary("Recover a wallet from mnemonic phrase")
            .WithDescription("Recovers an existing wallet from a BIP39 mnemonic phrase")
            .Produces<WalletDto>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status409Conflict);

        // POST /api/v1/wallets/recover/passkey - Passkey-bound recovery (Feature 060)
        walletGroup.MapPost("/recover/passkey", RecoverViaPasskey)
            .WithName("RecoverWalletViaPasskey")
            .WithSummary("Recover wallets using passkey authentication")
            .WithDescription("Recovers all wallets for the authenticated user using their FIDO2 passkey. "
                + "Revokes all delegations by default; returns pending review items for selective preservation.")
            .Produces<RecoveryResult>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        // POST /api/v1/wallets/recover/org - Organization-managed recovery (Feature 060)
        walletGroup.MapPost("/recover/org", RecoverViaOrg)
            .WithName("RecoverWalletViaOrg")
            .WithSummary("Recover wallets via organization admin")
            .WithDescription("Org admin recovers all wallets for a member. Requires Administrator role. "
                + "Delegation revocation can be skipped by the admin.")
            .Produces<RecoveryResult>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        // POST /api/v1/wallets/recover/delegations/preserve - Selective delegation preservation (Feature 060)
        walletGroup.MapPost("/recover/delegations/preserve", PreserveDelegations)
            .WithName("PreserveDelegations")
            .WithSummary("Selectively preserve delegations after recovery")
            .WithDescription("After recovery, re-grants specific delegations that were revoked. "
                + "Must be called by the recovered user.")
            .Produces<PreserveDelegationsResult>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        // GET /api/v1/wallets/recovery-status - Check recovery capabilities (Feature 060)
        walletGroup.MapGet("/recovery-status", GetRecoveryStatus)
            .WithName("GetRecoveryStatus")
            .WithSummary("Check recovery capabilities for current user")
            .WithDescription("Returns which recovery paths are available for the authenticated user, "
                + "and counts of wallets with/without recovery enabled.")
            .Produces<RecoveryStatusResponse>(StatusCodes.Status200OK);

        // GET /api/v1/wallets - List wallets for current user
        walletGroup.MapGet("/", ListWallets)
            .WithName("ListWallets")
            .WithSummary("List wallets for current user")
            .WithDescription("Retrieve all wallets owned by the current user in the current tenant")
            .Produces<IEnumerable<WalletDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        // GET /api/v1/wallets/by-owner/{ownerId} - List wallets owned by a specific user
        // Service-to-service lookup used by Blueprint Service (and any other service that needs
        // to resolve a user's wallets without depending on a stale wallet_address JWT claim).
        // Requires a service principal token — never exposed to end users.
        walletGroup.MapGet("/by-owner/{ownerId}", ListWalletsByOwner)
            .WithName("ListWalletsByOwner")
            .WithSummary("List wallets owned by a specific user (service only)")
            .WithDescription(
                "Returns all active wallets owned by the specified user id (the NameIdentifier / "
                + "sub claim used as Owner on wallet creation). Intended for service-to-service lookups "
                + "such as the Blueprint Service's pending-actions query, which must resolve a user's "
                + "wallets without relying on the `wallet_address` JWT claim (that claim is populated at "
                + "login/refresh time and can be stale or absent for users whose wallets were created "
                + "after the current token was issued).")
            .RequireAuthorization(Microsoft.Extensions.Hosting.AuthorizationPolicies.RequireService)
            .Produces<IEnumerable<WalletDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        // GET /api/v1/wallets/{address} - Get wallet by address
        walletGroup.MapGet("/{address}", GetWallet)
            .WithName("GetWallet")
            .WithSummary("Get wallet by address")
            .WithDescription("Retrieve detailed information about a specific wallet")
            .Produces<WalletDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        // PATCH /api/v1/wallets/{address} - Update wallet metadata
        // G1 (catch-up security review 2026-07-29): had NO ownership check. GET/sign/decrypt/
        // decapsulate on this group each verify wallet.Owner inline, but the two MUTATING routes
        // did not — so any authenticated org-scoped caller (i.e. any citizen) could rename, retag
        // or soft-DELETE any wallet by address. Gated with the shared primitive rather than another
        // hand-copied inline check, since hand-copied checks are exactly what went missing here.
        walletGroup.MapPatch("/{address}", UpdateWallet)
            .WithRequestValidation()
            .RequireWalletOwnership()
            .WithName("UpdateWallet")
            .WithSummary("Update wallet metadata")
            .WithDescription("Update wallet name and tags")
            .Produces<WalletDto>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        // DELETE /api/v1/wallets/{address} - Delete wallet (soft delete)
        walletGroup.MapDelete("/{address}", DeleteWallet)
            .RequireWalletOwnership()
            .WithName("DeleteWallet")
            .WithSummary("Delete wallet")
            .WithDescription("Soft delete a wallet (can be recovered by support)")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        // POST /api/v1/wallets/{address}/sign - Sign transaction
        walletGroup.MapPost("/{address}/sign", SignTransaction)
            .WithName("SignTransaction")
            .WithSummary("Sign a transaction")
            .WithDescription("Sign transaction data with the wallet's private key. Returns a base64 signature plus the algorithm identifier so a verifier can pick the correct verify path.")
            .Produces<SignTransactionResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .WithOpenApi(operation =>
            {
                // Spec 117 FR-006 — wallet signing MUST carry at least one example for
                // request and response. Payload modelled on a typical action-payload
                // signature submitted to a register.
                OpenApiExamples.SetRequestExample(operation, """
                    {
                      "transactionData": "eyJhY3Rpb25JZCI6Imluc3RhbmNlOjQ3OS9hY3Rpb246c3VibWl0LWludm9pY2UiLCJyZWdpc3RlcklkIjoidHJhZGUtZmluYW5jZS1uMSIsInBheWxvYWQiOnsidmVyaWZpZWRJbnZvaWNlVmNJZCI6InVybjp1dWlkOjhlMmMxYjk0LTdhMzEtNGYxMi05YmI4LWEzZTJmNWMxNGE5OSJ9LCJ0aW1lc3RhbXAiOiIyMDI2LTA1LTAyVDExOjMyOjAwWiJ9",
                      "isPreHashed": false
                    }
                    """);
                OpenApiExamples.SetResponseExample(operation, "200", """
                    {
                      "signature": "g0Y8N+lQbZ3wF8fTjP9c5sJrK4mE2nU1vR7QwI3xY6sBxA8Q9aL2hM5fZpW1dC4JtR8eX0gN3vP7sJrK9oF=",
                      "algorithm": "ED25519",
                      "publicKeyBase64": "u9gN3vP7sJrK9oF8sJrK4mE2nU1vR7QwI3xY6sBxA8Q="
                    }
                    """);
                return operation;
            });

        // POST /api/v1/wallets/{address}/decrypt - Decrypt payload
        walletGroup.MapPost("/{address}/decrypt", DecryptPayload)
            .WithName("DecryptPayload")
            .WithSummary("Decrypt a payload")
            .WithDescription("Decrypt an encrypted payload using the wallet's private key")
            .Produces<DecryptPayloadResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        // POST /api/v1/wallets/{address}/encrypt - Encrypt payload
        walletGroup.MapPost("/{address}/encrypt", EncryptPayload)
            .WithName("EncryptPayload")
            .WithSummary("Encrypt a payload")
            .WithDescription("Encrypt a payload for a recipient wallet using their public key")
            .Produces<EncryptPayloadResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        // POST /api/v1/wallets/{address}/addresses - Register derived address
        walletGroup.MapPost("/{address}/addresses", RegisterDerivedAddress)
            .WithRequestValidation()
            .WithName("RegisterDerivedAddress")
            .WithSummary("Register a client-derived HD address")
            .WithDescription("Register an HD wallet address that was derived client-side. " +
                "The client must derive the address using their mnemonic and provide the public key and derivation path. " +
                "This maintains security by never storing the mnemonic on the server.")
            .Produces<object>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        // GET /api/v1/wallets/{address}/addresses - List derived addresses
        walletGroup.MapGet("/{address}/addresses", ListAddresses)
            .WithName("ListAddresses")
            .WithSummary("List wallet addresses")
            .WithDescription("List all derived addresses for a wallet with optional filtering by type (receive/change), used status, account, and labels")
            .Produces<AddressListResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        // GET /api/v1/wallets/{address}/addresses/{id} - Get specific address
        walletGroup.MapGet("/{address}/addresses/{id:guid}", GetAddress)
            .WithName("GetAddress")
            .WithSummary("Get address by ID")
            .WithDescription("Retrieve detailed information about a specific derived address")
            .Produces<object>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        // PATCH /api/v1/wallets/{address}/addresses/{id} - Update address metadata
        walletGroup.MapPatch("/{address}/addresses/{id:guid}", UpdateAddress)
            .WithRequestValidation()
            .WithName("UpdateAddress")
            .WithSummary("Update address metadata")
            .WithDescription("Update address label, notes, tags, and metadata")
            .Produces<object>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        // POST /api/v1/wallets/{address}/addresses/{id}/mark-used - Mark address as used
        walletGroup.MapPost("/{address}/addresses/{id:guid}/mark-used", MarkAddressAsUsed)
            .WithName("MarkAddressAsUsed")
            .WithSummary("Mark address as used")
            .WithDescription("Mark an address as used (received a transaction). Updates gap limit calculations.")
            .Produces<object>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        // GET /api/v1/wallets/{address}/accounts - List accounts
        walletGroup.MapGet("/{address}/accounts", ListAccounts)
            .WithName("ListAccounts")
            .WithSummary("List BIP44 accounts")
            .WithDescription("List all BIP44 accounts for this wallet with address counts and gap status")
            .Produces<object>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        // GET /api/v1/wallets/{address}/gap-status - Get gap limit status
        walletGroup.MapGet("/{address}/gap-status", GetGapStatus)
            .WithName("GetGapStatus")
            .WithSummary("Get gap limit status")
            .WithDescription("Check BIP44 gap limit compliance for all accounts. Shows unused address counts and warnings.")
            .Produces<GapStatusResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        // POST /api/v1/wallets/{address}/encapsulate - PQC key encapsulation
        walletGroup.MapPost("/{address}/encapsulate", EncapsulateKey)
            .WithName("EncapsulateKey")
            .WithSummary("Encapsulate a shared secret using PQC key")
            .WithDescription("Performs ML-KEM-768 key encapsulation with the recipient's PQC public key, returning ciphertext and the encrypted payload.")
            .Produces<object>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized);

        // POST /api/v1/wallets/{address}/decapsulate - PQC key decapsulation
        walletGroup.MapPost("/{address}/decapsulate", DecapsulateKey)
            .WithName("DecapsulateKey")
            .WithSummary("Decapsulate a shared secret using PQC private key")
            .WithDescription("Performs ML-KEM-768 key decapsulation to recover the shared secret and decrypt the payload.")
            .Produces<object>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        // POST /api/v1/wallets/verify - Verify a signature (service-to-service)
        walletGroup.MapPost("/verify", VerifySignature)
            .WithName("VerifySignature")
            .WithSummary("Verify a cryptographic signature")
            .WithDescription("Verify a signature against data using the provided public key and algorithm. Used by services for wallet link verification.")
            .Produces<object>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }

    /// <summary>
    /// Create a new wallet
    /// </summary>
    private static async Task<IResult> CreateWallet(
        [FromBody] CreateWalletRequest request,
        WalletManager walletManager,
        ICryptoModule cryptoModule,
        IWalletUtilities walletUtilities,
        Sorcha.Wallet.Service.Services.Interfaces.IHolderAddressLookup holderAddressLookup,
        IServiceScopeFactory serviceScopeFactory,
        HttpContext context,
        ILogger<Program> logger,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var owner = GetCurrentUser(context);
            if (owner is null)
                return Results.Unauthorized();
            var tenant = GetCurrentTenant(context);

            // Parse optional signing mode override
            SigningMode? signingModeOverride = null;
            if (!string.IsNullOrWhiteSpace(request.SigningMode) &&
                Enum.TryParse<SigningMode>(request.SigningMode, ignoreCase: true, out var parsedMode))
            {
                signingModeOverride = parsedMode;
            }

            logger.LogInformation("Creating wallet for user {Owner} in tenant {Tenant}, Hybrid={Hybrid}, SigningMode={SigningMode}",
                owner, tenant, request.EnableHybrid, signingModeOverride?.ToString() ?? "policy-default");

            var (wallet, mnemonic) = await walletManager.CreateWalletAsync(
                request.Name,
                request.Algorithm,
                owner,
                tenant,
                request.WordCount,
                request.Passphrase,
                signingModeOverride,
                cancellationToken);

            // F114 PWA enrolment is the canonical population point for
            // CitizenHolderIndex (wallet ↔ PlatformUser.Id mapping the citizen-side
            // /v1/wallet/credentials endpoint reads against), but any flow that
            // creates a citizen wallet through this endpoint without going through
            // PWA enrol (walkthrough automation, scripted CI, future direct-create
            // surfaces) needs the same mapping or the citizen's credential list
            // surfaces empty. Wallets.Owner is the JWT NameIdentifier (UserIdentity.Id,
            // org-scoped) — NOT the PlatformUser.Id the citizen-credential tables key
            // against. The JWT has both — `sub` = UserIdentity.Id, `platform_user_id`
            // = PlatformUser.Id — so we pull the latter here, where we still have the
            // JWT, and pre-populate the index. RegisterAsync is idempotent on
            // (WalletAddress) and tolerates concurrent first-write races.
            // Skipped when the JWT doesn't carry a parseable platform_user_id (admin
            // / service tokens where the citizen-credential pipeline isn't in play).
            var platformUserIdClaim = context.User.FindFirstValue("platform_user_id");
            if (Guid.TryParse(platformUserIdClaim, out var platformUserId))
            {
                try
                {
                    await holderAddressLookup.RegisterAsync(wallet.Address, platformUserId, cancellationToken);
                }
                catch (Exception ex)
                {
                    // Non-critical: a transient index-write failure must NOT roll back
                    // the wallet creation. The projector's fallback (PR #873) will
                    // populate it lazily on the first inbound credential.
                    logger.LogWarning(ex,
                        "Failed to pre-populate CitizenHolderIndex for wallet {Address} platformUser {PlatformUserId} — projector fallback will retry",
                        wallet.Address, platformUserId);
                }
            }

            // Generate PQC key pair and ws2 address for hybrid wallets (computed before building the
            // immutable response so the fields can be set via the object initializer).
            string? pqcWalletAddress = null;
            string? pqcAlgorithm = null;
            if (request.EnableHybrid && !string.IsNullOrEmpty(request.PqcAlgorithm))
            {
                var pqcNetwork = AlgorithmMapper.ParseAlgorithm(request.PqcAlgorithm);
                var pqcKeyResult = await cryptoModule.GenerateKeySetAsync(pqcNetwork, cancellationToken: cancellationToken);
                if (pqcKeyResult.IsSuccess)
                {
                    pqcWalletAddress = walletUtilities.PublicKeyToWallet(pqcKeyResult.Value.PublicKey.Key!, (byte)pqcNetwork);
                    pqcAlgorithm = request.PqcAlgorithm;
                    logger.LogInformation("Hybrid wallet created with PQC address {PqcAddress}", pqcWalletAddress);
                }
                else
                {
                    logger.LogWarning("PQC key generation failed: {Error}, proceeding with classical-only wallet",
                        pqcKeyResult.ErrorMessage);
                }
            }

            var response = new CreateWalletResponse
            {
                Wallet = wallet.ToDto(),
                MnemonicWords = mnemonic.Phrase.Split(' '),
                PqcWalletAddress = pqcWalletAddress,
                PqcAlgorithm = pqcAlgorithm
            };

            // T012: Fire-and-forget auto-link — register participant + link wallet in Tenant Service.
            // Failures don't block wallet creation (FR-004).
            // Uses IServiceScopeFactory to create a new scope — the request scope is disposed
            // after Results.Created returns, before this background task executes.
            var walletAddress = wallet.Address;
            var walletAlgorithm = request.Algorithm;
            _ = Task.Run(async () =>
            {
                try
                {
                    var userId = Guid.TryParse(owner, out var uid) ? uid : Guid.Empty;
                    var orgId = Guid.TryParse(tenant, out var oid) ? oid : Guid.Empty;
                    if (userId == Guid.Empty || orgId == Guid.Empty)
                        return;

                    await using var scope = serviceScopeFactory.CreateAsyncScope();
                    var client = scope.ServiceProvider.GetRequiredService<IParticipantServiceClient>();
                    var bgLogger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

                    var autoLinkResult = await client.AutoLinkWalletAsync(
                        walletAddress,
                        userId,
                        orgId,
                        publicKeyBase64: null,
                        walletAlgorithm,
                        CancellationToken.None);

                    if (autoLinkResult.WalletLinked)
                    {
                        bgLogger.LogInformation(
                            "Auto-linked wallet {WalletAddress} to participant {ParticipantId} (created={Created})",
                            walletAddress, autoLinkResult.ParticipantId, autoLinkResult.ParticipantCreated);
                    }
                    else if (!string.IsNullOrEmpty(autoLinkResult.SkipReason))
                    {
                        bgLogger.LogWarning("Auto-link skipped for wallet {WalletAddress}: {Reason}",
                            walletAddress, autoLinkResult.SkipReason);
                    }
                }
                catch (Exception autoLinkEx)
                {
                    // Can't use scoped logger here if scope creation itself failed
                    Console.Error.WriteLine($"Auto-link failed for wallet {walletAddress}: {autoLinkEx.Message}");
                }
            });

            // Feature 106 hook A: announce primary wallet address to all register bloom filters.
            // Fire-and-forget via a fresh scope — bloom is a cache and its update must never
            // fail the wallet create. Startup-rebuild on the Register Service reconciles gaps.
            _ = Task.Run(async () =>
            {
                try
                {
                    await using var scope = serviceScopeFactory.CreateAsyncScope();
                    var svc = scope.ServiceProvider.GetRequiredService<IAddressRegistrationService>();
                    await svc.NotifyLocalAddressCreatedAsync(walletAddress, CancellationToken.None);
                }
                catch (Exception bloomEx)
                {
                    Console.Error.WriteLine($"Bloom fan-out failed for wallet {walletAddress}: {bloomEx.Message}");
                }
            });

            // Phase 2 of the Snackbar retirement — drop a durable "wallet created"
            // inbox entry for the owner. Fire-and-forget; the writer itself
            // catches transport errors so this is a hard no-op on failure.
            var ownerUserIdentityIdForInbox = Guid.TryParse(owner, out var ownerIdGuid) ? ownerIdGuid : Guid.Empty;
            var walletNameForInbox = request.Name ?? string.Empty;
            _ = Task.Run(async () =>
            {
                try
                {
                    await using var scope = serviceScopeFactory.CreateAsyncScope();
                    var writer = scope.ServiceProvider.GetRequiredService<Sorcha.Wallet.Service.Services.Implementation.IWalletWorkflowInboxWriter>();
                    await writer.WriteWalletCreatedAsync(walletAddress, walletNameForInbox, ownerUserIdentityIdForInbox, CancellationToken.None);
                }
                catch (Exception inboxEx)
                {
                    Console.Error.WriteLine($"Inbox-write failed for wallet-created {walletAddress}: {inboxEx.Message}");
                }
            });

            return Results.Created($"/api/v1/wallets/{wallet.Address}", response);
        }
        catch (ArgumentException ex)
        {
            logger.LogWarning(ex, "Invalid wallet creation request");
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid Request",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create wallet");
            return Results.Problem(
                title: "Wallet Creation Failed",
                detail: "An error occurred while creating the wallet",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Recover a wallet from mnemonic phrase
    /// </summary>
    private static async Task<IResult> RecoverWallet(
        [FromBody] RecoverWalletRequest request,
        WalletManager walletManager,
        IServiceScopeFactory serviceScopeFactory,
        HttpContext context,
        ILogger<Program> logger,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var owner = GetCurrentUser(context);
            if (owner is null)
                return Results.Unauthorized();
            var tenant = GetCurrentTenant(context);

            logger.LogInformation("Recovering wallet for user {Owner} in tenant {Tenant}", owner, tenant);

            var mnemonic = new Mnemonic(string.Join(" ", request.MnemonicWords));

            var wallet = await walletManager.RecoverWalletAsync(
                mnemonic,
                request.Name,
                request.Algorithm,
                owner,
                tenant,
                request.Passphrase,
                cancellationToken);

            // Phase 2 of the Snackbar retirement — drop a durable "wallet
            // recovered" inbox entry for the owner. Fire-and-forget; the writer
            // catches transport errors so recovery never fails because of inbox.
            var ownerUserIdentityIdForInbox = Guid.TryParse(owner, out var ownerIdGuid) ? ownerIdGuid : Guid.Empty;
            var walletAddressForInbox = wallet.Address;
            var walletNameForInbox = request.Name ?? string.Empty;
            _ = Task.Run(async () =>
            {
                try
                {
                    await using var scope = serviceScopeFactory.CreateAsyncScope();
                    var writer = scope.ServiceProvider.GetRequiredService<Sorcha.Wallet.Service.Services.Implementation.IWalletWorkflowInboxWriter>();
                    await writer.WriteWalletRecoveredAsync(walletAddressForInbox, walletNameForInbox, ownerUserIdentityIdForInbox, CancellationToken.None);
                }
                catch (Exception inboxEx)
                {
                    Console.Error.WriteLine($"Inbox-write failed for wallet-recovered {walletAddressForInbox}: {inboxEx.Message}");
                }
            });

            return Results.Ok(wallet.ToDto());
        }
        catch (ArgumentException ex)
        {
            logger.LogWarning(ex, "Invalid wallet recovery request");
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid Request",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already exists"))
        {
            logger.LogWarning(ex, "Wallet already exists");
            return Results.Conflict(new ProblemDetails
            {
                Title = "Wallet Already Exists",
                Detail = ex.Message,
                Status = StatusCodes.Status409Conflict
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to recover wallet");
            return Results.Problem(
                title: "Wallet Recovery Failed",
                detail: "An error occurred while recovering the wallet",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Get wallet by address
    /// </summary>
    private static async Task<IResult> GetWallet(
        string address,
        HttpContext context,
        WalletManager walletManager,
        DelegationService delegationService,
        CancellationToken cancellationToken = default)
    {
        var currentUser = GetCurrentUser(context);
        if (currentUser is null)
            return Results.Unauthorized();

        var wallet = await walletManager.GetWalletAsync(address, cancellationToken);

        if (wallet == null)
        {
            return Results.NotFound();
        }

        // Authorization: service tokens bypass ownership checks (trusted service-to-service calls)
        var isService = context.User.Claims.Any(c => c.Type == "token_type" && c.Value == "service");
        if (!isService && wallet.Owner != currentUser)
        {
            var hasAccess = await delegationService.HasAccessAsync(
                address, currentUser, AccessRight.ReadOnly, cancellationToken);
            if (!hasAccess)
            {
                return Results.Forbid();
            }
        }

        return Results.Ok(wallet.ToDto());
    }

    /// <summary>
    /// List wallets for current user
    /// </summary>
    private static async Task<IResult> ListWallets(
        HttpContext context,
        WalletManager walletManager,
        CancellationToken cancellationToken = default)
    {
        var owner = GetCurrentUser(context);
        if (owner is null)
            return Results.Unauthorized();
        var tenant = GetCurrentTenant(context);

        var wallets = await walletManager.GetWalletsByOwnerAsync(owner, tenant, cancellationToken);

        return Results.Ok(wallets.Select(w => w.ToDto()));
    }

    /// <summary>
    /// List wallets owned by a specific user id (service principal lookup).
    /// </summary>
    /// <remarks>
    /// This is the service-to-service twin of <see cref="ListWallets"/>. Where
    /// <see cref="ListWallets"/> reads the owner from the calling user's
    /// <c>NameIdentifier</c> claim, this variant takes the owner as a route
    /// parameter so a service principal can resolve any user's wallets —
    /// required by Blueprint Service's pending-actions query, which must
    /// operate even when the consumer's JWT has no <c>wallet_address</c> claim
    /// (e.g. because the wallet was created after their current token was
    /// issued and no refresh has happened since).
    /// </remarks>
    private static async Task<IResult> ListWalletsByOwner(
        string ownerId,
        HttpContext context,
        WalletManager walletManager,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ownerId))
            return Results.BadRequest(new { error = "ownerId is required" });

        var tenant = GetCurrentTenant(context);
        var wallets = await walletManager.GetWalletsByOwnerAsync(ownerId, tenant, cancellationToken);
        return Results.Ok(wallets.Select(w => w.ToDto()));
    }

    /// <summary>
    /// Update wallet metadata
    /// </summary>
    private static async Task<IResult> UpdateWallet(
        string address,
        [FromBody] UpdateWalletRequest request,
        WalletManager walletManager,
        ILogger<Program> logger,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var wallet = await walletManager.UpdateWalletAsync(
                address,
                request.Name,
                tags: request.Tags,
                cancellationToken: cancellationToken);

            return Results.Ok(wallet.ToDto());
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
        {
            return Results.NotFound();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update wallet {Address}", address);
            return Results.Problem(
                title: "Wallet Update Failed",
                detail: "An error occurred while updating the wallet",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Delete (soft delete) a wallet
    /// </summary>
    private static async Task<IResult> DeleteWallet(
        string address,
        WalletManager walletManager,
        IServiceScopeFactory serviceScopeFactory,
        ILogger<Program> logger,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Phase 2 of the Snackbar retirement — capture owner + name BEFORE
            // delete so the inbox entry carries the human-readable wallet name
            // that the soft-deleted row may no longer expose to other queries.
            var snapshotForInbox = await walletManager.GetWalletAsync(address, cancellationToken);

            await walletManager.DeleteWalletAsync(address, cancellationToken);

            if (snapshotForInbox is not null)
            {
                var ownerUserIdentityIdForInbox = Guid.TryParse(snapshotForInbox.Owner, out var ownerIdGuid)
                    ? ownerIdGuid : Guid.Empty;
                var walletNameForInbox = snapshotForInbox.Name ?? string.Empty;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await using var scope = serviceScopeFactory.CreateAsyncScope();
                        var writer = scope.ServiceProvider.GetRequiredService<Sorcha.Wallet.Service.Services.Implementation.IWalletWorkflowInboxWriter>();
                        await writer.WriteWalletDeletedAsync(address, walletNameForInbox, ownerUserIdentityIdForInbox, CancellationToken.None);
                    }
                    catch (Exception inboxEx)
                    {
                        Console.Error.WriteLine($"Inbox-write failed for wallet-deleted {address}: {inboxEx.Message}");
                    }
                });
            }

            return Results.NoContent();
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
        {
            return Results.NotFound();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete wallet {Address}", address);
            return Results.Problem(
                title: "Wallet Deletion Failed",
                detail: "An error occurred while deleting the wallet",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Sign a transaction with a wallet
    /// </summary>
    private static async Task<IResult> SignTransaction(
        string address,
        [FromBody] SignTransactionRequest request,
        WalletManager walletManager,
        // Nullable + explicit [FromServices]: WalletDbContext is registered ONLY when a Postgres
        // connection string is present (WalletServiceExtensions.AddWalletDatabase). Declared
        // non-nullable and un-attributed, minimal APIs could not infer it at endpoint-build time
        // and the whole host failed to start with "Failure to infer one or more parameters" —
        // so the Wallet Service could not run on the in-memory storage path at all, which
        // Pattern #13 explicitly supports outside Production. Nullable resolves via GetService.
        [FromServices] Sorcha.Wallet.Core.Data.WalletDbContext? dbContext,
        HttpContext context,
        ILogger<Program> logger,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // SEC-CRITICAL: Verify caller owns the wallet before signing.
            // Service tokens (token_type=service) bypass this check — they are trusted
            // internal service-to-service calls (e.g., Blueprint Service signing actions).
            // User tokens must own the wallet or have delegated access.
            var isService = context.User.Claims.Any(c => c.Type == "token_type" && c.Value == "service");
            if (!isService)
            {
                var currentUser = GetCurrentUser(context);
                if (currentUser is null)
                    return Results.Unauthorized();

                var wallet = await walletManager.GetWalletAsync(address, cancellationToken);
                if (wallet is null)
                    return Results.NotFound(new ProblemDetails
                    {
                        Title = "Wallet Not Found",
                        Detail = $"Wallet {address} not found",
                        Status = StatusCodes.Status404NotFound
                    });

                if (wallet.Owner != currentUser)
                {
                    logger.LogWarning(
                        "SEC-AUDIT: User {User} attempted to sign with wallet {Wallet} owned by {Owner}",
                        currentUser, address, wallet.Owner);
                    return Results.Forbid();
                }
            }
            else
            {
                // #1397 — the service-token bypass above is deliberately broad (Blueprint Service
                // legitimately signs OTHER organisations' wallets during credential issuance), but a
                // validator:*-owned wallet is the docket-signing / SSR-owner system key. Narrow the
                // bypass so only the trusted system principals that operate that key may sign with one:
                //   validator-service — seals dockets with the docket-signing key
                //   register-service  — signs register genesis at creation (finalize) and F189 governance
                // Any OTHER service token targeting it is the #1397 oracle. (Blueprint's sandbox-register
                // creation delegates finalize to register-service, so it is covered transitively; peer
                // replicates but never seals; genesis import uses /system/recover, not /sign.)
                var systemWallet = await walletManager.GetWalletAsync(address, cancellationToken);
                if (systemWallet is not null && systemWallet.Owner is not null &&
                    systemWallet.Owner.StartsWith("validator:", StringComparison.Ordinal))
                {
                    var clientId = context.User.Claims.FirstOrDefault(c => c.Type == "client_id")?.Value;
                    var isSystemSigner =
                        string.Equals(clientId, "validator-service", StringComparison.Ordinal) ||
                        string.Equals(clientId, "register-service", StringComparison.Ordinal);
                    if (!isSystemSigner)
                    {
                        logger.LogWarning(
                            "SEC-AUDIT: service principal {ClientId} attempted to sign system wallet {Wallet} owned by {Owner}",
                            clientId, address, systemWallet.Owner);
                        return Results.Forbid();
                    }
                }
            }

            // Only check derived key status if this is an org-derived wallet.
            // Use DerivedKeyRecordId FK to avoid a full table scan for standalone wallets.
            // Skipped entirely without a relational store: org key derivation is Postgres-backed,
            // so on the in-memory path there are no DerivedKeyRecords to rotate or revoke.
            var derivedKeyRecordId = dbContext is null
                ? null
                : await dbContext.Wallets
                    .Where(w => w.Address == address)
                    .Select(w => w.DerivedKeyRecordId)
                    .FirstOrDefaultAsync(cancellationToken);

            if (derivedKeyRecordId is not null && dbContext is not null)
            {
                var derivedKeyRecord = await dbContext.DerivedKeyRecords
                    .FirstOrDefaultAsync(d => d.Id == derivedKeyRecordId, cancellationToken);

                if (derivedKeyRecord is not null)
                {
                    if (derivedKeyRecord.Status == Sorcha.Wallet.Core.Domain.Enums.DerivedKeyStatus.Rotated)
                    {
                        return Results.Json(
                            new ProblemDetails
                            {
                                Title = "Key Rotated",
                                Detail = "Key has been rotated. Use the current active key.",
                                Status = StatusCodes.Status403Forbidden
                            },
                            statusCode: StatusCodes.Status403Forbidden);
                    }

                    if (derivedKeyRecord.Status == Sorcha.Wallet.Core.Domain.Enums.DerivedKeyStatus.Revoked)
                    {
                        return Results.Json(
                            new ProblemDetails
                            {
                                Title = "Key Revoked",
                                Detail = "Key has been revoked.",
                                Status = StatusCodes.Status403Forbidden
                            },
                            statusCode: StatusCodes.Status403Forbidden);
                    }
                }
            }

            var transactionData = Convert.FromBase64String(request.TransactionData);

            // Hybrid mode: sign with both classical (URL address) and PQC (PqcWalletAddress) wallets
            if (request.HybridMode)
            {
                if (string.IsNullOrWhiteSpace(request.PqcWalletAddress))
                {
                    return Results.BadRequest(new ProblemDetails
                    {
                        Title = "Invalid Request",
                        Detail = "PqcWalletAddress is required when HybridMode is true",
                        Status = StatusCodes.Status400BadRequest
                    });
                }

                // Look up actual wallet algorithms
                var classicalWallet = await walletManager.GetWalletAsync(address, cancellationToken);
                var pqcWallet = await walletManager.GetWalletAsync(request.PqcWalletAddress, cancellationToken);
                if (classicalWallet == null || pqcWallet == null)
                {
                    return Results.NotFound(new ProblemDetails
                    {
                        Title = "Wallet Not Found",
                        Detail = classicalWallet == null
                            ? $"Classical wallet not found: {address}"
                            : $"PQC wallet not found: {request.PqcWalletAddress}",
                        Status = StatusCodes.Status404NotFound
                    });
                }

                // Sign concurrently with both wallets
                var classicalTask = walletManager.SignTransactionAsync(
                    address, transactionData, request.DerivationPath, request.IsPreHashed, cancellationToken);
                var pqcTask = walletManager.SignTransactionAsync(
                    request.PqcWalletAddress, transactionData, request.DerivationPath, request.IsPreHashed, cancellationToken);
                await Task.WhenAll(classicalTask, pqcTask);

                var (classicalSig, classicalPublicKey) = await classicalTask;
                var (pqcSig, pqcPublicKey) = await pqcTask;

                var hybrid = new HybridSignature
                {
                    Classical = Convert.ToBase64String(classicalSig),
                    ClassicalAlgorithm = classicalWallet.Algorithm,
                    Pqc = Convert.ToBase64String(pqcSig),
                    PqcAlgorithm = pqcWallet.Algorithm,
                    WitnessPublicKey = Convert.ToBase64String(pqcPublicKey)
                };

                return Results.Ok(new SignTransactionResponse
                {
                    Signature = hybrid.ToJson(),
                    SignedBy = address,
                    SignedAt = DateTime.UtcNow,
                    PublicKey = Convert.ToBase64String(classicalPublicKey)
                });
            }

            var (signature, publicKey) = await walletManager.SignTransactionAsync(
                address,
                transactionData,
                request.DerivationPath,
                request.IsPreHashed,
                cancellationToken);

            var response = new SignTransactionResponse
            {
                Signature = Convert.ToBase64String(signature),
                SignedBy = address,
                SignedAt = DateTime.UtcNow,
                PublicKey = Convert.ToBase64String(publicKey)
            };

            return Results.Ok(response);
        }
        catch (FormatException ex)
        {
            logger.LogWarning(ex, "Invalid base64 transaction data");
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid Request",
                Detail = "Transaction data must be valid base64 encoded string",
                Status = StatusCodes.Status400BadRequest
            });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
        {
            return Results.NotFound();
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("InvalidParameter"))
        {
            logger.LogWarning(ex, "Unsupported signing operation for wallet {Address}", address);
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Signing Not Supported",
                Detail = $"This wallet's algorithm does not support signing operations. {ex.Message}",
                Status = StatusCodes.Status400BadRequest
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to sign transaction for wallet {Address}", address);
            return Results.Problem(
                title: "Transaction Signing Failed",
                detail: "An error occurred while signing the transaction",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Decrypt a payload using a wallet's private key
    /// </summary>
    private static async Task<IResult> DecryptPayload(
        string address,
        [FromBody] DecryptPayloadRequest request,
        WalletManager walletManager,
        HttpContext context,
        ILogger<Program> logger,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // SEC-CRITICAL: Verify caller owns the wallet before decrypting.
            var isService = context.User.Claims.Any(c => c.Type == "token_type" && c.Value == "service");
            if (!isService)
            {
                var currentUser = GetCurrentUser(context);
                if (currentUser is null)
                    return Results.Unauthorized();

                var wallet = await walletManager.GetWalletAsync(address, cancellationToken);
                if (wallet is null)
                    return Results.NotFound(new ProblemDetails
                    {
                        Title = "Wallet Not Found",
                        Detail = $"Wallet {address} not found",
                        Status = StatusCodes.Status404NotFound
                    });

                if (wallet.Owner != currentUser)
                {
                    logger.LogWarning(
                        "SEC-AUDIT: User {User} attempted to decrypt with wallet {Wallet} owned by {Owner}",
                        currentUser, address, wallet.Owner);
                    return Results.Forbid();
                }
            }

            var encryptedPayload = Convert.FromBase64String(request.EncryptedPayload);
            var decryptedPayload = await walletManager.DecryptPayloadAsync(
                address,
                encryptedPayload,
                cancellationToken);

            var response = new DecryptPayloadResponse
            {
                DecryptedPayload = Convert.ToBase64String(decryptedPayload),
                DecryptedBy = address,
                DecryptedAt = DateTime.UtcNow
            };

            return Results.Ok(response);
        }
        catch (FormatException ex)
        {
            logger.LogWarning(ex, "Invalid base64 encrypted payload");
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid Request",
                Detail = "Encrypted payload must be valid base64 encoded string",
                Status = StatusCodes.Status400BadRequest
            });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
        {
            return Results.NotFound();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to decrypt payload for wallet {Address}", address);
            return Results.Problem(
                title: "Payload Decryption Failed",
                detail: "An error occurred while decrypting the payload",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Encrypt a payload for a recipient wallet
    /// </summary>
    private static async Task<IResult> EncryptPayload(
        string address,
        [FromBody] EncryptPayloadRequest request,
        WalletManager walletManager,
        ILogger<Program> logger,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Use RecipientAddress from request body if provided, otherwise use address from route
            var recipientAddress = request.RecipientAddress ?? address;

            var payload = Convert.FromBase64String(request.Payload);
            var encryptedPayload = await walletManager.EncryptPayloadAsync(
                recipientAddress,
                payload,
                cancellationToken);

            var response = new EncryptPayloadResponse
            {
                EncryptedPayload = Convert.ToBase64String(encryptedPayload),
                RecipientAddress = recipientAddress,
                EncryptedAt = DateTime.UtcNow
            };

            return Results.Ok(response);
        }
        catch (FormatException ex)
        {
            logger.LogWarning(ex, "Invalid base64 payload");
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid Request",
                Detail = "Payload must be valid base64 encoded string",
                Status = StatusCodes.Status400BadRequest
            });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
        {
            return Results.NotFound();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to encrypt payload for recipient {Address}", address);
            return Results.Problem(
                title: "Payload Encryption Failed",
                detail: "An error occurred while encrypting the payload",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Register a client-derived HD wallet address
    /// </summary>
    private static async Task<IResult> RegisterDerivedAddress(
        string address,
        [FromBody] RegisterDerivedAddressRequest request,
        WalletManager walletManager,
        IServiceScopeFactory serviceScopeFactory,
        ILogger<Program> logger,
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation("Registering derived address for wallet {WalletAddress}", address);

            // Register the client-derived address
            var walletAddress = await walletManager.RegisterDerivedAddressAsync(
                walletAddress: address,
                derivedPublicKey: request.DerivedPublicKey,
                derivedAddress: request.DerivedAddress,
                derivationPath: request.DerivationPath,
                label: request.Label,
                notes: request.Notes,
                tags: request.Tags,
                metadata: request.Metadata,
                cancellationToken: cancellationToken);

            // Map to DTO
            var dto = walletAddress.ToDto();

            logger.LogInformation(
                "Successfully registered address {DerivedAddress} at path {Path}",
                request.DerivedAddress, request.DerivationPath);

            // Feature 106 hook B: announce BIP44-derived child address to all register bloom filters.
            // Fire-and-forget via a fresh scope; startup-rebuild reconciles failures.
            var derivedAddr = walletAddress.Address;
            _ = Task.Run(async () =>
            {
                try
                {
                    await using var scope = serviceScopeFactory.CreateAsyncScope();
                    var svc = scope.ServiceProvider.GetRequiredService<IAddressRegistrationService>();
                    await svc.NotifyLocalAddressCreatedAsync(derivedAddr, CancellationToken.None);
                }
                catch (Exception bloomEx)
                {
                    Console.Error.WriteLine($"Bloom fan-out failed for derived address {derivedAddr}: {bloomEx.Message}");
                }
            });

            // Phase 2 of the Snackbar retirement — drop a durable "new derived
            // address" inbox entry for the owning user. Look up the parent
            // wallet to resolve the owner; the endpoint has no HttpContext.
            // Fire-and-forget; the writer catches transport errors so the
            // address registration is never affected by an inbox failure.
            _ = Task.Run(async () =>
            {
                try
                {
                    await using var scope = serviceScopeFactory.CreateAsyncScope();
                    var manager = scope.ServiceProvider.GetRequiredService<WalletManager>();
                    var parent = await manager.GetWalletAsync(address, CancellationToken.None);
                    if (parent is null) return;

                    var ownerUserIdentityId = Guid.TryParse(parent.Owner, out var ownerIdGuid)
                        ? ownerIdGuid : Guid.Empty;
                    if (ownerUserIdentityId == Guid.Empty) return;

                    var writer = scope.ServiceProvider.GetRequiredService<Sorcha.Wallet.Service.Services.Implementation.IWalletWorkflowInboxWriter>();
                    await writer.WriteAddressRegisteredAsync(address, derivedAddr, ownerUserIdentityId, CancellationToken.None);
                }
                catch (Exception inboxEx)
                {
                    Console.Error.WriteLine($"Inbox-write failed for address-registered {derivedAddr}: {inboxEx.Message}");
                }
            });

            return Results.Created($"/api/v1/wallets/{address}/addresses/{walletAddress.Id}", dto);
        }
        catch (ArgumentException ex)
        {
            logger.LogWarning(ex, "Invalid request for wallet {Address}: {Message}", address, ex.Message);
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid Request",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
        {
            logger.LogWarning("Wallet {Address} not found", address);
            return Results.NotFound(new ProblemDetails
            {
                Title = "Wallet Not Found",
                Detail = "The requested resource was not found",
                Status = StatusCodes.Status404NotFound
            });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already exists"))
        {
            logger.LogWarning(ex, "Duplicate address for wallet {Address}", address);
            return Results.Conflict(new ProblemDetails
            {
                Title = "Address Already Exists",
                Detail = ex.Message,
                Status = StatusCodes.Status409Conflict
            });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Gap limit"))
        {
            logger.LogWarning(ex, "Gap limit exceeded for wallet {Address}", address);
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Gap Limit Exceeded",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to register derived address for wallet {Address}", address);
            return Results.Problem(
                title: "Address Registration Failed",
                detail: "An error occurred while registering the derived address",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// List all addresses for a wallet with optional filtering
    /// </summary>
    private static async Task<IResult> ListAddresses(
        string address,
        WalletManager walletManager,
        ILogger<Program> logger,
        [FromQuery] string? type = null,
        [FromQuery] bool? used = null,
        [FromQuery] uint? account = null,
        [FromQuery] string? label = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Get wallet with addresses
            var wallet = await walletManager.GetWalletAsync(address, cancellationToken);
            if (wallet == null)
            {
                return Results.NotFound(new ProblemDetails
                {
                    Title = "Wallet Not Found",
                    Detail = "The requested resource was not found",
                    Status = StatusCodes.Status404NotFound
                });
            }

            // Apply filters
            var addresses = wallet.Addresses.AsEnumerable();

            if (!string.IsNullOrEmpty(type))
            {
                var isChange = type.Equals("change", StringComparison.OrdinalIgnoreCase);
                addresses = addresses.Where(a => a.IsChange == isChange);
            }

            if (used.HasValue)
            {
                addresses = addresses.Where(a => a.IsUsed == used.Value);
            }

            if (account.HasValue)
            {
                addresses = addresses.Where(a => a.Account == account.Value);
            }

            if (!string.IsNullOrEmpty(label))
            {
                addresses = addresses.Where(a => a.Label != null && a.Label.Contains(label, StringComparison.OrdinalIgnoreCase));
            }

            // Pagination
            var totalCount = addresses.Count();
            var paginatedAddresses = addresses
                .OrderBy(a => a.Account)
                .ThenBy(a => a.IsChange)
                .ThenBy(a => a.Index)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(a => a.ToDto())
                .ToList();

            var response = new AddressListResponse
            {
                WalletAddress = address,
                Addresses = paginatedAddresses,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            };

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to list addresses for wallet {Address}", address);
            return Results.Problem(
                title: "Failed to List Addresses",
                detail: "An error occurred while listing addresses",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Get a specific address by ID
    /// </summary>
    private static async Task<IResult> GetAddress(
        string address,
        Guid id,
        WalletManager walletManager,
        ILogger<Program> logger,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var wallet = await walletManager.GetWalletAsync(address, cancellationToken);
            if (wallet == null)
            {
                return Results.NotFound(new ProblemDetails
                {
                    Title = "Wallet Not Found",
                    Detail = "The requested resource was not found",
                    Status = StatusCodes.Status404NotFound
                });
            }

            var walletAddress = wallet.Addresses.FirstOrDefault(a => a.Id == id);
            if (walletAddress == null)
            {
                return Results.NotFound(new ProblemDetails
                {
                    Title = "Address Not Found",
                    Detail = "The requested resource was not found",
                    Status = StatusCodes.Status404NotFound
                });
            }

            return Results.Ok(walletAddress.ToDto());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get address {Id} for wallet {Address}", id, address);
            return Results.Problem(
                title: "Failed to Get Address",
                detail: "An error occurred while retrieving the address",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Update address metadata
    /// </summary>
    private static async Task<IResult> UpdateAddress(
        string address,
        Guid id,
        [FromBody] UpdateAddressRequest request,
        WalletManager walletManager,
        ILogger<Program> logger,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var wallet = await walletManager.GetWalletAsync(address, cancellationToken);
            if (wallet == null)
            {
                return Results.NotFound(new ProblemDetails
                {
                    Title = "Wallet Not Found",
                    Detail = "The requested resource was not found",
                    Status = StatusCodes.Status404NotFound
                });
            }

            var walletAddress = wallet.Addresses.FirstOrDefault(a => a.Id == id);
            if (walletAddress == null)
            {
                return Results.NotFound(new ProblemDetails
                {
                    Title = "Address Not Found",
                    Detail = "The requested resource was not found",
                    Status = StatusCodes.Status404NotFound
                });
            }

            // Update fields if provided
            if (request.Label != null)
                walletAddress.Label = request.Label;
            if (request.Notes != null)
                walletAddress.Notes = request.Notes;
            if (request.Tags != null)
                walletAddress.Tags = request.Tags;
            if (request.Metadata != null)
            {
                foreach (var (key, value) in request.Metadata)
                {
                    walletAddress.Metadata[key] = value;
                }
            }

            // Note: Changes to wallet.Addresses collection are tracked, no explicit update needed
            logger.LogInformation("Updated address {Id} for wallet {Address}", id, address);
            return Results.Ok(walletAddress.ToDto());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update address {Id} for wallet {Address}", id, address);
            return Results.Problem(
                title: "Failed to Update Address",
                detail: "An error occurred while updating the address",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Mark an address as used
    /// </summary>
    private static async Task<IResult> MarkAddressAsUsed(
        string address,
        Guid id,
        WalletManager walletManager,
        ILogger<Program> logger,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var wallet = await walletManager.GetWalletAsync(address, cancellationToken);
            if (wallet == null)
            {
                return Results.NotFound(new ProblemDetails
                {
                    Title = "Wallet Not Found",
                    Detail = "The requested resource was not found",
                    Status = StatusCodes.Status404NotFound
                });
            }

            var walletAddress = wallet.Addresses.FirstOrDefault(a => a.Id == id);
            if (walletAddress == null)
            {
                return Results.NotFound(new ProblemDetails
                {
                    Title = "Address Not Found",
                    Detail = "The requested resource was not found",
                    Status = StatusCodes.Status404NotFound
                });
            }

            if (!walletAddress.IsUsed)
            {
                walletAddress.IsUsed = true;
                walletAddress.FirstUsedAt = DateTime.UtcNow;
                walletAddress.LastUsedAt = DateTime.UtcNow;
                logger.LogInformation("Marked address {Id} as used for wallet {Address}", id, address);
            }
            else
            {
                walletAddress.LastUsedAt = DateTime.UtcNow;
                logger.LogInformation("Updated last used timestamp for address {Id} on wallet {Address}", id, address);
            }

            // Note: Changes to wallet.Addresses collection are tracked, no explicit save needed

            return Results.Ok(walletAddress.ToDto());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to mark address {Id} as used for wallet {Address}", id, address);
            return Results.Problem(
                title: "Failed to Mark Address as Used",
                detail: "An error occurred while marking the address as used",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// List BIP44 accounts with address counts
    /// </summary>
    private static async Task<IResult> ListAccounts(
        string address,
        WalletManager walletManager,
        ILogger<Program> logger,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var wallet = await walletManager.GetWalletAsync(address, cancellationToken);
            if (wallet == null)
            {
                return Results.NotFound(new ProblemDetails
                {
                    Title = "Wallet Not Found",
                    Detail = "The requested resource was not found",
                    Status = StatusCodes.Status404NotFound
                });
            }

            // Group addresses by account
            var accountGroups = wallet.Addresses
                .GroupBy(a => a.Account)
                .Select(g => new
                {
                    Account = g.Key,
                    TotalAddresses = g.Count(),
                    ReceiveAddresses = g.Count(a => !a.IsChange),
                    ChangeAddresses = g.Count(a => a.IsChange),
                    UsedAddresses = g.Count(a => a.IsUsed),
                    UnusedReceive = g.Count(a => !a.IsChange && !a.IsUsed),
                    UnusedChange = g.Count(a => a.IsChange && !a.IsUsed),
                    LastUsedReceiveIndex = g.Where(a => !a.IsChange && a.IsUsed).Max(a => (int?)a.Index),
                    LastUsedChangeIndex = g.Where(a => a.IsChange && a.IsUsed).Max(a => (int?)a.Index)
                })
                .OrderBy(a => a.Account)
                .ToList();

            return Results.Ok(new
            {
                WalletAddress = address,
                Accounts = accountGroups
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to list accounts for wallet {Address}", address);
            return Results.Problem(
                title: "Failed to List Accounts",
                detail: "An error occurred while listing accounts",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Get gap limit status for all accounts
    /// </summary>
    private static async Task<IResult> GetGapStatus(
        string address,
        WalletManager walletManager,
        ILogger<Program> logger,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var wallet = await walletManager.GetWalletAsync(address, cancellationToken);
            if (wallet == null)
            {
                return Results.NotFound(new ProblemDetails
                {
                    Title = "Wallet Not Found",
                    Detail = "The requested resource was not found",
                    Status = StatusCodes.Status404NotFound
                });
            }

            var accountStatuses = new List<AccountGapStatus>();

            // Group by account and address type
            var groups = wallet.Addresses
                .GroupBy(a => new { a.Account, a.IsChange });

            foreach (var group in groups)
            {
                var unusedCount = group.Count(a => !a.IsUsed);
                var lastUsedIndex = group.Where(a => a.IsUsed).Max(a => (int?)a.Index);

                accountStatuses.Add(new AccountGapStatus
                {
                    Account = group.Key.Account,
                    AddressType = group.Key.IsChange ? "change" : "receive",
                    UnusedCount = unusedCount,
                    LastUsedIndex = lastUsedIndex,
                    MaxRecommendedGap = 20
                });
            }

            var response = new GapStatusResponse
            {
                WalletAddress = address,
                Accounts = accountStatuses
            };

            // Add warning if approaching limit
            var approaching = accountStatuses.Where(a => a.UnusedCount >= 15 && a.UnusedCount < 20).ToList();
            if (approaching.Any())
            {
                response.Warning = $"Warning: {approaching.Count} account/type combinations have 15+ unused addresses. " +
                    "Consider marking addresses as used or avoid generating more until existing addresses are used.";
            }

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get gap status for wallet {Address}", address);
            return Results.Problem(
                title: "Failed to Get Gap Status",
                detail: "An error occurred while checking gap status",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Create or retrieve a system wallet for a validator
    /// </summary>
    private static async Task<IResult> CreateOrRetrieveSystemWallet(
        [FromBody] SystemWalletRequest request,
        WalletManager walletManager,
        ILogger<Program> logger,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var validatorId = request.ValidatorId ?? "default-validator";
            var systemWalletName = $"system-wallet-{validatorId}";
            var systemTenant = "system";
            var systemOwner = $"validator:{validatorId}";

            logger.LogInformation(
                "Creating or retrieving system wallet for validator {ValidatorId}",
                validatorId);

            // Try to find existing system wallet
            var existingWallets = await walletManager.GetWalletsByOwnerAsync(
                systemOwner, systemTenant, cancellationToken);

            var existingWallet = existingWallets.FirstOrDefault(w =>
                w.Name == systemWalletName && w.Status == Wallet.Core.Domain.WalletStatus.Active);

            if (existingWallet != null)
            {
                logger.LogInformation(
                    "Found existing system wallet {Address} for validator {ValidatorId}",
                    existingWallet.Address,
                    validatorId);

                return Results.Ok(new SystemWalletResponse { Address = existingWallet.Address });
            }

            // Create new system wallet with ED25519 (fast signing)
            var (wallet, _) = await walletManager.CreateWalletAsync(
                systemWalletName,
                "ED25519",
                systemOwner,
                systemTenant,
                wordCount: 24, // Strong entropy for system wallets
                passphrase: null,
                signingModeOverride: null,
                cancellationToken);

            logger.LogInformation(
                "Created new system wallet {Address} for validator {ValidatorId}",
                wallet.Address,
                validatorId);

            return Results.Created(
                $"/api/v1/wallets/{wallet.Address}",
                new SystemWalletResponse { Address = wallet.Address });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create/retrieve system wallet");
            return Results.Problem(
                title: "System Wallet Creation Failed",
                detail: "An error occurred while creating the system wallet",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Recovers a system wallet from a BIP39 mnemonic. Used to seat the
    /// genesis-ceremony validator on a node so the Validator Service can sign
    /// system register dockets that match the embedded genesis roster.
    /// </summary>
    /// <remarks>
    /// Lookup uses the same (tenant=system, owner=validator:{id}, name=system-wallet-{id})
    /// keying as <see cref="CreateOrRetrieveSystemWallet"/>. If a system wallet for the
    /// validator already exists, the recover request is rejected with 409 Conflict
    /// — wiping a system wallet is destructive (existing rosters reference its pubkey)
    /// and should be done explicitly via reset, not silently.
    /// </remarks>
    private static async Task<IResult> RecoverSystemWallet(
        [FromBody] RecoverSystemWalletRequest request,
        WalletManager walletManager,
        ILogger<Program> logger,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ValidatorId))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["validatorId"] = ["ValidatorId is required."]
            });
        }
        if (string.IsNullOrWhiteSpace(request.Mnemonic))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["mnemonic"] = ["Mnemonic is required."]
            });
        }

        var validatorId = request.ValidatorId;
        var systemWalletName = $"system-wallet-{validatorId}";
        var systemTenant = "system";
        var systemOwner = $"validator:{validatorId}";

        try
        {
            var existing = await walletManager.GetWalletsByOwnerAsync(
                systemOwner, systemTenant, cancellationToken);
            var match = existing.FirstOrDefault(w =>
                w.Name == systemWalletName && w.Status == Wallet.Core.Domain.WalletStatus.Active);

            if (match != null)
            {
                logger.LogWarning(
                    "Refusing to recover system wallet for validator {ValidatorId} — one already exists ({Address})",
                    validatorId, match.Address);
                return Results.Conflict(new
                {
                    message = $"System wallet for validator '{validatorId}' already exists at {match.Address}. " +
                              "Recover would silently replace it; this is rejected to protect existing register rosters. " +
                              "Reset the wallet store explicitly if a re-import is needed.",
                });
            }

            // WalletManager.RecoverWalletAsync takes the project's own
            // Sorcha.Wallet.Core.Domain.ValueObjects.Mnemonic, which is
            // constructed from the space-separated phrase string.
            var mnemonic = new Mnemonic(request.Mnemonic);
            var wallet = await walletManager.RecoverWalletAsync(
                mnemonic,
                systemWalletName,
                request.Algorithm,
                systemOwner,
                systemTenant,
                passphrase: null,
                cancellationToken);

            logger.LogInformation(
                "Recovered system wallet {Address} for validator {ValidatorId} from supplied mnemonic",
                wallet.Address, validatorId);

            return Results.Created(
                $"/api/v1/wallets/{wallet.Address}",
                new SystemWalletResponse { Address = wallet.Address });
        }
        catch (FormatException ex)
        {
            logger.LogWarning(ex, "Invalid mnemonic supplied for system wallet recovery");
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["mnemonic"] = [$"Invalid BIP39 mnemonic: {ex.Message}"]
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to recover system wallet for validator {ValidatorId}", validatorId);
            return Results.Problem(
                title: "System Wallet Recovery Failed",
                detail: "An error occurred while recovering the system wallet",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Verify a cryptographic signature (service-to-service endpoint)
    /// </summary>
    private static async Task<IResult> VerifySignature(
        [FromBody] VerifySignatureRequest request,
        ICryptoModule cryptoModule,
        ILogger<Program> logger,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.PublicKey) ||
                string.IsNullOrWhiteSpace(request.Data) ||
                string.IsNullOrWhiteSpace(request.Signature) ||
                string.IsNullOrWhiteSpace(request.Algorithm))
            {
                return Results.BadRequest(new ProblemDetails
                {
                    Title = "Invalid Request",
                    Detail = "publicKey, data, signature, and algorithm are all required",
                    Status = StatusCodes.Status400BadRequest
                });
            }

            var publicKeyBytes = Convert.FromBase64String(request.PublicKey);
            var signatureBytes = Convert.FromBase64String(request.Signature);

            // Hash the data (UTF-8 string → SHA-256) to match how sign works with isPreHashed=false
            var dataBytes = System.Text.Encoding.UTF8.GetBytes(request.Data);
            var dataHash = SHA256.HashData(dataBytes);

            var network = request.Algorithm.ToUpperInvariant() switch
            {
                "ED25519" => WalletNetworks.ED25519,
                "NISTP256" or "NIST-P256" or "P-256" => WalletNetworks.NISTP256,
                "RSA4096" or "RSA-4096" => WalletNetworks.RSA4096,
                _ => (WalletNetworks?)null
            };

            if (network is null)
            {
                return Results.BadRequest(new ProblemDetails
                {
                    Title = "Invalid Algorithm",
                    Detail = $"Unsupported algorithm: {request.Algorithm}",
                    Status = StatusCodes.Status400BadRequest
                });
            }

            var status = await cryptoModule.VerifyAsync(
                signatureBytes, dataHash, (byte)network.Value, publicKeyBytes, cancellationToken);

            return Results.Ok(new { isValid = status == CryptoStatus.Success });
        }
        catch (FormatException ex)
        {
            logger.LogWarning(ex, "Invalid base64 in verify request");
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid Request",
                Detail = "publicKey and signature must be valid base64",
                Status = StatusCodes.Status400BadRequest
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Signature verification failed");
            return Results.Problem(
                title: "Verification Failed",
                detail: "An error occurred while verifying the signature",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    // Helper methods for authentication/authorization
    //
    // Identity model on a human JWT (Tenant Service TokenService.cs):
    //   sub                = UserIdentity.Id — the user-IN-this-org row (per-org, can change
    //                        if the user joins another org or their org-scoped record is
    //                        regenerated).
    //   platform_user_id   = PlatformUser.Id — the cross-org persistent identity.
    //
    // For admin tokens these two are equal by setup convention; for consumer-tier citizens
    // they differ. The wallet is owned by the PERSON, not their org-scoped row, so prefer
    // platform_user_id when it's present and fall back to NameIdentifier for service /
    // recovery tokens that don't carry it. New citizen wallets therefore land with
    // Owner=platform_user_id, aligning Wallets.Owner with CitizenHolderIndex.PlatformUserId
    // and CitizenCredentialEventLog.PlatformUserId — the keying inconsistency that drove
    // the manual SQL surgery on n1 the day after PR #875 deployed.
    //
    // Read paths that look up wallets by NameIdentifier need to be aware of both eras
    // (legacy Owner=sub vs new Owner=platform_user_id) — see
    // CitizenWalletEndpoints.ResolveCitizenContextAsync for the read-tolerant equivalent.
    private static string? GetCurrentUser(HttpContext context)
    {
        return context.User.FindFirstValue("platform_user_id")
            ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier);
    }

    private static async Task<IResult> EncapsulateKey(
        string address,
        [FromBody] EncapsulateRequest request,
        ICryptoModule cryptoModule,
        ILogger<Program> logger,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrEmpty(request.RecipientPublicKey))
                return Results.BadRequest(new ProblemDetails
                {
                    Title = "Invalid Request",
                    Detail = "recipientPublicKey is required",
                    Status = StatusCodes.Status400BadRequest
                });

            var publicKeyBytes = Convert.FromBase64String(request.RecipientPublicKey);

            var pqcProvider = new Sorcha.Cryptography.Core.PqcEncapsulationProvider();
            var result = await pqcProvider.EncryptWithKemAsync(
                Convert.FromBase64String(request.Plaintext ?? ""),
                publicKeyBytes,
                cancellationToken);

            if (!result.IsSuccess)
                return Results.BadRequest(new ProblemDetails
                {
                    Title = "Encapsulation Failed",
                    Detail = result.ErrorMessage,
                    Status = StatusCodes.Status400BadRequest
                });

            return Results.Ok(new
            {
                ciphertext = Convert.ToBase64String(result.Value!)
            });
        }
        catch (FormatException)
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid Request",
                Detail = "recipientPublicKey and plaintext must be valid base64",
                Status = StatusCodes.Status400BadRequest
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Encapsulation failed for wallet {Address}", address);
            return Results.Problem(
                title: "Encapsulation Failed",
                detail: "An error occurred during key encapsulation",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<IResult> DecapsulateKey(
        string address,
        [FromBody] DecapsulateRequest request,
        WalletManager walletManager,
        HttpContext context,
        ILogger<Program> logger,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // SEC-CRITICAL: Verify caller owns the wallet before decapsulating.
            var isService = context.User.Claims.Any(c => c.Type == "token_type" && c.Value == "service");
            if (!isService)
            {
                var currentUser = GetCurrentUser(context);
                if (currentUser is null)
                    return Results.Unauthorized();

                var wallet = await walletManager.GetWalletAsync(address, cancellationToken);
                if (wallet is null)
                    return Results.NotFound();

                if (wallet.Owner != currentUser)
                {
                    logger.LogWarning(
                        "SEC-AUDIT: User {User} attempted to decapsulate with wallet {Wallet} owned by {Owner}",
                        currentUser, address, wallet.Owner);
                    return Results.Forbid();
                }
            }

            if (string.IsNullOrEmpty(request.Ciphertext))
                return Results.BadRequest(new ProblemDetails
                {
                    Title = "Invalid Request",
                    Detail = "ciphertext is required",
                    Status = StatusCodes.Status400BadRequest
                });

            // Use the existing wallet decryption infrastructure (handles key access, storage)
            var ciphertextBytes = Convert.FromBase64String(request.Ciphertext);
            var plaintext = await walletManager.DecryptPayloadAsync(address, ciphertextBytes, cancellationToken);

            return Results.Ok(new
            {
                plaintext = Convert.ToBase64String(plaintext)
            });
        }
        catch (FormatException)
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid Request",
                Detail = "ciphertext must be valid base64",
                Status = StatusCodes.Status400BadRequest
            });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
        {
            return Results.NotFound(new ProblemDetails
            {
                Title = "Wallet Not Found",
                Detail = $"No wallet found with address {address}",
                Status = StatusCodes.Status404NotFound
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Decapsulation failed for wallet {Address}", address);
            return Results.Problem(
                title: "Decapsulation Failed",
                detail: "An error occurred during key decapsulation",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static string GetCurrentTenant(HttpContext context)
    {
        return context.User.FindFirstValue("tenant") ?? "default";
    }

    // ========================
    // Feature 060: Recovery endpoints
    // ========================

    /// <summary>
    /// POST /api/v1/wallets/recover/passkey — passkey-bound wallet recovery.
    /// SECURITY: WebAuthn assertion verification is not yet implemented.
    /// This endpoint is gated behind the "WalletRecovery" feature flag.
    /// </summary>
    private static async Task<IResult> RecoverViaPasskey(
        RecoverPasskeyRequest request,
        Services.Interfaces.IPasskeyRecoveryService recoveryService,
        IConfiguration configuration,
        HttpContext context,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        // Feature gate: recovery endpoints are disabled until WebAuthn assertion verification is implemented
        if (!configuration.GetValue<bool>("Features:WalletRecoveryEnabled"))
            return TypedResults.Problem("Wallet recovery is not yet enabled. WebAuthn assertion verification pending.",
                statusCode: StatusCodes.Status501NotImplemented);

        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var tenantId = GetCurrentTenant(context);

        if (string.IsNullOrEmpty(userId))
            return TypedResults.Unauthorized();

        try
        {
            var ipAddress = context.Connection.RemoteIpAddress?.ToString();
            var result = await recoveryService.RecoverAsync(
                userId, tenantId, request.PasskeyCredentialId, ipAddress, cancellationToken);

            if (result.WalletsRecovered == 0)
                return TypedResults.NotFound(new { error = "No recoverable wallets found for this passkey" });

            return TypedResults.Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Passkey recovery failed for user {UserId}", userId);
            return TypedResults.Problem("Recovery failed", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// POST /api/v1/wallets/recover/org — org admin wallet recovery.
    /// SECURITY: Org recovery key signature verification is not yet implemented.
    /// This endpoint is gated behind the "WalletRecovery" feature flag.
    /// </summary>
    private static async Task<IResult> RecoverViaOrg(
        RecoverOrgRequest request,
        Services.Interfaces.IOrgRecoveryService recoveryService,
        IConfiguration configuration,
        HttpContext context,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        // Feature gate: recovery endpoints are disabled until signature verification is implemented
        if (!configuration.GetValue<bool>("Features:WalletRecoveryEnabled"))
            return TypedResults.Problem("Wallet recovery is not yet enabled. Signature verification pending.",
                statusCode: StatusCodes.Status501NotImplemented);

        var adminUserId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var tenantId = GetCurrentTenant(context);

        if (string.IsNullOrEmpty(adminUserId))
            return TypedResults.Unauthorized();

        // Verify admin role
        if (!context.User.IsInRole("Administrator"))
            return TypedResults.Forbid();

        try
        {
            var ipAddress = context.Connection.RemoteIpAddress?.ToString();
            var result = await recoveryService.RecoverAsync(
                adminUserId, request.UserId, tenantId,
                request.OrgRecoveryKeySignature,
                request.SkipDelegationRevocation,
                ipAddress, cancellationToken);

            if (result.WalletsRecovered == 0)
                return TypedResults.NotFound(new { error = "No recoverable wallets found for target user" });

            return TypedResults.Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Org recovery failed for user {TargetUserId} by admin {AdminUserId}",
                request.UserId, adminUserId);
            return TypedResults.Problem("Recovery failed", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Uniform answer for endpoints whose feature genuinely requires the relational store, when
    /// the host is running on the in-memory storage path (no Postgres connection string). Returns
    /// 503 rather than throwing: the capability is unavailable in this configuration, which is a
    /// deployment fact, not a request error. Production/Staging already fail fast on an in-memory
    /// <c>IWalletRepository</c> (Pattern #13), so this is only reachable in dev/test.
    /// </summary>
    private static IResult RelationalStoreRequired(string capability) =>
        Results.Json(
            new ProblemDetails
            {
                Title = "Relational store required",
                Detail = $"{capability} requires the Postgres-backed wallet store, but this host is "
                       + "running on the in-memory storage path. Configure "
                       + "ConnectionStrings:Wallet:Postgres (or ConnectionStrings:Sorcha:Postgres).",
                Status = StatusCodes.Status503ServiceUnavailable
            },
            statusCode: StatusCodes.Status503ServiceUnavailable);

    /// <summary>
    /// POST /api/v1/wallets/recover/delegations/preserve — selective delegation preservation.
    /// </summary>
    private static async Task<IResult> PreserveDelegations(
        PreserveDelegationsRequest request,
        // See SignTransaction: nullable so the host still starts without Postgres.
        [FromServices] Sorcha.Wallet.Core.Data.WalletDbContext? dbContext,
        Sorcha.Wallet.Core.Services.Interfaces.IDelegationService delegationService,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return TypedResults.Unauthorized();

        if (dbContext is null)
            return RelationalStoreRequired("Selective delegation preservation");

        var preserved = 0;
        foreach (var delegationId in request.DelegationIds)
        {
            var delegation = await dbContext.WalletAccess.FindAsync([delegationId], cancellationToken);
            if (delegation is null) continue;

            // Verify the wallet belongs to this user
            var wallet = await dbContext.Wallets.FindAsync([delegation.ParentWalletAddress], cancellationToken);
            if (wallet?.Owner != userId) continue;

            // Re-grant the delegation
            await delegationService.GrantAccessAsync(
                delegation.ParentWalletAddress,
                delegation.Subject,
                delegation.AccessRight,
                userId,
                $"Preserved during recovery (original: {delegation.Reason})",
                delegation.ExpiresAt,
                cancellationToken);
            preserved++;
        }

        return TypedResults.Ok(new PreserveDelegationsResult { Preserved = preserved });
    }

    /// <summary>
    /// GET /api/v1/wallets/recovery-status — check recovery capabilities.
    /// </summary>
    private static async Task<IResult> GetRecoveryStatus(
        // See SignTransaction: nullable so the host still starts without Postgres.
        [FromServices] Sorcha.Wallet.Core.Data.WalletDbContext? dbContext,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return TypedResults.Unauthorized();

        if (dbContext is null)
            return RelationalStoreRequired("Recovery status");

        var wallets = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .ToListAsync(dbContext.Wallets.Where(w => w.Owner == userId), cancellationToken);

        var withRecovery = wallets.Count(w => w.RecoveryEnabled);

        var hasPasskeyWrap = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .AnyAsync(dbContext.RecoveryKeyWraps
                .Where(r => r.RecoveryPath == Sorcha.Wallet.Core.Domain.RecoveryPathType.Passkey
                    && r.RevokedAt == null
                    && wallets.Select(w => w.Address).Contains(r.WalletAddress)),
                cancellationToken);

        var hasOrgWrap = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .AnyAsync(dbContext.RecoveryKeyWraps
                .Where(r => r.RecoveryPath == Sorcha.Wallet.Core.Domain.RecoveryPathType.OrgManaged
                    && r.RevokedAt == null
                    && wallets.Select(w => w.Address).Contains(r.WalletAddress)),
                cancellationToken);

        return TypedResults.Ok(new RecoveryStatusResponse
        {
            PasskeyRecoveryAvailable = hasPasskeyWrap,
            OrgRecoveryAvailable = hasOrgWrap,
            WalletsWithRecovery = withRecovery,
            WalletsWithoutRecovery = wallets.Count - withRecovery
        });
    }
}

/// <summary>
/// Request to selectively preserve delegations after recovery.
/// </summary>
public class PreserveDelegationsRequest
{
    /// <summary>Wallet address to modify.</summary>
    public required string WalletAddress { get; set; }

    /// <summary>WalletAccess IDs to restore.</summary>
    public required Guid[] DelegationIds { get; set; }
}

/// <summary>
/// Result of delegation preservation.
/// </summary>
public class PreserveDelegationsResult
{
    /// <summary>Number of delegations re-granted.</summary>
    public int Preserved { get; set; }
}
