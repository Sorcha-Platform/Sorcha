// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.ServiceClients.SystemWallet;

/// <summary>
/// Secure, audited signing service for system-level transactions.
/// Manages wallet lifecycle, enforces operation whitelist, rate limiting, and audit logging.
/// </summary>
/// <remarks>
/// Must be explicitly opted into via <c>AddSystemWalletSigning()</c> — not automatically
/// available to all services. Only services that need system signing capability should register it.
/// </remarks>
public interface ISystemWalletSigningService
{
    /// <summary>
    /// Signs transaction data with the system wallet, enforcing security controls.
    /// </summary>
    /// <param name="registerId">Target register for rate limiting and audit</param>
    /// <param name="txId">Transaction ID being signed (64-char hex)</param>
    /// <param name="payloadHash">Hex-encoded SHA-256 hash of the payload</param>
    /// <param name="derivationPath">Signing derivation path (must be in whitelist)</param>
    /// <param name="transactionType">Transaction type for audit logging (e.g. "Genesis", "Control")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Signing result with signature bytes, public key, and algorithm</returns>
    /// <exception cref="InvalidOperationException">Derivation path not in whitelist</exception>
    /// <exception cref="InvalidOperationException">Rate limit exceeded for register</exception>
    /// <exception cref="InvalidOperationException">System wallet unavailable after retries</exception>
    Task<SystemSignResult> SignAsync(
        string registerId,
        string txId,
        string payloadHash,
        string derivationPath,
        string transactionType,
        CancellationToken cancellationToken = default);
}
