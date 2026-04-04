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
        string organizationId, string createdBy, string algorithm = "ED25519", CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(organizationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(createdBy);

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
            CreatedBy = createdBy
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
                existingRecord.CreatedAt,
                IsNewlyCreated: false);
        }

        // Decrypt master seed
        var seed = await _protectionProvider.DecryptSeedAsync(masterKey.EncryptedSeed, masterKey.ProtectionKeyId, ct);

        byte[]? childPrivateKeyBytes = null;
        byte[]? privateKeyBytes = null;
        try
        {
            // Derive child key using NBitcoin BIP32
            var masterExtKey = ExtKey.CreateFromSeed(seed);
            var keyPath = new KeyPath(path.Replace("m/", ""));
            var childExtKey = masterExtKey.Derive(keyPath);

            // Extract the 32-byte private key seed from the derived key for ED25519 generation
            childPrivateKeyBytes = childExtKey.PrivateKey.ToBytes();

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
            privateKeyBytes = keySet.PrivateKey.Key!;

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
            // Clear sensitive key material
            Array.Clear(seed);
            if (childPrivateKeyBytes is not null) Array.Clear(childPrivateKeyBytes);
            if (privateKeyBytes is not null) Array.Clear(privateKeyBytes);
        }
    }

    /// <inheritdoc />
    public async Task<DerivedKeyResult> RotateKeyAsync(string organizationId, Guid derivedKeyRecordId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(organizationId);

        // Find the existing derived key record
        var oldRecord = await _db.DerivedKeyRecords
            .FirstOrDefaultAsync(d => d.Id == derivedKeyRecordId, ct);

        if (oldRecord is null)
        {
            throw new KeyNotFoundException($"Derived key record {derivedKeyRecordId} not found");
        }

        if (oldRecord.OrganizationId != organizationId)
        {
            throw new UnauthorizedAccessException("Key does not belong to this organisation");
        }

        if (oldRecord.Status != DerivedKeyStatus.Active)
        {
            throw new InvalidOperationException(
                $"Cannot rotate key with status {oldRecord.Status}. Only Active keys can be rotated.");
        }

        // Load the org master key
        var masterKey = await _db.OrgMasterKeys
            .FirstOrDefaultAsync(m => m.Id == oldRecord.OrgMasterKeyId, ct);

        if (masterKey is null)
        {
            throw new InvalidOperationException(
                $"Org master key {oldRecord.OrgMasterKeyId} not found for derived key record");
        }

        // Build new derivation path at incremented key index
        var newKeyIndex = oldRecord.KeyIndex + 1;
        var newPath = DerivationPathBuilder.Build(
            Guid.Parse(oldRecord.OrganizationId),
            oldRecord.DepartmentId,
            Guid.Parse(oldRecord.UserId),
            oldRecord.KeyUsage,
            newKeyIndex);

        // Decrypt master seed
        var seed = await _protectionProvider.DecryptSeedAsync(masterKey.EncryptedSeed, masterKey.ProtectionKeyId, ct);

        byte[]? childPrivateKeyBytes = null;
        byte[]? privateKeyBytes = null;
        try
        {
            // Derive child key using NBitcoin BIP32
            var masterExtKey = ExtKey.CreateFromSeed(seed);
            var keyPath = new KeyPath(newPath.Replace("m/", ""));
            var childExtKey = masterExtKey.Derive(keyPath);

            // Extract private key bytes for ED25519 generation
            childPrivateKeyBytes = childExtKey.PrivateKey.ToBytes();

            // Generate ED25519 key pair from the derived seed
            var keySetResult = await _cryptoModule.GenerateKeySetAsync(
                WalletNetworks.ED25519, childPrivateKeyBytes, ct);

            if (!keySetResult.IsSuccess)
            {
                throw new InvalidOperationException(
                    $"Failed to generate ED25519 key from derived seed: {keySetResult.ErrorMessage}");
            }

            var keySet = keySetResult.Value;
            var publicKeyBytes = keySet.PublicKey.Key!;
            privateKeyBytes = keySet.PrivateKey.Key!;

            // Generate wallet address from public key
            var walletAddress = _walletUtilities.PublicKeyToWallet(
                publicKeyBytes, (byte)WalletNetworks.ED25519);

            if (string.IsNullOrEmpty(walletAddress))
            {
                throw new InvalidOperationException("Failed to generate wallet address from derived public key");
            }

            // Encrypt the derived private key
            var encResult = await _protectionProvider.EncryptSeedAsync(privateKeyBytes, ct);
            var encryptedPrivateKey = encResult.EncryptedSeed;
            var encKeyId = encResult.KeyId;

            // Mark the old record as Rotated
            oldRecord.Status = DerivedKeyStatus.Rotated;

            // Create new wallet entity
            var wallet = new Core.Domain.Entities.Wallet
            {
                Address = walletAddress,
                EncryptedPrivateKey = Convert.ToBase64String(encryptedPrivateKey),
                EncryptionKeyId = encKeyId,
                Algorithm = "ED25519",
                Owner = oldRecord.UserId,
                Tenant = oldRecord.OrganizationId,
                Name = $"Org-derived {oldRecord.KeyUsage} key (rotated)",
                Description = $"Rotated key derived at path {newPath} for organisation {oldRecord.OrganizationId}",
                PublicKey = Convert.ToBase64String(publicKeyBytes),
                Status = WalletStatus.Active,
                CustodyMode = CustodyMode.Custodial
            };

            // Create new derived key record
            var newRecord = new DerivedKeyRecord
            {
                OrgMasterKeyId = masterKey.Id,
                OrganizationId = oldRecord.OrganizationId,
                UserId = oldRecord.UserId,
                DepartmentId = oldRecord.DepartmentId,
                KeyUsage = oldRecord.KeyUsage,
                KeyIndex = newKeyIndex,
                DerivationPath = newPath,
                WalletAddress = walletAddress,
                Status = DerivedKeyStatus.Active,
                CustodyMode = CustodyMode.Custodial
            };

            _db.Wallets.Add(wallet);
            _db.DerivedKeyRecords.Add(newRecord);

            // Link wallet to derived key record
            wallet.DerivedKeyRecordId = newRecord.Id;

            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Rotated {Usage} key for user {UserId} in organisation {OrganizationId} from index {OldIndex} to {NewIndex}",
                oldRecord.KeyUsage, oldRecord.UserId, oldRecord.OrganizationId, oldRecord.KeyIndex, newKeyIndex);

            return new DerivedKeyResult(
                newRecord.Id,
                walletAddress,
                newPath,
                oldRecord.KeyUsage,
                newKeyIndex,
                DerivedKeyStatus.Active.ToString(),
                CustodyMode.Custodial.ToString(),
                newRecord.CreatedAt);
        }
        finally
        {
            // Clear sensitive key material
            Array.Clear(seed);
            if (childPrivateKeyBytes is not null) Array.Clear(childPrivateKeyBytes);
            if (privateKeyBytes is not null) Array.Clear(privateKeyBytes);
        }
    }

    /// <inheritdoc />
    public async Task RevokeKeyAsync(string organizationId, Guid derivedKeyRecordId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(organizationId);

        // Find the derived key record
        var record = await _db.DerivedKeyRecords
            .FirstOrDefaultAsync(d => d.Id == derivedKeyRecordId, ct);

        if (record is null)
        {
            throw new KeyNotFoundException($"Derived key record {derivedKeyRecordId} not found");
        }

        if (record.OrganizationId != organizationId)
        {
            throw new UnauthorizedAccessException("Key does not belong to this organisation");
        }

        if (record.Status == DerivedKeyStatus.Revoked)
        {
            throw new InvalidOperationException(
                $"Key {derivedKeyRecordId} is already revoked.");
        }

        // Mark as Revoked
        record.Status = DerivedKeyStatus.Revoked;
        record.RevokedAt = DateTime.UtcNow;

        // Lock the associated wallet
        var wallet = await _db.Wallets
            .FirstOrDefaultAsync(w => w.Address == record.WalletAddress, ct);

        if (wallet is not null)
        {
            wallet.Status = WalletStatus.Locked;
        }

        // If this is an Identity key, log that a DID revocation event should be published
        if (record.KeyUsage == KeyUsage.Identity)
        {
            // TODO: Publish DID revocation event when DID revocation service is available
            _logger.LogWarning(
                "Identity key {DerivedKeyRecordId} revoked for wallet {WalletAddress}. " +
                "DID revocation event should be published (DID revocation service not yet implemented).",
                derivedKeyRecordId, record.WalletAddress);
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Revoked {Usage} key {DerivedKeyRecordId} for user {UserId} in organisation {OrganizationId}. Wallet {WalletAddress} locked.",
            record.KeyUsage, derivedKeyRecordId, record.UserId, record.OrganizationId, record.WalletAddress);
    }
}
