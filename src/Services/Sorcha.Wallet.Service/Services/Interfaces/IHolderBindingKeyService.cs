// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;

namespace Sorcha.Wallet.Service.Services.Interfaces;

/// <summary>
/// Derives and signs with the per-wallet holder binding key used for
/// Key Binding JWTs (KB-JWT) in SD-JWT VC presentations.
/// The key is deterministically derived from the wallet seed under the
/// <c>sorcha:credential-holder-binding</c> BIP32 purpose.
/// </summary>
public interface IHolderBindingKeyService
{
    /// <summary>
    /// Returns the holder binding public key in JWK form for a given wallet.
    /// The key is derived deterministically from the wallet seed — same seed
    /// always produces the same key.
    /// </summary>
    /// <param name="walletAddress">The wallet address.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The holder binding public key as a JWK JsonElement.</returns>
    Task<JsonElement> GetPublicJwkAsync(string walletAddress, CancellationToken ct = default);

    /// <summary>
    /// Signs a KB-JWT signing input using the holder binding private key.
    /// </summary>
    /// <param name="walletAddress">The wallet address.</param>
    /// <param name="signingInput">The KB-JWT header.payload bytes to sign.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The signature bytes and the algorithm used.</returns>
    Task<(byte[] Signature, string Algorithm)> SignKbJwtAsync(
        string walletAddress,
        byte[] signingInput,
        CancellationToken ct = default);
}
