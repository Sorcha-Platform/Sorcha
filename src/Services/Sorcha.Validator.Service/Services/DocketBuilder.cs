// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Options;
using Sorcha.Validator.Service.Configuration;
using Sorcha.Validator.Service.Models;
using Sorcha.Validator.Service.Services.Interfaces;
using Sorcha.ServiceClients.Wallet;
using Sorcha.ServiceClients.Register;
using Sorcha.Cryptography.Utilities;

namespace Sorcha.Validator.Service.Services;

/// <summary>
/// Builds dockets from pending transactions with hybrid triggering
/// </summary>
public class DocketBuilder : IDocketBuilder
{
    private readonly IVerifiedTransactionQueue _verifiedQueue;
    private readonly IRegisterServiceClient _registerClient;
    private readonly IWalletServiceClient _walletClient;
    private readonly IGenesisManager _genesisManager;
    private readonly MerkleTree _merkleTree;
    private readonly DocketHasher _docketHasher;
    private readonly ValidatorConfiguration _validatorConfig;
    private readonly DocketBuildConfiguration _buildConfig;
    private readonly ILogger<DocketBuilder> _logger;

    public DocketBuilder(
        IVerifiedTransactionQueue verifiedQueue,
        IRegisterServiceClient registerClient,
        IWalletServiceClient walletClient,
        IGenesisManager genesisManager,
        MerkleTree merkleTree,
        DocketHasher docketHasher,
        IOptions<ValidatorConfiguration> validatorConfig,
        IOptions<DocketBuildConfiguration> buildConfig,
        ILogger<DocketBuilder> logger)
    {
        _verifiedQueue = verifiedQueue ?? throw new ArgumentNullException(nameof(verifiedQueue));
        _registerClient = registerClient ?? throw new ArgumentNullException(nameof(registerClient));
        _walletClient = walletClient ?? throw new ArgumentNullException(nameof(walletClient));
        _genesisManager = genesisManager ?? throw new ArgumentNullException(nameof(genesisManager));
        _merkleTree = merkleTree ?? throw new ArgumentNullException(nameof(merkleTree));
        _docketHasher = docketHasher ?? throw new ArgumentNullException(nameof(docketHasher));
        _validatorConfig = validatorConfig?.Value ?? throw new ArgumentNullException(nameof(validatorConfig));
        _buildConfig = buildConfig?.Value ?? throw new ArgumentNullException(nameof(buildConfig));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Builds a docket from pending transactions
    /// </summary>
    public async Task<Docket?> BuildDocketAsync(
        string registerId,
        bool forceBuild = false,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Building docket for register {RegisterId} (forced: {ForceBuild})",
            registerId, forceBuild);

        IReadOnlyList<VerifiedTransactionLease> leases = [];

        try
        {
            // Claim verified transactions under a lease. If the build crashes or this
            // process dies before ConfirmAsync, the lease auto-releases on the next
            // ClaimAsync (default 60s — see ValidatorMempool:LeaseDurationSeconds).
            leases = await _verifiedQueue.ClaimAsync(
                registerId,
                _buildConfig.MaxTransactionsPerDocket,
                TimeSpan.FromSeconds(_buildConfig.LeaseDurationSeconds),
                cancellationToken);
            var verifiedEntries = leases.Select(l => l.Transaction).ToList();

            // Check if register needs genesis docket
            var needsGenesis = await _genesisManager.NeedsGenesisDocketAsync(registerId, cancellationToken);
            if (needsGenesis)
            {
                _logger.LogInformation("Register {RegisterId} needs genesis docket", registerId);
                var transactions = verifiedEntries.Select(v => v.Transaction).ToList();
                var genesisDocket = await _genesisManager.CreateGenesisDocketAsync(registerId, transactions, cancellationToken);

                // Confirm the lease — transactions are now committed to the genesis docket.
                // Without this, leases would auto-release on the next claim and the same
                // transactions would re-surface in a subsequent normal docket, double-processing
                // them. (caught by claude-review on PR #416)
                await _verifiedQueue.ConfirmAsync(
                    registerId,
                    leases.Select(l => l.TransactionId),
                    cancellationToken);

                return genesisDocket;
            }

            // Unwrap verified transactions
            var pendingTransactions = verifiedEntries.Select(v => v.Transaction).ToList();

            // Check if we have transactions to build
            if (pendingTransactions.Count == 0)
            {
                if (!_buildConfig.AllowEmptyDockets)
                {
                    _logger.LogDebug("No pending transactions for register {RegisterId} and empty dockets not allowed",
                        registerId);
                    return null;
                }

                _logger.LogWarning("Building empty docket for register {RegisterId}", registerId);
            }

            // Get previous docket info
            var latestDocket = await _registerClient.ReadLatestDocketAsync(registerId, cancellationToken);

            if (latestDocket == null)
            {
                _logger.LogWarning(
                    "Register {RegisterId} passed genesis check but ReadLatestDocketAsync returned null. " +
                    "Cannot determine next docket number. Skipping docket build.",
                    registerId);
                return null;
            }

            var docketNumber = latestDocket.DocketNumber + 1;
            var previousHash = latestDocket.DocketHash;

            _logger.LogInformation("Building docket {DocketNumber} for register {RegisterId} with {TransactionCount} transactions",
                docketNumber, registerId, pendingTransactions.Count);

            // Compute Merkle root
            string merkleRoot;
            if (pendingTransactions.Count == 0)
            {
                merkleRoot = _merkleTree.ComputeMerkleRoot(new List<string>());
            }
            else
            {
                var txHashes = pendingTransactions.Select(tx =>
                    _docketHasher.ComputeTransactionHash(tx.TransactionId, tx.PayloadHash, tx.CreatedAt)
                ).ToList();

                merkleRoot = _merkleTree.ComputeMerkleRoot(txHashes);
                _logger.LogDebug("Computed Merkle root: {MerkleRoot}", merkleRoot);
            }

            var createdAt = DateTimeOffset.UtcNow;

            // Compute docket hash
            var docketHash = _docketHasher.ComputeDocketHash(
                registerId,
                docketNumber,
                previousHash,
                merkleRoot,
                createdAt);

            // Sign docket with system wallet
            var systemWalletAddress = _validatorConfig.SystemWalletAddress;

            if (string.IsNullOrEmpty(systemWalletAddress))
            {
                systemWalletAddress = await _walletClient.CreateOrRetrieveSystemWalletAsync(
                    _validatorConfig.ValidatorId, cancellationToken);
                _validatorConfig.SystemWalletAddress = systemWalletAddress;
            }

            // Sign with purpose-derived key (FR-012) — uses "sorcha:docket-signing" derivation
            // context so the signing key matches the key declared in the validator roster.
            var docketHashBytes = Convert.FromHexString(docketHash);
            var signResult = await _walletClient.SignTransactionAsync(
                systemWalletAddress, docketHashBytes,
                derivationPath: "sorcha:docket-signing",
                isPreHashed: true,
                cancellationToken);

            // Create docket with real cryptographic signature
            var docket = new Docket
            {
                DocketId = docketHash,
                RegisterId = registerId,
                DocketNumber = docketNumber,
                PreviousHash = previousHash,
                DocketHash = docketHash,
                CreatedAt = createdAt,
                Transactions = pendingTransactions,
                Status = DocketStatus.Proposed,
                ProposerValidatorId = _validatorConfig.ValidatorId,
                ProposerSignature = new Signature
                {
                    PublicKey = signResult.PublicKey,
                    SignatureValue = signResult.Signature,
                    Algorithm = signResult.Algorithm,
                    SignedAt = createdAt
                },
                MerkleRoot = merkleRoot
            };

            _logger.LogInformation("Built docket {DocketNumber} for register {RegisterId} with hash {DocketHash}",
                docketNumber, registerId, docketHash);

            // Confirm the lease — transactions are now committed to the docket.
            // Today's behaviour matches the previous Dequeue semantics: confirmation
            // happens at build success, not at downstream seal success. A future PR
            // can move ConfirmAsync to the seal-success callback site for stronger
            // crash safety; for now the build-success path matches the prior contract.
            await _verifiedQueue.ConfirmAsync(
                registerId,
                leases.Select(l => l.TransactionId),
                cancellationToken);

            return docket;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to build docket for register {RegisterId}", registerId);

            // Release the claim so the transactions return to the available pool
            // and another build cycle can pick them up.
            if (leases is { Count: > 0 })
            {
                try
                {
                    await _verifiedQueue.ReleaseAsync(
                        registerId,
                        leases.Select(l => l.TransactionId),
                        cancellationToken);
                    _logger.LogInformation("Released {Count} transactions back to verified queue after build failure for register {RegisterId}",
                        leases.Count, registerId);
                }
                catch (Exception releaseEx)
                {
                    // Lease will auto-release on next ClaimAsync; log and continue.
                    _logger.LogWarning(releaseEx, "ReleaseAsync failed after build failure — lease will auto-release on next claim");
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Checks if a register should build a docket based on hybrid triggers
    /// </summary>
    public async Task<bool> ShouldBuildDocketAsync(
        string registerId,
        DateTimeOffset lastBuildTime,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Check time threshold (hybrid trigger 1)
            var timeSinceLastBuild = DateTimeOffset.UtcNow - lastBuildTime;
            if (timeSinceLastBuild >= _buildConfig.TimeThreshold)
            {
                _logger.LogDebug("Time threshold met for register {RegisterId} ({TimeSinceLastBuild} >= {TimeThreshold})",
                    registerId, timeSinceLastBuild, _buildConfig.TimeThreshold);
                return true;
            }

            // Check size threshold (hybrid trigger 2)
            var transactionCount = _verifiedQueue.GetCount(registerId);
            if (transactionCount >= _buildConfig.SizeThreshold)
            {
                _logger.LogDebug("Size threshold met for register {RegisterId} ({TransactionCount} >= {SizeThreshold})",
                    registerId, transactionCount, _buildConfig.SizeThreshold);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking if register {RegisterId} should build docket", registerId);
            return false;
        }
    }
}
