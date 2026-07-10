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
    /// Derives the raw secp256k1 key at a BIP32 path for an auxiliary Ethereum identity (Feature 180).
    /// Unlike <see cref="DeriveKeyAtPathAsync"/> — which re-derives the wallet's primary algorithm key
    /// from the path — this returns the secp256k1 key itself: the 32-byte private scalar and the 65-byte
    /// uncompressed SEC1 public key. The wallet's primary algorithm and <c>WalletNetworks</c> are unchanged.
    /// </summary>
    /// <param name="seed">The BIP39 seed (or master key) to derive from.</param>
    /// <param name="derivationPath">The BIP32 path (e.g. <c>m/44'/60'/0'/0/0</c>).</param>
    /// <returns>The raw secp256k1 private key (32 bytes) and uncompressed public key (65 bytes).</returns>
    Task<(byte[] PrivateKey, byte[] PublicKey)> DeriveSecp256k1KeyAtPathAsync(
        byte[] seed,
        DerivationPath derivationPath);

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

    /// <summary>
    /// Creates a KMS-resident signing key pair. Key material never leaves the KMS.
    /// </summary>
    /// <param name="keyId">Logical identifier for the signing key.</param>
    /// <param name="algorithm">Cryptographic algorithm (must be supported by the KMS provider).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Information about the created key including its public key.</returns>
    Task<Encryption.Models.KmsKeyInfo> CreateKmsSigningKeyAsync(string keyId, string algorithm, CancellationToken ct = default);

    /// <summary>
    /// Signs data using a KMS-resident private key. The private key never leaves the KMS.
    /// </summary>
    /// <param name="kmsKeyId">Identifier of the KMS signing key.</param>
    /// <param name="data">The data to sign.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The digital signature bytes.</returns>
    Task<byte[]> SignWithKmsAsync(string kmsKeyId, byte[] data, CancellationToken ct = default);

    /// <summary>
    /// Whether a KMS signing provider is available.
    /// </summary>
    bool IsKmsSigningAvailable { get; }
}
