// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.Register.Models.Genesis;

/// <summary>
/// Complete genesis file model. Deserialized from system-register-genesis.json.
/// Contains the pre-signed genesis transaction, validator roster, and network identity.
/// </summary>
public class SystemRegisterGenesis
{
    /// <summary>Format version (currently 1).</summary>
    [JsonPropertyName("version")]
    public required int Version { get; set; }

    /// <summary>Human-readable network label (e.g., "sorcha-prod", "sorcha-dev").</summary>
    [JsonPropertyName("networkId")]
    public required string NetworkId { get; set; }

    /// <summary>The signed genesis transaction containing the control record.</summary>
    [JsonPropertyName("genesisTransaction")]
    public required GenesisTransactionData GenesisTransaction { get; set; }

    /// <summary>Authorized docket-signing keys for this network.</summary>
    [JsonPropertyName("validatorRoster")]
    public required ValidatorRoster ValidatorRoster { get; set; }

    /// <summary>SHA-256 fingerprint of the genesis public key (hex, truncated to 32 chars).</summary>
    [JsonPropertyName("genesisPublicKeyFingerprint")]
    public required string GenesisPublicKeyFingerprint { get; set; }

    /// <summary>Current supported format version.</summary>
    public const int CurrentVersion = 1;
}
