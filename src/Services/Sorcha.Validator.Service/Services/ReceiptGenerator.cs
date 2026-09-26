// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Sorcha.Cryptography.Utilities;
using Sorcha.Register.Models;
using Sorcha.ServiceClients.Wallet;
using Sorcha.Validator.Service.Configuration;
using Sorcha.Validator.Service.Models;
using Sorcha.Validator.Core;
using Sorcha.Wallet.Contracts.Constants;

namespace Sorcha.Validator.Service.Services;

/// <summary>
/// Generates signed transaction receipts with Merkle inclusion proofs.
/// Called by <see cref="ReceiptPublisher"/> after every docket write (#1704).
/// </summary>
public class ReceiptGenerator : IReceiptGenerator
{
    private readonly MerkleTree _merkleTree;
    private readonly DocketHasher _docketHasher;
    private readonly IWalletServiceClient _walletClient;
    private readonly ValidatorConfiguration _config;
    private readonly ILogger<ReceiptGenerator> _logger;

    public ReceiptGenerator(
        MerkleTree merkleTree,
        DocketHasher docketHasher,
        IWalletServiceClient walletClient,
        IOptions<ValidatorConfiguration> config,
        ILogger<ReceiptGenerator> logger)
    {
        _merkleTree = merkleTree ?? throw new ArgumentNullException(nameof(merkleTree));
        _docketHasher = docketHasher ?? throw new ArgumentNullException(nameof(docketHasher));
        _walletClient = walletClient ?? throw new ArgumentNullException(nameof(walletClient));
        _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<TransactionReceipt[]> GenerateReceiptsForDocketAsync(
        Sorcha.Validator.Service.Models.Docket docket,
        CancellationToken ct = default)
    {
        if (docket.Transactions.Count == 0)
            return [];

        _logger.LogDebug(
            "Generating {Count} receipts for docket {DocketNumber} on register {RegisterId}",
            docket.TransactionCount, docket.DocketNumber, docket.RegisterId);

        // Step 1: Compute transaction hashes (same as used for Merkle root during sealing)
        var txHashes = docket.Transactions
            .Select(tx => _docketHasher.ComputeTransactionHash(
                tx.TransactionId, tx.PayloadHash, tx.CreatedAt))
            .ToList();

        // Step 2: Generate all inclusion proofs in one pass
        var proofs = _merkleTree.GenerateAllProofs(txHashes);

        // Step 3: Build and sign each receipt
        var receipts = new TransactionReceipt[docket.Transactions.Count];
        var sealedAt = docket.ConsensusAchievedAt ?? DateTimeOffset.UtcNow;

        for (int i = 0; i < docket.Transactions.Count; i++)
        {
            var tx = docket.Transactions[i];
            var proof = proofs[i];

            // Convert Cryptography proof to Register.Models proof
            var inclusionProof = new MerkleInclusionProof
            {
                TransactionHash = proof.TransactionHash,
                DocketNumber = docket.DocketNumber,
                MerkleRoot = proof.MerkleRoot,
                ProofPath = proof.ProofPath.Select(s => new MerkleProofStep
                {
                    Hash = s.Hash,
                    Position = s.Position == MerkleProofPosition.Left
                        ? ProofPosition.Left
                        : ProofPosition.Right
                }).ToList(),
                LeafIndex = proof.LeafIndex,
                TreeSize = proof.TreeSize
            };

            // Build receipt (without signature yet)
            var receipt = new TransactionReceipt
            {
                ReceiptId = string.Empty, // Set after signing
                TransactionId = tx.TransactionId,
                RegisterId = docket.RegisterId,
                DocketNumber = docket.DocketNumber,
                MerkleRoot = docket.MerkleRoot,
                InclusionProof = inclusionProof,
                Signatures = [], // Set after signing
                SealedAt = sealedAt
            };

            // Sign the canonical receipt data with the DOCKET-SIGNING key (#1704) — the key the
            // register's validator roster publishes, so a verifier can resolve it from the roster.
            // This used to sign with the wallet's default key, which is on no roster, so a receipt
            // could never be verified against anything the register states. The bytes are signed as
            // given (isPreHashed), matching ReceiptValidator, which verifies over the raw signing data.
            var signingData = ReceiptValidator.BuildReceiptSigningData(receipt);

            try
            {
                var signingWallet = await ResolveSigningWalletAsync(ct);
                var signResult = await _walletClient.SignTransactionAsync(
                    signingWallet, signingData,
                    derivationPath: SorchaDerivationPaths.DocketSigning,
                    isPreHashed: true,
                    ct);

                var validatorSig = new ReceiptSignature
                {
                    ValidatorAddress = signingWallet,
                    SignatureValue = signResult.Signature,
                    Algorithm = signResult.Algorithm,
                    SignedAt = DateTimeOffset.UtcNow
                };

                // Compute deterministic receipt ID
                var receiptId = ComputeReceiptId(tx.TransactionId, docket.DocketNumber, docket.MerkleRoot);

                receipts[i] = receipt with
                {
                    ReceiptId = receiptId,
                    Signatures = [validatorSig]
                };
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex,
                    "Failed to sign receipt for transaction {TxId} in docket {DocketNumber}",
                    tx.TransactionId, docket.DocketNumber);

                // Still create the receipt without signature — it can be signed later
                var receiptId = ComputeReceiptId(tx.TransactionId, docket.DocketNumber, docket.MerkleRoot);
                receipts[i] = receipt with { ReceiptId = receiptId };
            }
        }

        _logger.LogInformation(
            "Generated {Count} receipts for docket {DocketNumber}",
            receipts.Length, docket.DocketNumber);

        return receipts;
    }

    /// <summary>
    /// The validator's system wallet — the one whose <c>sorcha:docket-signing</c> key is on the
    /// roster. Resolved the same way <c>DocketBuilder</c> resolves it, because the genesis path
    /// may generate receipts before any docket build has populated the configured address.
    /// </summary>
    private async Task<string> ResolveSigningWalletAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_config.SystemWalletAddress))
        {
            _config.SystemWalletAddress = await _walletClient.CreateOrRetrieveSystemWalletAsync(_config.ValidatorId, ct);
        }

        return _config.SystemWalletAddress;
    }

    /// <summary>
    /// Computes a deterministic receipt ID from receipt content.
    /// </summary>
    private static string ComputeReceiptId(string txId, long docketNumber, string merkleRoot)
    {
        var input = $"{txId}|{docketNumber}|{merkleRoot}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
