// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Cryptography.Utilities;
using Sorcha.Register.Core.Storage;
using Sorcha.Register.Models;
using Sorcha.Register.Service.Verification;
using Sorcha.Verification.Abstractions;
using Sorcha.Register.Models.Enums;
using Sorcha.Validator.Core;

namespace Sorcha.Register.Service.Endpoints;

/// <summary>
/// Minimal API endpoints for Merkle inclusion proofs, transaction revocation,
/// transaction lifecycle status, and offline verification bundles.
/// Implements tasks T027, T028, T034, T035, T036, T041, T042.
/// </summary>
public static class VerificationEndpoints
{
    /// <summary>
    /// Maps all verification-related endpoints to the application.
    /// </summary>
    public static void MapVerificationEndpoints(this WebApplication app)
    {
        MapInclusionProofEndpoints(app);
        MapRevocationEndpoints(app);
        MapVerificationBundleEndpoints(app);
        MapCredentialAnchorEndpoints(app);
    }

    // ===========================
    // T027 + T028: Inclusion Proof Endpoints
    // ===========================

    private static void MapInclusionProofEndpoints(WebApplication app)
    {
        // T027: GET /api/registers/{registerId}/transactions/{txId}/inclusion-proof
        app.MapGet("/api/registers/{registerId}/transactions/{txId}/inclusion-proof", async (
            IRegisterRepository repository,
            IHashProvider hashProvider,
            string registerId,
            string txId,
            CancellationToken cancellationToken) =>
        {
            // Verify the transaction exists
            var transaction = await repository.GetTransactionAsync(registerId, txId, cancellationToken);
            if (transaction is null)
            {
                return Results.NotFound(new { error = $"Transaction '{txId}' not found in register '{registerId}'" });
            }

            // Transaction must be sealed in a docket
            if (transaction.DocketNumber is null)
            {
                return Results.Conflict(new { error = "Transaction has not been sealed in a docket yet" });
            }

            // Get the docket containing this transaction
            var docket = await repository.GetDocketAsync(registerId, transaction.DocketNumber.Value, cancellationToken);
            if (docket is null)
            {
                return Results.NotFound(new { error = $"DocketHeader {transaction.DocketNumber} not found" });
            }

            // One leaf rule, one comparison — see DocketMerkleCommitment. This block used to build
            // the leaves inline, in whatever order the repository returned rows, and never consulted
            // the sealed root at all (#1372).
            var built = await BuildInclusionProofAsync(
                repository, hashProvider, registerId, docket, txId, cancellationToken);

            if (built is null)
            {
                return Results.Problem(
                    title: "Data integrity error",
                    detail: "DocketHeader has no transactions, or the transaction is not one of its leaves",
                    statusCode: 500);
            }

            // #1372 — fail LOUD rather than hand back a proof against a root the ledger never sealed.
            // A recomputation over altered stored data is internally self-consistent, so the proof
            // would verify perfectly against its own root and prove nothing about the ledger.
            if (built.Seal.Status == VerificationStatus.Failed)
            {
                return Results.Problem(
                    title: "Docket integrity failure",
                    detail: "This docket's stored transactions do not reproduce the Merkle root its proposing "
                          + "validator sealed, so no inclusion proof can be anchored to it. "
                          + $"Sealed: {built.Seal.SealedRoot} - recomputed: {built.Seal.RecomputedRoot}",
                    statusCode: StatusCodes.Status409Conflict);
            }

            return Results.Ok(built.Proof);
        })
        .WithName("GetMerkleInclusionProof")
        .WithSummary("Generate Merkle inclusion proof for a transaction")
        .WithDescription("Generates a compact Merkle inclusion proof that a transaction is a leaf in its docket's Merkle tree. " +
            "Returns the sibling hashes from leaf to root (log2(n) steps) for offline verification.")
        .WithTags("Verification")
        .RequireAuthorization("CanReadTransactions")
        .Produces<MerkleInclusionProof>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status401Unauthorized);

        // T028: POST /api/registers/{registerId}/inclusion-proofs/verify
        app.MapPost("/api/registers/{registerId}/inclusion-proofs/verify", async (
            IRegisterRepository repository,
            IHashProvider hashProvider,
            string registerId,
            VerifyMerkleInclusionProofRequest request,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.TransactionHash))
                return Results.BadRequest(new { error = "TransactionHash is required" });
            if (string.IsNullOrWhiteSpace(request.MerkleRoot))
                return Results.BadRequest(new { error = "MerkleRoot is required" });
            if (request.ProofPath is null || request.ProofPath.Count == 0)
                return Results.BadRequest(new { error = "ProofPath is required and must not be empty" });

            var validator = new InclusionProofValidator(hashProvider);
            var proofSteps = request.ProofPath.Select(step => new MerkleProofStep
            {
                Hash = step.Hash,
                Position = step.Position
            }).ToList().AsReadOnly();

            var result = validator.Verify(request.TransactionHash, request.MerkleRoot, proofSteps);

            // #1372 — isValid on its own is arithmetic: a proof path folds to SOME root, and a root
            // recomputed over altered data is internally self-consistent, so the proof verifies
            // perfectly while proving nothing about this register. ledgerAnchored is the missing
            // half, and it is a TRI-STATE: null means the caller did not name a docket, so the
            // question was never asked. Never report a check that did not run as a pass.
            string? anchored = null;
            string? anchorReason =
                "no docketNumber supplied - this response is about the proof path only, not about the ledger";

            if (result.IsValid && request.DocketNumber is { } docketNumber)
            {
                // A caller's negative number is a caller's mistake, not a server fault. `checked`
                // here would throw OverflowException and surface as a 500 (#1476's exact shape).
                var docket = docketNumber < 0
                    ? null
                    : await repository.GetDocketAsync(registerId, (ulong)docketNumber, cancellationToken);

                if (docket is null)
                {
                    anchorReason = $"docket {docketNumber} is not held on register '{registerId}'";
                }
                else if (string.IsNullOrWhiteSpace(docket.MerkleRoot))
                {
                    anchorReason =
                        "that docket was sealed before the platform kept the sealed Merkle root, so there is "
                        + "no commitment to compare against";
                }
                else
                {
                    var matches = string.Equals(
                        docket.MerkleRoot, result.ComputedRoot, StringComparison.OrdinalIgnoreCase);
                    anchored = matches ? "verified" : "failed";
                    anchorReason = matches
                        ? null
                        : "the root this proof folds to is NOT the root that docket's proposing validator sealed";
                }
            }

            return Results.Ok(new
            {
                isValid = result.IsValid,
                computedRoot = result.ComputedRoot,
                ledgerAnchored = anchored,
                ledgerAnchorReason = anchorReason
            });
        })
        .WithRequestValidation()
        .WithName("VerifyMerkleInclusionProof")
        .WithSummary("Verify a Merkle inclusion proof (public)")
        .WithDescription("Verifies a standalone Merkle inclusion proof by recomputing the root from the proof path. " +
            "No authentication required — suitable for offline verification workflows. " +
            "Supply the optional docketNumber to additionally cross-check the folded root against the root this " +
            "register actually sealed; without it, ledgerAnchored is null and isValid describes the proof path alone.")
        .WithTags("Verification")
        .AllowAnonymous()
        .Produces<object>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest);
    }

    // ===========================
    // T034 + T035: Revocation Endpoints
    // ===========================

    private static void MapRevocationEndpoints(WebApplication app)
    {
        // T034: POST /api/registers/{registerId}/transactions/revoke
        app.MapPost("/api/registers/{registerId}/transactions/revoke", async (
            IRegisterRepository repository,
            IHashProvider hashProvider,
            string registerId,
            RevokeTransactionRequest request,
            CancellationToken cancellationToken) =>
        {
            // Parse the reason string to enum
            if (!Enum.TryParse<RevocationReason>(request.Reason, ignoreCase: true, out var reason))
            {
                return Results.BadRequest(new { error = $"Invalid revocation reason: '{request.Reason}'. Valid values: {string.Join(", ", Enum.GetNames<RevocationReason>())}" });
            }

            // Validate the target transaction exists
            var targetTx = await repository.GetTransactionAsync(registerId, request.OriginalTxId, cancellationToken);
            if (targetTx is null)
            {
                return Results.NotFound(new { error = $"Target transaction '{request.OriginalTxId}' not found in register '{registerId}'" });
            }

            // Check the transaction is not already revoked
            var existingRevocation = await repository.FindRevocationForTransactionAsync(
                registerId, request.OriginalTxId, cancellationToken);
            if (existingRevocation is not null)
            {
                return Results.Conflict(new
                {
                    error = "Transaction is already revoked",
                    existingRevocationTxId = existingRevocation.TxId
                });
            }

            // Build and validate the revocation payload
            var payload = new RevocationPayload
            {
                OriginalTxId = request.OriginalTxId,
                OriginalDocketNumber = (long)(targetTx.DocketNumber ?? 0),
                Reason = reason,
                SupersededByTxId = request.SupersededByTxId,
                Metadata = request.Metadata
            };

            var validator = new RevocationValidator();
            var validation = validator.ValidatePayload(payload);
            if (!validation.IsValid)
            {
                return Results.BadRequest(new
                {
                    error = "Revocation payload validation failed",
                    errors = validation.Errors,
                    errorCode = validation.ErrorCode
                });
            }

            // If reason is Superseded, validate the superseding tx exists
            if (reason == RevocationReason.Superseded && !string.IsNullOrWhiteSpace(request.SupersededByTxId))
            {
                var supersedingTx = await repository.GetTransactionAsync(
                    registerId, request.SupersededByTxId, cancellationToken);
                if (supersedingTx is null)
                {
                    return Results.BadRequest(new { error = $"Superseding transaction '{request.SupersededByTxId}' not found" });
                }
            }

            // Serialize the payload to JSON for storage
            var payloadJson = JsonSerializer.Serialize(payload);
            var payloadBytes = System.Text.Encoding.UTF8.GetBytes(payloadJson);
            var payloadHash = hashProvider.ComputeHash(payloadBytes, Sorcha.Cryptography.Enums.HashType.SHA256);

            // Build the revocation transaction
            var txIdBytes = hashProvider.ComputeHash(
                System.Text.Encoding.UTF8.GetBytes($"{registerId}:{request.OriginalTxId}:{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}"),
                Sorcha.Cryptography.Enums.HashType.SHA256);
            var revocationTxId = Convert.ToHexString(txIdBytes).ToLowerInvariant();

            var revocationTx = new TransactionModel
            {
                RegisterId = registerId,
                TxId = revocationTxId,
                PrevTxId = targetTx.TxId,
                SenderWallet = request.SignerWalletAddress ?? string.Empty,
                TimeStamp = DateTime.UtcNow,
                MetaData = new TransactionMetaData
                {
                    RegisterId = registerId,
                    TransactionType = TransactionType.Revocation,
                    TrackingData = new Dictionary<string, string>
                    {
                        ["originalTxId"] = request.OriginalTxId,
                        ["reason"] = reason.ToString()
                    }
                },
                PayloadCount = 1,
                Payloads =
                [
                    new PayloadModel
                    {
                        Data = Convert.ToBase64String(payloadBytes),
                        Hash = Convert.ToHexString(payloadHash).ToLowerInvariant(),
                        PayloadSize = (ulong)payloadBytes.Length,
                        ContentType = "application/json",
                        ContentEncoding = "base64"
                    }
                ],
                Signature = request.SignerWalletAddress ?? string.Empty // Signer address for authority traceability
            };

            // Store the revocation transaction
            var stored = await repository.InsertTransactionAsync(revocationTx, cancellationToken);

            return Results.Accepted(
                $"/api/registers/{registerId}/transactions/{stored.TxId}",
                new
                {
                    revocationTxId = stored.TxId,
                    originalTxId = request.OriginalTxId,
                    status = "submitted"
                });
        })
        .WithRequestValidation()
        .WithName("RevokeTransaction")
        .WithSummary("Submit a transaction revocation")
        .WithDescription("Creates a revocation transaction that marks an existing transaction as revoked or superseded. " +
            "The revocation is stored as a new transaction with TransactionType.Revocation and a RevocationPayload.")
        .WithTags("Revocation")
        .RequireAuthorization("CanSubmitTransactions")
        .Produces<object>(StatusCodes.Status202Accepted)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status401Unauthorized);

        // T035: GET /api/registers/{registerId}/transactions/{txId}/status
        app.MapGet("/api/registers/{registerId}/transactions/{txId}/status", async (
            IRegisterRepository repository,
            string registerId,
            string txId,
            CancellationToken cancellationToken) =>
        {
            // Verify the transaction exists
            var transaction = await repository.GetTransactionAsync(registerId, txId, cancellationToken);
            if (transaction is null)
            {
                return Results.NotFound(new { error = $"Transaction '{txId}' not found in register '{registerId}'" });
            }

            // Check for revocation
            var revocationTx = await repository.FindRevocationForTransactionAsync(
                registerId, txId, cancellationToken);

            if (revocationTx is null)
            {
                return Results.Ok(new TransactionStatusResponse
                {
                    TransactionId = txId,
                    Status = TransactionLifecycleStatus.Active
                });
            }

            // Parse the revocation payload to get details
            RevocationPayload? revocationPayload = null;
            if (revocationTx.Payloads is { Length: > 0 })
            {
                try
                {
                    var data = revocationTx.Payloads[0].Data;
                    var payloadBytes = data.Contains('+') || data.Contains('/') || data.Contains('=')
                        ? Convert.FromBase64String(data)
                        : System.Buffers.Text.Base64Url.DecodeFromChars(data);

                    revocationPayload = JsonSerializer.Deserialize<RevocationPayload>(
                        payloadBytes,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
                catch (FormatException)
                {
                    // Failed to decode base64 — return basic revocation status
                }
                catch (JsonException)
                {
                    // Failed to deserialize — return basic revocation status
                }
            }

            var status = revocationPayload?.Reason == RevocationReason.Superseded
                ? TransactionLifecycleStatus.Superseded
                : TransactionLifecycleStatus.Revoked;

            return Results.Ok(new TransactionStatusResponse
            {
                TransactionId = txId,
                Status = status,
                RevocationTxId = revocationTx.TxId,
                SupersededByTxId = revocationPayload?.SupersededByTxId,
                RevokedAt = revocationTx.TimeStamp,
                Reason = revocationPayload?.Reason
            });
        })
        .WithName("GetTransactionStatus")
        .WithSummary("Get transaction lifecycle status")
        .WithDescription("Returns the lifecycle status of a transaction (active, revoked, or superseded) " +
            "by checking for revocation transactions that reference it.")
        .WithTags("Revocation")
        .RequireAuthorization("CanReadTransactions")
        .Produces<TransactionStatusResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status401Unauthorized);
    }

    // ===========================
    // T041 + T042: Verification Bundle Endpoints
    // ===========================

    private static void MapVerificationBundleEndpoints(WebApplication app)
    {
        // T041: GET /api/registers/{registerId}/transactions/{txId}/verification-bundle
        app.MapGet("/api/registers/{registerId}/transactions/{txId}/verification-bundle", async (
            IRegisterRepository repository,
            IHashProvider hashProvider,
            string registerId,
            string txId,
            CancellationToken cancellationToken) =>
        {
            // Verify the transaction exists
            var transaction = await repository.GetTransactionAsync(registerId, txId, cancellationToken);
            if (transaction is null)
            {
                return Results.NotFound(new { error = $"Transaction '{txId}' not found in register '{registerId}'" });
            }

            // Get the receipt (transaction must be sealed)
            var receipt = await repository.GetReceiptByTxIdAsync(registerId, txId, cancellationToken);
            if (receipt is null)
            {
                return Results.Conflict(new
                {
                    error = "Transaction has not been sealed yet. A verification bundle requires a sealed receipt.",
                    txId
                });
            }

            // Build credential from transaction payload
            JsonElement credential;
            if (transaction.Payloads is { Length: > 0 })
            {
                try
                {
                    credential = JsonSerializer.Deserialize<JsonElement>(
                        JsonSerializer.Serialize(transaction.Payloads));
                }
                catch (JsonException)
                {
                    credential = JsonSerializer.Deserialize<JsonElement>("[]");
                }
            }
            else
            {
                credential = JsonSerializer.Deserialize<JsonElement>("[]");
            }

            // Get revocation status
            var revocationTx = await repository.FindRevocationForTransactionAsync(
                registerId, txId, cancellationToken);

            TransactionStatusResponse revocationStatus;
            if (revocationTx is null)
            {
                revocationStatus = new TransactionStatusResponse
                {
                    TransactionId = txId,
                    Status = TransactionLifecycleStatus.Active
                };
            }
            else
            {
                RevocationPayload? revPayload = null;
                if (revocationTx.Payloads is { Length: > 0 })
                {
                    try
                    {
                        var data = revocationTx.Payloads[0].Data;
                        var payloadBytes = data.Contains('+') || data.Contains('/') || data.Contains('=')
                            ? Convert.FromBase64String(data)
                            : System.Buffers.Text.Base64Url.DecodeFromChars(data);

                        revPayload = JsonSerializer.Deserialize<RevocationPayload>(
                            payloadBytes,
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    }
                    catch (FormatException)
                    {
                        // Failed to decode base64 — continue with basic status
                    }
                    catch (JsonException)
                    {
                        // Failed to deserialize — continue with basic status
                    }
                }

                revocationStatus = new TransactionStatusResponse
                {
                    TransactionId = txId,
                    Status = revPayload?.Reason == RevocationReason.Superseded
                        ? TransactionLifecycleStatus.Superseded
                        : TransactionLifecycleStatus.Revoked,
                    RevocationTxId = revocationTx.TxId,
                    SupersededByTxId = revPayload?.SupersededByTxId,
                    RevokedAt = revocationTx.TimeStamp,
                    Reason = revPayload?.Reason
                };
            }

            // Extract validator public keys from receipt signatures
            var validatorKeys = receipt.Signatures.Select(sig => new ValidatorKeyInfo
            {
                Address = sig.ValidatorAddress,
                PublicKey = string.Empty, // Public keys must be resolved by the verifier
                Algorithm = sig.Algorithm
            }).ToList().AsReadOnly();

            var bundle = new VerificationBundle
            {
                Version = 1,
                TransactionId = txId,
                RegisterId = registerId,
                Credential = credential,
                Receipt = receipt,
                RevocationStatus = revocationStatus,
                ExportedAt = DateTimeOffset.UtcNow,
                ValidatorPublicKeys = validatorKeys
            };

            return Results.Ok(bundle);
        })
        .WithName("GetVerificationBundle")
        .WithSummary("Export an offline verification bundle for a transaction")
        .WithDescription("Assembles a portable verification bundle containing the transaction payload, " +
            "sealed receipt with inclusion proof, and point-in-time revocation status. " +
            "Returns 404 if the transaction does not exist, or 409 if not yet sealed (no receipt).")
        .WithTags("Verification")
        .RequireAuthorization("CanReadTransactions")
        .Produces<VerificationBundle>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status401Unauthorized);

        // T042: POST /api/registers/{registerId}/verification-bundles/verify
        app.MapPost("/api/registers/{registerId}/verification-bundles/verify", (
            IHashProvider hashProvider,
            string registerId,
            VerificationBundle bundle) =>
        {
            // Create validators
            var proofValidator = new InclusionProofValidator(hashProvider);
            var receiptValidator = new ReceiptValidator(proofValidator);
            var bundleVerifier = new BundleVerifier(receiptValidator, proofValidator);

            var result = bundleVerifier.VerifyBundle(bundle, bundle.ValidatorPublicKeys);

            return Results.Ok(new
            {
                isValid = result.IsValid,
                checks = new
                {
                    credentialSignatureValid = result.Checks.CredentialSignatureValid,
                    inclusionProofValid = result.Checks.InclusionProofValid,
                    receiptSignatureValid = result.Checks.ReceiptSignatureValid,
                    revocationStatusCurrent = result.Checks.RevocationStatusCurrent
                },
                warnings = result.Warnings,
                errors = result.Errors
            });
        })
        .WithName("VerifyVerificationBundle")
        .WithSummary("Verify an offline verification bundle (public)")
        .WithDescription("Verifies all four components of a verification bundle: " +
            "credential validity, Merkle inclusion proof, receipt signature, and revocation status. " +
            "No authentication required — suitable for offline verification workflows.")
        .WithTags("Verification")
        .AllowAnonymous()
        .Produces<object>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest);
    }

    // ===========================
    // T024 (Feature 155): Public Credential Anchor Read
    // ===========================

    private static void MapCredentialAnchorEndpoints(WebApplication app)
    {
        // T024: GET /api/registers/{registerId}/credentials/{credentialId}/anchor  (anonymous)
        app.MapGet("/api/registers/{registerId}/credentials/{credentialId}/anchor", async (
            IRegisterRepository repository,
            IHashProvider hashProvider,
            string registerId,
            string credentialId,
            CancellationToken cancellationToken) =>
        {
            // Validate path params (400 on empty / malformed)
            if (string.IsNullOrWhiteSpace(registerId))
            {
                return Results.BadRequest(new { error = "registerId is required" });
            }
            if (string.IsNullOrWhiteSpace(credentialId))
            {
                return Results.BadRequest(new { error = "credentialId is required" });
            }

            // Locate the credential-issuance transaction by the credential's own id.
            // 404 (not 4xx-failure) when none matches — the open verifier renders the anchor
            // layer as "unverified", distinct from a verification failure.
            var transaction = await repository.GetCredentialIssuanceTransactionAsync(
                registerId, credentialId, cancellationToken);
            if (transaction is null)
            {
                return Results.NotFound(new
                {
                    error = $"No credential-issuance transaction for credential '{credentialId}' on register '{registerId}'"
                });
            }

            // The issuance tx must be sealed in a docket to carry an inclusion proof.
            if (transaction.DocketNumber is null)
            {
                return Results.NotFound(new
                {
                    error = "Credential-issuance transaction has not been sealed in a docket yet"
                });
            }

            var docket = await repository.GetDocketAsync(
                registerId, transaction.DocketNumber.Value, cancellationToken);
            if (docket is null)
            {
                return Results.NotFound(new { error = $"DocketHeader {transaction.DocketNumber} not found" });
            }

            var txId = transaction.TxId ?? transaction.Id ?? string.Empty;

            // Generate the Merkle inclusion proof — same logic as the authenticated
            // GET .../inclusion-proof endpoint (shared helper below).
            var anchored = await BuildInclusionProofAsync(
                repository, hashProvider, registerId, docket, txId, cancellationToken);
            if (anchored is null)
            {
                return Results.Problem(
                    title: "Data integrity error",
                    detail: "Unable to generate inclusion proof for the issuance transaction",
                    statusCode: 500);
            }

            // #1372 — same refusal as proof generation. Handing a verifier a proof whose root the
            // ledger never sealed is worse than handing it nothing: the proof verifies against its
            // own recomputed root, so the verifier reports a pass it has no basis for.
            if (anchored.Seal.Status == VerificationStatus.Failed)
            {
                return Results.Problem(
                    title: "Docket integrity failure",
                    detail: "The docket sealing this credential's issuance no longer reproduces the Merkle root "
                          + "its proposing validator sealed, so its inclusion proof cannot be anchored.",
                    statusCode: StatusCodes.Status409Conflict);
            }

            // Resolve lifecycle status (Active / Revoked / Superseded) via the revocation index.
            var status = await ResolveLifecycleStatusAsync(repository, registerId, txId, cancellationToken);

            var anchorResponse = new CredentialAnchorResponse
            {
                RegisterId = registerId,
                CredentialId = credentialId,
                TxId = txId,
                DocketNumber = checked((long)docket.Id),
                SealedAt = new DateTimeOffset(docket.TimeStamp, TimeSpan.Zero),
                Status = status.ToString(),
                InclusionProof = anchored.Proof,
                // Tri-state on purpose (#1372). "verified" means the stored contents reproduce the
                // sealed commitment; "unverified" means the check could not run — a docket sealed
                // before the platform kept the root, or a node holding only part of it. Absent this
                // field a verifier cannot tell the two apart, and would read both as verified.
                SealStatus = anchored.Seal.Wire
            };

            return Results.Ok(anchorResponse);
        })
        .WithName("GetCredentialAnchor")
        .WithSummary("Resolve a credential's issuance anchor + inclusion proof (public)")
        .WithDescription("Finds the credential-issuance transaction whose tracking metadata carries the " +
            "given credentialId and returns its transaction id, sealing docket, lifecycle status, and a " +
            "Merkle inclusion proof verifiable via POST /inclusion-proofs/verify. Anonymous — exposes only " +
            "already-public register facts. Returns 404 (not a failure) when no issuance transaction matches.")
        .WithTags("Verification")
        .AllowAnonymous()
        .Produces<CredentialAnchorResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict);
    }

    // ===========================
    // Shared helpers
    // ===========================

    /// <summary>
    /// Builds a Merkle inclusion proof for a sealed transaction, together with the sealed-versus-
    /// recomputed comparison for the docket it is anchored to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The leaves come from <see cref="DocketMerkleCommitment.BuildLeaves"/> — the single rule shared
    /// with the Feature 188 provenance Seal check — so a proof and a provenance trail can never
    /// disagree about what a docket committed to.
    /// </para>
    /// <para>
    /// The comparison is returned, not enforced, because the two callers want different things from
    /// it: proof GENERATION refuses outright on a mismatch (issue #1372 — a proof against a root the
    /// ledger never sealed proves nothing), while the credential-anchor read reports it as a field so
    /// an auditor can see a docket that no longer reproduces its commitment.
    /// </para>
    /// <para>Returns null when the docket holds no transactions or the target is not one of its leaves.</para>
    /// </remarks>
    private static async Task<AnchoredInclusionProof?> BuildInclusionProofAsync(
        IRegisterRepository repository,
        IHashProvider hashProvider,
        string registerId,
        DocketHeader docket,
        string txId,
        CancellationToken cancellationToken)
    {
        var held = (await repository.GetTransactionsByDocketAsync(
            registerId, docket.Id, cancellationToken)).ToList();
        if (held.Count == 0)
        {
            return null;
        }

        var docketHasher = new DocketHasher(hashProvider);
        var leaves = DocketMerkleCommitment.BuildLeaves(docket, held, docketHasher);

        var merkleTree = new MerkleTree(hashProvider);
        var seal = DocketMerkleCommitment.Compare(docket, leaves, merkleTree);

        if (leaves is null || leaves.Hashes.Count == 0)
        {
            // The node does not hold every transaction this docket lists, so there is no committed
            // leaf sequence to index into. Unverifiable, not tampered.
            return null;
        }

        var leafIndex = leaves.OrderedTransactions.ToList().FindIndex(tx =>
            string.Equals(tx.TxId ?? tx.Id, txId, StringComparison.OrdinalIgnoreCase));
        if (leafIndex < 0)
        {
            return null;
        }

        var proof = merkleTree.GenerateInclusionProof(leafIndex, leaves.Hashes.ToList().AsReadOnly());

        var inclusionProof = new MerkleInclusionProof
        {
            TransactionHash = proof.TransactionHash,
            DocketNumber = checked((long)docket.Id),
            MerkleRoot = proof.MerkleRoot,
            ProofPath = proof.ProofPath.Select(step => new MerkleProofStep
            {
                Hash = step.Hash,
                Position = step.Position == MerkleProofPosition.Left
                    ? ProofPosition.Left
                    : ProofPosition.Right
            }).ToList().AsReadOnly(),
            LeafIndex = proof.LeafIndex,
            TreeSize = proof.TreeSize
        };

        return new AnchoredInclusionProof(inclusionProof, seal);
    }

    /// <summary>An inclusion proof plus what the docket it anchors to says about its own integrity.</summary>
    private sealed record AnchoredInclusionProof(
        MerkleInclusionProof Proof,
        DocketMerkleCommitment.SealComparison Seal);

    /// <summary>
    /// Resolves the lifecycle status of a transaction (Active / Revoked / Superseded) by looking
    /// up any revocation transaction that targets it. Mirrors the GET .../status endpoint.
    /// </summary>
    private static async Task<TransactionLifecycleStatus> ResolveLifecycleStatusAsync(
        IRegisterRepository repository,
        string registerId,
        string txId,
        CancellationToken cancellationToken)
    {
        var revocationTx = await repository.FindRevocationForTransactionAsync(
            registerId, txId, cancellationToken);
        if (revocationTx is null)
        {
            return TransactionLifecycleStatus.Active;
        }

        RevocationPayload? revocationPayload = null;
        if (revocationTx.Payloads is { Length: > 0 })
        {
            try
            {
                var data = revocationTx.Payloads[0].Data;
                var payloadBytes = data.Contains('+') || data.Contains('/') || data.Contains('=')
                    ? Convert.FromBase64String(data)
                    : System.Buffers.Text.Base64Url.DecodeFromChars(data);

                revocationPayload = JsonSerializer.Deserialize<RevocationPayload>(
                    payloadBytes,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (FormatException)
            {
                // Fall through to Revoked
            }
            catch (JsonException)
            {
                // Fall through to Revoked
            }
        }

        return revocationPayload?.Reason == RevocationReason.Superseded
            ? TransactionLifecycleStatus.Superseded
            : TransactionLifecycleStatus.Revoked;
    }
}

// ===========================
// Request/Response DTOs
// ===========================

/// <summary>
/// Request DTO for verifying a Merkle inclusion proof.
/// </summary>
public class VerifyMerkleInclusionProofRequest
{
    /// <summary>SHA-256 hash of the transaction to verify.</summary>
    [Required(AllowEmptyStrings = false)]
    [StringLength(200)]
    public string TransactionHash { get; set; } = string.Empty;

    /// <summary>Expected Merkle root hash.</summary>
    [Required(AllowEmptyStrings = false)]
    [StringLength(200)]
    public string MerkleRoot { get; set; } = string.Empty;

    /// <summary>Sibling hashes from leaf to root.</summary>
    [Required]
    public IReadOnlyList<MerkleProofStep> ProofPath { get; set; } = [];

    /// <summary>
    /// Optional. The docket this proof claims inclusion in — echoed by
    /// <c>GET /transactions/{txId}/inclusion-proof</c> as
    /// <see cref="MerkleInclusionProof.DocketNumber"/>.
    /// </summary>
    /// <remarks>
    /// Supply it and the response's <c>ledgerAnchored</c> says whether the root you verified against
    /// is the one this register actually sealed. Omit it and the endpoint does arithmetic only
    /// (issue #1372): a proof path always folds to SOME root, so <c>isValid</c> alone is a statement
    /// about the proof, never about the ledger.
    /// </remarks>
    public long? DocketNumber { get; set; }
}

/// <summary>
/// Request DTO for revoking a transaction.
/// </summary>
public class RevokeTransactionRequest
{
    /// <summary>Transaction ID to revoke.</summary>
    [Required(AllowEmptyStrings = false)]
    [StringLength(200)]
    public string OriginalTxId { get; set; } = string.Empty;

    /// <summary>Revocation reason (Superseded, Erroneous, Compromised, Expired, Withdrawn, Regulatory).</summary>
    [Required(AllowEmptyStrings = false)]
    [StringLength(64)]
    public string Reason { get; set; } = string.Empty;

    /// <summary>Replacement transaction ID (required when reason is Superseded).</summary>
    [StringLength(200)]
    public string? SupersededByTxId { get; set; }

    /// <summary>Additional context metadata (max 10 entries).</summary>
    public Dictionary<string, string>? Metadata { get; set; }

    /// <summary>Wallet address of the signer submitting the revocation.</summary>
    [StringLength(256)]
    public string? SignerWalletAddress { get; set; }
}

/// <summary>
/// Response DTO for the public credential anchor read (Feature 155).
/// Locates a credential's issuance transaction on a register and returns its
/// F079 Merkle inclusion proof so an open verifier can cross-check the register anchor.
/// </summary>
public class CredentialAnchorResponse
{
    /// <summary>The register the credential is anchored on.</summary>
    public string RegisterId { get; set; } = string.Empty;

    /// <summary>The credential's own identifier (jti), echoed back.</summary>
    public string CredentialId { get; set; } = string.Empty;

    /// <summary>The issuance transaction id.</summary>
    public string TxId { get; set; } = string.Empty;

    /// <summary>The number of the docket that sealed the issuance transaction.</summary>
    public long DocketNumber { get; set; }

    /// <summary>The time the sealing docket was created (UTC).</summary>
    public DateTimeOffset SealedAt { get; set; }

    /// <summary>Transaction lifecycle status: Active, Revoked, or Superseded.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>The F079 Merkle inclusion proof for the issuance transaction.</summary>
    public MerkleInclusionProof InclusionProof { get; set; } = null!;

    /// <summary>
    /// Whether the sealing docket's stored contents still reproduce the Merkle root its proposing
    /// validator sealed: <c>"verified"</c>, <c>"unverified"</c>, or <c>"failed"</c> (issue #1372).
    /// </summary>
    /// <remarks>
    /// A <c>failed</c> comparison is refused with 409 rather than returned, so in practice this is
    /// <c>verified</c> or <c>unverified</c>. The distinction still matters and must not be collapsed:
    /// <c>unverified</c> means the check could not run — a docket sealed before the platform kept the
    /// root, or a node that holds only part of the docket — and a verifier reading that as
    /// <c>verified</c> would be manufacturing confidence, which is the whole failure mode Feature 188
    /// exists to prevent.
    /// </remarks>
    public string SealStatus { get; set; } = string.Empty;
}
