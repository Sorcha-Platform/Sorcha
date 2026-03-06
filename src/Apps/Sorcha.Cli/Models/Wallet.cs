// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.Text.Json.Serialization;

namespace Sorcha.Cli.Models;

/// <summary>
/// Data transfer object for wallet information
/// </summary>
public class Wallet
{
    [JsonPropertyName("address")]
    public string Address { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("publicKey")]
    public string PublicKey { get; set; } = string.Empty;

    [JsonPropertyName("algorithm")]
    public string Algorithm { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("owner")]
    public string Owner { get; set; } = string.Empty;

    [JsonPropertyName("tenant")]
    public string Tenant { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; }

    [JsonPropertyName("metadata")]
    public Dictionary<string, string> Metadata { get; set; } = new();
}

/// <summary>
/// Request model for creating a new wallet
/// </summary>
public class CreateWalletRequest
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("algorithm")]
    public string Algorithm { get; set; } = string.Empty;

    [JsonPropertyName("wordCount")]
    public int WordCount { get; set; } = 12;

    [JsonPropertyName("passphrase")]
    public string? Passphrase { get; set; }

    [JsonPropertyName("tags")]
    public Dictionary<string, string>? Tags { get; set; }
}

/// <summary>
/// Response model for creating a new wallet
/// </summary>
public class CreateWalletResponse
{
    [JsonPropertyName("wallet")]
    public Wallet? Wallet { get; set; }

    [JsonPropertyName("mnemonicWords")]
    public string[] MnemonicWords { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Request model for recovering a wallet from mnemonic
/// </summary>
public class RecoverWalletRequest
{
    [JsonPropertyName("mnemonicWords")]
    public string[] MnemonicWords { get; set; } = Array.Empty<string>();

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("algorithm")]
    public string Algorithm { get; set; } = string.Empty;

    [JsonPropertyName("passphrase")]
    public string? Passphrase { get; set; }
}

/// <summary>
/// Request model for signing a transaction
/// </summary>
public class SignTransactionRequest
{
    [JsonPropertyName("transactionData")]
    public string TransactionData { get; set; } = string.Empty;

    /// <summary>
    /// Whether the transaction data is already pre-hashed (e.g., SHA-256 hash for attestation signing)
    /// </summary>
    [JsonPropertyName("isPreHashed")]
    public bool IsPreHashed { get; set; }

    /// <summary>
    /// Optional derivation path for HD wallets (BIP44 format)
    /// </summary>
    [JsonPropertyName("derivationPath")]
    public string? DerivationPath { get; set; }
}

/// <summary>
/// Response model for signing a transaction
/// </summary>
public class SignTransactionResponse
{
    [JsonPropertyName("signature")]
    public string Signature { get; set; } = string.Empty;

    [JsonPropertyName("signedBy")]
    public string SignedBy { get; set; } = string.Empty;

    [JsonPropertyName("signedAt")]
    public DateTimeOffset SignedAt { get; set; }

    /// <summary>
    /// Public key used for signing (Base64 encoded)
    /// </summary>
    [JsonPropertyName("publicKey")]
    public string PublicKey { get; set; } = string.Empty;

    /// <summary>
    /// Algorithm used for signing (Ed25519, EcdsaP256, etc.)
    /// </summary>
    [JsonPropertyName("algorithm")]
    public string Algorithm { get; set; } = string.Empty;
}

/// <summary>
/// Request model for updating wallet metadata
/// </summary>
public class UpdateWalletRequest
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("tags")]
    public Dictionary<string, string>? Tags { get; set; }
}

/// <summary>
/// Request model for granting wallet access.
/// </summary>
public class GrantAccessRequest
{
    [JsonPropertyName("subject")]
    public string Subject { get; set; } = string.Empty;

    [JsonPropertyName("accessRight")]
    public string AccessRight { get; set; } = string.Empty;

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    [JsonPropertyName("expiresAt")]
    public DateTime? ExpiresAt { get; set; }
}

/// <summary>
/// Response model for a wallet access grant.
/// </summary>
public class WalletAccessGrant
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("subject")]
    public string Subject { get; set; } = string.Empty;

    [JsonPropertyName("accessRight")]
    public string AccessRight { get; set; } = string.Empty;

    [JsonPropertyName("grantedBy")]
    public string GrantedBy { get; set; } = string.Empty;

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    [JsonPropertyName("grantedAt")]
    public DateTimeOffset GrantedAt { get; set; }

    [JsonPropertyName("expiresAt")]
    public DateTimeOffset? ExpiresAt { get; set; }

    [JsonPropertyName("isActive")]
    public bool IsActive { get; set; }
}

/// <summary>
/// Response model for an access check operation.
/// </summary>
public class AccessCheckResponse
{
    [JsonPropertyName("walletAddress")]
    public string WalletAddress { get; set; } = string.Empty;

    [JsonPropertyName("subject")]
    public string Subject { get; set; } = string.Empty;

    [JsonPropertyName("requiredRight")]
    public string RequiredRight { get; set; } = string.Empty;

    [JsonPropertyName("hasAccess")]
    public bool HasAccess { get; set; }
}
