// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NBitcoin;
using Sorcha.Cryptography.Enums;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Cryptography.Utilities;
using Sorcha.Wallet.Core.Domain.ValueObjects;
using Sorcha.Wallet.Core.Encryption.Configuration;
using Sorcha.Wallet.Core.Encryption.Interfaces;
using Sorcha.Wallet.Core.Encryption.Logging;
using Sorcha.Wallet.Core.Services.Interfaces;

namespace Sorcha.Wallet.Core.Services.Implementation;

/// <summary>
/// Implementation of key management service using NBitcoin and Sorcha.Cryptography.
/// Performs AES-256-GCM envelope encryption of private keys using DEKs managed
/// by an <see cref="IKeyProtectionProvider"/>.
/// </summary>
public class KeyManagementService : IKeyManagementService
{
    private readonly IKeyProtectionProvider _keyProtectionProvider;
    private readonly ISigningProvider? _signingProvider;
    private readonly ICryptoModule _cryptoModule;
    private readonly IWalletUtilities _walletUtilities;
    private readonly ILogger<KeyManagementService> _logger;
    private readonly EncryptionAuditLogger _auditLogger;
    private readonly WalletKeyManagementOptions _options;
    private readonly string _defaultKeyId;
    private readonly TimeSpan _cacheTtl;
    private readonly TimeSpan _graceperiod;

    /// <summary>
    /// In-memory cache of unwrapped DEKs with TTL.
    /// </summary>
    private readonly ConcurrentDictionary<string, (byte[] Key, DateTime LoadedAt)> _dekCache = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="KeyManagementService"/> class.
    /// Handles cryptographic key operations including HD wallet key derivation (BIP32/BIP39/BIP44),
    /// AES-256-GCM envelope encryption, and secure key storage using key protection providers.
    /// </summary>
    /// <param name="keyProtectionProvider">The key protection provider for wrapping/unwrapping DEKs.</param>
    /// <param name="cryptoModule">The cryptography module for signing and key operations.</param>
    /// <param name="walletUtilities">Utility service for wallet-specific operations.</param>
    /// <param name="logger">Logger for key management operations and security events.</param>
    /// <param name="options">Optional key management configuration.</param>
    /// <param name="signingProvider">Optional KMS signing provider for KMS-resident key operations.</param>
    public KeyManagementService(
        IKeyProtectionProvider keyProtectionProvider,
        ICryptoModule cryptoModule,
        IWalletUtilities walletUtilities,
        ILogger<KeyManagementService> logger,
        IOptions<WalletKeyManagementOptions>? options = null,
        ISigningProvider? signingProvider = null)
    {
        _keyProtectionProvider = keyProtectionProvider ?? throw new ArgumentNullException(nameof(keyProtectionProvider));
        _signingProvider = signingProvider; // Optional — null for Local-only deployments
        _cryptoModule = cryptoModule ?? throw new ArgumentNullException(nameof(cryptoModule));
        _walletUtilities = walletUtilities ?? throw new ArgumentNullException(nameof(walletUtilities));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var opts = options?.Value ?? new WalletKeyManagementOptions();
        _options = opts;
        _defaultKeyId = opts.DefaultKeyId;
        _cacheTtl = TimeSpan.FromMinutes(opts.DekCacheTtlMinutes);
        _graceperiod = TimeSpan.FromMinutes(opts.DekCacheGracePeriodMinutes);
        _auditLogger = new EncryptionAuditLogger(logger, "KeyManagementService");
    }

    /// <summary>
    /// Gets the default encryption key ID.
    /// </summary>
    public string GetDefaultKeyId() => _defaultKeyId;

    /// <inheritdoc/>
    public Task<byte[]> DeriveMasterKeyAsync(Domain.ValueObjects.Mnemonic mnemonic, string? passphrase = null)
    {
        if (mnemonic == null)
            throw new ArgumentNullException(nameof(mnemonic));

        try
        {
            var seed = mnemonic.DeriveSeed(passphrase);
            _logger.LogDebug("Derived master key from mnemonic with {WordCount} words", mnemonic.WordCount);
            return Task.FromResult(seed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to derive master key from mnemonic");
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<(byte[] PrivateKey, byte[] PublicKey)> DeriveKeyAtPathAsync(
        byte[] masterKey,
        DerivationPath derivationPath,
        string algorithm)
    {
        if (masterKey == null || masterKey.Length == 0)
            throw new ArgumentException("Master key cannot be empty", nameof(masterKey));
        if (derivationPath == null)
            throw new ArgumentNullException(nameof(derivationPath));
        if (string.IsNullOrWhiteSpace(algorithm))
            throw new ArgumentException("Algorithm cannot be empty", nameof(algorithm));

        try
        {
            // Use NBitcoin for HD key derivation - master key is the seed
            var extKey = ExtKey.CreateFromSeed(masterKey);
            var derived = extKey.Derive(derivationPath.KeyPath);
            var privateKeyBytes = derived.PrivateKey.ToBytes();

            // Generate key set using Sorcha.Cryptography
            var network = AlgorithmMapper.ParseAlgorithm(algorithm);
            var keySetResult = await _cryptoModule.GenerateKeySetAsync(network, privateKeyBytes);

            if (keySetResult.Status != CryptoStatus.Success)
            {
                throw new InvalidOperationException($"Failed to generate key set: {keySetResult.Status}");
            }

            var keySet = keySetResult.Value!;
            var privateKey = keySet.PrivateKey.Key;
            var publicKey = keySet.PublicKey.Key;

            if (privateKey == null || publicKey == null)
            {
                throw new InvalidOperationException("Generated key set contains null keys");
            }

            _logger.LogDebug("Derived key at path {Path} for algorithm {Algorithm}",
                derivationPath.Path, algorithm);

            return (privateKey, publicKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to derive key at path {Path}", derivationPath.Path);
            throw;
        }
    }

    /// <inheritdoc/>
    public Task<string> GenerateAddressAsync(byte[] publicKey, string algorithm)
    {
        if (publicKey == null || publicKey.Length == 0)
            throw new ArgumentException("Public key cannot be empty", nameof(publicKey));
        if (string.IsNullOrWhiteSpace(algorithm))
            throw new ArgumentException("Algorithm cannot be empty", nameof(algorithm));

        try
        {
            var network = AlgorithmMapper.ParseAlgorithm(algorithm);
            var address = _walletUtilities.PublicKeyToWallet(publicKey, (byte)network);

            if (string.IsNullOrEmpty(address))
            {
                throw new InvalidOperationException("Failed to generate wallet address from public key");
            }

            _logger.LogDebug("Generated address for {Algorithm} algorithm", algorithm);
            return Task.FromResult(address);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate address for algorithm {Algorithm}", algorithm);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<(string EncryptedKey, string KeyId)> EncryptPrivateKeyAsync(
        byte[] privateKey,
        string encryptionKeyId)
    {
        if (privateKey == null || privateKey.Length == 0)
            throw new ArgumentException("Private key cannot be empty", nameof(privateKey));

        try
        {
            var keyId = string.IsNullOrWhiteSpace(encryptionKeyId)
                ? _defaultKeyId
                : encryptionKeyId;

            var encryptedKey = await EncryptWithAesGcmAsync(privateKey, keyId);
            _logger.LogDebug("Encrypted private key using key ID {KeyId}", keyId);

            return (encryptedKey, keyId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to encrypt private key");
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<byte[]> DecryptPrivateKeyAsync(
        string encryptedPrivateKey,
        string encryptionKeyId)
    {
        if (string.IsNullOrWhiteSpace(encryptedPrivateKey))
            throw new ArgumentException("Encrypted private key cannot be empty", nameof(encryptedPrivateKey));
        if (string.IsNullOrWhiteSpace(encryptionKeyId))
            throw new ArgumentException("Encryption key ID cannot be empty", nameof(encryptionKeyId));

        try
        {
            var decryptedKey = await DecryptWithAesGcmAsync(encryptedPrivateKey, encryptionKeyId);
            _logger.LogDebug("Decrypted private key using key ID {KeyId}", encryptionKeyId);
            return decryptedKey;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to decrypt private key");
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<string> RotateEncryptionKeyAsync(
        string encryptedPrivateKey,
        string oldKeyId,
        string newKeyId)
    {
        if (string.IsNullOrWhiteSpace(encryptedPrivateKey))
            throw new ArgumentException("Encrypted private key cannot be empty", nameof(encryptedPrivateKey));
        if (string.IsNullOrWhiteSpace(oldKeyId))
            throw new ArgumentException("Old key ID cannot be empty", nameof(oldKeyId));
        if (string.IsNullOrWhiteSpace(newKeyId))
            throw new ArgumentException("New key ID cannot be empty", nameof(newKeyId));

        try
        {
            // Decrypt with old key
            var privateKey = await DecryptWithAesGcmAsync(encryptedPrivateKey, oldKeyId);

            // Re-encrypt with new key
            var reEncrypted = await EncryptWithAesGcmAsync(privateKey, newKeyId);

            _logger.LogInformation("Rotated encryption key from {OldKeyId} to {NewKeyId}", oldKeyId, newKeyId);
            return reEncrypted;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rotate encryption key from {OldKeyId} to {NewKeyId}", oldKeyId, newKeyId);
            throw;
        }
    }

    // ================================================================
    // KMS-resident signing operations
    // ================================================================

    /// <summary>
    /// Whether a KMS signing provider is available.
    /// </summary>
    public bool IsKmsSigningAvailable => _signingProvider is not null;

    /// <summary>
    /// Creates a KMS-resident signing key pair. Key material never leaves the KMS.
    /// </summary>
    /// <param name="keyId">Logical identifier for the signing key.</param>
    /// <param name="algorithm">Cryptographic algorithm (must be supported by the KMS provider).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Information about the created key including its public key.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no ISigningProvider is configured.</exception>
    public async Task<Encryption.Models.KmsKeyInfo> CreateKmsSigningKeyAsync(
        string keyId, string algorithm, CancellationToken ct = default)
    {
        if (_signingProvider is null)
            throw new InvalidOperationException(
                "KMS signing is not available. No ISigningProvider configured.");

        return await _signingProvider.CreateSigningKeyAsync(keyId, algorithm, ct);
    }

    /// <summary>
    /// Signs data using a KMS-resident private key. The private key never leaves the KMS.
    /// </summary>
    /// <param name="kmsKeyId">Identifier of the KMS signing key.</param>
    /// <param name="data">The data to sign.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The digital signature bytes.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no ISigningProvider is configured.</exception>
    public async Task<byte[]> SignWithKmsAsync(string kmsKeyId, byte[] data, CancellationToken ct = default)
    {
        if (_signingProvider is null)
            throw new InvalidOperationException(
                "KMS signing is not available. No ISigningProvider configured.");

        _logger.LogInformation("Signing data with KMS key '{KmsKeyId}'", kmsKeyId);
        return await _signingProvider.SignAsync(kmsKeyId, data, ct);
    }

    /// <summary>
    /// Retrieves the public key for a KMS-resident signing key.
    /// </summary>
    /// <param name="kmsKeyId">Identifier of the KMS signing key.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The public key bytes.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no ISigningProvider is configured.</exception>
    public async Task<byte[]> GetKmsPublicKeyAsync(string kmsKeyId, CancellationToken ct = default)
    {
        if (_signingProvider is null)
            throw new InvalidOperationException(
                "KMS signing is not available. No ISigningProvider configured.");

        return await _signingProvider.GetPublicKeyAsync(kmsKeyId, ct);
    }

    // ================================================================
    // AES-256-GCM envelope encryption (moved from EncryptionProviderBase)
    // Wire format: base64(nonce[12] + tag[16] + ciphertext[n])
    // ================================================================

    /// <summary>
    /// Encrypts plaintext using AES-256-GCM with a DEK identified by keyId.
    /// The DEK is obtained from the key protection provider (cached with TTL).
    /// </summary>
    private async Task<string> EncryptWithAesGcmAsync(byte[] plaintext, string keyId, CancellationToken ct = default)
    {
        if (plaintext == null || plaintext.Length == 0)
            throw new ArgumentException("Plaintext cannot be null or empty.", nameof(plaintext));

        if (string.IsNullOrWhiteSpace(keyId))
            throw new ArgumentException("Key ID cannot be null or empty.", nameof(keyId));

        using var timer = EncryptionOperationTimer.Start();

        try
        {
            var dek = await GetOrCreateDekAsync(keyId, ct);

            using var aes = new AesGcm(dek, AesGcm.TagByteSizes.MaxSize);

            var nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);
            var ciphertext = new byte[plaintext.Length];
            var tag = new byte[AesGcm.TagByteSizes.MaxSize];

            aes.Encrypt(nonce, plaintext, ciphertext, tag);

            // Combine: nonce (12) + tag (16) + ciphertext
            var combined = new byte[nonce.Length + tag.Length + ciphertext.Length];
            Buffer.BlockCopy(nonce, 0, combined, 0, nonce.Length);
            Buffer.BlockCopy(tag, 0, combined, nonce.Length, tag.Length);
            Buffer.BlockCopy(ciphertext, 0, combined, nonce.Length + tag.Length, ciphertext.Length);

            var result = Convert.ToBase64String(combined);
            _auditLogger.LogEncryptSuccess(keyId, timer.ElapsedMilliseconds);
            return result;
        }
        catch (Exception ex)
        {
            _auditLogger.LogEncryptFailure(keyId, ex, timer.ElapsedMilliseconds);
            throw;
        }
    }

    /// <summary>
    /// Decrypts a base64-encoded AES-256-GCM ciphertext using a DEK identified by keyId.
    /// </summary>
    private async Task<byte[]> DecryptWithAesGcmAsync(string ciphertextBase64, string keyId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ciphertextBase64))
            throw new ArgumentException("Ciphertext cannot be null or empty.", nameof(ciphertextBase64));

        if (string.IsNullOrWhiteSpace(keyId))
            throw new ArgumentException("Key ID cannot be null or empty.", nameof(keyId));

        using var timer = EncryptionOperationTimer.Start();

        try
        {
            var dek = await GetOrCreateDekAsync(keyId, ct);

            var combined = Convert.FromBase64String(ciphertextBase64);

            var nonceSize = AesGcm.NonceByteSizes.MaxSize;
            var tagSize = AesGcm.TagByteSizes.MaxSize;

            if (combined.Length < nonceSize + tagSize)
            {
                throw new CryptographicException(
                    $"Ciphertext too short. Expected at least {nonceSize + tagSize} bytes, got {combined.Length} bytes.");
            }

            var nonce = new byte[nonceSize];
            var tag = new byte[tagSize];
            var encryptedData = new byte[combined.Length - nonceSize - tagSize];

            Buffer.BlockCopy(combined, 0, nonce, 0, nonceSize);
            Buffer.BlockCopy(combined, nonceSize, tag, 0, tagSize);
            Buffer.BlockCopy(combined, nonceSize + tagSize, encryptedData, 0, encryptedData.Length);

            using var aes = new AesGcm(dek, AesGcm.TagByteSizes.MaxSize);
            var plaintext = new byte[encryptedData.Length];

            aes.Decrypt(nonce, encryptedData, tag, plaintext);

            _auditLogger.LogDecryptSuccess(keyId, timer.ElapsedMilliseconds);
            return plaintext;
        }
        catch (Exception ex)
        {
            _auditLogger.LogDecryptFailure(keyId, ex, timer.ElapsedMilliseconds);
            throw;
        }
    }

    /// <summary>
    /// Gets or creates a DEK for the given key ID with TTL-based caching.
    /// </summary>
    private async Task<byte[]> GetOrCreateDekAsync(string keyId, CancellationToken ct)
    {
        // Check cache first (with TTL)
        if (_dekCache.TryGetValue(keyId, out var cached))
        {
            var age = DateTime.UtcNow - cached.LoadedAt;
            if (age < _cacheTtl)
            {
                return cached.Key;
            }

            // TTL expired but within grace period — try to refresh, fall back to stale
            if (age < _cacheTtl + _graceperiod)
            {
                try
                {
                    var refreshed = await UnwrapDekFromProviderAsync(keyId, ct);
                    if (refreshed != null)
                    {
                        // Zero old DEK bytes before overwriting cache entry
                        Array.Clear(cached.Key, 0, cached.Key.Length);
                        _dekCache[keyId] = (refreshed, DateTime.UtcNow);
                        return refreshed;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to refresh DEK for key '{KeyId}' — using stale cached value within grace period", keyId);
                    return cached.Key;
                }
            }

            // Beyond grace period — evict and re-fetch
            if (_dekCache.TryRemove(keyId, out var expired))
            {
                Array.Clear(expired.Key, 0, expired.Key.Length);
            }
        }

        // Try to unwrap from provider
        var dek = await UnwrapDekFromProviderAsync(keyId, ct);

        if (dek == null)
        {
            // Key doesn't exist — create a new DEK
            dek = RandomNumberGenerator.GetBytes(32); // AES-256
            await _keyProtectionProvider.CreateKeyAsync(keyId, ct);
            await _keyProtectionProvider.WrapKeyAsync(keyId, dek, ct);
            _auditLogger.LogCreateKeySuccess(keyId, 0);
        }

        // Zero old DEK bytes before overwriting cache entry
        if (_dekCache.TryGetValue(keyId, out var old))
            Array.Clear(old.Key, 0, old.Key.Length);

        _dekCache[keyId] = (dek, DateTime.UtcNow);

        // Evict oldest entry if cache exceeds MaxCachedDeks
        if (_dekCache.Count > _options.MaxCachedDeks)
        {
            var oldest = _dekCache.OrderBy(kvp => kvp.Value.LoadedAt).First();
            if (_dekCache.TryRemove(oldest.Key, out var evicted))
                Array.Clear(evicted.Key, 0, evicted.Key.Length);
        }

        return dek;
    }

    /// <summary>
    /// Attempts to unwrap a DEK from the key protection provider.
    /// Returns null if the key does not exist.
    /// </summary>
    private async Task<byte[]?> UnwrapDekFromProviderAsync(string keyId, CancellationToken ct)
    {
        var exists = await _keyProtectionProvider.KeyExistsAsync(keyId, ct);
        if (!exists)
            return null;

        return await _keyProtectionProvider.UnwrapKeyAsync(keyId, Array.Empty<byte>(), ct);
    }
}
