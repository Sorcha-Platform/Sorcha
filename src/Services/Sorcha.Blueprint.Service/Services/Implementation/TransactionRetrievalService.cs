// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.Cryptography.Enums;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Cryptography.Models;
using Sorcha.ServiceClients.Wallet;
using Sorcha.TransactionHandler.Encryption.Models;

namespace Sorcha.Blueprint.Service.Services.Implementation;

/// <summary>
/// Service for retrieving and decrypting transaction payloads for authorized recipients.
/// Handles symmetric key unwrapping via the Wallet Service, payload decryption,
/// SHA-256 integrity verification, and merging of multiple disclosure groups.
/// </summary>
public class TransactionRetrievalService : ITransactionRetrievalService
{
    private readonly IWalletServiceClient _walletClient;
    private readonly ISymmetricCrypto _symmetricCrypto;
    private readonly IHashProvider _hashProvider;
    private readonly ILogger<TransactionRetrievalService> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="TransactionRetrievalService"/>.
    /// </summary>
    /// <param name="walletClient">Client for wallet decryption operations.</param>
    /// <param name="symmetricCrypto">Symmetric cryptography provider.</param>
    /// <param name="hashProvider">Hash computation provider.</param>
    /// <param name="logger">Logger instance.</param>
    public TransactionRetrievalService(
        IWalletServiceClient walletClient,
        ISymmetricCrypto symmetricCrypto,
        IHashProvider hashProvider,
        ILogger<TransactionRetrievalService> logger)
    {
        _walletClient = walletClient ?? throw new ArgumentNullException(nameof(walletClient));
        _symmetricCrypto = symmetricCrypto ?? throw new ArgumentNullException(nameof(symmetricCrypto));
        _hashProvider = hashProvider ?? throw new ArgumentNullException(nameof(hashProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<DecryptionResult> DecryptPayloadForRecipientAsync(
        EncryptedPayloadGroup[] encryptedGroups,
        string recipientWalletAddress,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(encryptedGroups);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipientWalletAddress);

        // 1. Find all groups where recipientWalletAddress has a WrappedKey
        var matchingGroups = encryptedGroups
            .Where(g => g.WrappedKeys.Any(wk =>
                string.Equals(wk.WalletAddress, recipientWalletAddress, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        // T056: Handle access denied — wallet not found in any group's WrappedKeys
        if (matchingGroups.Count == 0)
        {
            _logger.LogWarning(
                "Wallet {WalletAddress} is not authorized for any disclosure group in transaction",
                recipientWalletAddress);
            return DecryptionResult.Failed(
                $"Access denied: wallet {recipientWalletAddress} is not authorized for any disclosure group");
        }

        var mergedPayload = new Dictionary<string, object>();

        // 2. For each matching group, decrypt
        foreach (var group in matchingGroups)
        {
            // 2a. Find the WrappedKey for this wallet
            var wrappedKey = group.WrappedKeys.First(wk =>
                string.Equals(wk.WalletAddress, recipientWalletAddress, StringComparison.OrdinalIgnoreCase));

            // 2b. Call wallet service to unwrap the symmetric key
            byte[] symmetricKey;
            try
            {
                symmetricKey = await _walletClient.DecryptPayloadAsync(
                    recipientWalletAddress,
                    wrappedKey.EncryptedKey,
                    ct);
            }
            catch (Exception ex) when (ex.Message.Contains("decrypt") || ex is HttpRequestException)
            {
                // T058: Handle rotated key failure
                _logger.LogError(ex,
                    "Decryption failed for wallet {WalletAddress} in group {GroupId}",
                    recipientWalletAddress, group.GroupId);
                return DecryptionResult.Failed(
                    $"Decryption failed for wallet {recipientWalletAddress}: " +
                    "the original encryption key may have been rotated. " +
                    "The key that was active when the transaction was created is required for decryption.");
            }

            // 2c. Build SymmetricCiphertext from group's data and unwrapped key
            using var ciphertext = new SymmetricCiphertext
            {
                Data = group.Ciphertext,
                Key = symmetricKey,
                IV = group.Nonce,
                Type = group.EncryptionAlgorithm
            };

            // 2d. Decrypt payload via ISymmetricCrypto
            var decryptResult = await _symmetricCrypto.DecryptAsync(ciphertext, ct);
            if (!decryptResult.IsSuccess || decryptResult.Value == null)
            {
                _logger.LogError(
                    "Symmetric decryption failed for group {GroupId}: {Error}",
                    group.GroupId, decryptResult.ErrorMessage);
                return DecryptionResult.Failed(
                    $"Decryption failed for group {group.GroupId}: {decryptResult.ErrorMessage}");
            }

            var decryptedBytes = decryptResult.Value;

            // 2e. T055: Verify SHA-256 integrity hash
            var actualHash = _hashProvider.ComputeHash(decryptedBytes, HashType.SHA256);
            if (!actualHash.SequenceEqual(group.PlaintextHash))
            {
                _logger.LogError(
                    "Integrity verification failed for group {GroupId}: hash mismatch indicates payload tampering",
                    group.GroupId);
                return DecryptionResult.Failed(
                    "Integrity verification failed: payload may have been tampered with");
            }

            // 2f. Deserialize decrypted bytes to Dictionary<string, object>
            var groupPayload = JsonSerializer.Deserialize<Dictionary<string, object>>(decryptedBytes);
            if (groupPayload == null)
            {
                _logger.LogError(
                    "Failed to deserialize decrypted payload for group {GroupId}",
                    group.GroupId);
                return DecryptionResult.Failed(
                    $"Failed to deserialize decrypted payload for group {group.GroupId}");
            }

            // 3. Merge into the combined result
            foreach (var kvp in groupPayload)
            {
                mergedPayload[kvp.Key] = kvp.Value;
            }

            _logger.LogDebug(
                "Successfully decrypted group {GroupId} with {FieldCount} fields for wallet {WalletAddress}",
                group.GroupId, groupPayload.Count, recipientWalletAddress);
        }

        _logger.LogInformation(
            "Successfully decrypted {GroupCount} payload group(s) with {FieldCount} total fields for wallet {WalletAddress}",
            matchingGroups.Count, mergedPayload.Count, recipientWalletAddress);

        // 4. Return DecryptionResult
        return DecryptionResult.Succeeded(mergedPayload);
    }
}
