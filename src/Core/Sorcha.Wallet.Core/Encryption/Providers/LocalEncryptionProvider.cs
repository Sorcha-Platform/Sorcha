// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Sorcha.Wallet.Core.Encryption.Interfaces;

namespace Sorcha.Wallet.Core.Encryption.Providers;

/// <summary>
/// Local encryption provider using AES-256-GCM for development and testing.
/// WARNING: This stores keys in memory only and should not be used in production.
/// </summary>
public class LocalEncryptionProvider : IEncryptionProvider
{
    private readonly ILogger<LocalEncryptionProvider> _logger;
    private readonly Dictionary<string, byte[]> _keys = new();
    private const string DefaultKeyId = "local-default-key";
    private const int KeySizeBytes = 32; // 256 bits
    private const int NonceSizeBytes = 12; // 96 bits for GCM
    private const int TagSizeBytes = 16; // 128 bits

    /// <summary>
    /// Initializes a new instance of the <see cref="LocalEncryptionProvider"/> class.
    /// Creates a default encryption key for development and testing purposes.
    /// WARNING: This provider stores keys in memory only and should NOT be used in production.
    /// </summary>
    /// <param name="logger">Logger for diagnostic and warning messages.</param>
    public LocalEncryptionProvider(ILogger<LocalEncryptionProvider> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Initialize with a default key for convenience
        _keys[DefaultKeyId] = RandomNumberGenerator.GetBytes(KeySizeBytes);
        _logger.LogWarning("LocalEncryptionProvider initialized. This provider is for development only.");
    }

    /// <inheritdoc/>
    public Task<string> EncryptAsync(byte[] data, string keyId, CancellationToken cancellationToken = default)
    {
        if (data == null || data.Length == 0)
            throw new ArgumentException("Data cannot be empty", nameof(data));
        if (string.IsNullOrWhiteSpace(keyId))
            throw new ArgumentException("Key ID cannot be empty", nameof(keyId));

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            if (!_keys.TryGetValue(keyId, out var key))
            {
                throw new InvalidOperationException($"Encryption key '{keyId}' not found");
            }

            // Generate random nonce
            var nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);

            // Encrypt using AES-GCM
            var ciphertext = new byte[data.Length];
            var tag = new byte[TagSizeBytes];

            using var aesGcm = new AesGcm(key, TagSizeBytes);
            aesGcm.Encrypt(nonce, data, ciphertext, tag);

            // Combine nonce + tag + ciphertext and encode as base64
            var combined = new byte[NonceSizeBytes + TagSizeBytes + ciphertext.Length];
            Buffer.BlockCopy(nonce, 0, combined, 0, NonceSizeBytes);
            Buffer.BlockCopy(tag, 0, combined, NonceSizeBytes, TagSizeBytes);
            Buffer.BlockCopy(ciphertext, 0, combined, NonceSizeBytes + TagSizeBytes, ciphertext.Length);

            var encrypted = Convert.ToBase64String(combined);
            _logger.LogDebug("Encrypted {DataSize} bytes using key {KeyId}", data.Length, keyId);

            return Task.FromResult(encrypted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to encrypt data using key {KeyId}", keyId);
            throw;
        }
    }

    /// <inheritdoc/>
    public Task<byte[]> DecryptAsync(string encryptedData, string keyId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(encryptedData))
            throw new ArgumentException("Encrypted data cannot be empty", nameof(encryptedData));
        if (string.IsNullOrWhiteSpace(keyId))
            throw new ArgumentException("Key ID cannot be empty", nameof(keyId));

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            if (!_keys.TryGetValue(keyId, out var key))
            {
                throw new InvalidOperationException($"Encryption key '{keyId}' not found");
            }

            // Decode from base64
            var combined = Convert.FromBase64String(encryptedData);

            if (combined.Length < NonceSizeBytes + TagSizeBytes)
            {
                throw new ArgumentException("Invalid encrypted data format", nameof(encryptedData));
            }

            // Extract nonce, tag, and ciphertext
            var nonce = new byte[NonceSizeBytes];
            var tag = new byte[TagSizeBytes];
            var ciphertext = new byte[combined.Length - NonceSizeBytes - TagSizeBytes];

            Buffer.BlockCopy(combined, 0, nonce, 0, NonceSizeBytes);
            Buffer.BlockCopy(combined, NonceSizeBytes, tag, 0, TagSizeBytes);
            Buffer.BlockCopy(combined, NonceSizeBytes + TagSizeBytes, ciphertext, 0, ciphertext.Length);

            // Decrypt using AES-GCM
            var plaintext = new byte[ciphertext.Length];

            using var aesGcm = new AesGcm(key, TagSizeBytes);
            aesGcm.Decrypt(nonce, ciphertext, tag, plaintext);

            _logger.LogDebug("Decrypted {DataSize} bytes using key {KeyId}", plaintext.Length, keyId);
            return Task.FromResult(plaintext);
        }
        catch (CryptographicException ex)
        {
            _logger.LogError(ex, "Failed to decrypt data - authentication failed or corrupted data");
            throw new InvalidOperationException("Decryption failed - data may be corrupted or tampered with", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to decrypt data using key {KeyId}", keyId);
            throw;
        }
    }

    /// <inheritdoc/>
    public string GetDefaultKeyId()
    {
        return DefaultKeyId;
    }

    /// <inheritdoc/>
    public Task<bool> KeyExistsAsync(string keyId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(keyId))
            throw new ArgumentException("Key ID cannot be empty", nameof(keyId));

        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_keys.ContainsKey(keyId));
    }

    /// <inheritdoc/>
    public Task CreateKeyAsync(string keyId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(keyId))
            throw new ArgumentException("Key ID cannot be empty", nameof(keyId));

        cancellationToken.ThrowIfCancellationRequested();

        if (_keys.ContainsKey(keyId))
        {
            throw new InvalidOperationException($"Key '{keyId}' already exists");
        }

        _keys[keyId] = RandomNumberGenerator.GetBytes(KeySizeBytes);
        _logger.LogInformation("Created new encryption key with ID {KeyId}", keyId);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var kvp in _keys)
        {
            Array.Clear(kvp.Value, 0, kvp.Value.Length);
        }
        _keys.Clear();
    }
}
