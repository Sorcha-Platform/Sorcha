// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Wallet.Core.Services.Interfaces;

/// <summary>
/// Manages recovery key lifecycle: generation, wrapping/unwrapping, and master key encryption.
/// </summary>
public interface IRecoveryKeyService
{
    /// <summary>
    /// Generates a new AES-256 recovery key (32 bytes).
    /// </summary>
    Task<byte[]> GenerateRecoveryKeyAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Wraps (encrypts) a recovery key to a recipient's public key using asymmetric encryption.
    /// </summary>
    /// <param name="recoveryKey">The AES-256 recovery key to wrap.</param>
    /// <param name="recipientPublicKey">The recipient's public key (passkey or org recovery key).</param>
    /// <param name="algorithm">Asymmetric algorithm (e.g., "ED25519", "NISTP256").</param>
    /// <returns>Base64-encoded encrypted recovery key.</returns>
    Task<string> WrapRecoveryKeyAsync(byte[] recoveryKey, byte[] recipientPublicKey, string algorithm, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unwraps (decrypts) a recovery key using the recipient's private key.
    /// </summary>
    /// <param name="encryptedRecoveryKey">Base64-encoded encrypted recovery key.</param>
    /// <param name="recipientPrivateKey">The recipient's private key.</param>
    /// <param name="algorithm">Asymmetric algorithm used for wrapping.</param>
    /// <returns>The decrypted AES-256 recovery key.</returns>
    Task<byte[]> UnwrapRecoveryKeyAsync(string encryptedRecoveryKey, byte[] recipientPrivateKey, string algorithm, CancellationToken cancellationToken = default);

    /// <summary>
    /// Encrypts a master key using the recovery key (AES-256-GCM).
    /// </summary>
    /// <param name="masterKey">The wallet master key to encrypt.</param>
    /// <param name="recoveryKey">The AES-256 recovery key.</param>
    /// <returns>Base64-encoded encrypted master key blob.</returns>
    Task<string> EncryptMasterKeyAsync(byte[] masterKey, byte[] recoveryKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Decrypts a master key using the recovery key.
    /// </summary>
    /// <param name="encryptedBlob">Base64-encoded encrypted master key blob.</param>
    /// <param name="recoveryKey">The AES-256 recovery key.</param>
    /// <returns>The decrypted master key.</returns>
    Task<byte[]> DecryptMasterKeyAsync(string encryptedBlob, byte[] recoveryKey, CancellationToken cancellationToken = default);
}
