// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.Register.Models.Genesis;

/// <summary>
/// Cryptographic signature attached to a genesis transaction.
/// </summary>
public class GenesisSignature
{
    /// <summary>Base64-encoded signer's public key.</summary>
    [JsonPropertyName("publicKey")]
    public required string PublicKey { get; set; }

    /// <summary>Base64-encoded signature bytes.</summary>
    [JsonPropertyName("signatureValue")]
    public required string SignatureValue { get; set; }

    /// <summary>Signing algorithm (e.g., "ED25519").</summary>
    [JsonPropertyName("algorithm")]
    public required string Algorithm { get; set; }

    /// <summary>Timestamp of signing.</summary>
    [JsonPropertyName("signedAt")]
    public required DateTimeOffset SignedAt { get; set; }
}
