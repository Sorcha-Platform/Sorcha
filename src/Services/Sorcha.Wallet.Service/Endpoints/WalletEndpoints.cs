// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using Sorcha.Cryptography.Enums;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Cryptography.Models;
using Sorcha.Cryptography.Utilities;
using Sorcha.Wallet.Core.Domain;
using Sorcha.Wallet.Core.Domain.Entities;
using Sorcha.Wallet.Core.Domain.ValueObjects;
using Sorcha.Wallet.Core.Services.Implementation;
using Sorcha.ServiceClients.Wallet.Models;
using Sorcha.Wallet.Service.Mappers;
using Sorcha.Wallet.Service.Models;

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
            .AllowAnonymous() // System wallets are service-to-service, use service auth
            .Produces<SystemWalletResponse>(StatusCodes.Status200OK)
            .Produces<SystemWalletResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        // POST /api/v1/wallets - Create new wallet
        walletGroup.MapPost("/", CreateWallet)
            .WithName("CreateWallet")
            .WithSummary("Create a new wallet")
            .WithDescription("Creates a new HD wallet with the specified algorithm and returns the mnemonic phrase for backup")
            .Produces<CreateWalletResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized);

        // POST /api/v1/wallets/recover - Recover wallet from mnemonic
        walletGroup.MapPost("/recover", RecoverWallet)
            .WithName("RecoverWallet")
            .WithSummary("Recover a wallet from mnemonic phrase")
            .WithDescription("Recovers an existing wallet from a BIP39 mnemonic phrase")
            .Produces<WalletDto>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status409Conflict);

        // GET /api/v1/wallets - List wallets for current user
        walletGroup.MapGet("/", ListWallets)
            .WithName("ListWallets")
            .WithSummary("List wallets for current user")
            .WithDescription("Retrieve all wallets owned by the current user in the current tenant")
            .Produces<IEnumerable<WalletDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

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
        walletGroup.MapPatch("/{address}", UpdateWallet)
            .WithName("UpdateWallet")
            .WithSummary("Update wallet metadata")
            .WithDescription("Update wallet name and tags")
            .Produces<WalletDto>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        // DELETE /api/v1/wallets/{address} - Delete wallet (soft delete)
        walletGroup.MapDelete("/{address}", DeleteWallet)
            .WithName("DeleteWallet")
            .WithSummary("Delete wallet")
            .WithDescription("Soft delete a wallet (can be recovered by support)")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        // POST /api/v1/wallets/{address}/sign - Sign transaction
        walletGroup.MapPost("/{address}/sign", SignTransaction)
            .WithName("SignTransaction")
            .WithSummary("Sign a transaction")
            .WithDescription("Sign transaction data with the wallet's private key")
            .Produces<SignTransactionResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

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

            logger.LogInformation("Creating wallet for user {Owner} in tenant {Tenant}, Hybrid={Hybrid}",
                owner, tenant, request.EnableHybrid);

            var (wallet, mnemonic) = await walletManager.CreateWalletAsync(
                request.Name,
                request.Algorithm,
                owner,
                tenant,
                request.WordCount,
                request.Passphrase,
                cancellationToken);

            var response = new CreateWalletResponse
            {
                Wallet = wallet.ToDto(),
                MnemonicWords = mnemonic.Phrase.Split(' ')
            };

            // Generate PQC key pair and ws2 address for hybrid wallets
            if (request.EnableHybrid && !string.IsNullOrEmpty(request.PqcAlgorithm))
            {
                var pqcNetwork = AlgorithmMapper.ParseAlgorithm(request.PqcAlgorithm);
                var pqcKeyResult = await cryptoModule.GenerateKeySetAsync(pqcNetwork, cancellationToken: cancellationToken);
                if (pqcKeyResult.IsSuccess)
                {
                    var pqcAddress = walletUtilities.PublicKeyToWallet(pqcKeyResult.Value.PublicKey.Key!, (byte)pqcNetwork);
                    response.PqcWalletAddress = pqcAddress;
                    response.PqcAlgorithm = request.PqcAlgorithm;
                    logger.LogInformation("Hybrid wallet created with PQC address {PqcAddress}", pqcAddress);
                }
                else
                {
                    logger.LogWarning("PQC key generation failed: {Error}, proceeding with classical-only wallet",
                        pqcKeyResult.ErrorMessage);
                }
            }

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
        ILogger<Program> logger,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await walletManager.DeleteWalletAsync(address, cancellationToken);
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
        ILogger<Program> logger,
        CancellationToken cancellationToken = default)
    {
        try
        {
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
        ILogger<Program> logger,
        CancellationToken cancellationToken = default)
    {
        try
        {
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
    private static string? GetCurrentUser(HttpContext context)
    {
        return context.User.FindFirstValue(ClaimTypes.NameIdentifier);
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
        ILogger<Program> logger,
        CancellationToken cancellationToken = default)
    {
        try
        {
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

}
