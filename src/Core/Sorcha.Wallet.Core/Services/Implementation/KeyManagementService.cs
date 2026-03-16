// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using Microsoft.Extensions.Logging;
using NBitcoin;
using Sorcha.Cryptography.Enums;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Cryptography.Utilities;
using Sorcha.Wallet.Core.Domain.ValueObjects;
using Sorcha.Wallet.Core.Encryption.Interfaces;
using Sorcha.Wallet.Core.Services.Interfaces;

namespace Sorcha.Wallet.Core.Services.Implementation;

/// <summary>
/// Implementation of key management service using NBitcoin and Sorcha.Cryptography
/// </summary>
public class KeyManagementService : IKeyManagementService
{
    private readonly IEncryptionProvider _encryptionProvider;
    private readonly ICryptoModule _cryptoModule;
    private readonly IWalletUtilities _walletUtilities;
    private readonly ILogger<KeyManagementService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="KeyManagementService"/> class.
    /// Handles cryptographic key operations including HD wallet key derivation (BIP32/BIP39/BIP44),
    /// key encryption/decryption, and secure key storage using encryption providers.
    /// </summary>
    /// <param name="encryptionProvider">The encryption provider for securing private keys (e.g., Azure Key Vault, AWS KMS, local).</param>
    /// <param name="cryptoModule">The cryptography module for signing and key operations.</param>
    /// <param name="walletUtilities">Utility service for wallet-specific operations.</param>
    /// <param name="logger">Logger for key management operations and security events.</param>
    public KeyManagementService(
        IEncryptionProvider encryptionProvider,
        ICryptoModule cryptoModule,
        IWalletUtilities walletUtilities,
        ILogger<KeyManagementService> logger)
    {
        _encryptionProvider = encryptionProvider ?? throw new ArgumentNullException(nameof(encryptionProvider));
        _cryptoModule = cryptoModule ?? throw new ArgumentNullException(nameof(cryptoModule));
        _walletUtilities = walletUtilities ?? throw new ArgumentNullException(nameof(walletUtilities));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

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
            // Create ExtKey from the seed/master key bytes
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

            // Get the KeySet from the result
            var keySet = keySetResult.Value!; // KeySet (force unwrap nullable)
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
                ? _encryptionProvider.GetDefaultKeyId()
                : encryptionKeyId;

            var encryptedKey = await _encryptionProvider.EncryptAsync(privateKey, keyId);
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
            var decryptedKey = await _encryptionProvider.DecryptAsync(encryptedPrivateKey, encryptionKeyId);
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
            var privateKey = await _encryptionProvider.DecryptAsync(encryptedPrivateKey, oldKeyId);

            // Re-encrypt with new key
            var reEncrypted = await _encryptionProvider.EncryptAsync(privateKey, newKeyId);

            _logger.LogInformation("Rotated encryption key from {OldKeyId} to {NewKeyId}", oldKeyId, newKeyId);
            return reEncrypted;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rotate encryption key from {OldKeyId} to {NewKeyId}", oldKeyId, newKeyId);
            throw;
        }
    }

}
