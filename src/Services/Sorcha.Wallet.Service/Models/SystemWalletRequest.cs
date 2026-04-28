// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Wallet.Service.Models;

/// <summary>
/// Request to create or retrieve a system wallet for a validator
/// </summary>
public class SystemWalletRequest
{
    /// <summary>
    /// Validator ID requesting the system wallet
    /// </summary>
    public string? ValidatorId { get; set; }
}

/// <summary>
/// Response containing the system wallet address
/// </summary>
public class SystemWalletResponse
{
    /// <summary>
    /// Address of the system wallet
    /// </summary>
    public required string Address { get; set; }
}

/// <summary>
/// Request to recover (import) a system wallet from a BIP39 mnemonic.
/// Used to seat the genesis-ceremony validator on a node so the Validator
/// Service can sign system register dockets.
/// </summary>
public class RecoverSystemWalletRequest
{
    /// <summary>Validator ID to recover the system wallet under (matches Validator__ValidatorId).</summary>
    public required string ValidatorId { get; set; }

    /// <summary>BIP39 mnemonic words (space-separated).</summary>
    public required string Mnemonic { get; set; }

    /// <summary>Signing algorithm. Defaults to ED25519 (validator docket-signing default).</summary>
    public string Algorithm { get; set; } = "ED25519";
}
