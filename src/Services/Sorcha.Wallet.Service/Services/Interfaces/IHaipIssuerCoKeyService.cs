// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Wallet.Service.Services.Interfaces;

/// <summary>
/// Manages the classical co-key for HAIP-facing credential issuance.
/// Wallets whose primary algorithm is PQC (ML-DSA, SLH-DSA) derive a
/// classical key (ES256 by default) under the <c>sorcha:haip-issuer-signing</c>
/// purpose. Wallets already using a classical algorithm use their primary key directly.
/// </summary>
public interface IHaipIssuerCoKeyService
{
    /// <summary>
    /// Returns the signing key and algorithm to use for HAIP-path credential issuance.
    /// For PQC-primary wallets with the HaipIssuer capability, this returns the
    /// derived classical co-key. For classical-primary wallets, it returns the primary key.
    /// </summary>
    /// <param name="walletAddress">The issuer wallet address.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Tuple of (privateKeyBytes, publicKeyBytes, algorithm).</returns>
    /// <exception cref="InvalidOperationException">Wallet lacks the HaipIssuer capability.</exception>
    Task<(byte[] PrivateKey, byte[] PublicKey, string Algorithm)> GetSigningKeyForHaipIssuanceAsync(
        string walletAddress,
        CancellationToken ct = default);
}
