// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sorcha.Cryptography.Enums;
using Sorcha.Cryptography.Interfaces;
using Sorcha.TransactionHandler.Encryption.Models;

namespace Sorcha.TransactionHandler.Encryption;

/// <summary>
/// Orchestrates envelope encryption of disclosed payloads for action transactions.
/// For each disclosure group, encrypts the payload with a symmetric key (XChaCha20-Poly1305),
/// then wraps that key per recipient using their asymmetric public key.
/// </summary>
/// <remarks>
/// FR-002: Envelope encryption pattern (symmetric encrypt data, asymmetric wrap key per recipient).
/// FR-023: Cryptographic failures are atomic — if any key wrap fails, the entire operation fails.
/// </remarks>
public sealed class EncryptionPipelineService : IEncryptionPipelineService
{
    private readonly ISymmetricCrypto _symmetricCrypto;
    private readonly ICryptoModule _cryptoModule;
    private readonly IHashProvider _hashProvider;
    private readonly ILogger<EncryptionPipelineService> _logger;

    /// <summary>
    /// XChaCha20-Poly1305 overhead: 24-byte nonce + 16-byte Poly1305 tag.
    /// </summary>
    private const int XChaCha20Overhead = 40;

    /// <summary>
    /// Per-group metadata overhead estimate for JSON field names, base64 encoding inflation, etc.
    /// </summary>
    private const int MetadataOverheadPerGroup = 200;

    private static readonly JsonSerializerOptions CompactJsonOptions = new()
    {
        WriteIndented = false
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="EncryptionPipelineService"/> class.
    /// </summary>
    /// <param name="symmetricCrypto">Symmetric encryption provider.</param>
    /// <param name="cryptoModule">Asymmetric encryption provider for key wrapping.</param>
    /// <param name="hashProvider">Hash provider for plaintext integrity hashes.</param>
    /// <param name="logger">Logger instance.</param>
    public EncryptionPipelineService(
        ISymmetricCrypto symmetricCrypto,
        ICryptoModule cryptoModule,
        IHashProvider hashProvider,
        ILogger<EncryptionPipelineService> logger)
    {
        _symmetricCrypto = symmetricCrypto ?? throw new ArgumentNullException(nameof(symmetricCrypto));
        _cryptoModule = cryptoModule ?? throw new ArgumentNullException(nameof(cryptoModule));
        _hashProvider = hashProvider ?? throw new ArgumentNullException(nameof(hashProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<EncryptionResult> EncryptDisclosedPayloadsAsync(
        DisclosureGroup[] disclosureGroups,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // T025: Empty payload handling — bypass encryption entirely
        if (disclosureGroups is null || disclosureGroups.Length == 0)
        {
            _logger.LogDebug("No disclosure groups provided; bypassing encryption");
            return EncryptionResult.Succeeded([]);
        }

        // Filter out groups with empty payloads
        var nonEmptyGroups = disclosureGroups
            .Where(g => g.FilteredPayload.Count > 0)
            .ToArray();

        if (nonEmptyGroups.Length == 0)
        {
            _logger.LogDebug("All disclosure groups have empty payloads; bypassing encryption");
            return EncryptionResult.Succeeded([]);
        }

        _logger.LogInformation(
            "Encrypting {GroupCount} disclosure groups for {TotalRecipients} total recipients",
            nonEmptyGroups.Length,
            nonEmptyGroups.Sum(g => g.Recipients.Length));

        var encryptedGroups = new List<EncryptedPayloadGroup>(nonEmptyGroups.Length);

        foreach (var group in nonEmptyGroups)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var groupResult = await EncryptGroupAsync(group, cancellationToken);
            if (!groupResult.Success)
            {
                // T026: Atomic failure — if any group fails, the entire operation fails
                _logger.LogError(
                    "Encryption failed for group {GroupId}: {Error}",
                    group.GroupId, groupResult.Error);
                return EncryptionResult.Failed(
                    groupResult.Error!,
                    groupResult.FailedRecipient);
            }

            encryptedGroups.Add(groupResult.Groups[0]);
        }

        _logger.LogInformation(
            "Successfully encrypted {GroupCount} disclosure groups",
            encryptedGroups.Count);

        return EncryptionResult.Succeeded(encryptedGroups.ToArray());
    }

    /// <inheritdoc />
    public long EstimateEncryptedSize(DisclosureGroup[] disclosureGroups)
    {
        if (disclosureGroups is null || disclosureGroups.Length == 0)
        {
            return 0;
        }

        long totalSize = 0;

        foreach (var group in disclosureGroups)
        {
            if (group.FilteredPayload.Count == 0)
            {
                continue;
            }

            // Serialize to estimate plaintext size
            var plaintextBytes = JsonSerializer.SerializeToUtf8Bytes(
                group.FilteredPayload, CompactJsonOptions);
            var plaintextSize = plaintextBytes.Length;

            // Ciphertext = plaintext + XChaCha20-Poly1305 overhead (24 nonce + 16 tag)
            var ciphertextSize = plaintextSize + XChaCha20Overhead;

            // Wrapped key overhead per recipient
            long wrappedKeyTotal = 0;
            foreach (var recipient in group.Recipients)
            {
                wrappedKeyTotal += GetWrappedKeyOverhead(recipient.Algorithm);
            }

            // Total for this group: ciphertext + wrapped keys + metadata
            totalSize += ciphertextSize + wrappedKeyTotal + MetadataOverheadPerGroup;
        }

        return totalSize;
    }

    /// <summary>
    /// Encrypts a single disclosure group's payload and wraps the symmetric key for each recipient.
    /// </summary>
    private async Task<EncryptionResult> EncryptGroupAsync(
        DisclosureGroup group,
        CancellationToken cancellationToken)
    {
        // Step 1: Serialize filtered payload to JSON bytes
        var plaintextBytes = JsonSerializer.SerializeToUtf8Bytes(
            group.FilteredPayload, CompactJsonOptions);

        _logger.LogDebug(
            "Group {GroupId}: serialized payload is {Size} bytes for {RecipientCount} recipients",
            group.GroupId, plaintextBytes.Length, group.Recipients.Length);

        // Step 2: Compute SHA-256 hash of plaintext for post-decryption integrity
        var plaintextHash = _hashProvider.ComputeHash(plaintextBytes, HashType.SHA256);

        // Step 3: Symmetric encryption (generate random key + encrypt)
        var symResult = await _symmetricCrypto.EncryptAsync(
            plaintextBytes,
            EncryptionType.XCHACHA20_POLY1305,
            cancellationToken: cancellationToken);

        if (!symResult.IsSuccess)
        {
            return EncryptionResult.Failed(
                $"Symmetric encryption failed for group {group.GroupId}: {symResult.ErrorMessage}");
        }

        using var ciphertext = symResult.Value!;
        var symmetricKey = ciphertext.Key;

        // Step 4: Wrap symmetric key for each recipient
        var wrappedKeys = new List<WrappedKey>(group.Recipients.Length);

        foreach (var recipient in group.Recipients)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var wrapResult = await _cryptoModule.EncryptAsync(
                symmetricKey,
                (byte)recipient.Algorithm,
                recipient.PublicKey,
                cancellationToken);

            if (!wrapResult.IsSuccess)
            {
                // T026: Fail entire operation on any key wrapping failure
                _logger.LogError(
                    "Key wrapping failed for recipient {Wallet} (algorithm {Algorithm}): {Error}",
                    recipient.WalletAddress, recipient.Algorithm, wrapResult.ErrorMessage);

                return EncryptionResult.Failed(
                    $"Key wrapping failed for recipient {recipient.WalletAddress}: {wrapResult.ErrorMessage}",
                    recipient.WalletAddress);
            }

            wrappedKeys.Add(new WrappedKey
            {
                WalletAddress = recipient.WalletAddress,
                EncryptedKey = wrapResult.Value!,
                Algorithm = recipient.Algorithm
            });
        }

        // Step 5: Assemble encrypted payload group
        // Clone Data and IV because SymmetricCiphertext.Dispose() will zeroize them
        var encryptedGroup = new EncryptedPayloadGroup
        {
            GroupId = group.GroupId,
            DisclosedFields = group.DisclosedFields,
            Ciphertext = (byte[])ciphertext.Data.Clone(),
            Nonce = (byte[])ciphertext.IV.Clone(),
            PlaintextHash = plaintextHash,
            EncryptionAlgorithm = EncryptionType.XCHACHA20_POLY1305,
            WrappedKeys = wrappedKeys.ToArray()
        };

        return EncryptionResult.Succeeded([encryptedGroup]);
    }

    /// <summary>
    /// Returns the estimated wrapped key size in bytes for a given algorithm.
    /// </summary>
    private static long GetWrappedKeyOverhead(WalletNetworks algorithm) => algorithm switch
    {
        WalletNetworks.ED25519 => 80,
        WalletNetworks.NISTP256 => 112,
        WalletNetworks.RSA4096 => 512,
        WalletNetworks.ML_KEM_768 => 1120,
        _ => 128 // conservative default for unknown algorithms
    };
}
