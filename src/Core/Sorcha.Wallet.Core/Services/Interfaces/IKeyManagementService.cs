// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using Sorcha.Wallet.Core.Domain.ValueObjects;

namespace Sorcha.Wallet.Core.Services.Interfaces;

/// <summary>
/// Service for cryptographic key management
/// </summary>
public interface IKeyManagementService
{
    /// <summary>
    /// Derives a master key from a mnemonic
    /// </summary>
    /// <param name="mnemonic">BIP39 mnemonic</param>
    /// <param name="passphrase">Optional passphrase</param>
    /// <returns>Master extended key</returns>
    Task<byte[]> DeriveMasterKeyAsync(Mnemonic mnemonic, string? passphrase = null);

    /// <summary>
    /// Derives a key at a specific BIP44 path
    /// </summary>
    /// <param name="masterKey">Master key bytes</param>
    /// <param name="derivationPath">BIP44 derivation path</param>
    /// <param name="algorithm">Cryptographic algorithm</param>
    /// <returns>Derived key pair (private and public)</returns>
    Task<(byte[] PrivateKey, byte[] PublicKey)> DeriveKeyAtPathAsync(
        byte[] masterKey,
        DerivationPath derivationPath,
        string algorithm);

    /// <summary>
    /// Generates a public address from a public key
    /// </summary>
    /// <param name="publicKey">Public key bytes</param>
    /// <param name="algorithm">Cryptographic algorithm</param>
    /// <returns>Address string</returns>
    Task<string> GenerateAddressAsync(byte[] publicKey, string algorithm);

    /// <summary>
    /// Encrypts a private key for storage
    /// </summary>
    /// <param name="privateKey">Private key to encrypt</param>
    /// <param name="encryptionKeyId">Encryption key identifier</param>
    /// <returns>Encrypted private key and metadata</returns>
    Task<(string EncryptedKey, string KeyId)> EncryptPrivateKeyAsync(
        byte[] privateKey,
        string encryptionKeyId);

    /// <summary>
    /// Decrypts a private key
    /// </summary>
    /// <param name="encryptedPrivateKey">Encrypted private key</param>
    /// <param name="encryptionKeyId">Encryption key identifier</param>
    /// <returns>Decrypted private key bytes</returns>
    Task<byte[]> DecryptPrivateKeyAsync(
        string encryptedPrivateKey,
        string encryptionKeyId);

    /// <summary>
    /// Rotates the encryption key for a wallet
    /// </summary>
    /// <param name="encryptedPrivateKey">Current encrypted private key</param>
    /// <param name="oldKeyId">Old encryption key ID</param>
    /// <param name="newKeyId">New encryption key ID</param>
    /// <returns>Re-encrypted private key</returns>
    Task<string> RotateEncryptionKeyAsync(
        string encryptedPrivateKey,
        string oldKeyId,
        string newKeyId);
}
