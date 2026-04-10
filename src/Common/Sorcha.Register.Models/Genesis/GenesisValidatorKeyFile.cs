// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.Register.Models.Genesis;

/// <summary>
/// Private key material output by the genesis ceremony.
/// Used for one-time import into the first validator's Wallet Service.
/// This file should be secured or destroyed after import.
/// </summary>
public class GenesisValidatorKeyFile
{
    /// <summary>Format version (currently 1).</summary>
    [JsonPropertyName("version")]
    public required int Version { get; set; }

    /// <summary>Must match the genesis file's NetworkId.</summary>
    [JsonPropertyName("networkId")]
    public required string NetworkId { get; set; }

    /// <summary>Derived wallet address for this key.</summary>
    [JsonPropertyName("walletAddress")]
    public required string WalletAddress { get; set; }

    /// <summary>
    /// Base64-encoded private key bytes. Null when mnemonic-based recovery is used instead.
    /// </summary>
    [JsonPropertyName("privateKey")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PrivateKey { get; set; }

    /// <summary>Base64-encoded public key bytes.</summary>
    [JsonPropertyName("publicKey")]
    public required string PublicKey { get; set; }

    /// <summary>Key algorithm (e.g., "ED25519").</summary>
    [JsonPropertyName("algorithm")]
    public required string Algorithm { get; set; }

    /// <summary>Ceremony timestamp.</summary>
    [JsonPropertyName("createdAt")]
    public required DateTimeOffset CreatedAt { get; set; }

    /// <summary>SHA-256 fingerprint matching the genesis file.</summary>
    [JsonPropertyName("fingerprint")]
    public required string Fingerprint { get; set; }

    /// <summary>
    /// BIP39 mnemonic phrase for wallet recovery.
    /// The wallet recovered from this mnemonic will produce the correct signing keys
    /// at the sorcha:docket-signing derivation path.
    /// </summary>
    [JsonPropertyName("mnemonic")]
    public string? Mnemonic { get; set; }
}
