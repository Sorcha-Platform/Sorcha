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

    /// <summary>
    /// Feature 137 (cross-node submission, C3) — returns the citizen's public delivery keys so a
    /// blueprint <c>sorcha-holder-key</c> form field can carry them into the submission. Combines
    /// the slot-108 holder JWK (for the SD-JWT <c>cnf</c> binding) with the wallet's primary public
    /// key + algorithm (for the on-register AEAD envelope). The encryption key is the wallet's own
    /// signing public key — the encryption pipeline derives X25519 from it, so it is byte-identical
    /// to the register-resolved recipient key. Public material only.
    /// </summary>
    /// <param name="walletAddress">Citizen's primary Sorcha wallet address.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The citizen's holder JWK, base64 encryption public key, and delivery algorithm.</returns>
    /// <exception cref="KeyNotFoundException">Wallet does not exist or has no public key.</exception>
    /// <exception cref="NotSupportedException">Wallet algorithm is not a supported delivery key type.</exception>
    Task<CitizenHolderDeliveryKeys> GetDeliveryKeysAsync(string walletAddress, CancellationToken ct = default);
}

/// <summary>
/// Feature 137 — the citizen's public delivery keys for a cross-node credential round-trip.
/// </summary>
/// <param name="HolderJwk">Slot-108 holder public JWK for the SD-JWT <c>cnf</c> binding.</param>
/// <param name="EncryptionPublicKey">Base64 wallet public key used to wrap the on-register AEAD envelope.</param>
/// <param name="Algorithm">Delivery algorithm: <c>ED25519</c> or <c>NISTP256</c>.</param>
/// <param name="WalletAddress">The citizen's resolved wallet address.</param>
public sealed record CitizenHolderDeliveryKeys(
    JsonElement HolderJwk,
    string EncryptionPublicKey,
    string Algorithm,
    string WalletAddress);
