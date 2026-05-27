// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
namespace Sorcha.UI.Core.Services.Encryption;

/// <summary>
/// Service for AES-256-GCM encryption using Web Crypto API
/// </summary>
public interface IEncryptionProvider
{
    /// <summary>
    /// Encrypts plaintext using AES-256-GCM
    /// </summary>
    /// <param name="plaintext">Text to encrypt</param>
    /// <returns>Base64-encoded ciphertext with format: {iv}:{ciphertext}</returns>
    Task<string> EncryptAsync(string plaintext);

    /// <summary>
    /// Decrypts ciphertext using AES-256-GCM
    /// </summary>
    /// <param name="ciphertext">Base64-encoded ciphertext with format: {iv}:{ciphertext}</param>
    /// <returns>Decrypted plaintext</returns>
    Task<string> DecryptAsync(string ciphertext);

    /// <summary>
    /// Checks if encryption is supported (Web Crypto API available)
    /// </summary>
    /// <returns>True if encryption is available, false otherwise</returns>
    Task<bool> IsAvailableAsync();

    /// <summary>
    /// Generates a new encryption key (for testing purposes)
    /// </summary>
    /// <returns>Base64-encoded encryption key</returns>
    Task<string> GenerateKeyAsync();
}
