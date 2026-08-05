// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Options;
using Sorcha.Validator.Service.Configuration;
using Sorcha.Validator.Service.Models;
using Sorcha.ServiceClients.Wallet;
using Sorcha.ServiceClients.Register;
using Sorcha.Cryptography.Utilities;
using Sorcha.Register.Models;

namespace Sorcha.Validator.Service.Services;

/// <summary>
/// Manages genesis docket creation for new registers
/// </summary>
public class GenesisManager : IGenesisManager
{
    private readonly IRegisterServiceClient _registerClient;
    private readonly IWalletServiceClient _walletClient;
    private readonly ISystemWalletProvider _systemWalletProvider;
    private readonly MerkleTree _merkleTree;
    private readonly DocketHasher _docketHasher;
    private readonly ValidatorConfiguration _config;
    private readonly ILogger<GenesisManager> _logger;

    public GenesisManager(
        IRegisterServiceClient registerClient,
        IWalletServiceClient walletClient,
        ISystemWalletProvider systemWalletProvider,
        MerkleTree merkleTree,
        DocketHasher docketHasher,
        IOptions<ValidatorConfiguration> config,
        ILogger<GenesisManager> logger)
    {
        _registerClient = registerClient ?? throw new ArgumentNullException(nameof(registerClient));
        _walletClient = walletClient ?? throw new ArgumentNullException(nameof(walletClient));
        _systemWalletProvider = systemWalletProvider ?? throw new ArgumentNullException(nameof(systemWalletProvider));
        _merkleTree = merkleTree ?? throw new ArgumentNullException(nameof(merkleTree));
        _docketHasher = docketHasher ?? throw new ArgumentNullException(nameof(docketHasher));
        _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Creates a genesis docket (first docket in a register's chain)
    /// </summary>
    public async Task<Docket> CreateGenesisDocketAsync(
        string registerId,
        List<Transaction> transactions,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating genesis docket for register {RegisterId} with {TransactionCount} transactions",
            registerId, transactions.Count);

        try
        {
            var createdAt = DateTimeOffset.UtcNow;

            // Compute Merkle root
            string merkleRoot;
            if (transactions.Count == 0)
            {
                // Empty Merkle root
                merkleRoot = _merkleTree.ComputeMerkleRoot(new List<string>());
                _logger.LogInformation("Genesis docket for register {RegisterId} has no transactions", registerId);
            }
            else
            {
                // Hash each transaction
                var txHashes = transactions.Select(tx =>
                    _docketHasher.ComputeTransactionHash(tx.TransactionId, tx.PayloadHash, tx.CreatedAt)
                ).ToList();

                merkleRoot = _merkleTree.ComputeMerkleRoot(txHashes);
                _logger.LogDebug("Computed Merkle root for {Count} transactions: {MerkleRoot}",
                    transactions.Count, merkleRoot);
            }

            // Compute docket hash (genesis has null previous hash)
            var docketHash = _docketHasher.ComputeDocketHash(
                registerId,
                docketNumber: 0,
                previousHash: null,
                merkleRoot,
                createdAt);

            // Sign docket hash with system wallet using the docket-signing derivation path
            var systemWalletId = _systemWalletProvider.GetSystemWalletId();
            if (string.IsNullOrWhiteSpace(systemWalletId))
            {
                throw new InvalidOperationException("System wallet not initialized - cannot sign genesis docket");
            }

            var signResult = await _walletClient.SignDataAsync(systemWalletId, docketHash, cancellationToken);

            _logger.LogDebug(
                "Docket signed by system wallet {WalletId} using {Algorithm}",
                systemWalletId, signResult.Algorithm);

            // Create genesis docket with real cryptographic signature
            var genesisDocket = new Docket
            {
                DocketId = docketHash,
                RegisterId = registerId,
                DocketNumber = 0,
                PreviousHash = null, // Genesis docket has no previous
                DocketHash = docketHash,
                CreatedAt = createdAt,
                Transactions = transactions,
                Status = DocketStatus.Proposed,
                ProposerValidatorId = _config.ValidatorId,
                ProposerSignature = new RegisterSignature
                {
                    PublicKey = signResult.PublicKey,
                    SignatureValue = signResult.Signature,
                    Algorithm = signResult.Algorithm,
                    SignedAt = createdAt
                },
                MerkleRoot = merkleRoot
            };

            _logger.LogInformation("Created genesis docket for register {RegisterId} with hash {DocketHash}",
                registerId, docketHash);

            return genesisDocket;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create genesis docket for register {RegisterId}", registerId);
            throw;
        }
    }

    /// <summary>
    /// Checks if a register needs a genesis docket (height = 0)
    /// </summary>
    public async Task<bool> NeedsGenesisDocketAsync(string registerId, CancellationToken cancellationToken = default)
    {
        var height = await _registerClient.GetRegisterHeightAsync(registerId, cancellationToken);

        if (height < 0)
        {
            _logger.LogWarning(
                "Register {RegisterId} not found or unreachable (height={Height}), treating as needs genesis",
                registerId, height);
            return true;
        }

        var needsGenesis = height == 0;

        if (needsGenesis)
        {
            _logger.LogInformation("Register {RegisterId} needs genesis docket (height = 0)", registerId);
        }

        return needsGenesis;
    }
}
