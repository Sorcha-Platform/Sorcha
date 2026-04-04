// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
namespace Sorcha.Wallet.Core.Services.Interfaces;

/// <summary>
/// Service for transaction signing and verification
/// </summary>
public interface ITransactionService
{
    /// <summary>
    /// Signs transaction data with a private key
    /// </summary>
    /// <param name="transactionData">Data to sign</param>
    /// <param name="privateKey">Private key for signing</param>
    /// <param name="algorithm">Cryptographic algorithm</param>
    /// <param name="isPreHashed">When true, data is already hashed and should be signed directly without additional SHA-256</param>
    /// <returns>Signature bytes</returns>
    Task<byte[]> SignTransactionAsync(
        byte[] transactionData,
        byte[] privateKey,
        string algorithm,
        bool isPreHashed = false);

    /// <summary>
    /// Verifies a transaction signature
    /// </summary>
    /// <param name="transactionData">Original data</param>
    /// <param name="signature">Signature to verify</param>
    /// <param name="publicKey">Public key for verification</param>
    /// <param name="algorithm">Cryptographic algorithm</param>
    /// <param name="isPreHashed">When true, data is already hashed and should be verified directly without additional SHA-256</param>
    /// <returns>True if signature is valid</returns>
    Task<bool> VerifySignatureAsync(
        byte[] transactionData,
        byte[] signature,
        byte[] publicKey,
        string algorithm,
        bool isPreHashed = false);

    /// <summary>
    /// Decrypts a transaction payload
    /// </summary>
    /// <param name="encryptedPayload">Encrypted payload</param>
    /// <param name="privateKey">Private key for decryption</param>
    /// <param name="algorithm">Cryptographic algorithm</param>
    /// <returns>Decrypted payload bytes</returns>
    Task<byte[]> DecryptPayloadAsync(
        byte[] encryptedPayload,
        byte[] privateKey,
        string algorithm);

    /// <summary>
    /// Encrypts a transaction payload for a recipient
    /// </summary>
    /// <param name="payload">Payload to encrypt</param>
    /// <param name="recipientPublicKey">Recipient's public key</param>
    /// <param name="algorithm">Cryptographic algorithm</param>
    /// <returns>Encrypted payload bytes</returns>
    Task<byte[]> EncryptPayloadAsync(
        byte[] payload,
        byte[] recipientPublicKey,
        string algorithm);
}
