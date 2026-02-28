// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Sorcha.Wallet.Core.Encryption.Interfaces;
using Sorcha.Wallet.Core.Encryption.Logging;

namespace Sorcha.Wallet.Core.Encryption.Providers;

/// <summary>
/// Abstract base class for encryption providers that share AES-256-GCM
/// encrypt/decrypt logic, TTL-based key caching, and key file path helpers.
///
/// Derived classes implement platform-specific key protection and storage:
/// - <see cref="WindowsDpapiEncryptionProvider"/>: Windows DPAPI
/// - <see cref="LinuxSecretServiceEncryptionProvider"/>: Linux Secret Service / file-based fallback
/// </summary>
public abstract class EncryptionProviderBase : IEncryptionProvider
{
    private readonly string _defaultKeyId;

    /// <summary>
    /// In-memory cache of decrypted DEKs with TTL expiry.
    /// </summary>
    protected readonly ConcurrentDictionary<string, (byte[] key, DateTime loadedAt)> KeyCache;

    /// <summary>
    /// Audit logger for encryption operations.
    /// </summary>
    protected readonly EncryptionAuditLogger AuditLogger;

    /// <summary>
    /// Logger instance for derived classes.
    /// </summary>
    protected readonly ILogger Logger;

    private bool _disposed;

    /// <summary>
    /// TTL for cached DEKs (30 minutes).
    /// </summary>
    protected static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Initializes the base encryption provider.
    /// </summary>
    /// <param name="defaultKeyId">Default key identifier for new encryptions</param>
    /// <param name="logger">Logger for diagnostics and audit trail</param>
    /// <param name="providerName">Provider name for audit logging (e.g., "WindowsDpapi", "LinuxSecretService")</param>
    protected EncryptionProviderBase(
        string defaultKeyId,
        ILogger logger,
        string providerName)
    {
        _defaultKeyId = defaultKeyId ?? throw new ArgumentNullException(nameof(defaultKeyId));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        KeyCache = new ConcurrentDictionary<string, (byte[] key, DateTime loadedAt)>();
        AuditLogger = new EncryptionAuditLogger(logger, providerName);
    }

    /// <inheritdoc />
    public string GetDefaultKeyId() => _defaultKeyId;

    /// <inheritdoc />
    public async Task<string> EncryptAsync(
        byte[] plaintext,
        string keyId,
        CancellationToken cancellationToken = default)
    {
        if (plaintext == null || plaintext.Length == 0)
            throw new ArgumentException("Plaintext cannot be null or empty.", nameof(plaintext));

        if (string.IsNullOrWhiteSpace(keyId))
            throw new ArgumentException("Key ID cannot be null or empty.", nameof(keyId));

        using var timer = EncryptionOperationTimer.Start();

        try
        {
            // Get or create DEK
            var dek = await GetOrCreateKeyAsync(keyId, cancellationToken);

            // Encrypt data with AES-256-GCM using DEK
            using var aes = new AesGcm(dek, AesGcm.TagByteSizes.MaxSize);

            // Generate random nonce (12 bytes for GCM)
            var nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);

            // Allocate space for ciphertext and authentication tag
            var ciphertext = new byte[plaintext.Length];
            var tag = new byte[AesGcm.TagByteSizes.MaxSize];

            // Encrypt with authenticated encryption
            aes.Encrypt(nonce, plaintext, ciphertext, tag);

            // Combine: nonce (12) + tag (16) + ciphertext
            var combined = new byte[nonce.Length + tag.Length + ciphertext.Length];
            Buffer.BlockCopy(nonce, 0, combined, 0, nonce.Length);
            Buffer.BlockCopy(tag, 0, combined, nonce.Length, tag.Length);
            Buffer.BlockCopy(ciphertext, 0, combined, nonce.Length + tag.Length, ciphertext.Length);

            // Return base64-encoded result (for database storage)
            var result = Convert.ToBase64String(combined);

            AuditLogger.LogEncryptSuccess(keyId, timer.ElapsedMilliseconds);

            return result;
        }
        catch (Exception ex)
        {
            AuditLogger.LogEncryptFailure(keyId, ex, timer.ElapsedMilliseconds);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<byte[]> DecryptAsync(
        string ciphertext,
        string keyId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ciphertext))
            throw new ArgumentException("Ciphertext cannot be null or empty.", nameof(ciphertext));

        if (string.IsNullOrWhiteSpace(keyId))
            throw new ArgumentException("Key ID cannot be null or empty.", nameof(keyId));

        using var timer = EncryptionOperationTimer.Start();

        try
        {
            // Get DEK from cache or storage
            var dek = await GetOrCreateKeyAsync(keyId, cancellationToken);

            // Decode base64 ciphertext
            var combined = Convert.FromBase64String(ciphertext);

            // Validate minimum length (12 nonce + 16 tag)
            var nonceSize = AesGcm.NonceByteSizes.MaxSize;
            var tagSize = AesGcm.TagByteSizes.MaxSize;

            if (combined.Length < nonceSize + tagSize)
            {
                throw new CryptographicException(
                    $"Ciphertext too short. Expected at least {nonceSize + tagSize} bytes, got {combined.Length} bytes.");
            }

            // Extract components: nonce + tag + ciphertext
            var nonce = new byte[nonceSize];
            var tag = new byte[tagSize];
            var encryptedData = new byte[combined.Length - nonceSize - tagSize];

            Buffer.BlockCopy(combined, 0, nonce, 0, nonceSize);
            Buffer.BlockCopy(combined, nonceSize, tag, 0, tagSize);
            Buffer.BlockCopy(combined, nonceSize + tagSize, encryptedData, 0, encryptedData.Length);

            // Decrypt with AES-256-GCM
            using var aes = new AesGcm(dek, AesGcm.TagByteSizes.MaxSize);
            var plaintext = new byte[encryptedData.Length];

            aes.Decrypt(nonce, encryptedData, tag, plaintext);

            AuditLogger.LogDecryptSuccess(keyId, timer.ElapsedMilliseconds);

            return plaintext;
        }
        catch (Exception ex)
        {
            AuditLogger.LogDecryptFailure(keyId, ex, timer.ElapsedMilliseconds);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> KeyExistsAsync(string keyId, CancellationToken cancellationToken = default)
    {
        using var timer = EncryptionOperationTimer.Start();

        // Check in-memory cache first (TTL-aware)
        if (KeyCache.TryGetValue(keyId, out var cached) && DateTime.UtcNow - cached.loadedAt < CacheTtl)
        {
            AuditLogger.LogKeyExists(keyId, exists: true, timer.ElapsedMilliseconds);
            return true;
        }

        // Delegate to platform-specific store check
        var exists = await KeyExistsInStoreAsync(keyId, cancellationToken);

        AuditLogger.LogKeyExists(keyId, exists, timer.ElapsedMilliseconds);

        return exists;
    }

    /// <inheritdoc />
    public async Task CreateKeyAsync(string keyId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(keyId))
            throw new ArgumentException("Key ID cannot be null or empty.", nameof(keyId));

        using var timer = EncryptionOperationTimer.Start();

        try
        {
            // Generate random 256-bit DEK (32 bytes for AES-256)
            var dek = RandomNumberGenerator.GetBytes(32);

            // Delegate to platform-specific protection and storage
            await ProtectAndStoreKeyAsync(keyId, dek, cancellationToken);

            // Cache decrypted DEK in memory for performance
            KeyCache[keyId] = (dek, DateTime.UtcNow);

            AuditLogger.LogCreateKeySuccess(keyId, timer.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            AuditLogger.LogCreateKeyFailure(keyId, ex, timer.ElapsedMilliseconds);
            throw;
        }
    }

    /// <summary>
    /// Gets or creates encryption key (DEK) with TTL cache management.
    /// </summary>
    protected virtual async Task<byte[]> GetOrCreateKeyAsync(string keyId, CancellationToken cancellationToken)
    {
        // Check cache first (with TTL)
        if (KeyCache.TryGetValue(keyId, out var cached))
        {
            if (DateTime.UtcNow - cached.loadedAt < CacheTtl)
            {
                return cached.key;
            }

            // Cache expired -- evict and re-fetch from storage
            if (KeyCache.TryRemove(keyId, out var expired))
            {
                Array.Clear(expired.key, 0, expired.key.Length);
            }
        }

        // Delegate to platform-specific retrieval
        var dek = await RetrieveKeyAsync(keyId, cancellationToken);

        // Create if not found
        if (dek == null)
        {
            await CreateKeyAsync(keyId, cancellationToken);
            return KeyCache[keyId].key;
        }

        // Cache and return
        KeyCache[keyId] = (dek, DateTime.UtcNow);
        return dek;
    }

    /// <summary>
    /// Gets sanitized file path for key storage.
    /// </summary>
    /// <param name="keyStorePath">Base directory for key files</param>
    /// <param name="keyId">Key identifier</param>
    /// <returns>Full path to the .key file</returns>
    protected static string GetKeyFilePath(string keyStorePath, string keyId)
    {
        // Sanitize key ID for file system (remove invalid characters)
        var safeKeyId = string.Join("_", keyId.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(keyStorePath, $"{safeKeyId}.key");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var kvp in KeyCache)
        {
            Array.Clear(kvp.Value.key, 0, kvp.Value.key.Length);
        }
        KeyCache.Clear();

        OnDispose();

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Called during disposal for platform-specific cleanup logging.
    /// </summary>
    protected abstract void OnDispose();

    /// <summary>
    /// Checks whether a key exists in the platform-specific store (disk, Secret Service, etc.).
    /// Called by <see cref="KeyExistsAsync"/> after the in-memory cache check fails.
    /// </summary>
    protected abstract Task<bool> KeyExistsInStoreAsync(string keyId, CancellationToken cancellationToken);

    /// <summary>
    /// Protects a DEK using platform-specific encryption and stores it.
    /// Called by <see cref="CreateKeyAsync"/> after DEK generation.
    /// </summary>
    /// <param name="keyId">Key identifier</param>
    /// <param name="dek">Raw 256-bit data encryption key</param>
    /// <param name="cancellationToken">Cancellation token</param>
    protected abstract Task ProtectAndStoreKeyAsync(string keyId, byte[] dek, CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves and unprotects a DEK from platform-specific storage.
    /// Returns null if the key does not exist in the store.
    /// Called by <see cref="GetOrCreateKeyAsync"/> after cache miss.
    /// </summary>
    /// <param name="keyId">Key identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Decrypted DEK bytes, or null if key not found</returns>
    protected abstract Task<byte[]?> RetrieveKeyAsync(string keyId, CancellationToken cancellationToken);
}
