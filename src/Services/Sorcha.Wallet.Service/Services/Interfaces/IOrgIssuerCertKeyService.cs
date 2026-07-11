// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Wallet.Service.Services.Interfaces;

/// <summary>
/// Feature 181 US4 — resolves and signs with an organisation's P-256 (ES256) certificate-issuing key
/// for the external X.509 rail. The X.509 rail is P-256-only (EUDI mandate), so this always resolves a
/// P-256 key: the wallet's primary key when it is ES256-primary, otherwise the derived HAIP classical
/// co-key at <c>sorcha:haip-issuer-signing</c> (research R9). The private key never leaves the Wallet
/// Service — the Tenant Service builds the CSR with a remote-signing generator that calls
/// <see cref="SignPreHashedAsync"/> (research R10).
/// </summary>
/// <remarks>
/// Distinct from <see cref="IHaipIssuerCoKeyService"/>: that service treats ED25519 as "classical" and
/// returns the primary Ed25519 key, which is not a P-256 key and cannot back an X.509 certificate. This
/// service always yields a genuine P-256 key and does not gate on the <c>HaipIssuer</c> flag — eligibility
/// is purely "can a P-256 key be resolved".
/// </remarks>
public interface IOrgIssuerCertKeyService
{
    /// <summary>
    /// Resolve the org's P-256 certificate-issuing public key (SPKI). Eligibility fails only when no P-256
    /// key can be resolved (wallet missing / no master key / derivation error — the rare
    /// <c>CERT_KEY_NOT_ELIGIBLE</c> case, FR-024). Never returns or exposes the private key.
    /// </summary>
    Task<OrgIssuerCertKeyResolution> ResolveAsync(string walletAddress, CancellationToken ct = default);

    /// <summary>
    /// Sign a pre-computed digest with the org's P-256 certificate-issuing key. Returns the raw
    /// IEEE P1363 <c>r‖s</c> signature (64 bytes for P-256) — the caller converts to DER as needed.
    /// Throws <see cref="OrgIssuerCertKeyNotEligibleException"/> when no P-256 key resolves.
    /// </summary>
    Task<byte[]> SignPreHashedAsync(string walletAddress, byte[] digest, CancellationToken ct = default);
}

/// <summary>Feature 181 US4 — result of resolving an org's P-256 cert-issuing key.</summary>
/// <param name="Eligible">True when a P-256 key was resolved.</param>
/// <param name="Reason">Machine-readable reason when not eligible (e.g. <c>CERT_KEY_NOT_ELIGIBLE</c>).</param>
/// <param name="BoundKeySource"><c>Primary</c> or <c>HaipCoKey</c> when eligible; null otherwise.</param>
/// <param name="PublicKeySpki">DER SubjectPublicKeyInfo of the P-256 key when eligible; null otherwise.</param>
public sealed record OrgIssuerCertKeyResolution(
    bool Eligible,
    string? Reason,
    string? BoundKeySource,
    byte[]? PublicKeySpki);

/// <summary>Feature 181 US4 — thrown when a sign is requested for an org with no resolvable P-256 key.</summary>
public sealed class OrgIssuerCertKeyNotEligibleException(string walletAddress)
    : InvalidOperationException($"No P-256 certificate-issuing key resolvable for wallet {walletAddress}.");
