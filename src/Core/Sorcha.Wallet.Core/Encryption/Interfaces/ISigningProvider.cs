// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Wallet.Core.Encryption.Models;

namespace Sorcha.Wallet.Core.Encryption.Interfaces;

/// <summary>
/// Provides KMS-resident key creation and signing operations.
/// Key material never leaves the KMS.
/// </summary>
public interface ISigningProvider
{
    /// <summary>
    /// Creates a new signing key pair within the KMS.
    /// </summary>
    /// <param name="keyId">Logical identifier for the signing key.</param>
    /// <param name="algorithm">Cryptographic algorithm (e.g. ED25519, NISTP256).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Information about the created key including its public key.</returns>
    Task<KmsKeyInfo> CreateSigningKeyAsync(string keyId, string algorithm, CancellationToken ct = default);

    /// <summary>
    /// Signs data using a KMS-resident private key.
    /// </summary>
    /// <param name="kmsKeyId">Identifier of the KMS signing key.</param>
    /// <param name="data">The data to sign.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The digital signature bytes.</returns>
    Task<byte[]> SignAsync(string kmsKeyId, byte[] data, CancellationToken ct = default);

    /// <summary>
    /// Verifies a signature using a KMS-resident public key.
    /// </summary>
    /// <param name="kmsKeyId">Identifier of the KMS signing key.</param>
    /// <param name="data">The original data that was signed.</param>
    /// <param name="signature">The signature to verify.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the signature is valid; otherwise false.</returns>
    Task<bool> VerifyAsync(string kmsKeyId, byte[] data, byte[] signature, CancellationToken ct = default);

    /// <summary>
    /// Retrieves the public key for a KMS-resident signing key.
    /// </summary>
    /// <param name="kmsKeyId">Identifier of the KMS signing key.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The public key bytes.</returns>
    Task<byte[]> GetPublicKeyAsync(string kmsKeyId, CancellationToken ct = default);
}
