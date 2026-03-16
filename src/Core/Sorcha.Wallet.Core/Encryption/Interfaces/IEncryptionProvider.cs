// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
namespace Sorcha.Wallet.Core.Encryption.Interfaces;

/// <summary>
/// Provider for encrypting and decrypting private keys at rest
/// </summary>
public interface IEncryptionProvider : IDisposable
{
    /// <summary>
    /// Encrypts data using the specified key
    /// </summary>
    /// <param name="plaintext">Data to encrypt</param>
    /// <param name="keyId">Encryption key identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Encrypted data as base64 string</returns>
    Task<string> EncryptAsync(
        byte[] plaintext,
        string keyId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Decrypts data using the specified key
    /// </summary>
    /// <param name="ciphertext">Encrypted data (base64 string)</param>
    /// <param name="keyId">Encryption key identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Decrypted data</returns>
    Task<byte[]> DecryptAsync(
        string ciphertext,
        string keyId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the default encryption key ID
    /// </summary>
    /// <returns>Default key identifier</returns>
    string GetDefaultKeyId();

    /// <summary>
    /// Checks if a key exists
    /// </summary>
    /// <param name="keyId">Key identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if key exists</returns>
    Task<bool> KeyExistsAsync(string keyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new encryption key
    /// </summary>
    /// <param name="keyId">Key identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task CreateKeyAsync(string keyId, CancellationToken cancellationToken = default);
}
