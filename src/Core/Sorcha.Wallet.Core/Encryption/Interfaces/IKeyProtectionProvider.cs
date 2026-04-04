// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Wallet.Core.Encryption.Interfaces;

/// <summary>
/// Provides envelope encryption for Data Encryption Keys (DEKs).
/// Implementations wrap/unwrap DEKs using platform or cloud KMS.
/// </summary>
public interface IKeyProtectionProvider : IDisposable
{
    /// <summary>
    /// Creates a new key encryption key (KEK) in the backing store.
    /// </summary>
    /// <param name="keyId">Logical identifier for the key.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The provider-specific key identifier.</returns>
    Task<string> CreateKeyAsync(string keyId, CancellationToken ct = default);

    /// <summary>
    /// Wraps (encrypts) a plaintext DEK using the specified KEK.
    /// </summary>
    /// <param name="keyId">Identifier of the wrapping key.</param>
    /// <param name="plaintext">The plaintext key material to wrap.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The wrapped (encrypted) key bytes.</returns>
    Task<byte[]> WrapKeyAsync(string keyId, byte[] plaintext, CancellationToken ct = default);

    /// <summary>
    /// Unwraps (decrypts) a previously wrapped DEK using the specified KEK.
    /// </summary>
    /// <param name="keyId">Identifier of the wrapping key.</param>
    /// <param name="ciphertext">The wrapped key bytes to unwrap.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The unwrapped (plaintext) key bytes.</returns>
    Task<byte[]> UnwrapKeyAsync(string keyId, byte[] ciphertext, CancellationToken ct = default);

    /// <summary>
    /// Checks whether a key with the given identifier exists in the backing store.
    /// </summary>
    /// <param name="keyId">Identifier of the key to check.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the key exists; otherwise false.</returns>
    Task<bool> KeyExistsAsync(string keyId, CancellationToken ct = default);
}
