// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.Wallet.Core.Constants;
using Sorcha.Wallet.Core.Domain.ValueObjects;
using Sorcha.Wallet.Core.Repositories.Interfaces;
using Sorcha.Wallet.Core.Services.Interfaces;
using Sorcha.Wallet.Service.Services.Interfaces;

namespace Sorcha.Wallet.Service.Services.Implementation;

/// <summary>
/// Manages the classical co-key for HAIP-facing credential issuance.
/// PQC-primary wallets derive a classical key (ES256) under
/// <c>sorcha:haip-issuer-signing</c>; classical-primary wallets
/// use their primary key directly.
/// </summary>
public class HaipIssuerCoKeyService : IHaipIssuerCoKeyService
{
    private static readonly HashSet<string> ClassicalAlgorithms = new(StringComparer.OrdinalIgnoreCase)
    {
        "ED25519", "EDDSA", "ES256", "P-256", "P256", "NIST-P256", "NISTP256", "ECDSA-P256",
        "RS256", "RSA", "RSA-4096"
    };

    private static readonly HashSet<string> PqcAlgorithms = new(StringComparer.OrdinalIgnoreCase)
    {
        "ML-DSA-65", "ML-DSA-87", "SLH-DSA-SHA2-128S", "SLH-DSA-SHA2-128F",
        "SLH-DSA-SHA2-192S", "SLH-DSA-SHA2-192F", "SLH-DSA-SHA2-256S", "SLH-DSA-SHA2-256F"
    };

    private const string DefaultClassicalAlgorithm = "ES256";

    private readonly IWalletRepository _repository;
    private readonly IKeyManagementService _keyManagement;
    private readonly ILogger<HaipIssuerCoKeyService> _logger;

    /// <summary>
    /// Initialises a new instance of the <see cref="HaipIssuerCoKeyService"/> class.
    /// </summary>
    public HaipIssuerCoKeyService(
        IWalletRepository repository,
        IKeyManagementService keyManagement,
        ILogger<HaipIssuerCoKeyService> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _keyManagement = keyManagement ?? throw new ArgumentNullException(nameof(keyManagement));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<(byte[] PrivateKey, byte[] PublicKey, string Algorithm)> GetSigningKeyForHaipIssuanceAsync(
        string walletAddress,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(walletAddress);

        var wallet = await _repository.GetByAddressAsync(walletAddress, false, false, false, ct)
            ?? throw new KeyNotFoundException($"Wallet {walletAddress} not found");

        if (!wallet.HaipIssuer)
        {
            throw new InvalidOperationException(
                $"Wallet {walletAddress} lacks the HaipIssuer capability. " +
                "Set HaipIssuer=true before issuing HAIP-facing credentials.");
        }

        var masterKey = await _keyManagement.DecryptPrivateKeyAsync(
            wallet.EncryptedPrivateKey, wallet.EncryptionKeyId);

        // If the primary algorithm is classical, use the primary key directly (FR-029)
        if (ClassicalAlgorithms.Contains(wallet.Algorithm))
        {
            var publicKey = Convert.FromBase64String(wallet.PublicKey!);

            _logger.LogInformation(
                "HAIP issuance for wallet {Address}: primary algorithm {Algorithm} is classical, using primary key",
                walletAddress, wallet.Algorithm);

            return (masterKey, publicKey, wallet.Algorithm);
        }

        // PQC primary: derive a classical co-key (FR-030)
        var resolvedPath = SorchaDerivationPaths.ResolvePath(SorchaDerivationPaths.HaipIssuerSigning);
        var parsedPath = new DerivationPath(resolvedPath);

        var (derivedPrivate, derivedPublic) = await _keyManagement.DeriveKeyAtPathAsync(
            masterKey, parsedPath, DefaultClassicalAlgorithm);

        _logger.LogInformation(
            "HAIP issuance for wallet {Address}: PQC primary ({PrimaryAlg}), derived classical co-key ({CoKeyAlg})",
            walletAddress, wallet.Algorithm, DefaultClassicalAlgorithm);

        return (derivedPrivate, derivedPublic, DefaultClassicalAlgorithm);
    }
}
