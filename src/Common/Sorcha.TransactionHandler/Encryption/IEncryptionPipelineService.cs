// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.TransactionHandler.Encryption.Models;

namespace Sorcha.TransactionHandler.Encryption;

/// <summary>
/// Orchestrates envelope encryption of disclosed payloads for action transactions.
/// Encrypts payload data with a symmetric key, then wraps the key per recipient.
/// </summary>
public interface IEncryptionPipelineService
{
    /// <summary>
    /// Encrypts disclosed payloads for a set of recipients using envelope encryption.
    /// Each disclosure group gets one ciphertext, with individually wrapped keys per recipient.
    /// </summary>
    /// <param name="disclosureGroups">Pre-grouped recipients with their filtered payloads.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Encryption result with encrypted payload groups or error details.</returns>
    /// <remarks>
    /// FR-002: Envelope encryption (symmetric encrypt data, asymmetric wrap key per recipient).
    /// FR-023: Cryptographic failures are atomic (fail entire operation). Key resolution not-found is skip-with-warning.
    /// </remarks>
    Task<EncryptionResult> EncryptDisclosedPayloadsAsync(
        DisclosureGroup[] disclosureGroups,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Estimates the encrypted transaction size before performing encryption.
    /// Used for pre-flight size check (FR-019).
    /// </summary>
    /// <param name="disclosureGroups">Pre-grouped recipients with their filtered payloads.</param>
    /// <returns>Estimated total size in bytes.</returns>
    long EstimateEncryptedSize(DisclosureGroup[] disclosureGroups);
}
