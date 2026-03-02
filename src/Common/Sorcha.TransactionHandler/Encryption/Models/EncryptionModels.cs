// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Cryptography.Enums;

namespace Sorcha.TransactionHandler.Encryption.Models;

/// <summary>
/// A single disclosure group's encrypted payload with per-recipient wrapped keys.
/// Persisted on-chain as part of the transaction.
/// </summary>
public sealed class EncryptedPayloadGroup
{
    /// <summary>
    /// Deterministic SHA-256 hex hash of sorted disclosed field names.
    /// </summary>
    public required string GroupId { get; init; }

    /// <summary>
    /// Sorted list of JSON Pointer paths included in this group.
    /// </summary>
    public required string[] DisclosedFields { get; init; }

    /// <summary>
    /// Encrypted payload data (XChaCha20-Poly1305 or AES-256-GCM).
    /// </summary>
    public required byte[] Ciphertext { get; init; }

    /// <summary>
    /// Encryption nonce/IV (24 bytes for XChaCha20, 12 bytes for AES-GCM).
    /// </summary>
    public required byte[] Nonce { get; init; }

    /// <summary>
    /// SHA-256 hash of plaintext for post-decryption integrity verification.
    /// </summary>
    public required byte[] PlaintextHash { get; init; }

    /// <summary>
    /// Symmetric cipher used for payload encryption.
    /// </summary>
    public required EncryptionType EncryptionAlgorithm { get; init; }

    /// <summary>
    /// Per-recipient wrapped symmetric keys.
    /// </summary>
    public required WrappedKey[] WrappedKeys { get; init; }
}

/// <summary>
/// A symmetric key encrypted (wrapped) for a specific recipient.
/// </summary>
public sealed class WrappedKey
{
    /// <summary>
    /// Recipient's wallet address.
    /// </summary>
    public required string WalletAddress { get; init; }

    /// <summary>
    /// Symmetric key wrapped with recipient's public key.
    /// </summary>
    public required byte[] EncryptedKey { get; init; }

    /// <summary>
    /// Asymmetric algorithm used for wrapping.
    /// </summary>
    public required WalletNetworks Algorithm { get; init; }
}

/// <summary>
/// Intermediate grouping of recipients who share identical disclosure field sets.
/// Used during encryption pipeline (not persisted on-chain).
/// </summary>
public sealed class DisclosureGroup
{
    /// <summary>
    /// Deterministic SHA-256 hex hash of sorted field names.
    /// </summary>
    public required string GroupId { get; init; }

    /// <summary>
    /// Sorted JSON Pointer paths for this disclosure set.
    /// </summary>
    public required string[] DisclosedFields { get; init; }

    /// <summary>
    /// Disclosure-filtered payload data (plaintext, pre-encryption).
    /// </summary>
    public required Dictionary<string, object> FilteredPayload { get; init; }

    /// <summary>
    /// Recipients sharing this exact disclosure set.
    /// </summary>
    public required RecipientInfo[] Recipients { get; init; }
}

/// <summary>
/// Recipient with resolved public key for encryption.
/// </summary>
public sealed class RecipientInfo
{
    /// <summary>
    /// Recipient wallet address.
    /// </summary>
    public required string WalletAddress { get; init; }

    /// <summary>
    /// Resolved public key bytes.
    /// </summary>
    public required byte[] PublicKey { get; init; }

    /// <summary>
    /// Key algorithm.
    /// </summary>
    public required WalletNetworks Algorithm { get; init; }

    /// <summary>
    /// How the key was obtained.
    /// </summary>
    public required KeySource Source { get; init; }
}

/// <summary>
/// How a recipient's public key was obtained.
/// </summary>
public enum KeySource
{
    /// <summary>
    /// Resolved from register's published participant index.
    /// </summary>
    Register,

    /// <summary>
    /// Provided externally in the action submission request.
    /// </summary>
    External
}

/// <summary>
/// Result of the encryption pipeline for a single transaction.
/// </summary>
public sealed class EncryptionResult
{
    /// <summary>
    /// Whether the encryption succeeded.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Encrypted payload groups (one per unique disclosure field set).
    /// </summary>
    public EncryptedPayloadGroup[] Groups { get; init; } = [];

    /// <summary>
    /// Error message if encryption failed.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Wallet address of the recipient that caused a failure (if applicable).
    /// </summary>
    public string? FailedRecipient { get; init; }

    /// <summary>
    /// Recipients that were skipped because their public key could not be resolved
    /// and no external key was provided. Per FR-023, key resolution not-found is a warning, not an atomic failure.
    /// </summary>
    public List<string> SkippedRecipients { get; init; } = [];

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    public static EncryptionResult Succeeded(EncryptedPayloadGroup[] groups, List<string>? skipped = null) =>
        new() { Success = true, Groups = groups, SkippedRecipients = skipped ?? [] };

    /// <summary>
    /// Creates a failure result.
    /// </summary>
    public static EncryptionResult Failed(string error, string? failedRecipient = null) =>
        new() { Success = false, Error = error, FailedRecipient = failedRecipient };
}
