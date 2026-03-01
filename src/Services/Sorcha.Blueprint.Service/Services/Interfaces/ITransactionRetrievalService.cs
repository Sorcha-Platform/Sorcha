// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Blueprint.Service.Models;
using Sorcha.TransactionHandler.Encryption.Models;

namespace Sorcha.Blueprint.Service.Services.Interfaces;

/// <summary>
/// Service for retrieving and decrypting transaction payloads for authorized recipients.
/// </summary>
public interface ITransactionRetrievalService
{
    /// <summary>
    /// Decrypts encrypted payload groups for a specific recipient wallet address.
    /// Finds all groups where the wallet has a wrapped key, unwraps the symmetric key
    /// via the Wallet Service, decrypts each group's ciphertext, verifies integrity hashes,
    /// and merges the results into a single payload dictionary.
    /// </summary>
    /// <param name="encryptedGroups">The encrypted payload groups from the transaction.</param>
    /// <param name="recipientWalletAddress">The wallet address of the recipient requesting decryption.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="DecryptionResult"/> containing the merged decrypted payload or an error.</returns>
    Task<DecryptionResult> DecryptPayloadForRecipientAsync(
        EncryptedPayloadGroup[] encryptedGroups,
        string recipientWalletAddress,
        CancellationToken ct = default);

    /// <summary>
    /// Determines whether a transaction is a legacy (unencrypted) transaction
    /// that does not contain encrypted payload groups.
    /// </summary>
    /// <param name="groups">The encrypted payload groups from the transaction, or null.</param>
    /// <returns>True if the transaction is legacy/unencrypted and should return plaintext directly.</returns>
    static bool IsLegacyTransaction(EncryptedPayloadGroup[]? groups)
    {
        return groups == null || groups.Length == 0;
    }
}
