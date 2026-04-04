// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Wallet.Core.Services.Interfaces;

/// <summary>
/// Pluggable provider for encrypting/decrypting organisation master seeds.
/// Ships with software encryption; Azure KMS implementation slots in from Feature 082.
/// </summary>
public interface IOrgKeyProtectionProvider
{
    /// <summary>
    /// Encrypts a master seed for storage.
    /// </summary>
    /// <param name="seed">Raw master seed bytes</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Encrypted seed bytes and the key identifier used for encryption</returns>
    Task<(byte[] EncryptedSeed, string KeyId)> EncryptSeedAsync(byte[] seed, CancellationToken ct = default);

    /// <summary>
    /// Decrypts a stored master seed.
    /// </summary>
    /// <param name="encryptedSeed">Encrypted master seed bytes</param>
    /// <param name="keyId">Key identifier used during encryption</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Decrypted raw master seed bytes</returns>
    Task<byte[]> DecryptSeedAsync(byte[] encryptedSeed, string keyId, CancellationToken ct = default);

    /// <summary>
    /// Provider name for storage metadata (e.g., "Software", "AzureKeyVault").
    /// </summary>
    string ProviderName { get; }
}
