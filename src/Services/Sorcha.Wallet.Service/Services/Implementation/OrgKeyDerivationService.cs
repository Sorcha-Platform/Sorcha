// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Sorcha.Cryptography.Enums;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Wallet.Core.Data;
using Sorcha.Wallet.Core.Domain;
using Sorcha.Wallet.Core.Domain.Entities;
using Sorcha.Wallet.Core.Domain.Enums;
using Sorcha.Wallet.Core.Services.Interfaces;

namespace Sorcha.Wallet.Service.Services.Implementation;

/// <summary>
/// Service for org-level HD key derivation. Provisions master keys,
/// derives user keys, rotates, and revokes.
/// </summary>
public class OrgKeyDerivationService : IOrgKeyDerivationService
{
    private readonly WalletDbContext _db;
    private readonly IOrgKeyProtectionProvider _protectionProvider;
    private readonly ICryptoModule _cryptoModule;
    private readonly IWalletUtilities _walletUtilities;
    private readonly ILogger<OrgKeyDerivationService> _logger;

    /// <summary>
    /// Initialises a new instance of the <see cref="OrgKeyDerivationService"/> class.
    /// </summary>
    /// <param name="db">Wallet database context.</param>
    /// <param name="protectionProvider">Key protection provider for seed encryption.</param>
    /// <param name="cryptoModule">Cryptographic module for key generation.</param>
    /// <param name="walletUtilities">Wallet address utilities.</param>
    /// <param name="logger">Logger instance.</param>
    public OrgKeyDerivationService(
        WalletDbContext db,
        IOrgKeyProtectionProvider protectionProvider,
        ICryptoModule cryptoModule,
        IWalletUtilities walletUtilities,
        ILogger<OrgKeyDerivationService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _protectionProvider = protectionProvider ?? throw new ArgumentNullException(nameof(protectionProvider));
        _cryptoModule = cryptoModule ?? throw new ArgumentNullException(nameof(cryptoModule));
        _walletUtilities = walletUtilities ?? throw new ArgumentNullException(nameof(walletUtilities));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<OrgMasterKeyProvisionResult> ProvisionMasterKeyAsync(
        string organizationId, string algorithm = "ED25519", CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(organizationId);

        var exists = await _db.OrgMasterKeys.AnyAsync(m => m.OrganizationId == organizationId, ct);
        if (exists)
        {
            throw new InvalidOperationException("Organisation already has a provisioned master key");
        }

        // Generate 24-word BIP39 mnemonic
        var mnemonic = new Mnemonic(Wordlist.English, WordCount.TwentyFour);
        var seed = mnemonic.DeriveSeed();

        // Derive master extended key for public key storage
        var masterKey = ExtKey.CreateFromSeed(seed);
        var masterPublicKey = masterKey.Neuter().ToString(Network.Main);

        // Encrypt seed at rest
        var (encryptedSeed, keyId) = await _protectionProvider.EncryptSeedAsync(seed, ct);

        var orgMasterKey = new OrgMasterKey
        {
            OrganizationId = organizationId,
            EncryptedSeed = encryptedSeed,
            ProtectionProvider = _protectionProvider.ProviderName,
            ProtectionKeyId = keyId,
            Algorithm = algorithm,
            MasterPublicKey = masterPublicKey,
            Status = OrgMasterKeyStatus.Active,
            CreatedBy = organizationId // Admin provisioning on behalf of org
        };

        _db.OrgMasterKeys.Add(orgMasterKey);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Provisioned master key for organisation {OrganizationId}", organizationId);

        return new OrgMasterKeyProvisionResult(
            organizationId,
            masterPublicKey,
            mnemonic.ToString(),
            algorithm);
    }

    /// <inheritdoc />
    public async Task<DerivedKeyResult> DeriveUserKeyAsync(
        string organizationId, string userId, uint departmentId, KeyUsage usage, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(organizationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        // Find active org master key
        var masterKey = await _db.OrgMasterKeys
            .FirstOrDefaultAsync(m => m.OrganizationId == organizationId && m.Status == OrgMasterKeyStatus.Active, ct);

        if (masterKey is null)
        {
            throw new InvalidOperationException(
                $"No active master key found for organisation {organizationId}. Provision one first.");
        }

        // Build derivation path
        var path = DerivationPathBuilder.Build(
            Guid.Parse(organizationId), departmentId, Guid.Parse(userId), usage, 0);

        // Check if already derived (idempotent)
        var existingRecord = await _db.DerivedKeyRecords
            .FirstOrDefaultAsync(d => d.OrgMasterKeyId == masterKey.Id && d.DerivationPath == path, ct);

        if (existingRecord is not null)
        {
            return new DerivedKeyResult(
                existingRecord.Id,
                existingRecord.WalletAddress,
                existingRecord.DerivationPath,
                existingRecord.KeyUsage,
                existingRecord.KeyIndex,
                existingRecord.Status.ToString(),
                existingRecord.CustodyMode.ToString(),
                existingRecord.CreatedAt);
        }

        // Decrypt master seed
        var seed = await _protectionProvider.DecryptSeedAsync(masterKey.EncryptedSeed, masterKey.ProtectionKeyId, ct);

        try
        {
            // Derive child key using NBitcoin BIP32
            var masterExtKey = ExtKey.CreateFromSeed(seed);
            var keyPath = new KeyPath(path.Replace("m/", ""));
            var childExtKey = masterExtKey.Derive(keyPath);

            // Extract the 32-byte private key seed from the derived key for ED25519 generation
            var childPrivateKeyBytes = childExtKey.PrivateKey.ToBytes();

            // Generate ED25519 key pair from the derived seed using Sorcha crypto module
            var keySetResult = await _cryptoModule.GenerateKeySetAsync(
                WalletNetworks.ED25519, childPrivateKeyBytes, ct);

            if (!keySetResult.IsSuccess)
            {
                throw new InvalidOperationException(
                    $"Failed to generate ED25519 key from derived seed: {keySetResult.ErrorMessage}");
            }

            var keySet = keySetResult.Value;
            var publicKeyBytes = keySet.PublicKey.Key!;
            var privateKeyBytes = keySet.PrivateKey.Key!;

            // Generate wallet address from public key
            var walletAddress = _walletUtilities.PublicKeyToWallet(
                publicKeyBytes, (byte)WalletNetworks.ED25519);

            if (string.IsNullOrEmpty(walletAddress))
            {
                throw new InvalidOperationException("Failed to generate wallet address from derived public key");
            }

            // Encrypt the derived private key using the same protection provider as the master seed
            var encResult = await _protectionProvider.EncryptSeedAsync(privateKeyBytes, ct);
            var encryptedPrivateKey = encResult.EncryptedSeed;
            var encKeyId = encResult.KeyId;

            // Create wallet entity
            var wallet = new Core.Domain.Entities.Wallet
            {
                Address = walletAddress,
                EncryptedPrivateKey = Convert.ToBase64String(encryptedPrivateKey),
                EncryptionKeyId = encKeyId,
                Algorithm = "ED25519",
                Owner = userId,
                Tenant = organizationId,
                Name = $"Org-derived {usage} key",
                Description = $"Derived at path {path} for organisation {organizationId}",
                PublicKey = Convert.ToBase64String(publicKeyBytes),
                Status = WalletStatus.Active,
                CustodyMode = CustodyMode.Custodial
            };

            // Create derived key record
            var derivedKeyRecord = new DerivedKeyRecord
            {
                OrgMasterKeyId = masterKey.Id,
                OrganizationId = organizationId,
                UserId = userId,
                DepartmentId = departmentId,
                KeyUsage = usage,
                KeyIndex = 0,
                DerivationPath = path,
                WalletAddress = walletAddress,
                Status = DerivedKeyStatus.Active,
                CustodyMode = CustodyMode.Custodial
            };

            _db.Wallets.Add(wallet);
            _db.DerivedKeyRecords.Add(derivedKeyRecord);

            // Link wallet to derived key record via FK
            wallet.DerivedKeyRecordId = derivedKeyRecord.Id;

            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Derived {Usage} key for user {UserId} in organisation {OrganizationId} at path {Path}",
                usage, userId, organizationId, path);

            return new DerivedKeyResult(
                derivedKeyRecord.Id,
                walletAddress,
                path,
                usage,
                0,
                DerivedKeyStatus.Active.ToString(),
                CustodyMode.Custodial.ToString(),
                derivedKeyRecord.CreatedAt);
        }
        finally
        {
            // Clear sensitive seed material
            Array.Clear(seed);
        }
    }

    /// <inheritdoc />
    public Task<DerivedKeyResult> RotateKeyAsync(Guid derivedKeyRecordId, CancellationToken ct = default)
    {
        // TODO: Implement in Phase 6 (T040)
        throw new NotImplementedException();
    }

    /// <inheritdoc />
    public Task RevokeKeyAsync(Guid derivedKeyRecordId, CancellationToken ct = default)
    {
        // TODO: Implement in Phase 7 (T045)
        throw new NotImplementedException();
    }
}
