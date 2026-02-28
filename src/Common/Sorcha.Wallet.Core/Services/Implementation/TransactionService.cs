// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using Microsoft.Extensions.Logging;
using Sorcha.Cryptography.Enums;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Cryptography.Utilities;
using Sorcha.Wallet.Core.Services.Interfaces;

namespace Sorcha.Wallet.Core.Services.Implementation;

/// <summary>
/// Implementation of transaction service using Sorcha.Cryptography
/// </summary>
public class TransactionService : ITransactionService
{
    private readonly ICryptoModule _cryptoModule;
    private readonly IHashProvider _hashProvider;
    private readonly ILogger<TransactionService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TransactionService"/> class.
    /// Provides transaction signing, verification, and cryptographic hashing for secure transaction processing.
    /// </summary>
    /// <param name="cryptoModule">The cryptography module for signing operations (supports ED25519, NIST P-256, RSA-4096).</param>
    /// <param name="hashProvider">The hash provider for computing transaction hashes (SHA-256, SHA-512, Keccak).</param>
    /// <param name="logger">Logger for transaction operations and security events.</param>
    public TransactionService(
        ICryptoModule cryptoModule,
        IHashProvider hashProvider,
        ILogger<TransactionService> logger)
    {
        _cryptoModule = cryptoModule ?? throw new ArgumentNullException(nameof(cryptoModule));
        _hashProvider = hashProvider ?? throw new ArgumentNullException(nameof(hashProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<byte[]> SignTransactionAsync(
        byte[] transactionData,
        byte[] privateKey,
        string algorithm,
        bool isPreHashed = false)
    {
        if (transactionData == null || transactionData.Length == 0)
            throw new ArgumentException("Transaction data cannot be empty", nameof(transactionData));
        if (privateKey == null || privateKey.Length == 0)
            throw new ArgumentException("Private key cannot be empty", nameof(privateKey));
        if (string.IsNullOrWhiteSpace(algorithm))
            throw new ArgumentException("Algorithm cannot be empty", nameof(algorithm));

        try
        {
            // When isPreHashed is true, data is already a hash - sign directly without additional hashing
            var dataToSign = isPreHashed
                ? transactionData
                : _hashProvider.ComputeHash(transactionData, HashType.SHA256);

            var network = AlgorithmMapper.ParseAlgorithm(algorithm);
            var signResult = await _cryptoModule.SignAsync(
                dataToSign,
                (byte)network,
                privateKey);

            if (signResult.Status != CryptoStatus.Success || signResult.Value == null)
            {
                throw new InvalidOperationException($"Failed to sign transaction: {signResult.Status}");
            }

            _logger.LogDebug(
                "Signed transaction data using {Algorithm} algorithm (preHashed: {IsPreHashed})",
                algorithm, isPreHashed);
            return signResult.Value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sign transaction using {Algorithm}", algorithm);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> VerifySignatureAsync(
        byte[] transactionData,
        byte[] signature,
        byte[] publicKey,
        string algorithm,
        bool isPreHashed = false)
    {
        if (transactionData == null || transactionData.Length == 0)
            throw new ArgumentException("Transaction data cannot be empty", nameof(transactionData));
        if (signature == null || signature.Length == 0)
            throw new ArgumentException("Signature cannot be empty", nameof(signature));
        if (publicKey == null || publicKey.Length == 0)
            throw new ArgumentException("Public key cannot be empty", nameof(publicKey));
        if (string.IsNullOrWhiteSpace(algorithm))
            throw new ArgumentException("Algorithm cannot be empty", nameof(algorithm));

        try
        {
            // When isPreHashed is true, data is already a hash - verify directly without additional hashing
            var dataToVerify = isPreHashed
                ? transactionData
                : _hashProvider.ComputeHash(transactionData, HashType.SHA256);

            var network = AlgorithmMapper.ParseAlgorithm(algorithm);
            var verifyStatus = await _cryptoModule.VerifyAsync(
                signature,
                dataToVerify,
                (byte)network,
                publicKey);

            var isValid = verifyStatus == CryptoStatus.Success;
            _logger.LogDebug(
                "Verified signature using {Algorithm} algorithm (preHashed: {IsPreHashed}): {IsValid}",
                algorithm, isPreHashed, isValid);
            return isValid;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to verify signature using {Algorithm}", algorithm);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<byte[]> DecryptPayloadAsync(
        byte[] encryptedPayload,
        byte[] privateKey,
        string algorithm)
    {
        if (encryptedPayload == null || encryptedPayload.Length == 0)
            throw new ArgumentException("Encrypted payload cannot be empty", nameof(encryptedPayload));
        if (privateKey == null || privateKey.Length == 0)
            throw new ArgumentException("Private key cannot be empty", nameof(privateKey));
        if (string.IsNullOrWhiteSpace(algorithm))
            throw new ArgumentException("Algorithm cannot be empty", nameof(algorithm));

        try
        {
            var network = AlgorithmMapper.ParseAlgorithm(algorithm);
            var decryptResult = await _cryptoModule.DecryptAsync(
                encryptedPayload,
                (byte)network,
                privateKey);

            if (decryptResult.Status != CryptoStatus.Success || decryptResult.Value == null)
            {
                throw new InvalidOperationException($"Failed to decrypt payload: {decryptResult.Status}");
            }

            _logger.LogDebug("Decrypted payload using {Algorithm} algorithm", algorithm);
            return decryptResult.Value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to decrypt payload using {Algorithm}", algorithm);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<byte[]> EncryptPayloadAsync(
        byte[] payload,
        byte[] recipientPublicKey,
        string algorithm)
    {
        if (payload == null || payload.Length == 0)
            throw new ArgumentException("Payload cannot be empty", nameof(payload));
        if (recipientPublicKey == null || recipientPublicKey.Length == 0)
            throw new ArgumentException("Recipient public key cannot be empty", nameof(recipientPublicKey));
        if (string.IsNullOrWhiteSpace(algorithm))
            throw new ArgumentException("Algorithm cannot be empty", nameof(algorithm));

        try
        {
            var network = AlgorithmMapper.ParseAlgorithm(algorithm);
            var encryptResult = await _cryptoModule.EncryptAsync(
                payload,
                (byte)network,
                recipientPublicKey);

            if (encryptResult.Status != CryptoStatus.Success || encryptResult.Value == null)
            {
                throw new InvalidOperationException($"Failed to encrypt payload: {encryptResult.Status}");
            }

            _logger.LogDebug("Encrypted payload using {Algorithm} algorithm", algorithm);
            return encryptResult.Value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to encrypt payload using {Algorithm}", algorithm);
            throw;
        }
    }

}
