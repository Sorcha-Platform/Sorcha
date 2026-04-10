// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.Register.Models.Genesis;

/// <summary>
/// The signed control record within a genesis file.
/// </summary>
public class GenesisTransactionData
{
    /// <summary>Deterministic transaction ID: SHA256(UTF8("genesis-{SystemRegisterId}")).</summary>
    [JsonPropertyName("txId")]
    public required string TxId { get; set; }

    /// <summary>Base64Url-encoded RegisterControlRecord JSON.</summary>
    [JsonPropertyName("payload")]
    public required string Payload { get; set; }

    /// <summary>SHA-256 hex hash of the decoded payload bytes.</summary>
    [JsonPropertyName("payloadHash")]
    public required string PayloadHash { get; set; }

    /// <summary>Cryptographic signature over the transaction.</summary>
    [JsonPropertyName("signature")]
    public required GenesisSignature Signature { get; set; }
}
