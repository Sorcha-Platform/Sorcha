// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.Text.Json.Serialization;

namespace Sorcha.Cli.Models;

// The canonical wallet DTOs (WalletDto, CreateWalletRequest, CreateWalletResponse,
// SignTransactionRequest, SignTransactionResponse) live in Sorcha.Wallet.Contracts.Models.
// Only CLI-specific request/response shapes remain here.

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
/// Request model for recovering a system (validator) wallet from a mnemonic.
/// The wallet is created in tenant=system with owner=validator:{validatorId}
/// so the Validator Service finds it via CreateOrRetrieveSystemWalletAsync.
/// </summary>
public class RecoverSystemWalletApiRequest
{
    [JsonPropertyName("validatorId")]
    public string ValidatorId { get; set; } = string.Empty;

    [JsonPropertyName("mnemonic")]
    public string Mnemonic { get; set; } = string.Empty;

    [JsonPropertyName("algorithm")]
    public string Algorithm { get; set; } = "ED25519";
}

/// <summary>
/// Response from RecoverSystemWalletAsync.
/// </summary>
public class RecoverSystemWalletResponse
{
    [JsonPropertyName("address")]
    public string Address { get; set; } = string.Empty;
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
