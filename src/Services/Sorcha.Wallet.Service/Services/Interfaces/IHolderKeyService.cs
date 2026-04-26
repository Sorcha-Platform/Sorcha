// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;

namespace Sorcha.Wallet.Service.Services.Interfaces;

/// <summary>
/// Derives and signs with the citizen wallet holder key (Feature 114).
/// One holder key per citizen wallet, derived under
/// <see cref="Sorcha.Wallet.Core.Constants.SorchaDerivationPaths.CitizenHolder"/>
/// (slot 108, <c>m/44'/0'/0'/0/108</c>).
/// </summary>
/// <remarks>
/// Distinct from <see cref="IHolderBindingKeyService"/> which serves the existing
/// online HAIP path (KB-JWT signing for individual SD-JWT VC presentations).
/// The citizen-holder key is the citizen's stable cross-device identity that
/// signs <em>device delegation credentials</em> consumed by offline verifiers.
/// Issuers bind citizen-wallet credentials to this key (via the credential's
/// <c>cnf</c> claim).
/// </remarks>
public interface IHolderKeyService
{
    /// <summary>
    /// Returns the public JWK of the citizen wallet holder identity for the
    /// given wallet. Always classical (ES256 / Ed25519) — verifiers consume the
    /// JWK directly, so PQC algorithms are derived as classical co-keys.
    /// </summary>
    /// <param name="walletAddress">Citizen's primary Sorcha wallet address.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>JWK as a <see cref="JsonElement"/> ready to embed in a credential <c>cnf</c> claim.</returns>
    /// <exception cref="KeyNotFoundException">Wallet does not exist.</exception>
    Task<JsonElement> GetHolderPublicJwkAsync(string walletAddress, CancellationToken ct = default);

    /// <summary>
    /// Signs <paramref name="signingInput"/> with the citizen wallet holder key.
    /// Used by <see cref="IDeviceDelegationIssuer"/> to sign delegation credentials.
    /// </summary>
    /// <param name="walletAddress">Citizen's primary Sorcha wallet address.</param>
    /// <param name="signingInput">Bytes to sign (typically the JWS signing input).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Signature bytes plus the JOSE algorithm identifier (e.g. <c>ES256</c>, <c>EdDSA</c>).</returns>
    /// <exception cref="KeyNotFoundException">Wallet does not exist.</exception>
    Task<(byte[] Signature, string Algorithm)> SignAsync(
        string walletAddress,
        byte[] signingInput,
        CancellationToken ct = default);

    /// <summary>
    /// Returns a stable JWK thumbprint (RFC 7638) for the citizen's holder key,
    /// used as the locator in the <c>did:sorcha:holder:{thumbprint}</c> identifier.
    /// </summary>
    /// <param name="walletAddress">Citizen's primary Sorcha wallet address.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>43-character base64url SHA-256 thumbprint.</returns>
    Task<string> GetHolderJwkThumbprintAsync(string walletAddress, CancellationToken ct = default);
}
