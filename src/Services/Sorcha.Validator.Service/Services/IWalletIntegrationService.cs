// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Validator.Service.Models;
using Sorcha.Register.Models;

namespace Sorcha.Validator.Service.Services;

/// <summary>
/// Service interface for wallet integration with the Wallet Service.
/// </summary>
/// <remarks>
/// <para>
/// This service provides cryptographic operations for the Validator Service using
/// a dedicated wallet from the Wallet Service. Operations include docket signing,
/// consensus vote signing, and signature verification.
/// </para>
///
/// <para><b>Key Responsibilities:</b></para>
/// <list type="bullet">
///   <item>Retrieve and cache wallet details from Wallet Service</item>
///   <item>Sign dockets using validator wallet</item>
///   <item>Sign consensus votes using validator wallet</item>
///   <item>Verify signatures from peer validators</item>
///   <item>Manage derived private keys for local cryptography</item>
///   <item>Detect wallet rotation and invalidate cache</item>
///   <item>Handle wallet deletion gracefully</item>
/// </list>
///
/// <para><b>Security Model:</b></para>
/// <list type="bullet">
///   <item>Root private key NEVER accessed (FR-012)</item>
///   <item>Derived path private keys retrieved for performance (FR-017)</item>
///   <item>Local signing via Sorcha.Cryptography for 12x speedup</item>
///   <item>Private keys cached in memory only, never persisted (SC-006)</item>
///   <item>Retry logic with exponential backoff (FR-009)</item>
/// </list>
///
/// <para><b>Related Requirements:</b></para>
/// <list type="bullet">
///   <item>FR-001: Authenticate to Wallet Service at startup</item>
///   <item>FR-002: Cache wallet details for service lifetime</item>
///   <item>FR-003: Sign all dockets before broadcasting</item>
///   <item>FR-004: Sign all consensus votes</item>
///   <item>FR-005: Verify peer vote signatures</item>
///   <item>SC-001: Wallet initialization &lt; 5 seconds</item>
///   <item>SC-003: Sign 10+ dockets/second</item>
///   <item>SC-004: Verify signatures &lt; 100ms/vote</item>
/// </list>
/// </remarks>
public interface IWalletIntegrationService
{
    /// <summary>
    /// Retrieves wallet details from the Wallet Service with in-memory caching.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// Cached wallet details including wallet ID, address, algorithm, and version.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This method caches wallet details for the service lifetime to avoid gRPC
    /// calls on every operation (~120ms saved per operation). The cache is only
    /// invalidated when wallet rotation is detected.
    /// </para>
    ///
    /// <para><b>Performance:</b></para>
    /// <list type="bullet">
    ///   <item>First call: ~120ms (gRPC to Wallet Service)</item>
    ///   <item>Subsequent calls: ~1ms (in-memory cache hit)</item>
    /// </list>
    ///
    /// <para><b>Related Requirements:</b></para>
    /// <list type="bullet">
    ///   <item>FR-002: MUST cache wallet details for service lifetime</item>
    ///   <item>FR-015: MUST detect and handle wallet rotation</item>
    ///   <item>SC-001: Wallet initialization &lt; 5 seconds</item>
    /// </list>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Thrown if wallet not found or Wallet Service unavailable after retries.
    /// </exception>
    Task<WalletDetails> GetWalletDetailsAsync(CancellationToken ct = default);

    /// <summary>
    /// Signs a docket hash using the validator's wallet.
    /// </summary>
    /// <param name="docketHash">The SHA-256 hash of the docket to sign (32 bytes).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// Signature containing public key, signature bytes, algorithm, and timestamp.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This method retrieves a derived private key from the Wallet Service (BIP44 path
    /// m/44'/0'/0'/0/0) and performs signing locally using Sorcha.Cryptography for
    /// performance. This achieves 12x faster signing compared to delegating every
    /// sign operation to the Wallet Service.
    /// </para>
    ///
    /// <para><b>Performance:</b></para>
    /// <list type="bullet">
    ///   <item>Local signing (with cached derived key): ~10ms</item>
    ///   <item>Remote signing (via Wallet Service gRPC): ~120ms</item>
    /// </list>
    ///
    /// <para><b>Related Requirements:</b></para>
    /// <list type="bullet">
    ///   <item>FR-003: MUST sign all dockets before broadcasting</item>
    ///   <item>FR-006: MUST include wallet address in ProposerValidatorId</item>
    ///   <item>FR-017: MAY use local crypto with derived keys</item>
    ///   <item>SC-003: Sign >= 10 dockets/second</item>
    /// </list>
    /// </remarks>
    /// <exception cref="CryptographicException">
    /// Thrown if signing fails or derived key retrieval fails.
    /// </exception>
    Task<RegisterSignature> SignDocketAsync(byte[] docketHash, CancellationToken ct = default);

    /// <summary>
    /// Signs a consensus vote hash using the validator's wallet.
    /// </summary>
    /// <param name="voteHash">The hash of the vote data to sign (32 bytes).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// Signature containing public key, signature bytes, algorithm, and timestamp.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This method uses a different BIP44 derivation path (m/44'/0'/0'/1/0) than
    /// docket signing to provide key isolation between different operation types.
    /// Signing is performed locally using Sorcha.Cryptography with a cached derived key.
    /// </para>
    ///
    /// <para><b>Related Requirements:</b></para>
    /// <list type="bullet">
    ///   <item>FR-004: MUST sign all consensus votes</item>
    ///   <item>FR-017: MAY use local crypto with derived keys</item>
    /// </list>
    /// </remarks>
    /// <exception cref="CryptographicException">
    /// Thrown if signing fails or derived key retrieval fails.
    /// </exception>
    Task<RegisterSignature> SignVoteAsync(byte[] voteHash, CancellationToken ct = default);

    /// <summary>
    /// Verifies a signature against a hash and public key.
    /// </summary>
    /// <param name="signature">The signature bytes to verify.</param>
    /// <param name="hash">The hash that was signed (32 bytes).</param>
    /// <param name="publicKey">The public key to verify against.</param>
    /// <param name="algorithm">The algorithm used to create the signature.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// True if the signature is valid; otherwise, false.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This method performs signature verification locally using Sorcha.Cryptography
    /// for performance. Verification is significantly faster than signing and completes
    /// in under 100ms per vote (typically ~10ms for ED25519).
    /// </para>
    ///
    /// <para><b>Performance:</b></para>
    /// <list type="bullet">
    ///   <item>ED25519 verification: ~5ms</item>
    ///   <item>NISTP256 verification: ~10ms</item>
    ///   <item>RSA4096 verification: ~50ms</item>
    /// </list>
    ///
    /// <para><b>Related Requirements:</b></para>
    /// <list type="bullet">
    ///   <item>FR-005: MUST verify peer vote signatures</item>
    ///   <item>FR-017: MAY use local crypto for verification</item>
    ///   <item>SC-004: Verify signatures &lt; 100ms/vote</item>
    /// </list>
    /// </remarks>
    Task<bool> VerifySignatureAsync(
        byte[] signature,
        byte[] hash,
        byte[] publicKey,
        WalletAlgorithm algorithm,
        CancellationToken ct = default);
}
